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
/// <remarks>
/// There is no option for the transmission log, on purpose. It lives in Documents, which this app
/// does not own and never deletes from, so uninstall cannot reach it by construction. That replaced
/// a separate keep/delete prompt in v1.0.0-beta2: a dialog guards a hazard, while an unreachable
/// file has none. <see cref="Lp100a.Core.TxLogWriter"/> follows the same principle — archive aside,
/// never delete.
/// </remarks>
public readonly record struct UninstallOptions(bool RemoveSettings);

/// <summary>Outcome of an install.</summary>
/// <param name="ExePath">The installed executable.</param>
/// <param name="Registered">
/// Whether the desktop integration is in place: the Start Menu shortcut on Windows, the
/// <c>.desktop</c> entry on Linux. The install itself succeeded either way — the program is copied
/// and runs — but when this is false it has no menu entry, which is worth telling the user rather
/// than reporting a clean install.
/// </param>
public readonly record struct InstallResult(string ExePath, bool Registered);

/// <summary>
/// An install could not proceed for a reason the user can act on — almost always because the
/// installed copy is still running. Carries a message meant to be shown as-is.
/// </summary>
public sealed class InstallBlockedException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Installs and removes the per-user copy of the app.
///
/// Per-user by necessity, not preference: <see cref="UpdateService.ApplyAndRestart"/> replaces the
/// running executable in place, which needs no elevation under %LOCALAPPDATA% and would need it on
/// every single update under Program Files. A machine-wide install would quietly break the updater.
///
/// There is deliberately <b>no installed-apps registry entry on Windows</b>, so the app does not
/// appear in Settings → Apps and is removed from its own Setup instead. It used to write one, and
/// from a shell launch the write never reached the registry: Windows' Program Compatibility
/// Assistant attaches a compatibility layer to this unsigned exe whenever Explorer or the updater's
/// helper starts it, and that layer virtualises every registry write — reg.exe and in-process alike,
/// children included — into an overlay the process reads back consistently and loses on exit. So the
/// app wrote the entry, verified it, and reported success, and the real key never changed; that is
/// the whole of the 0.9.18 → 0.9.22 "registration goes missing" saga. A manifest opt-out, in-process
/// writes and a scrubbed relaunch were all tested in the W2 port and none escaped it; the one untested
/// lever is an Authenticode signature, which is a purchase, not a build setting. Rather than ship a
/// feature that reports success while doing nothing, the registry was taken out (W2 BACKLOG,
/// 2026-09-04, with the full ruled-out list). If the exe is ever signed, the registration code is one
/// commit back in history. Windows integration is therefore shortcuts only, which are files and were
/// never affected.
/// </summary>
public static class InstallService
{
    /// <summary>Display name, used for the Start Menu shortcut and the Linux menu entry.</summary>
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
    /// Copy this executable into the install directory and give it its desktop integration. Returns
    /// the path of the installed copy, which the caller should launch before exiting.
    /// </summary>
    /// <remarks>
    /// Copying only the executable is sufficient because the published build is self-contained and
    /// single-file — there is no payload beside it to keep in step. Settings live in
    /// <see cref="ConfigStore.DataDir"/> and the transmission log in <see cref="ConfigStore.LogDirectory"/>,
    /// so an install picks up whatever was there before and an uninstall leaves both behind.
    /// </remarks>
    public static InstallResult Install()
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

