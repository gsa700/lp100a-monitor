using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Lp100a.Core;

namespace Lp100a.App.Services;

/// <summary>What an uninstall should take with it besides the program itself.</summary>
/// <param name="RemoveSettings">Delete <c>config.json</c>. Trivially recreated by reconfiguring.</param>
/// <param name="RemoveLogs">
/// Delete <c>TXlog.csv</c> and its archives. Asked separately from settings and defaulted to
/// false on purpose — the log is operating history, not app state, and nothing can bring it back.
/// <see cref="Lp100a.Core.TxLogWriter"/> already refuses to delete a log even when clearing it;
/// removal must not be an easier mistake to make from here.
/// </param>
public readonly record struct UninstallOptions(bool RemoveSettings, bool RemoveLogs);

/// <summary>
/// An install could not proceed for a reason the user can act on — almost always because the
/// installed copy is still running. Carries a message meant to be shown as-is.
/// </summary>
public sealed class InstallBlockedException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Installs and removes the per-user copy of the app on Windows.
///
/// Per-user by necessity, not preference: <see cref="UpdateService.ApplyAndRestart"/> replaces the
/// running executable in place, which needs no elevation under %LOCALAPPDATA% and would need it on
/// every single update under Program Files. A machine-wide install would quietly break the updater.
///
/// The registry work shells out to <c>reg.exe</c> rather than using <c>Microsoft.Win32.Registry</c>.
/// The app targets plain <c>net10.0</c> so it can cross-publish Linux and Raspberry Pi builds from
/// one TFM, and the registry APIs only ship in <c>net10.0-windows</c>; the standalone package is
/// deprecated and stuck at 5.0.0. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>,
/// so paths with spaces need no hand-quoting. Same "Windows-only, guarded at runtime" shape the
/// WMI adapter-serial code already uses.
/// </summary>
public static class InstallService
{
    /// <summary>Registry key under HKCU that puts the app in Settings → Apps → Installed apps.</summary>
    private const string UninstallKey =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Lp100aMonitor";

    /// <summary>Display name, used for the installed-apps entry and the Start Menu shortcut.</summary>
    public const string DisplayName = "LP-100A Monitor";

    public static string ExeFileName => OperatingSystem.IsWindows() ? "Lp100aMonitor.exe" : "Lp100aMonitor";

    /// <summary>Full path of the running executable.</summary>
    public static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine the current executable path.");

    public static string ExeDirectory => Path.GetDirectoryName(ExePath)!;

    /// <summary>
    /// Per-user programs directory: <c>%LOCALAPPDATA%\Programs</c> on Windows,
    /// <c>~/.local/share</c> on Linux. <see cref="Environment.SpecialFolder.LocalApplicationData"/>
    /// already resolves to the right base on both; only Windows wants the extra <c>Programs</c>
    /// level, because <c>~/.local/share</c> is itself where per-user application data belongs.
    /// </summary>
    public static string ProgramsDirectory
    {
        get
        {
            var b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return OperatingSystem.IsWindows() ? Path.Combine(b, "Programs") : b;
        }
    }

    private static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Where the menu entry goes: <c>~/.local/share/applications</c>.</summary>
    private static string DesktopFilePath =>
        Path.Combine(ProgramsDirectory, "applications", DesktopEntry.FileName);

    /// <summary>Icon path in the XDG hicolor theme, at the 256px size IconGen emits.</summary>
    private static string IconFilePath => Path.Combine(
        ProgramsDirectory, "icons", "hicolor", "256x256", "apps", "lp100a-monitor.png");

    /// <summary>
    /// Convenience symlink so <c>lp100a-monitor</c> works from a terminal. <c>~/.local/bin</c> is
    /// on PATH by default on Raspberry Pi OS and most desktop distributions.
    /// </summary>
    private static string SymlinkPath =>
        Path.Combine(HomeDirectory, ".local", "bin", "lp100a-monitor");

    public static string InstallDirectory => InstallLayout.InstallDirectoryUnder(ProgramsDirectory);

    public static string InstalledExePath => Path.Combine(InstallDirectory, ExeFileName);

    /// <summary>Directories accepted as installed — the canonical one plus pre-installer hand-installs.</summary>
    public static IEnumerable<string> InstalledDirectories =>
        InstallLayout.InstalledDirectoriesUnder(ProgramsDirectory);

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", DisplayName + ".lnk");

    /// <summary>How this copy is running. Derived from its path every time — never cached or stored.</summary>
    public static InstallMode Mode => InstallLayout.Detect(
        ExeDirectory,
        File.Exists(Path.Combine(ExeDirectory, InstallLayout.PortableMarker)),
        InstallDirectory,
        InstalledDirectories);

