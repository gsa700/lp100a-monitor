using Avalonia.Threading;
using Lp100a.Core;

namespace Lp100a.App.Services;

/// <summary>
/// Single shared owner of the LP-100A connection. Wraps <see cref="SerialReader"/>,
/// marshals its background-thread events onto the UI thread, and re-broadcasts them so
/// any number of views (main meter, vector window) observe one serial connection.
/// </summary>
public sealed class MeterService : IDisposable
{
    private const double StaleAfterSeconds = 2.0;

    private readonly SerialReader _reader = new();
    private readonly DispatcherTimer _watchdog;
    private DateTime _lastReadingUtc;

    public Lp100Reading? Current { get; private set; }
    public bool IsConnected { get; private set; }
    public string? CurrentPort { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public bool StatusIsError { get; private set; }

    /// <summary>True when connected but no frame has arrived recently — the meter is off, the
    /// cable is out, or it's been knocked off its Watts screen, so the readouts are frozen,
    /// not live. A dead serial link often stops delivering frames without raising an error.</summary>
    public bool IsStale { get; private set; }

    /// <summary>Fires on the UI thread for every decoded frame.</summary>
    public event Action<Lp100Reading>? ReadingReceived;

    /// <summary>Fires on the UI thread when connection/status changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when a view (e.g. Setup) asks to clear the session peak-forward.</summary>
    public event Action? PeakResetRequested;
    public void RequestPeakReset() => PeakResetRequested?.Invoke();

    /// <summary>Cycle the meter's Avg → Peak → Tune power mode (sends 'F').</summary>
    public void CyclePowerMode()
    {
        if (IsConnected) _reader.CyclePowerMode();
    }

    /// <summary>Cycle the meter's SWR alarm setpoint OFF → 1.5 → 2.0 → 2.5 → 3.0 → User (sends 'A').</summary>
    public void CycleAlarm()
    {
        if (IsConnected) _reader.CycleAlarm();
    }

    public MeterService()
    {
        _reader.ReadingReceived += r => Dispatcher.UIThread.Post(() =>
        {
            // A frame can still be queued on the UI thread when Disconnect runs; ignore it so
            // it doesn't revive Current/IsStale after we've torn the connection down.
            if (!IsConnected) return;
            _lastReadingUtc = DateTime.UtcNow;
            Current = r;
            if (IsStale) { IsStale = false; StateChanged?.Invoke(); }
            ReadingReceived?.Invoke(r);
        });

        _reader.StatusChanged += (msg, isError) => Dispatcher.UIThread.Post(() =>
        {
            Status = msg;
            StatusIsError = isError;
            if (isError) { IsConnected = false; IsStale = false; }
            StateChanged?.Invoke();
        });

        // Watchdog: flag a connection whose frames have stopped (without a serial error) so the
        // UI can stop implying the frozen values are live.
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _watchdog.Tick += (_, _) =>
        {
            if (!IsConnected || IsStale) return;
            if ((DateTime.UtcNow - _lastReadingUtc).TotalSeconds >= StaleAfterSeconds)
            {
                IsStale = true;
                StateChanged?.Invoke();
            }
        };
        _watchdog.Start();
    }

    public static string[] GetPortNames() => SerialReader.GetPortNames();

    public void Connect(string port)
    {
        CurrentPort = port;
        Status = $"Connecting {port}…";
        StatusIsError = false;
        IsConnected = true;
        IsStale = false;
        _lastReadingUtc = DateTime.UtcNow;   // grace period before the watchdog can flag stale
        _reader.Start(port);
        StateChanged?.Invoke();
    }

    public void Disconnect()
    {
        _reader.Stop();
        IsConnected = false;
        IsStale = false;
        Current = null;
        Status = "Disconnected";
        StatusIsError = false;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _watchdog.Stop();
        _reader.Dispose();
    }
}
