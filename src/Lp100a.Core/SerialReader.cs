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
    private SerialPort? _port;
    private Thread? _thread;
    private volatile bool _running;

    public event Action<Lp100Reading>? ReadingReceived;
    public event Action<string, bool>? StatusChanged;  // (message, isError)

    public bool IsRunning => _running;

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Start(string portName)
    {
        Stop();
        _framer.Reset();
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

            while (_running)
            {
                var now = DateTime.UtcNow;
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
