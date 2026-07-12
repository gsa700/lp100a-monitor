using Lp100a.App.ViewModels;

namespace Lp100a.App.Settings;

/// <summary>
/// Shared, observable "what to show" flags. The Setup window toggles them; the main
/// window binds row visibility to them; config persists them. Mirrors W2 Monitor's
/// $show hashtable, extended with the LP-100A-only vector quantities.
/// </summary>
public sealed class DisplaySettings : ViewModelBase
{
    private bool _showStatusLine = true;
    public bool ShowStatusLine { get => _showStatusLine; set => SetProperty(ref _showStatusLine, value); }

    private bool _showPowerBar = true;
    public bool ShowPowerBar { get => _showPowerBar; set => SetProperty(ref _showPowerBar, value); }

    private bool _showSwrBar = true;
    public bool ShowSwrBar { get => _showSwrBar; set => SetProperty(ref _showSwrBar, value); }

    private bool _showReflected = true;
    public bool ShowReflected { get => _showReflected; set => SetProperty(ref _showReflected, value); }

    private bool _showReturnLoss = true;
    public bool ShowReturnLoss { get => _showReturnLoss; set => SetProperty(ref _showReturnLoss, value); }

    private bool _showDbm = true;
    public bool ShowDbm { get => _showDbm; set => SetProperty(ref _showDbm, value); }

    private bool _showPeak = true;
    public bool ShowPeak { get => _showPeak; set => SetProperty(ref _showPeak, value); }

    private bool _showTx = true;
    public bool ShowTx { get => _showTx; set => SetProperty(ref _showTx, value); }

    // LP-100A-only vector rows.
    private bool _showZ;
    public bool ShowZ { get => _showZ; set => SetProperty(ref _showZ, value); }

    private bool _showPhase;
    public bool ShowPhase { get => _showPhase; set => SetProperty(ref _showPhase, value); }

    private bool _showRx = true;   // R + jX — the LP-100A headline, on by default
    public bool ShowRx { get => _showRx; set => SetProperty(ref _showRx, value); }

    private bool _showVectorWindow;
    public bool ShowVectorWindow { get => _showVectorWindow; set => SetProperty(ref _showVectorWindow, value); }

    private bool _alwaysOnTop;
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }

    // --- behavior (not display toggles, but shared+persisted here) ---
    private bool _peakHoldEnabled = true;
    public bool PeakHoldEnabled { get => _peakHoldEnabled; set => SetProperty(ref _peakHoldEnabled, value); }

    private bool _swrAlarmEnabled;
    public bool SwrAlarmEnabled { get => _swrAlarmEnabled; set => SetProperty(ref _swrAlarmEnabled, value); }

    private decimal _swrAlarmThreshold = 2.5m;
    public decimal SwrAlarmThreshold { get => _swrAlarmThreshold; set => SetProperty(ref _swrAlarmThreshold, value); }

    // Seconds the peak-hold marker sits at the peak before it starts to decay.
    private decimal _peakHoldSeconds = 1.5m;
    public decimal PeakHoldSeconds { get => _peakHoldSeconds; set => SetProperty(ref _peakHoldSeconds, value); }
}
