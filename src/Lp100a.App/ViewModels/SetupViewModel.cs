using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Lp100a.App.Services;
using Lp100a.App.Settings;
using Lp100a.Core;

namespace Lp100a.App.ViewModels;

public sealed class SetupViewModel : ViewModelBase
{
    private readonly MeterService _meter;
    private readonly TxLoggingService _logging;
    private readonly FrequencyService _frequency;

    public SetupViewModel(MeterService meter, DisplaySettings display, TxLoggingService logging,
        FrequencyService frequency)
    {
        _meter = meter;
        Display = display;
        _logging = logging;
        _frequency = frequency;
        _meter.StateChanged += OnStateChanged;
        _meter.ReadingReceived += OnReading;
        _logging.Changed += OnLogChanged;
        _frequency.Changed += OnFrequencyChanged;
        _rigctldEnabled = frequency.Enabled;
        _rigctldEndpoint = frequency.Endpoint;

        ConnectCommand = new RelayCommand(ToggleConnect, () => IsConnected || SelectedPort is not null);
        RefreshCommand = new RelayCommand(RefreshPorts);
        UpdateCommand = new RelayCommand(() => _ = UpdateButtonAsync(), () => !_updateBusy);
        OpenReleaseCommand = new RelayCommand(OpenRelease);
        OpenLogCommand = new RelayCommand(OpenLog);
        ApplyRigctldCommand = new RelayCommand(ApplyRigctld);
        ResetPeakCommand = new RelayCommand(_meter.RequestPeakReset);
        CycleAlarmCommand = new RelayCommand(_meter.CycleAlarm, () => _meter.IsConnected);
        UpdateStatus = $"You have {UpdateService.CurrentVersion}.";
        RefreshPorts();
        OnStateChanged();
    }

    public DisplaySettings Display { get; }
    public ObservableCollection<string> Ports { get; } = new();
    public RelayCommand ConnectCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand OpenLogCommand { get; }
    public RelayCommand ApplyRigctldCommand { get; }
    public RelayCommand ResetPeakCommand { get; }
    public RelayCommand CycleAlarmCommand { get; }

    // --- logging ---
    private bool _logEachTx;
    public bool LogEachTx
    {
        get => _logEachTx;
        set
        {
            if (!SetProperty(ref _logEachTx, value)) return;
            _logging.Enabled = value;
            OnLogChanged();
        }
    }

    public string LogStatusText =>
        _logging.LastError is { } err ? $"Log error: {err}"
        : !LogEachTx ? "Logging off."
        : _logging.LoggedCount == 0 ? "Logging on — waiting for the first transmission."
        : $"{_logging.LoggedCount} transmission(s) logged this session.";

    public IBrush LogStatusBrush => _logging.LastError is not null ? Palette.RedBrush : Palette.DimBrush;

    private void OnLogChanged()
    {
        OnPropertyChanged(nameof(LogStatusText));
        OnPropertyChanged(nameof(LogStatusBrush));
    }

    // --- frequency (CAT) ---
    private bool _rigctldEnabled;
    public bool RigctldEnabled
    {
        get => _rigctldEnabled;
        set
        {
            if (!SetProperty(ref _rigctldEnabled, value)) return;
            _frequency.Enabled = value;
            OnFrequencyChanged();
        }
    }

    private string _rigctldEndpoint = FrequencyService.DefaultEndpoint;
    public string RigctldEndpoint
    {
        get => _rigctldEndpoint;
        set => SetProperty(ref _rigctldEndpoint, value);
    }

    public string FreqStatusText => _frequency.Status;
    public IBrush FreqStatusBrush => _frequency.StatusIsError ? Palette.RedBrush
        : _frequency.IsConnected ? Palette.GreenBrush : Palette.DimBrush;
    public string FreqText => _frequency.FreqText;

    private void OnFrequencyChanged()
    {
        OnPropertyChanged(nameof(FreqStatusText));
        OnPropertyChanged(nameof(FreqStatusBrush));
        OnPropertyChanged(nameof(FreqText));
    }

    /// <summary>Apply the typed endpoint and reconnect (explicit, so we don't restart per keystroke).</summary>
    private void ApplyRigctld()
    {
        _frequency.Endpoint = RigctldEndpoint;
        RigctldEndpoint = _frequency.Endpoint;   // echo back the normalized value
        _frequency.Restart();
        OnFrequencyChanged();
    }

    private void OpenLog()
    {
        // Open the CSV in the default handler (Excel) once it exists; otherwise reveal the folder.
        var target = File.Exists(_logging.LogPath) ? _logging.LogPath : ConfigStore.DataDir;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { /* ignore */ }
    }

    // Meter SWR alarm setpoint, shown/settable here so it's reachable even when the main-window
    // METER ALARM row is toggled off.
    private string _alarmSetpointText = "—";
    public string AlarmSetpointText { get => _alarmSetpointText; private set => SetProperty(ref _alarmSetpointText, value); }

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

    private void OnReading(Lp100Reading r) => AlarmSetpointText = r.AlarmSetpointText;

    private void OnStateChanged()
    {
        StatusText = _meter.Status;
        StatusBrush = _meter.StatusIsError ? Palette.RedBrush
            : _meter.IsConnected ? Palette.GreenBrush : Palette.DimBrush;
        if (!_meter.IsConnected) AlarmSetpointText = "—";
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectLabel));
        ConnectCommand.RaiseCanExecuteChanged();
        CycleAlarmCommand.RaiseCanExecuteChanged();
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
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set { if (SetProperty(ref _updateAvailable, value)) OnPropertyChanged(nameof(UpdateButtonLabel)); }
    }

    /// <summary>One button that checks, then (once an update is found) applies — like W2 Monitor.</summary>
    public string UpdateButtonLabel => UpdateAvailable ? "Update now" : "Check for updates";

    private Task UpdateButtonAsync() => UpdateAvailable ? UpdateNowAsync() : CheckUpdatesAsync();

    public async Task CheckUpdatesAsync()
    {
        _updateBusy = true; UpdateCommand.RaiseCanExecuteChanged();
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

        _updateBusy = false; UpdateCommand.RaiseCanExecuteChanged();
    }

    private async Task UpdateNowAsync()
    {
        if (_updateInfo?.AssetUrl is not { } url) return;
        _updateBusy = true; UpdateCommand.RaiseCanExecuteChanged();
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
            _updateBusy = false; UpdateCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenRelease()
    {
        var url = _updateInfo?.ReleaseUrl ?? $"https://github.com/{UpdateService.Repo}/releases/latest";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
