namespace Lp100a.Core;

/// <summary>
/// The two decisions that keep the saved meter pin honest. Pure, so both are tested — the 1.0.0
/// regression they guard against shipped precisely because they lived inline in the view-model and
/// the app's close handler, where nothing exercised the "meter absent" case.
///
/// The pin is the adapter's chip serial (Windows) or its <c>/dev/serial/by-id</c> name (Linux), plus
/// the last port it was seen on. It exists so the meter is found again after a renumber. It must
/// therefore only ever describe the meter — and the meter is only known through a connection.
/// </summary>
public static class PortPin
{
    /// <summary>
    /// What to save on exit. Only a port the app actually connected to this run may replace the
    /// saved pin. A port that was merely showing in the Setup list is not a pin, however it got
    /// there — and in 1.0.0 it got there by <see cref="Reselect"/>'s predecessor guessing.
    /// </summary>
    /// <param name="connectedPort">The port of this run's connection, or null if there was none.</param>
    /// <param name="serialFor">Looks up the pin identity for a port; null when the device isn't present.</param>
    /// <param name="savedPort">What was loaded at startup.</param>
    /// <param name="savedSerial">What was loaded at startup.</param>
    /// <remarks>
    /// When the connected device has gone by the time of the save (unplugged mid-session), its serial
    /// can't be read back, and the saved serial is kept: the port is updated, the identity is not
    /// thrown away. The serial is what a later launch resolves by, so it wins over a stale port name.
    /// </remarks>
    public static (string? Port, string? Serial) ForSave(
        string? connectedPort, Func<string, string?> serialFor, string? savedPort, string? savedSerial)
    {
        if (connectedPort is null) return (savedPort, savedSerial);
        return (connectedPort, serialFor(connectedPort) ?? savedSerial);
    }

    /// <summary>
    /// Which entry the Setup list shows selected after a refresh: the one already selected if it is
    /// still present, otherwise none. Never a guess. On a fresh install nothing is selected and the
    /// user picks — which is what the README has always said to do.
    /// </summary>
    /// <remarks>
    /// The previous rule fell back to the first port in the list. With the meter unplugged that
    /// silently showed some other device as "selected", and on the way out the app saved it as the
    /// meter. Case-insensitive because Windows port names are; returns the list's own spelling.
    /// </remarks>
    public static string? Reselect(string? current, IEnumerable<string> ports)
    {
        if (current is null) return null;
        foreach (var p in ports)
            if (string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}
