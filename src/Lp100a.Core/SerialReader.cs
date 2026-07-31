using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;

namespace Lp100a.Core;

/// <summary>
/// Opens an LP-100A serial port (115200 8N1, no flow control), polls it with "P",
/// and raises <see cref="ReadingReceived"/> for every decoded frame. UI-agnostic:
/// events fire on a background thread, so subscribers must marshal to their UI thread.
///
/// Resilience: the poll runs under a supervisor that detects a dropped device (a hard port I/O
/// error, or <see cref="LinkHealth"/> seeing a long run of silent polls), closes the port so the OS
/// handle is released, then backs off and reconnects. If a <c>resolvePort</c> delegate is supplied
/// it is re-queried on each attempt, so a device that comes back on a different COM number after a
/// sleep/resume or a replug is followed to wherever it now lives.
///
/// Before this the loop sat *inside* the try: one exception ended the thread and the app stayed
/// disconnected until someone reconnected it by hand. That is what a sleep/resume produced whenever
/// the USB device re-enumerated — see CLAUDE.md.
/// </summary>
public sealed class SerialReader : IDisposable
{
    private const int BaudRate = 115200;
    private const int PollIntervalMs = 80;      // matches the LP-100A VCP default (~12 samples/s)
    private const int ReconnectDelayMs = 1000;  // backoff between reconnect attempts
    private const int OpenTimeoutMs = 4000;     // cap a native Open() that wedges on a bad device
    private const int CloseTimeoutMs = 1500;    // cap a native Close() that wedges on a removed device

    /// <summary>
    /// How long the port may stay open but silent before the link is treated as lost. Deliberately
    /// long: an LP-100A parked on a screen other than Watts is legitimately silent, and reconnecting
    /// cannot fix that — see <see cref="LinkHealth"/>. This is a backstop for a handle that survives
    /// a resume but never delivers again; the hard-error path is what normally triggers a reconnect.
    /// </summary>
    private const int SilenceTimeoutMs = 6000;

    private readonly StreamFramer _framer = new();
    private readonly ConcurrentQueue<byte> _outbox = new();  // control commands to send on the poll thread
    private readonly ManualResetEventSlim _stop = new(false);  // signalled by Stop(); also wakes backoff waits
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _linkFaulted;    // set when a read/write hits a hard port error (device gone)
    private volatile bool _everConnected;  // true once a session has connected since Start(): a later
                                           // open failure is a reconnect, not a first-time setup problem
    private int _disposed;                 // 0/1 via Interlocked — makes Dispose() idempotent

    public event Action<Lp100Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    /// <summary>True once a session has connected, so callers can word a failure as a reconnect.</summary>
    public bool EverConnected => _everConnected;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    /// <summary>
    /// Queue a Peak/Avg/Tune cycle command ('F') for the meter. Only 'F' is ever sent —
    /// it advances the power mode while staying on the Watts screen (unlike 'M', which
    /// changes the whole display and must not be sent). Written on the poll thread so it
    /// never races a 'P' poll.
    /// </summary>
    public void CyclePowerMode()
    {
        if (_running) _outbox.Enqueue((byte)'F');
    }

    /// <summary>
    /// Queue an SWR-alarm-setpoint cycle command ('A') for the meter. Advances the alarm
    /// through OFF → 1.5 → 2.0 → 2.5 → 3.0 → User. Like 'F', it stays on the current screen.
    /// Written on the poll thread so it never races a 'P' poll.
    /// </summary>
    public void CycleAlarm()
    {
        if (_running) _outbox.Enqueue((byte)'A');
    }