    /// <summary>
    /// Copy this executable into the install directory and register it with Windows. Returns the
    /// path of the installed copy, which the caller should launch before exiting.
    /// </summary>
    /// <remarks>
    /// Copying only the executable is sufficient because the published build is self-contained and
    /// single-file — there is no payload beside it to keep in step. Settings and the transmission
    /// log already live in <see cref="AppConfig.DataDir"/>, so an install picks up whatever was
    /// there before and an uninstall can leave it behind.
    /// </remarks>
    public static string Install()
    {
        Directory.CreateDirectory(InstallDirectory);

        var target = InstalledExePath;
        if (!InstallLayout.SamePath(ExeDirectory, InstallDirectory))
        {
            try
            {
                File.Copy(ExePath, target, overwrite: true);
            }
            catch (IOException ex)
            {
                // Running a newly downloaded copy while the installed one is still open is an
                // ordinary thing to do, and Windows will not let the open executable be replaced.
                // Say that, rather than surfacing a raw sharing-violation from File.Copy.
                throw new InstallBlockedException(
                    "LP-100A Monitor is already running from the install folder. "
                    + "Close it and try installing again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InstallBlockedException(
                    $"Could not write to {InstallDirectory}. Check the folder's permissions.", ex);
            }
        }

        // A copied executable arrives without its executable bit on Unix; without this the
        // installed copy and the menu entry both silently fail to launch.
        if (!OperatingSystem.IsWindows()) MakeExecutable(target);

        Register(target);
        return target;
    }

    /// <summary>
    /// Register the copy at <paramref name="exePath"/> with the desktop environment: an
    /// installed-apps entry and Start Menu shortcut on Windows, a <c>.desktop</c> entry, icon and
    /// <c>~/.local/bin</c> symlink on Linux. Safe to call repeatedly — everything overwrites rather
    /// than duplicating.
    /// </summary>
    public static void Register(string exePath)
    {
        if (OperatingSystem.IsWindows()) RegisterWindows(exePath);
        else RegisterUnix(exePath);
    }

