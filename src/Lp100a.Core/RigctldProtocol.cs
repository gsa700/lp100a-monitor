using System.Globalization;

namespace Lp100a.Core;

/// <summary>
/// Pure parsing for the Hamlib <c>rigctld</c> network protocol. Kept separate from the socket
/// client so the wire-format handling is unit-testable without a daemon.
///
/// rigctld answers a short command with the bare value on a line: <c>f</c> → frequency in Hz,
/// <c>t</c> → PTT as 0/1. Failures come back as <c>RPRT -n</c>.
/// </summary>
public static class RigctldProtocol
{
    /// <summary>Hamlib's default rigctld port.</summary>
    public const int DefaultPort = 4532;

    /// <summary>Frequency query (get_freq).</summary>
    public const string FrequencyCommand = "f\n";

    /// <summary>PTT query (get_ptt).</summary>
    public const string PttCommand = "t\n";

    /// <summary>
    /// Parse a get_freq reply into Hz. Accepts a bare all-digit line, else falls back to the first
    /// long digit run in the response. Rejects <c>RPRT -n</c> errors and implausibly small values.
    /// </summary>
    public static bool TryParseFrequencyHz(string? response, out long hz)
    {
        hz = 0;
        if (string.IsNullOrWhiteSpace(response)) return false;

        // Preferred: a line that is nothing but digits (what rigctld actually sends).
        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 4 || !IsAllDigits(line)) continue;
            if (long.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out hz) && hz >= 10_000)
                return true;
        }

        // Fallback: the first run of >= 5 digits anywhere (tolerates chatty/extended responses).
        var run = LongestLeadingDigitRun(response, minLength: 5);
        if (run is not null &&
            long.TryParse(run, NumberStyles.Integer, CultureInfo.InvariantCulture, out hz) && hz >= 10_000)
            return true;

        hz = 0;
        return false;
    }

    /// <summary>Parse a get_freq reply into MHz.</summary>
    public static bool TryParseFrequencyMhz(string? response, out double mhz)
    {
        if (TryParseFrequencyHz(response, out var hz))
        {
            mhz = hz / 1_000_000.0;
            return true;
        }
        mhz = 0;
        return false;
    }

    /// <summary>
    /// Parse a get_ptt reply. Hamlib reports 0 for receive and non-zero (1/2/3, the PTT type) for
    /// transmit. Returns false when the rig/backend can't report PTT (<c>RPRT -n</c>).
    /// </summary>
    public static bool TryParsePtt(string? response, out bool transmitting)
    {
        transmitting = false;
        if (string.IsNullOrWhiteSpace(response)) return false;

        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !IsAllDigits(line)) continue;
            if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) continue;
            transmitting = v != 0;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Split a "host" or "host:port" string. A missing/blank port falls back to
    /// <see cref="DefaultPort"/>. Returns false only when the text is unusable.
    /// (IPv6 literals are not supported — rigctld is addressed by name or IPv4 in practice.)
    /// </summary>
    public static bool TryParseEndpoint(string? text, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim();
        var colon = t.LastIndexOf(':');
        if (colon < 0)
        {
            host = t;
            return true;
        }

        host = t[..colon].Trim();
        if (host.Length == 0) return false;

        var portText = t[(colon + 1)..].Trim();
        if (portText.Length == 0) return true;   // "host:" -> default port
        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
            || p <= 0 || p > 65535)
            return false;

        port = p;
        return true;
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
            if (c is < '0' or > '9') return false;
        return s.Length > 0;
    }

    private static string? LongestLeadingDigitRun(string s, int minLength)
    {
        var start = -1;
        for (var i = 0; i <= s.Length; i++)
        {
            var digit = i < s.Length && s[i] is >= '0' and <= '9';
            if (digit)
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                if (i - start >= minLength) return s[start..i];
                start = -1;
            }
        }
        return null;
    }
}
