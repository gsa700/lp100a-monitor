namespace Lp100a.Core;

/// <summary>
/// Turns raw serial exceptions into actionable, platform-aware messages. Ported from the W2
/// monitor. On Windows an access-denied usually means another app already holds the port. On
/// Linux/Pi it is one of two things — the user isn't in the 'dialout' group, or another program has
/// the port open (System.IO.Ports takes a lock on open and reports a held port with the very same
/// exception) — and the message has to name both, the in-use case first: an operator already in
/// the group was sent off to usermod when the real answer was "Shack Power has it" (1.0.1).
/// </summary>
public static class SerialErrors
{
    public static string Describe(Exception ex, string port, bool isLinux, bool reconnecting = false)
    {
        // A transient access error mid-reconnect just means the device is still re-enumerating after a
        // replug or a resume from sleep (on Linux udev hasn't re-applied the 'dialout' perms yet; on
        // Windows the old handle is still tearing down) — not a real permissions problem. Skip the
        // alarming dialout / "another app" hint and show a calm status; a genuine first-connect denial
        // (reconnecting: false) still gets it.
        if (reconnecting && ex is UnauthorizedAccessException)
            return $"{port} reconnecting…";

        return ex switch
        {
            UnauthorizedAccessException when isLinux =>
                $"{port} is in use or access denied — if another program has it open, close that. " +
                "Otherwise add your user to the 'dialout' group: sudo usermod -aG dialout $USER " +
                "(then log out and back in).",
            UnauthorizedAccessException =>
                $"{port} is in use or access denied — another app may have it open.",
            FileNotFoundException =>
                $"{port} not found — is the LP-100A plugged in?",
            IOException =>
                $"{port} could not be opened — check the cable and that the LP-100A is powered.",
            _ => $"Error: {ex.Message}",
        };
    }
}