        return new InstallResult(target, Register(target));
    }

    /// <summary>
    /// Give the copy at <paramref name="exePath"/> its desktop integration: a Start Menu shortcut on
    /// Windows; a <c>.desktop</c> entry, icon and <c>~/.local/bin</c> symlink on Linux. Safe to call
    /// repeatedly — everything overwrites rather than duplicating.
    /// </summary>
    /// <returns>Whether the menu entry — the one piece that makes the app findable — is on disk afterwards.</returns>
    public static bool Register(string exePath) =>
        OperatingSystem.IsWindows() ? RegisterWindows(exePath) : RegisterUnix(exePath);

    /// <returns>Whether the desktop entry is on disk afterwards — the icon and the
    /// <c>~/.local/bin</c> symlink are conveniences, but without the entry there is no menu item.</returns>
    private static bool RegisterUnix(string exePath)
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

        return File.Exists(DesktopFilePath);
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

    /// <summary>
    /// Windows integration is the Start Menu shortcut, and nothing else. There is deliberately no
    /// installed-apps registry entry — see the class remarks. Returns whether the shortcut, the thing
    /// that makes the app findable, is on disk afterwards.
    /// </summary>
    private static bool RegisterWindows(string exePath)
    {
        if (!OperatingSystem.IsWindows()) return false;

        CreateShortcut(StartMenuShortcut, exePath, Path.GetDirectoryName(exePath)!,
            "Monitor for the TelePost LP-100A wattmeter");

        return File.Exists(StartMenuShortcut);
    }

    /// <summary>
    /// Called at every startup of an installed copy: re-asserts the Start Menu shortcut on Windows
    /// and the menu entry, icon and symlink on Linux, so a copy adopted from a pre-installer folder
    /// gets its integration without being reinstalled. Loose and portable copies are left alone.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Mode != InstallMode.Installed) return;
        Register(ExePath);
    }

    /// <summary>
    /// Whether the copy is launchable from the desktop environment's menu: the Start Menu shortcut
    /// on Windows, the <c>.desktop</c> entry on Linux.
    /// </summary>
    public static bool IsRegistered() =>
        File.Exists(OperatingSystem.IsWindows() ? StartMenuShortcut : DesktopFilePath);

    /// <summary>
    /// Remove the desktop integration, then hand off to a detached helper that deletes the install
    /// directory once this process has exited. The caller must exit immediately after.
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

        // Only ever delete a directory this app created. When Mode is Installed the folder is the
        // app's own and removing it whole is safe — it must never become a shared directory such as
        // ~/.local/bin, see SymlinkPath, which is removed as a single file for exactly that reason.
        // A Loose or Portable copy sits in a folder belonging to whoever put it there, routinely
        // Downloads or the Desktop, and a recursive delete would take everything else with it. Those
        // uninstalls remove the integration and leave the file where its owner left it.
        var toDelete = new List<string>();
        if (InstallLayout.OwnsExeDirectory(Mode)) toDelete.Add(ExeDirectory);
        toDelete.AddRange(DataFilesToRemove(options));

        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "lp100a-uninstall.ps1");
            var lines = new List<string>
            {
                $"while (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 300 }}",
            };
            // Retried for up to ten seconds rather than attempted once. The wait loop above sees the
            // process object vanish a beat before the kernel has released its executable mapping,
            // and a single Remove-Item in that gap fails on the locked exe — silently, because the
            // helper cannot show anything — and leaves the install folder behind with the program
            // still in it. Seen on 2026-09-04 when a hung uninstall was force-killed.
            lines.AddRange(toDelete.Select(p =>
            {
                var q = p.Replace("'", "''");
                return $"for ($i = 0; $i -lt 40 -and (Test-Path -LiteralPath '{q}'); $i++) {{ " +
                       $"Remove-Item -LiteralPath '{q}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                       $"if (Test-Path -LiteralPath '{q}') {{ Start-Sleep -Milliseconds 250 }} }}";
            }));
            // Take the helper with it, so an uninstall doesn't leave its own tooling behind in temp.
            lines.Add($"Remove-Item -LiteralPath '{script.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue");

            File.WriteAllText(script, string.Join("\n", lines) + "\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                // The helper must not inherit this process's working directory: an installed copy
                // runs with its own folder as the working directory, and Windows will not remove a
                // directory that is any live process's current directory — including the one doing
                // the removing. Without this the helper deletes the files and then fails on the
                // folder itself, every time, from inside it (2026-09-04).
                WorkingDirectory = Path.GetTempPath(),
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
                // Same reason as the Windows branch. Linux will unlink a directory that is a
                // process's cwd, but the helper then sits in a deleted directory, and nothing
                // about that is worth keeping.
                WorkingDirectory = Path.GetTempPath(),
            });
        }
    }

    /// <summary>
    /// Wrap a path in single quotes for /bin/sh, closing and reopening around any single quote it
    /// contains. Paths come from the environment, so they are not assumed to be tame.
    /// </summary>
    private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Which files under the data directory an uninstall should take. Named, never swept: the
    /// directory itself is not removed, so anything a later version puts there — or a log that a
    /// failed relocation left behind — survives an older uninstall.
    /// </summary>
    public static IEnumerable<string> DataFilesToRemove(UninstallOptions options)
    {
        if (options.RemoveSettings)
        {
            var config = Path.Combine(ConfigStore.DataDir, "config.json");
            if (File.Exists(config)) yield return config;
        }
    }

    private static void Unregister()
    {
        if (OperatingSystem.IsWindows())
        {
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
    /// <remarks>
    /// The working directory is set explicitly, and must stay that way. A child process otherwise
    /// inherits this one's, and after an install that is the folder the user just installed FROM --
    /// which Windows then refuses to delete, because a live process's current directory cannot be
    /// removed. The install appears to finish and the download folder becomes undeletable for as
    /// long as the app runs, with nothing on screen connecting the two.
    /// </remarks>
    public static void LaunchDetached(string exePath) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true,
        });

    /// <summary>Run a console tool with no window and return its exit code (-1 if it wouldn't start).</summary>
    private static int Run(string fileName, IEnumerable<string> arguments) =>
        RunCapture(fileName, arguments).Code;

    /// <summary>
    /// Run a console tool with no window and return its exit code and output (-1 if it wouldn't
    /// start). Both streams are read to completion *before* waiting: they are redirected, so a child
    /// that fills a pipe buffer while nobody drains it blocks forever, taking the wait with it.
    /// </summary>
    private static (int Code, string Out, string Err) RunCapture(string fileName, IEnumerable<string> arguments)
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
            if (p is null) return (-1, "", "");
            var so = p.StandardOutput.ReadToEnd();
            var se = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, so, se);
        }
        catch (System.ComponentModel.Win32Exception ex) { return (-1, "", ex.Message); }
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
