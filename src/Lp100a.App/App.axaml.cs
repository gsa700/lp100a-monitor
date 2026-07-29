using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lp100a.App.Services;
using Lp100a.App.Settings;
using Lp100a.App.ViewModels;
using Lp100a.App.Views;
using Lp100a.Core;

namespace Lp100a.App;

public partial class App : Application
{
    private AppConfig _config = new();
    private MeterService _meter = null!;
    private FrequencyService _frequency = null!;
    private TxLoggingService _logging = null!;
    private DisplaySettings _display = null!;

    private SetupViewModel _setupVm = null!;
    private VectorViewModel _vectorVm = null!;
    private LogViewModel? _logVm;

    private MainWindow _mainWindow = null!;
    private SetupWindow? _setupWindow;
    private VectorWindow? _vectorWindow;
    private LogWindow? _logWindow;

    public bool IsExiting { get; private set; }

    /// <summary>An uninstall is in flight; don't write settings back out on the way down.</summary>
    private bool _uninstalling;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();
            _display = new DisplaySettings();
            _config.ApplyTo(_display);

            _meter = new MeterService();
            _frequency = new FrequencyService(_config.RigctldEnabled,
                _config.RigctldEndpoint ?? FrequencyService.DefaultEndpoint);
            _logging = new TxLoggingService(_meter, ConfigStore.LogFilePath, _config.LogEachTx, _frequency,
                timeoutSeconds: (int)_display.TxTimeoutSeconds);
            _setupVm = new SetupViewModel(_meter, _display, _logging, _frequency)
            {
                CheckUpdatesAtStartup = _config.CheckUpdatesAtStartup,
                LogEachTx = _config.LogEachTx,
                SelectedTabIndex = Math.Clamp(_config.SetupTab, 0, SetupViewModel.TabCount - 1),
            };
            _vectorVm = new VectorViewModel(_meter);

            // A copy installed by hand before there was an installer is adopted where it stands,
            // so it appears in Installed apps without being copied to a second location.
            try { InstallService.EnsureRegistered(); } catch { /* never block startup over this */ }

            // Follow the cable by its chip serial across COM renumbering, then auto-connect.
            var startupPort = PortIdentity.ResolvePort(_config.Port, _config.Serial);
            _setupVm.SelectPort(startupPort);
            // Don't take the serial port for a run that exists only to uninstall.
            if (!Program.PendingUninstall
                && startupPort is not null && MeterService.GetPortNames().Contains(startupPort))
                _meter.Connect(startupPort);

            _mainWindow = new MainWindow { DataContext = new MainWindowViewModel(_meter, _display) };
            RestoreMainBounds(_mainWindow);
            _mainWindow.Topmost = _display.AlwaysOnTop;
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;   // closing main shuts the app (and its owned children)

