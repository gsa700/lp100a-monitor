using Avalonia.Media;
using Lp100a.App.Services;
using Lp100a.App.Settings;
using Lp100a.Core;

namespace Lp100a.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const double TxHangSeconds = 2.0;
    private static readonly double[] BarSteps = { 5, 10, 25, 50, 100, 150, 250, 400, 600, 1000, 1500, 2500, 3000 };

    private readonly MeterService _meter;

    // TX / peak tracking.
    private bool _txActive;
    private DateTime _txStart;
    private DateTime _txLast;
    private double _sessionPeak;
    private double _heldPeak;
    private DateTime _heldPeakAt;
    private double _barRef;   // decaying reference that drives the bar's auto-range full-scale

    public MainWindowViewModel(MeterService meter, DisplaySettings display)
    {
        _meter = meter;
        Display = display;

        Display.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DisplaySettings.ShowSwrBar))
                OnPropertyChanged(nameof(SwrBarVisible));
        };

        _meter.ReadingReceived += OnReading;
        _meter.StateChanged += OnStateChanged;
        _meter.PeakResetRequested += ResetPeak;
        ResetPeakCommand = new RelayCommand(ResetPeak);
        CyclePowerModeCommand = new RelayCommand(_meter.CyclePowerMode, () => _meter.IsConnected);
        CycleAlarmCommand = new RelayCommand(_meter.CycleAlarm, () => _meter.IsConnected);
        OnStateChanged();
    }

    public DisplaySettings Display { get; }
    public RelayCommand ResetPeakCommand { get; }
    public RelayCommand CyclePowerModeCommand { get; }
    public RelayCommand CycleAlarmCommand { get; }

    public string TitleText => "LP-100A MONITOR";

    /// <summary>Dim the readouts when the feed goes stale so the frozen values don't read as live.</summary>
    public double ReadoutOpacity => _meter is { IsConnected: true, IsStale: true } ? 0.4 : 1.0;

    // --- status / header ---
    private string _statusText = "Disconnected";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _statusBrush = Palette.DimBrush;
    public IBrush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    private IBrush _connDotBrush = Palette.DimBrush;
    public IBrush ConnDotBrush { get => _connDotBrush; private set => SetProperty(ref _connDotBrush, value); }

    private string _callsignText = "";
    public string CallsignText { get => _callsignText; private set => SetProperty(ref _callsignText, value); }

    // --- hero readouts ---
    private string _powerText = "— W";
    public string PowerText { get => _powerText; private set => SetProperty(ref _powerText, value); }

    private string _swrText = "—";
    public string SwrText { get => _swrText; private set => SetProperty(ref _swrText, value); }

    private IBrush _swrBrush = Palette.DimBrush;
    public IBrush SwrBrush { get => _swrBrush; private set => SetProperty(ref _swrBrush, value); }

    // --- bars ---
    private double _powerBarValue;
    public double PowerBarValue { get => _powerBarValue; private set => SetProperty(ref _powerBarValue, value); }

    private double _powerBarMax = 5;
    public double PowerBarMax { get => _powerBarMax; private set => SetProperty(ref _powerBarMax, value); }

    private double _powerBarPeak;
    public double PowerBarPeak { get => _powerBarPeak; private set => SetProperty(ref _powerBarPeak, value); }

    private double _swrBarValue = 1.0;
    public double SwrBarValue { get => _swrBarValue; private set => SetProperty(ref _swrBarValue, value); }

    // --- secondary rows ---
    private string _reflectedText = "— W";
    public string ReflectedText { get => _reflectedText; private set => SetProperty(ref _reflectedText, value); }

    private string _returnLossText = "— dB";
    public string ReturnLossText { get => _returnLossText; private set => SetProperty(ref _returnLossText, value); }

    private string _dbmText = "— dBm";
    public string DbmText { get => _dbmText; private set => SetProperty(ref _dbmText, value); }

    private string _peakText = "0.0 W";
    public string PeakText { get => _peakText; private set => SetProperty(ref _peakText, value); }

    private string _zText = "— Ω";
    public string ZText { get => _zText; private set => SetProperty(ref _zText, value); }

    private string _phaseText = "—°";
    public string PhaseText { get => _phaseText; private set => SetProperty(ref _phaseText, value); }

    private string _rxText = "—";
    public string RxText { get => _rxText; private set => SetProperty(ref _rxText, value); }

    private string _txTimerText = "0:00";
    public string TxTimerText { get => _txTimerText; private set => SetProperty(ref _txTimerText, value); }

    private string _meterModeText = "—";
    public string MeterModeText { get => _meterModeText; private set => SetProperty(ref _meterModeText, value); }

    private string _alarmSetpointText = "—";
    public string AlarmSetpointText { get => _alarmSetpointText; private set => SetProperty(ref _alarmSetpointText, value); }

    private IBrush _txBrush = Palette.DimBrush;
    public IBrush TxBrush { get => _txBrush; private set => SetProperty(ref _txBrush, value); }

    private bool _alarmActive;
    public bool AlarmActive
    {
        get => _alarmActive;
        private set
        {
            if (SetProperty(ref _alarmActive, value))
                OnPropertyChanged(nameof(SwrBarVisible));
        }
    }

    // The alarm text embedded in the SWR bar when it trips.
    private string _alarmText = "HIGH SWR";
    public string AlarmText { get => _alarmText; private set => SetProperty(ref _alarmText, value); }

    // Meter alarm setpoint (numeric; 0 for Off/User) — anchors where the SWR bar's colours break.
    private double _swrAlarmSetpoint;
    public double SwrAlarmSetpoint { get => _swrAlarmSetpoint; private set => SetProperty(ref _swrAlarmSetpoint, value); }

    // Keep the bar (and thus the alarm) visible whenever it's tripped, even if the user has
    // the SWR bar toggled off.
    public bool SwrBarVisible => Display.ShowSwrBar || _alarmActive;

    private void OnStateChanged()
    {
        var stale = _meter is { IsConnected: true, IsStale: true };
        StatusText = stale ? $"{_meter.Status} — no data (check the meter)" : _meter.Status;
        StatusBrush = _meter.StatusIsError ? Palette.RedBrush
            : stale ? Palette.AmberBrush
            : _meter.IsConnected ? Palette.GreenBrush : Palette.DimBrush;
        ConnDotBrush = _meter.StatusIsError ? Palette.RedBrush
            : _meter is { IsConnected: true, IsStale: false, Current: not null } ? Palette.GreenBrush
            : _meter.IsConnected ? Palette.AmberBrush : Palette.DimBrush;
        OnPropertyChanged(nameof(ReadoutOpacity));

        CyclePowerModeCommand.RaiseCanExecuteChanged();
        CycleAlarmCommand.RaiseCanExecuteChanged();
        if (!_meter.IsConnected) BlankReadouts();
    }

    private void OnReading(Lp100Reading r)
    {
        ConnDotBrush = Palette.GreenBrush;
        CallsignText = string.IsNullOrWhiteSpace(r.Callsign) ? "" : r.Callsign;

        PowerText = $"{r.ForwardPowerW:0.0} W";
        SwrText = $"{r.Swr:0.00}";
        SwrBrush = r.Swr < 1.5 ? Palette.GreenBrush : r.Swr < 2.0 ? Palette.AmberBrush : Palette.RedBrush;

        ReflectedText = $"{ReflectedPower(r):0.00} W";
        ReturnLossText = $"{r.ReturnLossDb:0.0} dB";
        DbmText = $"{r.Dbm:0.0} dBm";
        ZText = $"{r.ZOhms:0.0} Ω";
        PhaseText = $"{r.PhaseDeg:+0.0;-0.0}°";
        RxText = $"{r.ResistanceOhms:0.0} {(r.ReactanceOhms >= 0 ? "+" : "−")} j{Math.Abs(r.ReactanceOhms):0.0} Ω";
        MeterModeText = r.MeterModeText;
        AlarmSetpointText = r.AlarmSetpointText;

        // Peak-hold marker (computed first so the bar scale can honor it): jump to new peaks,
        // hold for the configured decay time, then ease toward live.
        if (Display.PeakHoldEnabled)
        {
            var f = r.ForwardPowerW;
            if (f >= _heldPeak) { _heldPeak = f; _heldPeakAt = DateTime.Now; }
            else if ((DateTime.Now - _heldPeakAt).TotalSeconds > (double)Display.PeakHoldSeconds)
            {
                _heldPeak -= (_heldPeak - f) * 0.34;
                if (_heldPeak < f) _heldPeak = f;
            }
            PowerBarPeak = _heldPeak;
        }
        else { _heldPeak = 0; PowerBarPeak = 0; }

        // Bars. Full-scale jumps up to fit, then HOLDS while the peak marker is elevated — so the
        // marker slides down a fixed scale (analog peak-hold feel) — and only decays to refit live
        // power once the peak has eased. Decaying while the peak is up collapses the scale under the
        // marker, which makes the fill/marker look stuck near the top.
        PowerBarValue = r.ForwardPowerW;
        var barTarget = Math.Max(r.ForwardPowerW, _heldPeak);
        if (barTarget >= _barRef) _barRef = barTarget;
        else if (_heldPeak <= r.ForwardPowerW + 0.5) _barRef -= (_barRef - barTarget) * 0.06;
        // ~40% headroom so the peak sits ~70% up the bar instead of jammed at the top.
        PowerBarMax = FitBarMax(_barRef / 0.7);
        SwrBarValue = Math.Min(3.0, r.Swr);

        // HIGH SWR alarm, embedded in the SWR bar: a visual echo of the meter's own alarm. Uses
        // the meter's setpoint (field [3]) as the single threshold. OFF/User send no numeric over
        // serial, so it can't fire there (the meter's hardware alarm/relay still works).
        AlarmText = $"HIGH SWR {r.Swr:0.0}";
        SwrAlarmSetpoint = r.AlarmThreshold ?? 0.0;
        AlarmActive = Display.SwrBannerEnabled && r.IsTransmitting
                      && r.AlarmThreshold is double trip && r.Swr >= trip;

        // Peak forward.
        if (r.ForwardPowerW > _sessionPeak) { _sessionPeak = r.ForwardPowerW; PeakText = $"{_sessionPeak:0.0} W"; }

        TrackTx(r);
    }

    private void ResetPeak()
    {
        _sessionPeak = 0;
        _heldPeak = 0;
        _barRef = 0;
        PowerBarPeak = 0;
        PeakText = "0.0 W";
    }

    private static double ReflectedPower(Lp100Reading r)
    {
        // Pr = Pf · |Γ|²,  |Γ| = (SWR-1)/(SWR+1)
        if (r.Swr <= 1.0) return 0.0;
        var g = (r.Swr - 1.0) / (r.Swr + 1.0);
        return r.ForwardPowerW * g * g;
    }

    private void TrackTx(Lp100Reading r)
    {
        var now = DateTime.Now;
        if (r.IsTransmitting)
        {
            if (!_txActive) { _txActive = true; _txStart = now; }
            _txLast = now;
            TxBrush = Palette.AmberBrush;
        }
        else if (_txActive && (now - _txLast).TotalSeconds > TxHangSeconds)
        {
            _txActive = false;
            TxBrush = Palette.DimBrush;
        }

        var span = _txActive ? now - _txStart : _txLast - _txStart;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        TxTimerText = $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private static double FitBarMax(double peak)
    {
        foreach (var step in BarSteps)
            if (peak <= step) return step;
        return BarSteps[^1];
    }

    private void BlankReadouts()
    {
        PowerText = "— W"; SwrText = "—"; SwrBrush = Palette.DimBrush;
        ReflectedText = "— W"; ReturnLossText = "— dB"; DbmText = "— dBm";
        ZText = "— Ω"; PhaseText = "—°"; RxText = "—"; MeterModeText = "—"; AlarmSetpointText = "—";
        SwrAlarmSetpoint = 0;
        PowerBarValue = 0; PowerBarPeak = 0; _heldPeak = 0; _barRef = 0; SwrBarValue = 1.0; TxBrush = Palette.DimBrush;
        AlarmActive = false;
        CallsignText = "";
    }
}