    /// <param name="portName">Port to open, and the fallback if <paramref name="resolvePort"/> yields nothing.</param>
    /// <param name="resolvePort">
    /// Optional: re-queried before every attempt so a renumbered device is followed. The App layer
    /// passes one that looks the meter up by its USB chip serial, which is the whole reason a
    /// reconnect can survive the port changing underneath it.
    /// </param>
    public void Start(string portName, Func<string?>? resolvePort = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Stop();
        _framer.Reset();
        _outbox.Clear();
        _everConnected = false;
        _stop.Reset();
        _running = true;
        _thread = new Thread(() => Supervise(portName, resolvePort))
        {
            IsBackground = true,
            Name = $"LP100A-{portName}",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        // Set() happens to tolerate a disposed event today; don't rely on that from a shutdown path.
        try { _stop.Set(); } catch (ObjectDisposedException) { /* nothing left to wake */ }
        try { _thread?.Join(3000); } catch { /* ignore */ }
        _thread = null;
        ClosePort();
    }

    /// <summary>
    /// Outer loop: (re-)resolve the port, run one connected session, and — unless we were asked to
    /// stop — back off and try again. Every session closes its port in a finally, so a dropped
    /// device never leaks a handle, and a replug is picked up by re-querying <paramref name="resolvePort"/>.
    /// </summary>
    private void Supervise(string portName, Func<string?>? resolvePort)
    {
        try
        {
            while (_running)
            {
                var port = SafeResolve(resolvePort) ?? portName;
                RunSession(port);
                if (!_running) break;
                if (WaitForStop(ReconnectDelayMs)) break;   // Stop() during backoff → exit
            }
        }
        catch (Exception ex)
        {
            // Nothing may escape this thread: an unhandled exception on a background thread tears down
            // the whole process, so a reader fault would take the app with it. The realistic trigger is
            // Stop()'s join timing out on a wedged session, after which Dispose() disposes _stop while
            // this loop is still going — but a throwing StatusChanged subscriber would do it too.
            Report($"{portName} reader stopped unexpectedly: {ex.Message}", true);
        }
        finally
        {
            ClosePort();
            if (!_running) Report("Disconnected", false);
        }
    }

    private static string? SafeResolve(Func<string?>? resolvePort)
    {
        try { return resolvePort?.Invoke(); } catch { return null; }
    }

    /// <summary>
    /// Raise <see cref="StatusChanged"/> without letting a subscriber's exception escape the reader
    /// thread — see the catch in <see cref="Supervise"/> for why that matters.
    /// </summary>
    private void Report(string message, bool isError)
    {
        try { StatusChanged?.Invoke(message, isError); } catch { /* subscriber's problem, not ours */ }
    }

    /// <summary>
    /// Wait on the stop signal, treating a disposed event as "stop now". <see cref="Stop"/>'s join can
    /// time out on a wedged session and <see cref="Dispose"/> then disposes <c>_stop</c> underneath this
    /// thread; without this the wait would throw and, before the catch above, crash the process.
    /// </summary>
    private bool WaitForStop(int milliseconds)
    {
        try { return _stop.Wait(milliseconds); } catch (ObjectDisposedException) { return true; }
    }

    /// <summary>
    /// Run <paramref name="action"/> on a throwaway background thread and wait up to
    /// <paramref name="timeoutMs"/>. Returns whether it finished and any exception it threw. This is
    /// the guard around <c>SerialPort.Open()/Close()</c>, which can block for a long time when the
    /// device is surprise-removed — if it wedges we abandon that thread (it unblocks once the USB
    /// stack finishes tearing the device down) and let the supervisor get on with reconnecting.
    /// </summary>
    private static (bool completed, Exception? error) Guard(Action action, int timeoutMs)
    {
        Exception? error = null;
        var done = new ManualResetEventSlim(false);
        new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        })
        { IsBackground = true, Name = "LP100A-io" }.Start();
        return (done.Wait(timeoutMs), error);
    }

    /// <summary>
    /// Open one port under the <see cref="Guard"/> watchdog, with an explicit ownership handoff so a
    /// slow open can't orphan the handle. If the native <c>Open()</c> outruns the timeout the caller
    /// abandons that thread — but the open may still succeed a moment later, and the resulting port
    /// would then be held by nobody: no field references it, so only the finalizer would ever close
    /// it, and the next reconnect attempt can hit a self-inflicted "port in use." So the opener and
    /// the supervisor race for a single atomic claim, and whichever side loses it closes the port.
    /// </summary>
    private static (bool completed, Exception? error, SerialPort? port) OpenGuarded(string portName)
    {
        SerialPort? handoff = null;
        var claim = 0;   // 0 = unclaimed, 1 = opener published it, 2 = caller abandoned the open

        var (completed, error) = Guard(() =>
        {
            var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 200,
                WriteTimeout = 500,
                Encoding = Encoding.ASCII,
            };
            try { port.Open(); }
            catch { port.Dispose(); throw; }   // failed open: nothing to hand off, don't leak the object

            // Publish before claiming: if our claim loses, the caller has already seen `handoff` (its
            // own interlocked op fences the read) and closes it; if it wins, the caller never looks.
            handoff = port;
            if (Interlocked.CompareExchange(ref claim, 1, 0) == 0) return;

            // The caller gave up on us. Nobody is watching this port, so close it here — this thread
            // is already abandoned, so blocking on a removed device's Close() costs nothing.
            handoff = null;
            CloseQuietly(port);
        }, OpenTimeoutMs);

        if (completed) return (true, error, handoff);

        // Timed out. Take the claim so a late-completing open cleans up after itself; if the opener
        // beat us to it, the port is ours and we close it — we've already blown the watchdog budget,
        // so let the supervisor back off and start a fresh session rather than use it.
        if (Interlocked.CompareExchange(ref claim, 2, 0) != 0 && handoff is { } late)
            Guard(() => CloseQuietly(late), CloseTimeoutMs);

        return (false, error, null);
    }

    /// <summary>Close and dispose a port, swallowing anything it throws. Can block if the device is gone.</summary>
    private static void CloseQuietly(SerialPort port)
    {
        try { if (port.IsOpen) port.Close(); } catch { /* ignore */ }
        try { port.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>One connected session: open, poll until the link drops or we're stopped, then close.</summary>
    private void RunSession(string portName)
    {
        _linkFaulted = false;
        var health = new LinkHealth(Math.Max(1, SilenceTimeoutMs / PollIntervalMs));
        var isLinux = !OperatingSystem.IsWindows();

        // Open under a watchdog: a healthy adapter opens in well under a second, but a stale/removed
        // node can block the native call — bound it so a bad port never stalls the reconnect loop.
        var (opened, openError, port) = OpenGuarded(portName);

        if (!opened)
        {
            if (_running) Report($"{portName} not responding — retrying…", true);
            return;   // abandon the wedged open thread; supervisor backs off and retries
        }
        if (openError is not null)
        {
            if (_running) Report(SerialErrors.Describe(openError, portName, isLinux, _everConnected), true);
            return;
        }
        if (port is null) return;   // completed without error but no port (shouldn't happen); retry
        _port = port;

        try
        {
            _framer.Reset();   // a part-frame from the previous session must not glue onto this one
            try { port.DiscardInBuffer(); } catch { /* non-fatal */ }
            _everConnected = true;
            Report($"Connected on {portName}", false);

            var poll = new byte[] { (byte)'P' };
            var buffer = new byte[512];
            var one = new byte[1];
            var nextPoll = DateTime.UtcNow;
            var framesThisCycle = 0;

            while (_running && !_linkFaulted && !health.IsLost)
            {
                // Send any queued control commands ('F'/'A') before polling, so the write never
                // overlaps a 'P' and the meter has time to act on it.
                var sentCommand = false;
                while (_outbox.TryDequeue(out var cmd))
                {
                    one[0] = cmd;
                    port.Write(one, 0, 1);
                    Thread.Sleep(20);   // settle time for the meter to process the command
                    sentCommand = true;
                }

                var now = DateTime.UtcNow;
                // After a control command, poll straight away so the new state reads back
                // immediately instead of waiting up to a full poll interval.
                if (sentCommand) nextPoll = now;
                if (now >= nextPoll)
                {
                    // One poll interval has closed: judge the previous one before opening the next.
                    health.RecordCycle(framesThisCycle > 0);
                    framesThisCycle = 0;
                    port.Write(poll, 0, poll.Length);
                    nextPoll = now.AddMilliseconds(PollIntervalMs);
                }

                var available = port.BytesToRead;
                if (available > 0)
                {
                    var n = port.Read(buffer, 0, Math.Min(available, buffer.Length));
                    var text = Encoding.ASCII.GetString(buffer, 0, n);
                    foreach (var body in _framer.Feed(text))
                    {
                        if (FrameParser.TryParse(body, out var reading))
                        {
                            framesThisCycle++;
                            ReadingReceived?.Invoke(reading);
                        }
                    }
                }
                else if (WaitForStop(5))
                {
                    break;
                }
            }

            if (_running && (health.IsLost || _linkFaulted))
                Report($"{portName} lost — reconnecting…", true);
        }
        catch (Exception ex) when (_running)
        {
            _linkFaulted = true;
            Report(SerialErrors.Describe(ex, portName, isLinux, _everConnected), true);
        }
        finally
        {
            ClosePort();   // always release the handle — a dropped device must not leave one dangling
        }
    }

    private void ClosePort()
    {
        var port = _port;
        _port = null;
        if (port is null) return;
        // A removed device can wedge the native Close(); bound it so shutdown and reconnect stay responsive.
        Guard(() => CloseQuietly(port), CloseTimeoutMs);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _stop.Dispose();
    }
}
