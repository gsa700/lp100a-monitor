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

namespace Lp100a.App;

public partial class App : Application
{
    private AppConfig _config = new();
    private MeterService _meter = null!;
    private TxLoggingService _logging = null!;
    private DisplaySettings _display = null!;

    private SetupViewModel _setupVm = null!;
    private VectorViewModel _vectorVm = null!;

    private MainWindow _mainWindow = null!;
    private SetupWindow? _setupWindow;
    private VectorWindow? _vectorWindow;

    public bool IsExiting { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();
            _display = new DisplaySettings();
            _config.ApplyTo(_display);

            _meter = new MeterService();
            _logging = new TxLoggingService(_meter, ConfigStore.LogFilePath, _config.LogEachTx);
            _setupVm = new SetupViewModel(_meter, _display, _logging)
            {
                CheckUpdatesAtStartup = _config.CheckUpdatesAtStartup,
                LogEachTx = _config.LogEachTx,
            };
            _vectorVm = new VectorViewModel(_meter);

            // Follow the cable by its chip serial across COM renumbering, then auto-connect.
            var startupPort = PortIdentity.ResolvePort(_config.Port, _config.Serial);
            _setupVm.SelectPort(startupPort);
            if (startupPort is not null && MeterService.GetPortNames().Contains(startupPort))
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
                if (_display.ShowVectorWindow) EnsureVectorVisible();
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

    /// <summary>Close the app so the staged update helper can swap the executable and relaunch.</summary>
    public void ExitForUpdate() => _mainWindow.Close();

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

    private void SaveAndCleanup()
    {
        if (IsExiting) return;   // main.Closing fires once; guard against re-entry
        IsExiting = true;
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

            var port = _meter.CurrentPort ?? _setupVm.SelectedPort;
            _config.Port = port;
            if (port is not null && PortIdentity.SerialFor(port) is { } serial) _config.Serial = serial;
            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.LogEachTx = _setupVm.LogEachTx;
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _logging.Dispose();
        _meter.Dispose();
    }
}
