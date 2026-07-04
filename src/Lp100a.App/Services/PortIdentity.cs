using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Lp100a.App.Services;

/// <summary>
/// Pins a USB-serial adapter by its chip serial number instead of its COM number, so the
/// LP-100A keeps working when Windows renumbers the port. Windows-only (WMI); on other
/// platforms every method is a graceful no-op and we just use the saved COM name.
/// Mirrors W2 Monitor's FTDI pinning, with a generic USB fallback.
/// </summary>
public static class PortIdentity
{
    private static readonly Regex Ftdi =
        new(@"FTDIBUS\\VID_[0-9A-Fa-f]{4}\+PID_[0-9A-Fa-f]{4}\+([^\\]+)\\", RegexOptions.Compiled);
    private static readonly Regex Usb =
        new(@"USB\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}\\([^\\]+)$", RegexOptions.Compiled);
    private static readonly Regex ComName = new(@"\((COM\d+)\)", RegexOptions.Compiled);

    /// <summary>COM name -> adapter serial for every port that reports a stable serial.</summary>
    public static Dictionary<string, string> GetMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return map;
        try { PopulateWindows(map); } catch { /* WMI unavailable */ }
        return map;
    }

    [SupportedOSPlatform("windows")]
    private static void PopulateWindows(Dictionary<string, string> map)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            if (o["Name"] is not string name || o["PNPDeviceID"] is not string pnp) continue;
            var cm = ComName.Match(name);
            if (!cm.Success) continue;
            var serial = ExtractSerial(pnp);
            if (serial is not null) map[cm.Groups[1].Value] = serial;
        }
    }

    private static string? ExtractSerial(string pnp)
    {
        var f = Ftdi.Match(pnp);
        if (f.Success) return f.Groups[1].Value;
        var u = Usb.Match(pnp);
        if (u.Success && !u.Groups[1].Value.Contains('&')) return u.Groups[1].Value;  // '&' = location id, not a real serial
        return null;
    }

    /// <summary>Serial of the adapter currently on <paramref name="port"/>, or null.</summary>
    public static string? SerialFor(string port) => GetMap().TryGetValue(port, out var s) ? s : null;

    /// <summary>The COM port that currently hosts <paramref name="serial"/>; falls back to savedPort.</summary>
    public static string? ResolvePort(string? savedPort, string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return savedPort;
        foreach (var kv in GetMap())
            if (string.Equals(kv.Value, serial, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return savedPort;
    }
}
