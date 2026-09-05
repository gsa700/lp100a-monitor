using System.Text.Json;
using Lp100a.App.Settings;

namespace Lp100a.App.Services;

/// <summary>Persisted state: window bounds, selected port, and display flags.</summary>
public sealed class AppConfig
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? SetupX { get; set; }
    public double? SetupY { get; set; }
    public int SetupTab { get; set; }
    public double? VectorX { get; set; }
    public double? VectorY { get; set; }
    public double? VectorW { get; set; }
    public double? VectorH { get; set; }
    public double? LogX { get; set; }
    public double? LogY { get; set; }
    public double? LogW { get; set; }
    public double? LogH { get; set; }
    public string? Port { get; set; }
    public string? Serial { get; set; }   // FTDI/USB chip serial, so the cable is followed across COM renumbering
    public bool CheckUpdatesAtStartup { get; set; }
    public bool LogEachTx { get; set; }
    public bool RigctldEnabled { get; set; }
    public string? RigctldEndpoint { get; set; }   // "host" or "host:port"; blank = 127.0.0.1:4532
    public DisplayConfig Display { get; set; } = new();

    public void ApplyTo(DisplaySettings d)
    {
        d.ShowStatusLine = Display.ShowStatusLine;
        d.ShowPowerBar = Display.ShowPowerBar;
        d.ShowSwrBar = Display.ShowSwrBar;
        d.ShowReflected = Display.ShowReflected;
        d.ShowReturnLoss = Display.ShowReturnLoss;
        d.ShowDbm = Display.ShowDbm;
        d.ShowPeak = Display.ShowPeak;
        d.ShowTx = Display.ShowTx;
        d.ShowMeterMode = Display.ShowMeterMode;
        d.ShowMeterAlarm = Display.ShowMeterAlarm;
        d.ShowZ = Display.ShowZ;
        d.ShowPhase = Display.ShowPhase;
        d.ShowRx = Display.ShowRx;
        d.ShowVectorWindow = Display.ShowVectorWindow;
        d.AlwaysOnTop = Display.AlwaysOnTop;
        d.PeakHoldEnabled = Display.PeakHoldEnabled;
        d.SwrBannerEnabled = Display.SwrBannerEnabled;
        d.PeakHoldSeconds = Display.PeakHoldSeconds;
        d.TxTimeoutEnabled = Display.TxTimeoutEnabled;
        d.TxTimeoutSeconds = Display.TxTimeoutSeconds;
        d.IdTimerEnabled = Display.IdTimerEnabled;
        d.IdIntervalMinutes = Display.IdIntervalMinutes;
    }

    public void CaptureFrom(DisplaySettings d)
    {
        Display.ShowStatusLine = d.ShowStatusLine;
        Display.ShowPowerBar = d.ShowPowerBar;
        Display.ShowSwrBar = d.ShowSwrBar;
        Display.ShowReflected = d.ShowReflected;
        Display.ShowReturnLoss = d.ShowReturnLoss;
        Display.ShowDbm = d.ShowDbm;
        Display.ShowPeak = d.ShowPeak;
        Display.ShowTx = d.ShowTx;
        Display.ShowMeterMode = d.ShowMeterMode;
        Display.ShowMeterAlarm = d.ShowMeterAlarm;
        Display.ShowZ = d.ShowZ;
        Display.ShowPhase = d.ShowPhase;
        Display.ShowRx = d.ShowRx;
        Display.ShowVectorWindow = d.ShowVectorWindow;
        Display.AlwaysOnTop = d.AlwaysOnTop;
        Display.PeakHoldEnabled = d.PeakHoldEnabled;
        Display.SwrBannerEnabled = d.SwrBannerEnabled;
        Display.PeakHoldSeconds = d.PeakHoldSeconds;
        Display.TxTimeoutEnabled = d.TxTimeoutEnabled;
        Display.TxTimeoutSeconds = d.TxTimeoutSeconds;
        Display.IdTimerEnabled = d.IdTimerEnabled;
        Display.IdIntervalMinutes = d.IdIntervalMinutes;
    }
}

public sealed class DisplayConfig
{
    public bool ShowStatusLine { get; set; } = true;
    public bool ShowPowerBar { get; set; } = true;
    public bool ShowSwrBar { get; set; } = true;
    public bool ShowReflected { get; set; } = true;
    public bool ShowReturnLoss { get; set; } = true;
    public bool ShowDbm { get; set; } = true;
    public bool ShowPeak { get; set; } = true;
    public bool ShowTx { get; set; } = true;
    public bool ShowMeterMode { get; set; } = true;
    public bool ShowMeterAlarm { get; set; } = true;
    public bool ShowZ { get; set; }
    public bool ShowPhase { get; set; }
    public bool ShowRx { get; set; } = true;
    public bool ShowVectorWindow { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool PeakHoldEnabled { get; set; } = true;
    public bool SwrBannerEnabled { get; set; } = true;
    public decimal PeakHoldSeconds { get; set; } = 1.0m;
    public bool TxTimeoutEnabled { get; set; } = true;
    public decimal TxTimeoutSeconds { get; set; } = 180m;
    public bool IdTimerEnabled { get; set; }
    public decimal IdIntervalMinutes { get; set; } = 10m;
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Per-user app-data directory (created on access). Home for <c>config.json</c> — app state the
    /// user never needs to see. The transmission log used to live here too and doesn't any more; see
    /// <see cref="LogDirectory"/>.
    /// </summary>
    public static string DataDir
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lp100aMonitor");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>File name of the per-transmission CSV log; archives are <c>TXlog_&lt;stamp&gt;.csv</c> beside it.</summary>
    public const string LogFileName = "TXlog.csv";

    /// <summary>
    /// Where the transmission log lives: a folder of its own under the user's Documents (created on
    /// access). Documents rather than app data because the log is operating history, not app state —
    /// the thing you open in a spreadsheet, keep for years and expect to be backed up, none of which
    /// is true of <c>%APPDATA%</c>. It also puts the log out of uninstall's reach by construction
    /// rather than by a dialog.
    /// </summary>
    /// <remarks>
    /// On Linux this reads <c>XDG_DOCUMENTS_DIR</c> from <c>user-dirs.dirs</c>, because .NET's
    /// <c>MyDocuments</c> there is just <c>$HOME</c>; a machine with no documents directory configured
    /// (a headless Pi) gets <c>~/Documents</c>, created rather than a CSV dropped into the top of home.
    /// </remarks>
    public static string LogDirectory
    {
        get
        {
            var dir = System.IO.Path.Combine(DocumentsDirectory(), "LP-100A Monitor");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Path of the per-transmission CSV log.</summary>
    public static string LogFilePath => System.IO.Path.Combine(LogDirectory, LogFileName);

    private static string DocumentsDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome)) configHome = System.IO.Path.Combine(home, ".config");

        string? contents = null;
        try
        {
            var f = System.IO.Path.Combine(configHome, "user-dirs.dirs");
            if (File.Exists(f)) contents = File.ReadAllText(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return Lp100a.Core.XdgUserDirs.Resolve(contents, Lp100a.Core.XdgUserDirs.DocumentsKey, home)
            ?? System.IO.Path.Combine(home, "Documents");
    }

    private static string Path => System.IO.Path.Combine(DataDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path)) ?? new AppConfig();
        }
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(config, Options)); }
        catch { /* best effort */ }
    }
}
