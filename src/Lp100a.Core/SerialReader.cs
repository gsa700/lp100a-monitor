using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;

namespace Lp100a.Core;

/// <summary>
/// Opens an LP-100A serial port (115200 8N1, no flow control), polls it with "P",
/// and raises <see cref="ReadingReceived"/> for every decoded frame. UI-agnostic:
/// events fire on a background thread, so subscribers must marshal to their UI thread.
/// </summary>
public sealed class SerialReader : IDisposable
{
    private const int BaudRate = 115200;
    private const int PollIntervalMs = 80;   // matches the LP-100A VCP default (~12 samples/s)

    private readonly StreamFramer _framer = new();
    private readonly ConcurrentQueue<byte> _outbox = new();  // control commands to send on the poll thread
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;

    public event Action<Lp100Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

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

    public void Start(string portName)
    {
        Stop();
        _framer.Reset();
        _outbox.Clear();
        _running = true;
        _thread = new Thread(() => Loop(portName))
        {
            IsBackground = true,
            Name = $"LP100A-{portName}",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(500); } catch { /* ignore */ }
        _thread = null;
        ClosePort();
    }

    private void Loop(string portName)
    {
        try
        {
            _port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 200,
                WriteTimeout = 500,
                Encoding = Encoding.ASCII,
            };
            _port.Open();
            _port.DiscardInBuffer();
            StatusChanged?.Invoke($"Connected on {portName}", false);

            var poll = new byte[] { (byte)'P' };
            var buffer = new byte[512];
            var nextPoll = DateTime.UtcNow;

            var one = new byte[1];
            while (_running)
            {
                // Send any queued control commands ('F'/'A') before polling, so the write never
                // overlaps a 'P' and the meter has time to act on it.
                var sentCommand = false;
                while (_outbox.TryDequeue(out var cmd))
                {
                    one[0] = cmd;
                    _port.Write(one, 0, 1);
                    Thread.Sleep(20);   // settle time for the meter to process the command
                    sentCommand = true;
                }

                var now = DateTime.UtcNow;
                // After a control command, poll straight away so the new state reads back
                // immediately instead of waiting up to a full poll interval.
                if (sentCommand) nextPoll = now;
                if (now >= nextPoll)
                {
                    _port.Write(poll, 0, poll.Length);
                    nextPoll = now.AddMilliseconds(PollIntervalMs);
                }

                var available = _port.BytesToRead;
                if (available > 0)
                {
                    var n = _port.Read(buffer, 0, Math.Min(available, buffer.Length));
                    var text = Encoding.ASCII.GetString(buffer, 0, n);
                    foreach (var body in _framer.Feed(text))
                    {
                        if (FrameParser.TryParse(body, out var reading))
                            ReadingReceived?.Invoke(reading);
                    }
                }
                else
                {
                    Thread.Sleep(5);
                }
            }
        }
        catch (Exception ex) when (_running)
        {
            StatusChanged?.Invoke($"Error: {ex.Message}", true);
        }
        finally
        {
            ClosePort();
            if (!_running) StatusChanged?.Invoke("Disconnected", false);
        }
    }

    private void ClosePort()
    {
        try
        {
            if (_port is { IsOpen: true }) _port.Close();
        }
        catch { /* ignore */ }
        finally
        {
            _port?.Dispose();
            _port = null;
        }
    }

    public void Dispose() => Stop();
}
