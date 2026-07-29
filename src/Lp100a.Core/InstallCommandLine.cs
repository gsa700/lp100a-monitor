namespace Lp100a.Core;

/// <summary>What the command line asked the app to do about installation.</summary>
public enum InstallAction
{
    /// <summary>Nothing install-related was asked; run the app normally.</summary>
    None,

    /// <summary>Install this copy into the per-user install directory, then exit.</summary>
    Install,

    /// <summary>Remove the installed copy and its OS registrations, then exit.</summary>
    Uninstall,
}

/// <summary>An install request parsed off the command line.</summary>
/// <param name="Action">What was asked.</param>
/// <param name="Quiet">
/// Suppress every prompt and answer conservatively. Drives unattended installs, and it is what the
/// installed-apps entry passes on removal, since Windows gives the user no way to answer a dialog
/// it didn't expect.
/// </param>
/// <param name="KeepData">
/// Uninstall only: leave the app-data directory alone. Quiet uninstalls set this, because the
/// directory holds the transmission log as well as settings and silently deleting operating
/// history is not a defensible default.
/// </param>
public readonly record struct InstallRequest(InstallAction Action, bool Quiet, bool KeepData);

/// <summary>
/// Parses the install-related switches. Pure and separate from the app's startup so the precedence
/// rules are testable without launching a UI.
/// </summary>
public static class InstallCommandLine
{
    /// <summary>
    /// Read an <see cref="InstallRequest"/> from raw args. Unknown arguments are ignored — Avalonia
    /// and the OS both pass switches of their own, and an unrecognised one must never be mistaken
    /// for an instruction to modify the machine.
    /// </summary>
    /// <remarks>
    /// <c>--uninstall</c> beats <c>--install</c> if somebody passes both. Between two contradictory
    /// instructions, the one that ends up doing less to the machine is the safer reading.
    /// </remarks>
    public static InstallRequest Parse(IEnumerable<string>? args)
    {
        if (args is null) return new InstallRequest(InstallAction.None, false, false);

        var install = false;
        var uninstall = false;
        var quiet = false;
        var keepData = false;

        foreach (var raw in args)
        {
            switch (Canonical(raw))
            {
                case "install": install = true; break;
                case "uninstall": uninstall = true; break;
                case "quiet": quiet = true; break;
                case "keep-data": keepData = true; break;
            }
        }

        var action = uninstall ? InstallAction.Uninstall
                   : install ? InstallAction.Install
                   : InstallAction.None;

        // An unattended uninstall keeps the data directory whether or not it was asked: there is
        // nobody present to be asked about the transmission log, and it can't be brought back.
        if (action == InstallAction.Uninstall && quiet) keepData = true;

        return new InstallRequest(action, quiet, keepData);
    }

    /// <summary>
    /// Strip a leading <c>--</c>, <c>-</c> or <c>/</c> and lower-case, so <c>--Quiet</c>,
    /// <c>/quiet</c> and <c>-quiet</c> all read the same. Windows shortcuts and the installed-apps
    /// entry each have their own habits about switch prefixes.
    /// </summary>
    private static string Canonical(string arg)
    {
        var s = arg.Trim();
        if (s.StartsWith("--", StringComparison.Ordinal)) s = s[2..];
        else if (s.StartsWith('-') || s.StartsWith('/')) s = s[1..];
        return s.ToLowerInvariant();
    }
}
