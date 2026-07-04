using System.Collections.ObjectModel;
using Avalonia.Media;
using Lp100a.App.Services;
using Lp100a.App.Settings;

namespace Lp100a.App.ViewModels;

public sealed class SetupViewModel : ViewModelBase
{
    private readonly MeterService _meter;

    public SetupViewModel(MeterService meter, DisplaySettings display)
    {
        _meter = meter;
        Display = display;
        _meter.StateChanged += OnStateChanged;

        ConnectCommand = new RelayCommand(ToggleConnect, () => IsConnected || SelectedPort is not null);
        RefreshCommand = new RelayCommand(RefreshPorts);
        RefreshPorts();
        OnStateChanged();
    }

    public DisplaySettings Display { get; }
    public ObservableCollection<string> Ports { get; } = new();
    public RelayCommand ConnectCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set { if (SetProperty(ref _selectedPort, value)) ConnectCommand.RaiseCanExecuteChanged(); }
    }

    public bool IsConnected => _meter.IsConnected;
    public string ConnectLabel => IsConnected ? "Disconnect" : "Connect";

    private string _statusText = "Disconnected.";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _statusBrush = Palette.DimBrush;
    public IBrush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    /// <summary>Pre-select a saved port (called at startup by App).</summary>
    public void SelectPort(string? port)
    {
        if (port is not null && Ports.Contains(port)) SelectedPort = port;
    }

    private void ToggleConnect()
    {
        if (IsConnected) _meter.Disconnect();
        else if (SelectedPort is { } port) _meter.Connect(port);
    }

    private void RefreshPorts()
    {
        var current = SelectedPort;
        Ports.Clear();
        foreach (var p in MeterService.GetPortNames().OrderBy(x => x))
            Ports.Add(p);
        SelectedPort = current is not null && Ports.Contains(current) ? current : Ports.FirstOrDefault();
    }

    private void OnStateChanged()
    {
        StatusText = _meter.Status;
        StatusBrush = _meter.StatusIsError ? Palette.RedBrush
            : _meter.IsConnected ? Palette.GreenBrush : Palette.DimBrush;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectLabel));
        ConnectCommand.RaiseCanExecuteChanged();
    }
}
