using Avalonia.Media;
using Lp100a.App.Services;
using Lp100a.App.Settings;
using Lp100a.Core;

namespace Lp100a.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const double TxHangSeconds = 2.0;
    private static readonly double[] BarSteps = { 5, 25, 100, 250, 1000, 2000 };

    private readonly MeterService _meter;

    // TX / peak tracking.
    private bool _txActive;
    private DateTime _txStart;
    private DateTime _txLast;
    private double _sessionPeak;
    private double _heldPeak;
    private DateTime _heldPeakAt;

    public MainWindowViewModel(MeterService meter, DisplaySettings display)
    {
        _meter = meter;
        Display = display;

        _meter.ReadingReceived += OnReading;
        _meter.StateChanged += OnStateChanged;
        ResetPeakCommand = new RelayCommand(() => { _sessionPeak = 0; _heldPeak = 0; PowerBarPeak = 0; PeakText = "0.0 W"; });
        OnStateChanged();
    }

    public DisplaySettings Display { get; }
    public RelayCommand ResetPeakCommand { get; }

    public string TitleText => "LP-100A MONITOR";

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

    private IBrush _txBrush = Palette.DimBrush;
    public IBrush TxBrush { get => _txBrush; private set => SetProperty(ref _txBrush, value); }

    private bool _alarmActive;
    public bool AlarmActive { get => _alarmActive; private set => SetProperty(ref _alarmActive, value); }

    private void OnStateChanged()
    {
        StatusText = _meter.Status;
        StatusBrush = _meter.StatusIsError ? Palette.RedBrush
            : _meter.IsConnected ? Palette.GreenBrush : Palette.DimBrush;
        ConnDotBrush = _meter.StatusIsError ? Palette.RedBrush
            : _meter is { IsConnected: true, Current: not null } ? Palette.GreenBrush
            : _meter.IsConnected ? Palette.AmberBrush : Palette.DimBrush;

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

        // Bars.
        PowerBarValue = r.ForwardPowerW;
        PowerBarMax = FitBarMax(Math.Max(_sessionPeak, r.ForwardPowerW));
        SwrBarValue = Math.Min(3.0, r.Swr);

        // Peak-hold marker on the power bar: jump to new peaks, hold ~1.5 s, then ease toward live.
        if (Display.PeakHoldEnabled)
        {
            var f = r.ForwardPowerW;
            if (f >= _heldPeak) { _heldPeak = f; _heldPeakAt = DateTime.Now; }
            else if ((DateTime.Now - _heldPeakAt).TotalSeconds > 1.5)
            {
                _heldPeak -= (_heldPeak - f) * 0.34;
                if (_heldPeak < f) _heldPeak = f;
            }
            PowerBarPeak = _heldPeak;
        }
        else { _heldPeak = 0; PowerBarPeak = 0; }

        // SWR alarm: app-side watch of live SWR against the user threshold, while transmitting.
        AlarmActive = Display.SwrAlarmEnabled && r.IsTransmitting && r.Swr >= (double)Display.SwrAlarmThreshold;

        // Peak forward.
        if (r.ForwardPowerW > _sessionPeak) { _sessionPeak = r.ForwardPowerW; PeakText = $"{_sessionPeak:0.0} W"; }

        TrackTx(r);
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
        ZText = "— Ω"; PhaseText = "—°"; RxText = "—";
        PowerBarValue = 0; PowerBarPeak = 0; _heldPeak = 0; SwrBarValue = 1.0; TxBrush = Palette.DimBrush;
        AlarmActive = false;
        CallsignText = "";
    }
}
