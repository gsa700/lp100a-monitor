using System.Globalization;

namespace Lp100a.Core;

/// <summary>Parses a single ';'-stripped LP-100A frame body (comma-separated fields).</summary>
public static class FrameParser
{
    private const int MinFields = 9;

    public static bool TryParse(string frameBody, out Lp100Reading reading)
    {
        reading = new Lp100Reading();
        if (string.IsNullOrWhiteSpace(frameBody)) return false;

        var f = frameBody.Split(',');
        if (f.Length < MinFields) return false;

        // All numeric fields are invariant-culture (dot decimal).
        if (!TryNum(f[0], out var fwd)) return false;
        if (!TryNum(f[1], out var z)) return false;
        if (!TryNum(f[2], out var phase)) return false;
        TryNum(f[3], out var alarm);
        TryInt(f[5], out var state);
        TryInt(f[6], out var field6);
        TryNum(f[7], out var dbm);
        if (!TryNum(f[8], out var swr)) return false;

        reading = new Lp100Reading
        {
            ForwardPowerW = fwd,
            ZOhms = z,
            PhaseDeg = phase,
            AlarmSetpoint = alarm,
            Callsign = f[4].Trim(),
            StateFlag = state,
            Field6 = field6,
            Dbm = dbm,
            Swr = swr,
        };
        return true;
    }

    private static bool TryNum(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
