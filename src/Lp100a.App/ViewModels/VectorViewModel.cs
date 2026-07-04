using Lp100a.App.Services;
using Lp100a.Core;

namespace Lp100a.App.ViewModels;

/// <summary>Drives the Vector (Smith chart) window from the shared meter feed.</summary>
public sealed class VectorViewModel : ViewModelBase
{
    public VectorViewModel(MeterService meter)
    {
        meter.ReadingReceived += OnReading;
    }

    private double _resistance = 50.0;
    public double Resistance { get => _resistance; private set => SetProperty(ref _resistance, value); }

    private double _reactance;
    public double Reactance { get => _reactance; private set => SetProperty(ref _reactance, value); }

    private string _rxText = "—";
    public string RxText { get => _rxText; private set => SetProperty(ref _rxText, value); }

    private string _zText = "—";
    public string ZText { get => _zText; private set => SetProperty(ref _zText, value); }

    private string _swrText = "SWR —";
    public string SwrText { get => _swrText; private set => SetProperty(ref _swrText, value); }

    private void OnReading(Lp100Reading r)
    {
        Resistance = r.ResistanceOhms;
        Reactance = r.ReactanceOhms;
        RxText = $"{r.ResistanceOhms:0.0} {(r.ReactanceOhms >= 0 ? "+" : "−")} j{Math.Abs(r.ReactanceOhms):0.0} Ω";
        ZText = $"{r.ZOhms:0.0} Ω  ∠ {r.PhaseDeg:+0.0;-0.0}°";
        SwrText = $"SWR {r.Swr:0.00}";
    }
}