    private static void RegisterUnix(string exePath)
    {
        // Write the icon first: the entry should not reference a file that isn't there yet.
        string? icon = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconFilePath)!);
            using var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("app-icon.png");
            if (src is not null)
            {
                using var dst = File.Create(IconFilePath);
                src.CopyTo(dst);
                icon = IconFilePath;
            }
        }
        catch (IOException) { /* an entry without an icon still launches */ }
        catch (UnauthorizedAccessException) { }

        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFilePath)!);
        File.WriteAllText(DesktopFilePath, DesktopEntry.Build(
            DisplayName,
            exePath,
            icon,
            "Monitor for the TelePost LP-100A vector RF wattmeter"));

        // Some environments only notice a new entry once the database is rebuilt; others watch the
        // directory. Best effort, and harmless where the tool isn't installed.
        Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SymlinkPath)!);
            if (File.Exists(SymlinkPath) || Directory.Exists(SymlinkPath)) File.Delete(SymlinkPath);
            File.CreateSymbolicLink(SymlinkPath, exePath);
        }
        catch (IOException) { /* the menu entry is the point; the symlink is a convenience */ }
        catch (UnauthorizedAccessException) { }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RegisterWindows(string exePath)
    {
        if (!OperatingSystem.IsWindows()) return;

        var dir = Path.GetDirectoryName(exePath)!;
        var version = UpdateService.CurrentVersion;
        var sizeKb = FileSizeKb(exePath);

        RegSet(UninstallKey, "DisplayName", DisplayName);
        RegSet(UninstallKey, "DisplayVersion", version);
        RegSet(UninstallKey, "Publisher", "David Erickson (AB0R)");
        RegSet(UninstallKey, "DisplayIcon", exePath);
        RegSet(UninstallKey, "InstallLocation", dir);
        RegSet(UninstallKey, "URLInfoAbout", $"https://github.com/{UpdateService.Repo}");

        // Windows gives the user no way to answer a dialog it did not expect, so the entry's own
        // button runs the quiet path — which keeps the data directory.
        RegSet(UninstallKey, "UninstallString", $"\"{exePath}\" --uninstall");
        RegSet(UninstallKey, "QuietUninstallString", $"\"{exePath}\" --uninstall --quiet");

        RegSet(UninstallKey, "NoModify", "1", "REG_DWORD");
        RegSet(UninstallKey, "NoRepair", "1", "REG_DWORD");
        if (sizeKb > 0) RegSet(UninstallKey, "EstimatedSize", sizeKb.ToString(), "REG_DWORD");

        CreateShortcut(StartMenuShortcut, exePath, dir, "Monitor for the TelePost LP-100A wattmeter");
    }

    /// <summary>
    /// Adopt a copy that is already sitting in an install directory but was put there by hand,
    /// before there was an installer. Registers it where it stands rather than copying it to the
    /// canonical folder, which would leave the original behind as an orphan.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Mode != InstallMode.Installed) return;
        if (IsRegistered()) return;
        Register(ExePath);
    }

    /// <summary>
    /// Whether the desktop environment already knows about this copy — an installed-apps entry on
    /// Windows, a <c>.desktop</c> file on Linux.
    /// </summary>
    public static bool IsRegistered() => OperatingSystem.IsWindows()
        ? Run("reg.exe", ["query", UninstallKey, "/v", "DisplayName"]) == 0
        : File.Exists(DesktopFilePath);

    /// <summary>
    /// Remove the Windows registrations, then hand off to a detached helper that deletes the
    /// install directory once this process has exited. The caller must exit immediately after.
    /// </summary>
    /// <remarks>
    /// A running executable cannot delete itself, which is the same constraint
    /// <see cref="UpdateService.ApplyAndRestart"/> works around; this uses the same trampoline
    /// shape. The helper is written to the temp directory rather than the install directory,
    /// because the install directory is what it is about to remove.
    /// </remarks>
    public static void Uninstall(UninstallOptions options)
    {
        Unregister();

        // The install directory is private to the app on both platforms, so removing it whole is
        // safe. It must never become a shared directory such as ~/.local/bin — see SymlinkPath,
        // which is removed as a single file for exactly that reason.
        var toDelete = new List<string> { ExeDirectory };
        toDelete.AddRange(DataFilesToRemove(options));

        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "lp100a-uninstall.ps1");
            var lines = new List<string>
            {
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}",
            };
            lines.AddRange(toDelete.Select(p =>
                $"Remove-Item -LiteralPath '{p.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue"));
            // Take the helper with it, so an uninstall doesn't leave its own tooling behind in temp.
            lines.Add($"Remove-Item -LiteralPath '{script.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "lp100a-uninstall.sh");
            var lines = new List<string>
            {
                "#!/bin/sh",
                $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done",
            };
            lines.AddRange(toDelete.Select(p => $"rm -rf {ShellQuote(p)}"));
            lines.Add($"rm -f {ShellQuote(script)}");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            MakeExecutable(script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { script },
                UseShellExecute = false,
            });
        }
    }

    /// <summary>
    /// Wrap a path in single quotes for /bin/sh, closing and reopening around any single quote it
    /// contains. Paths come from the environment, so they are not assumed to be tame.
    /// </summary>
    private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Which files under the data directory an uninstall should take. The directory itself is never
    /// removed wholesale: settings and irreplaceable operating history share it, and only the files
    /// actually consented to are listed.
    /// </summary>
    public static IEnumerable<string> DataFilesToRemove(UninstallOptions options)
    {
        var dir = ConfigStore.DataDir;

        if (options.RemoveSettings)
        {
            var config = Path.Combine(dir, "config.json");
            if (File.Exists(config)) yield return config;
        }

        if (options.RemoveLogs && Directory.Exists(dir))
        {
            // The live log plus every archive "Clear log" has set aside beside it. The pattern is
            // derived from the real log path rather than spelled out, so renaming the log can't
            // quietly leave archives behind.
            var log = ConfigStore.LogFilePath;
            var pattern = Path.GetFileNameWithoutExtension(log) + "*" + Path.GetExtension(log);
            foreach (var f in Directory.EnumerateFiles(dir, pattern))
                yield return f;
        }
    }

    private static void Unregister()
    {
        if (OperatingSystem.IsWindows())
        {
            Run("reg.exe", ["delete", UninstallKey, "/f"]);
            TryDelete(StartMenuShortcut);
            return;
        }

        // Each removed as a single file. ~/.local/bin and the icon theme are shared directories:
        // nothing here may delete a directory it does not own.
        TryDelete(DesktopFilePath);
        TryDelete(IconFilePath);
        TryDelete(SymlinkPath);
        Run("update-desktop-database", [Path.GetDirectoryName(DesktopFilePath)!]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            // File.Exists follows symlinks, so a link whose target is already gone reports false;
            // ask the link itself whether it is there.
            if (File.Exists(path) || File.ResolveLinkTarget(path, returnFinalTarget: false) is not null)
                File.Delete(path);
        }
        catch (IOException) { /* a locked or vanished file is not worth failing an uninstall over */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Launch a copy of the app detached from this process.</summary>
    public static void LaunchDetached(string exePath) =>
        Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });

    private static int FileSizeKb(string path)
    {
        try { return (int)(new FileInfo(path).Length / 1024); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static void RegSet(string key, string name, string value, string type = "REG_SZ") =>
        Run("reg.exe", ["add", key, "/v", name, "/t", type, "/d", value, "/f"]);

    /// <summary>Run a console tool with no window and return its exit code (-1 if it wouldn't start).</summary>
    private static int Run(string fileName, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    /// <summary>
    /// Write a .lnk via Windows Script Host. Reached by reflection rather than a <c>dynamic</c>
    /// call so nothing depends on the C# runtime binder being present in a single-file build.
    /// A missing shortcut is not worth failing an install over, so every failure here is swallowed.
    /// </summary>
    private static void CreateShortcut(string lnkPath, string target, string workingDirectory, string description)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);

            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [lnkPath]);
            if (link is null) return;

            var linkType = link.GetType();
            void Set(string property, object value) =>
                linkType.InvokeMember(property, BindingFlags.SetProperty, null, link, [value]);

            Set("TargetPath", target);
            Set("WorkingDirectory", workingDirectory);
            Set("IconLocation", target + ",0");
            Set("Description", description);
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception)
        {
            // Windows Script Host can be disabled by policy. The app is fully usable without a
            // Start Menu entry, so this must not take the install down with it.
        }
    }
}
