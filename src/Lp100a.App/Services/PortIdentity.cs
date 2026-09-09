using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Lp100a.Core;

namespace Lp100a.App.Services;

/// <summary>
/// Pins the LP-100A's USB-serial adapter by a stable identity instead of its volatile port name,
/// so the meter keeps working when the OS renumbers the port. Mirrors W2 Monitor's pinning.
///
/// - Windows: id = the adapter's chip serial (WMI); port = COMx.
/// - Linux/Pi: id = the <c>/dev/serial/by-id/*</c> name, which udev keeps stable per cable;
///   port = the <c>/dev/ttyUSB*</c> it currently links to. Note this is the whole by-id name, not a
///   serial pulled out of it — udev's rendering of an FTDI serial is 8 characters where Windows
///   reports 9, and nothing here ever compares the two, because each machine keeps its own config.
/// - Anything else: no map, and every method falls back gracefully to the saved port name.
///
/// Until 2026-09-09 the Linux half was missing entirely: the map was empty off Windows, so a Linux
/// install reconnected by the saved <c>/dev/ttyUSB0</c> and lost the meter on any renumber — and the
/// serial supervisor's re-resolve delegate had nothing to resolve with.
/// </summary>
public static class PortIdentity
{
    private static readonly Regex ComName = new(@"\((COM\d+)\)", RegexOptions.Compiled);
    private const string ByIdDir = "/dev/serial/by-id";

    /// <summary>Current port name -> stable adapter id, for every port that has one.</summary>
    public static Dictionary<string, string> GetMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (OperatingSystem.IsWindows()) PopulateWindows(map);
            else if (OperatingSystem.IsLinux()) PopulateLinux(map);
        }
        catch { /* WMI or /dev unavailable — fall back to the saved port name */ }
        return map;
    }

    /// <summary>
    /// Each <c>/dev/serial/by-id/*</c> entry is a stable symlink to the volatile <c>/dev/ttyUSB*</c>
    /// or <c>ttyACM*</c>. Map {resolved tty -> by-id name} so the name pins the cable across a renumber.
    /// </summary>
    private static void PopulateLinux(Dictionary<string, string> map)
    {
        if (!Directory.Exists(ByIdDir)) return;
        foreach (var link in Directory.GetFileSystemEntries(ByIdDir))
        {
            // Guard each entry: a dangling symlink, or a device torn down mid-enumeration, makes
            // ResolveLinkTarget throw. Without this one bad entry would abort the loop and drop every
            // adapter after it — the exact failure by-id pinning exists to prevent. (W2 hit this.)
            try
            {
                var target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
                if (target is not null) map[target] = Path.GetFileName(link);
            }
            catch { /* skip this entry, keep the rest */ }
        }
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
            var serial = UsbSerial.Extract(pnp);
            if (serial is not null) map[cm.Groups[1].Value] = serial;
        }
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
