using System.Net.Sockets;
using System.Text;

namespace Lp100a.Core;

/// <summary>
/// Frequency source backed by a Hamlib <c>rigctld</c> daemon over TCP.
///
/// This is the portable path: Hamlib supports virtually every rig, and rigctld is explicitly a
/// *sharing* daemon — it owns the physical CAT port and serves many clients — so the monitor never
/// contends for a serial port another app (logger, SmartSDR) already holds. Wire-format handling
/// lives in <see cref="RigctldProtocol"/>; this class is just the socket and the poll loop.
///
/// Polls on a background thread and reconnects on its own, mirroring <see cref="SerialReader"/>'s
/// lifecycle. <see cref="Changed"/> fires off the UI thread — subscribers must marshal.
/// </summary>
public sealed class RigctldFrequencySource : IFrequencySource
{
    private const int ConnectTimeoutMs = 1500;
    private const int ReadTimeoutMs = 600;
    private const int ReconnectDelayMs = 2000;

    private readonly string _host;
    private readonly int _port;
    private readonly int _pollMs;
    private readonly object _lock = new();

    private Thread? _thread;
    private volatile bool _running;

    private double? _freqMhz;
    private bool? _transmitting;
    private bool _connected;
    private string _status = "Not started.";
    private bool _statusIsError;

    /// <param name="host">rigctld host name or address.</param>
    /// <param name="port">rigctld port (Hamlib default 4532).</param>
    /// <param name="pollMs">Poll interval. 400 ms matches the W2's proven rigctld cadence — fast
    /// enough to catch a band change mid-QSO without hammering the daemon.</param>
    public RigctldFrequencySource(string host, int port = RigctldProtocol.DefaultPort, int pollMs = 400)
    {
        _host = host;
        _port = port;
        _pollMs = Math.Max(100, pollMs);
    }

    /// <summary>Build a source from a "host" or "host:port" string. Returns null if unparseable.</summary>
    public static RigctldFrequencySource? FromEndpoint(string? endpoint, int pollMs = 400) =>
        RigctldProtocol.TryParseEndpoint(endpoint, out var host, out var port)
            ? new RigctldFrequencySource(host, port, pollMs)
            : null;

    public string Name => $"rigctld {_host}:{_port}";

    public bool IsConnected { get { lock (_lock) return _connected; } }
    public string Status { get { lock (_lock) return _status; } }
    public bool StatusIsError { get { lock (_lock) return _statusIsError; } }
    public double? FreqMhz { get { lock (_lock) return _freqMhz; } }
    public bool? IsTransmitting { get { lock (_lock) return _transmitting; } }

    public event Action? Changed;

    public void Start()
    {
        Stop();
        _running = true;
        SetStatus($"Connecting to {_host}:{_port}…", isError: false, connected: false);
        _thread = new Thread(Loop) { IsBackground = true, Name = $"rigctld-{_host}" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(1000); } catch { /* ignore */ }
        _thread = null;
        SetReadings(null, null);
        SetStatus("Stopped.", isError: false, connected: false);
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                using var client = new TcpClient();
                if (!client.ConnectAsync(_host, _port).Wait(ConnectTimeoutMs) || !client.Connected)
                    throw new SocketException((int)SocketError.TimedOut);

                client.NoDelay = true;
                using var stream = client.GetStream();
                stream.ReadTimeout = ReadTimeoutMs;
                stream.WriteTimeout = ReadTimeoutMs;
                SetStatus($"Connected to {_host}:{_port}", isError: false, connected: true);

                while (_running)
                {
                    var freq = RigctldProtocol.TryParseFrequencyMhz(Query(stream, RigctldProtocol.FrequencyCommand), out var mhz)
                        ? mhz
                        : (double?)null;

                    // A backend that can't report PTT answers RPRT -n; that stays null ("unknown"),
                    // never false, so callers can't mistake it for "definitely receiving".
                    var tx = RigctldProtocol.TryParsePtt(Query(stream, RigctldProtocol.PttCommand), out var keyed)
                        ? keyed
                        : (bool?)null;

                    SetReadings(freq, tx);
                    Sleep(_pollMs);
                }
            }
            catch (Exception ex) when (_running)
            {
                SetReadings(null, null);
                SetStatus($"{_host}:{_port} — {Describe(ex)}", isError: true, connected: false);
                Sleep(ReconnectDelayMs);
            }
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        AggregateException agg when agg.InnerException is not null => Describe(agg.InnerException),
        SocketException { SocketErrorCode: SocketError.TimedOut } => "connect timed out (is rigctld running?)",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "connection refused (is rigctld running?)",
        _ => ex.Message,
    };

    private static string Query(NetworkStream stream, string command)
    {
        var payload = Encoding.ASCII.GetBytes(command);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();

        var sb = new StringBuilder();
        var buffer = new byte[256];
        var deadline = DateTime.UtcNow.AddMilliseconds(ReadTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (!stream.DataAvailable)
            {
                Thread.Sleep(5);
                continue;
            }
            var n = stream.Read(buffer, 0, buffer.Length);
            if (n <= 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
            if (sb.ToString().Contains('\n')) break;
        }
        return sb.ToString();
    }

    /// <summary>Interruptible sleep so <see cref="Stop"/> doesn't wait out a full interval.</summary>
    private void Sleep(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (_running && DateTime.UtcNow < end) Thread.Sleep(20);
    }

    private void SetReadings(double? freqMhz, bool? transmitting)
    {
        bool changed;
        lock (_lock)
        {
            changed = _freqMhz != freqMhz || _transmitting != transmitting;
            _freqMhz = freqMhz;
            _transmitting = transmitting;
        }
        if (changed) Changed?.Invoke();
    }

    private void SetStatus(string status, bool isError, bool connected)
    {
        bool changed;
        lock (_lock)
        {
            changed = _status != status || _statusIsError != isError || _connected != connected;
            _status = status;
            _statusIsError = isError;
            _connected = connected;
        }
        if (changed) Changed?.Invoke();
    }

    public void Dispose() => Stop();
}