            _display.PropertyChanged += OnDisplayChanged;
            _mainWindow.Closing += (_, _) => SaveAndCleanup();
            // Reopen a persisted Vector window only after main is shown (an owned window needs a visible owner).
            _mainWindow.Opened += async (_, _) =>
            {
                // This run exists only to uninstall: ask, act, and go. Nothing else should start.
                if (Program.PendingUninstall)
                {
                    await RunUninstallAsync();
                    return;
                }

                if (_display.ShowVectorWindow) EnsureVectorVisible();

                // A copy running from wherever it was unzipped offers to install itself.
                if (InstallService.Mode == InstallMode.Loose && await OfferInstallAsync()) return;

                if (_config.CheckUpdatesAtStartup)
                {
                    await _setupVm.CheckUpdatesAsync();
                    if (_setupVm.UpdateAvailable) ShowSetup();   // surface it
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowSetup()
    {
        if (_setupWindow is null)
        {
            _setupWindow = new SetupWindow { DataContext = _setupVm, Topmost = _display.AlwaysOnTop };
            RestoreSetupBounds(_setupWindow);
            _setupWindow.Show(_mainWindow);   // owned by main -> closes with it
        }
        else
        {
            _setupWindow.Show();
        }
        _setupWindow.Activate();
    }

    /// <summary>Called by the main-window "Vector" button; the flag drives the window.</summary>
    public void ShowVector() => _display.ShowVectorWindow = true;

    /// <summary>Open the TX log viewer (Setup → Logging → View log).</summary>
    public void ShowLog()
    {
        if (_logWindow is null)
        {
            // Built on demand: the view model reads the CSV and then follows the logging service,
            // so there's no reason to hold it while the window is closed.
            _logVm = new LogViewModel(_logging);
            _logWindow = new LogWindow { DataContext = _logVm, Topmost = _display.AlwaysOnTop };
            RestoreLogBounds(_logWindow);
            _logWindow.Show(_mainWindow);   // owned by main -> closes with it
        }
        else
        {
            _logVm?.Refresh();
            _logWindow.Show();
        }
        _logWindow.Activate();
    }

    /// <summary>Close the app so the staged update helper can swap the executable and relaunch.</summary>
    public void ExitForUpdate() => _mainWindow.Close();

    /// <summary>
    /// Offer to install a loose copy. Returns true if the app is handing over to the installed
    /// copy and the caller should stop starting things up.
    /// </summary>
    private async Task<bool> OfferInstallAsync()
    {
        var accepted = await ConfirmDialog.ShowAsync(
            _mainWindow,
            "Install LP-100A Monitor",
            "Install LP-100A Monitor on this computer?",
            affirmative: "Install",
            negative: "Not now",
            detail: $"Copies the program to {InstallService.InstallDirectory} and lists it in "
                  + "Settings → Apps → Installed apps, with a Start Menu shortcut. Your settings and "
                  + "transmission log are untouched either way.\n\n"
                  + $"To run from here permanently without being asked again, put a file named "
                  + $"{InstallLayout.PortableMarker} beside the program.");

        if (!accepted) return false;

        try
        {
            var installed = InstallService.Install();

            // Installed but not listed is a real outcome, not a detail: the program works, yet the
            // usual way to remove it is missing. Say so here rather than report a clean install and
            // leave it to be discovered later in Settings.
            if (!installed.Registered)
            {
                await ConfirmDialog.ShowNoticeAsync(_mainWindow, "Installed, with one problem",
                    $"LP-100A Monitor is installed in {InstallService.InstallDirectory} and will run "
                    + "normally, but it could not add itself to Settings → Apps → Installed apps.",
                    detail: "Starting the installed copy again usually adds the entry. Failing that, "
                          + "run it once with --install from a command prompt.");
            }

            InstallService.LaunchDetached(installed.ExePath);
            // Closing runs the normal save path on purpose, so settings carry over to the
            // installed copy, which reads the same per-user data directory.
            _mainWindow.Close();
            return true;
        }
        catch (Exception ex)
        {
            await ConfirmDialog.ShowNoticeAsync(_mainWindow, "Install failed",
                "LP-100A Monitor could not install itself.", detail: ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Interactive uninstall. Settings and the transmission log are asked about separately, and
    /// both default to being kept: they share a directory but not their stakes, and the log is
    /// operating history that nothing can bring back.
    /// </summary>
    private async Task RunUninstallAsync()
    {
        var confirmed = await ConfirmDialog.ShowAsync(
            _mainWindow,
            "Uninstall LP-100A Monitor",
            "Remove LP-100A Monitor from this computer?",
            affirmative: "Uninstall",
            negative: "Cancel",
            detail: $"Deletes the program from {InstallService.ExeDirectory} and removes its "
                  + "Start Menu shortcut and Installed apps entry.");

        if (!confirmed)
        {
            _mainWindow.Close();
            return;
        }

        var removeSettings = await ConfirmDialog.ShowAsync(
            _mainWindow,
            "Settings",
            "Also delete your settings?",
            affirmative: "Delete settings",
            negative: "Keep settings",
            detail: "Serial port, display rows, alarm thresholds and window positions. Keeping them "
                  + "means a later reinstall picks up exactly where you left off.");

        var removeLogs = await ConfirmDialog.ShowAsync(
            _mainWindow,
            "Transmission log",
            "Also delete your transmission log?",
            affirmative: "Delete the log",
            negative: "Keep the log",
            detail: TransmissionLogWarning(),
            danger: true);

        _uninstalling = true;
        InstallService.Uninstall(new UninstallOptions(removeSettings, removeLogs));
        _mainWindow.Close();
    }

    /// <summary>
    /// Spell out what deleting the log actually costs, in overs rather than in filenames — "1,284
    /// transmissions" is a decision someone can make; "TXlog.csv" is not.
    /// </summary>
    private static string TransmissionLogWarning()
    {
        var where = ConfigStore.DataDir;
        var count = CountLoggedOvers();
        var what = count > 0
            ? $"This is your record of {count:N0} transmission{(count == 1 ? "" : "s")} — frequency, "
            + "power, SWR and impedance for every over you have logged."
            : "This is your record of every over you have logged.";

        return what + " It cannot be recovered, and it is what the planned impedance-signature "
             + $"analysis is built on.\n\nKeeping it leaves the files in {where}, where a later "
             + "install will find them again.";
    }

    private static int CountLoggedOvers()
    {
        try
        {
            var path = ConfigStore.LogFilePath;
            if (!File.Exists(path)) return 0;
            // Rows minus the header; blank trailing lines don't count.
            return Math.Max(0, File.ReadLines(path).Count(l => !string.IsNullOrWhiteSpace(l)) - 1);
        }
        catch { return 0; }
    }

    /// <summary>A child window is closing; capture its bounds and drop the reference.</summary>
    public void NotifySetupClosing(SetupWindow w)
    {
        _config.SetupX = w.Position.X;
        _config.SetupY = w.Position.Y;
        _setupWindow = null;
    }

    public void NotifyVectorClosing(VectorWindow w)
    {
        _config.VectorX = w.Position.X;
        _config.VectorY = w.Position.Y;
        _config.VectorW = w.Width;
        _config.VectorH = w.Height;
        _vectorWindow = null;
        // Keep the toggle in sync when the user closes it directly (but not during app exit,
        // so a Vector window left open reopens next launch).
        if (!IsExiting) _display.ShowVectorWindow = false;
    }

    public void NotifyLogClosing(LogWindow w)
    {
        _config.LogX = w.Position.X;
        _config.LogY = w.Position.Y;
        _config.LogW = w.Width;
        _config.LogH = w.Height;
        // Unhook from the logging service so a closed window isn't still re-reading the CSV on
        // every over.
        _logVm?.Detach();
        _logVm = null;
        _logWindow = null;
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.ShowVectorWindow):
                if (_display.ShowVectorWindow) EnsureVectorVisible();
                else _vectorWindow?.Close();
                break;
            case nameof(DisplaySettings.AlwaysOnTop):
                _mainWindow.Topmost = _display.AlwaysOnTop;
                if (_setupWindow is not null) _setupWindow.Topmost = _display.AlwaysOnTop;
                if (_vectorWindow is not null) _vectorWindow.Topmost = _display.AlwaysOnTop;
                if (_logWindow is not null) _logWindow.Topmost = _display.AlwaysOnTop;
                break;
        }
    }

    private void EnsureVectorVisible()
    {
        if (_vectorWindow is null)
        {
            _vectorWindow = new VectorWindow { DataContext = _vectorVm, Topmost = _display.AlwaysOnTop };
            RestoreVectorBounds(_vectorWindow);
            _vectorWindow.Show(_mainWindow);   // owned by main -> closes with it
        }
        else
        {
            _vectorWindow.Show();
        }
        _vectorWindow.Activate();
    }

    private void RestoreMainBounds(Window w)
    {
        // Width is fixed and height auto-fits content, so only the position is restored.
        if (_config is { X: not null, Y: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.X.Value, (int)_config.Y.Value);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void RestoreSetupBounds(Window w)
    {
        if (_config is { SetupX: not null, SetupY: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.SetupX.Value, (int)_config.SetupY.Value);
        }
    }

    private void RestoreVectorBounds(Window w)
    {
        if (_config.VectorW is > 300) w.Width = _config.VectorW.Value;
        if (_config.VectorH is > 300) w.Height = _config.VectorH.Value;
        if (_config is { VectorX: not null, VectorY: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.VectorX.Value, (int)_config.VectorY.Value);
        }
    }

    private void RestoreLogBounds(Window w)
    {
        if (_config.LogW is > 400) w.Width = _config.LogW.Value;
        if (_config.LogH is > 240) w.Height = _config.LogH.Value;
        if (_config is { LogX: not null, LogY: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.LogX.Value, (int)_config.LogY.Value);
        }
    }

    private void SaveAndCleanup()
    {
        if (IsExiting) return;   // main.Closing fires once; guard against re-entry
        IsExiting = true;

        // An uninstall must not write config.json back out on its way down — the user may have
        // just asked for it to be deleted, and recreating it here would undo that answer.
        if (_uninstalling)
        {
            _logging.Dispose();
            _frequency.Dispose();
            _meter.Dispose();
            return;
        }

        try
        {
            _config.X = _mainWindow.Position.X;
            _config.Y = _mainWindow.Position.Y;

            if (_setupWindow is not null)
            {
                _config.SetupX = _setupWindow.Position.X;
                _config.SetupY = _setupWindow.Position.Y;
            }
            if (_vectorWindow is not null)
            {
                _config.VectorX = _vectorWindow.Position.X;
                _config.VectorY = _vectorWindow.Position.Y;
                _config.VectorW = _vectorWindow.Width;
                _config.VectorH = _vectorWindow.Height;
            }
            if (_logWindow is not null)
            {
                _config.LogX = _logWindow.Position.X;
                _config.LogY = _logWindow.Position.Y;
                _config.LogW = _logWindow.Width;
                _config.LogH = _logWindow.Height;
            }

            var port = _meter.CurrentPort ?? _setupVm.SelectedPort;
            _config.Port = port;
            if (port is not null && PortIdentity.SerialFor(port) is { } serial) _config.Serial = serial;
            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.SetupTab = _setupVm.SelectedTabIndex;
            _config.LogEachTx = _setupVm.LogEachTx;
            _config.RigctldEnabled = _setupVm.RigctldEnabled;
            _config.RigctldEndpoint = _setupVm.RigctldEndpoint;
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _logging.Dispose();
        _frequency.Dispose();
        _meter.Dispose();
    }
}
