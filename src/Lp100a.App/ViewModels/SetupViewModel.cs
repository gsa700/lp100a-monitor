using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
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
        CheckUpdatesCommand = new RelayCommand(() => _ = CheckUpdatesAsync(), () => !_updateBusy);
        UpdateNowCommand = new RelayCommand(() => _ = UpdateNowAsync(), () => _updateInfo?.AssetUrl is not null && !_updateBusy);
        OpenReleaseCommand = new RelayCommand(OpenRelease);
        UpdateStatus = $"You have {UpdateService.CurrentVersion}.";
        RefreshPorts();
        OnStateChanged();
    }

    public DisplaySettings Display { get; }
    public ObservableCollection<string> Ports { get; } = new();
    public RelayCommand ConnectCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CheckUpdatesCommand { get; }
    public RelayCommand UpdateNowCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }

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

    // --- updates ---
    private UpdateInfo? _updateInfo;
    private string? _stagedExe;
    private bool _updateBusy;

    private bool _checkUpdatesAtStartup;
    public bool CheckUpdatesAtStartup { get => _checkUpdatesAtStartup; set => SetProperty(ref _checkUpdatesAtStartup, value); }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    private IBrush _updateStatusBrush = Palette.DimBrush;
    public IBrush UpdateStatusBrush { get => _updateStatusBrush; private set => SetProperty(ref _updateStatusBrush, value); }

    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; private set => SetProperty(ref _updateAvailable, value); }

    public async Task CheckUpdatesAsync()
    {
        _updateBusy = true; CheckUpdatesCommand.RaiseCanExecuteChanged(); UpdateNowCommand.RaiseCanExecuteChanged();
        UpdateStatus = "Checking for updates…"; UpdateStatusBrush = Palette.DimBrush;

        var info = await UpdateService.CheckAsync();
        _updateInfo = info;
        if (info.Error is not null)
        {
            UpdateStatus = $"Update check failed: {info.Error}"; UpdateStatusBrush = Palette.RedBrush; UpdateAvailable = false;
        }
        else if (info.UpdateAvailable && info.AssetUrl is not null)
        {
            UpdateStatus = $"Update available: {info.LatestTag} (you have {info.CurrentVersion})."; UpdateStatusBrush = Palette.GreenBrush; UpdateAvailable = true;
        }
        else if (info.UpdateAvailable)
        {
            UpdateStatus = $"{info.LatestTag} is available, but has no build for this platform."; UpdateStatusBrush = Palette.AmberBrush; UpdateAvailable = false;
        }
        else
        {
            UpdateStatus = $"Up to date ({info.CurrentVersion})."; UpdateStatusBrush = Palette.GreenBrush; UpdateAvailable = false;
        }

        _updateBusy = false; CheckUpdatesCommand.RaiseCanExecuteChanged(); UpdateNowCommand.RaiseCanExecuteChanged();
    }

    private async Task UpdateNowAsync()
    {
        if (_updateInfo?.AssetUrl is not { } url) return;
        _updateBusy = true; UpdateNowCommand.RaiseCanExecuteChanged(); CheckUpdatesCommand.RaiseCanExecuteChanged();
        try
        {
            UpdateStatus = "Downloading update…"; UpdateStatusBrush = Palette.DimBrush;
            _stagedExe = await UpdateService.DownloadAndStageAsync(url);
            UpdateStatus = "Update ready — restarting to apply…"; UpdateStatusBrush = Palette.GreenBrush;
            UpdateService.ApplyAndRestart(_stagedExe);
            (Application.Current as App)?.ExitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update failed: {ex.Message}"; UpdateStatusBrush = Palette.RedBrush;
            _updateBusy = false; UpdateNowCommand.RaiseCanExecuteChanged(); CheckUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenRelease()
    {
        var url = _updateInfo?.ReleaseUrl ?? $"https://github.com/{UpdateService.Repo}/releases/latest";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
