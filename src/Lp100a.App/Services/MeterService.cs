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
    private readonly SerialReader _reader = new();

    public Lp100Reading? Current { get; private set; }
    public bool IsConnected { get; private set; }
    public string? CurrentPort { get; private set; }
    public string Status { get; private set; } = "Disconnected";
    public bool StatusIsError { get; private set; }

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
            Current = r;
            ReadingReceived?.Invoke(r);
        });

        _reader.StatusChanged += (msg, isError) => Dispatcher.UIThread.Post(() =>
        {
            Status = msg;
            StatusIsError = isError;
            if (isError) IsConnected = false;
            StateChanged?.Invoke();
        });
    }

    public static string[] GetPortNames() => SerialReader.GetPortNames();

    public void Connect(string port)
    {
        CurrentPort = port;
        Status = $"Connecting {port}…";
        StatusIsError = false;
        IsConnected = true;
        _reader.Start(port);
        StateChanged?.Invoke();
    }

    public void Disconnect()
    {
        _reader.Stop();
        IsConnected = false;
        Current = null;
        Status = "Disconnected";
        StatusIsError = false;
        StateChanged?.Invoke();
    }

    public void Dispose() => _reader.Dispose();
}
