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
    public double? VectorX { get; set; }
    public double? VectorY { get; set; }
    public double? VectorW { get; set; }
    public double? VectorH { get; set; }
    public string? Port { get; set; }
    public string? Serial { get; set; }   // FTDI/USB chip serial, so the cable is followed across COM renumbering
    public bool CheckUpdatesAtStartup { get; set; }
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
        d.ShowZ = Display.ShowZ;
        d.ShowPhase = Display.ShowPhase;
        d.ShowRx = Display.ShowRx;
        d.ShowVectorWindow = Display.ShowVectorWindow;
        d.AlwaysOnTop = Display.AlwaysOnTop;
        d.PeakHoldEnabled = Display.PeakHoldEnabled;
        d.SwrAlarmEnabled = Display.SwrAlarmEnabled;
        d.SwrAlarmThreshold = Display.SwrAlarmThreshold;
        d.PeakHoldSeconds = Display.PeakHoldSeconds;
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
        Display.ShowZ = d.ShowZ;
        Display.ShowPhase = d.ShowPhase;
        Display.ShowRx = d.ShowRx;
        Display.ShowVectorWindow = d.ShowVectorWindow;
        Display.AlwaysOnTop = d.AlwaysOnTop;
        Display.PeakHoldEnabled = d.PeakHoldEnabled;
        Display.SwrAlarmEnabled = d.SwrAlarmEnabled;
        Display.SwrAlarmThreshold = d.SwrAlarmThreshold;
        Display.PeakHoldSeconds = d.PeakHoldSeconds;
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
    public bool ShowZ { get; set; }
    public bool ShowPhase { get; set; }
    public bool ShowRx { get; set; } = true;
    public bool ShowVectorWindow { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool PeakHoldEnabled { get; set; } = true;
    public bool SwrAlarmEnabled { get; set; }
    public decimal SwrAlarmThreshold { get; set; } = 2.5m;
    public decimal PeakHoldSeconds { get; set; } = 1.5m;
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string Path
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lp100aMonitor");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "config.json");
        }
    }

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
