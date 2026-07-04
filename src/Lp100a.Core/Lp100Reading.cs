using System.Globalization;

namespace Lp100a.Core;

/// <summary>
/// One decoded LP-100A serial frame.
/// Wire format (confirmed live on David's unit, 2026-07-03), frame delimited by a
/// leading ';' with NO CR/LF, fields comma-separated:
///   [0] forward power (W)   [1] Z magnitude (ohms)   [2] phase (deg)
///   [3] alarm setpoint      [4] callsign (6ch, space-padded)
///   [5] state flag (1=TX/RF present, 2=idle)          [6] unknown (constant 1)
///   [7] dBm                 [8] SWR
/// NOTE: power/dBm/Z/phase only populate when the meter is on the Watts screen.
/// </summary>
public sealed record Lp100Reading
{
    public double ForwardPowerW { get; init; }
    public double ZOhms { get; init; }
    public double PhaseDeg { get; init; }
    public double AlarmSetpoint { get; init; }
    public string Callsign { get; init; } = string.Empty;
    public int StateFlag { get; init; }
    public int Field6 { get; init; }
    public double Dbm { get; init; }
    public double Swr { get; init; }

    /// <summary>Resistive part of the load impedance, R = |Z|·cos(phase).</summary>
    public double ResistanceOhms => ZOhms * Math.Cos(PhaseDeg * Math.PI / 180.0);

    /// <summary>Reactive part of the load impedance, X = |Z|·sin(phase). +inductive / -capacitive.</summary>
    public double ReactanceOhms => ZOhms * Math.Sin(PhaseDeg * Math.PI / 180.0);

    /// <summary>Return loss in dB, derived from SWR. Capped at 60 dB for a perfect match.</summary>
    public double ReturnLossDb
    {
        get
        {
            if (Swr <= 1.0) return 60.0;
            var rl = -20.0 * Math.Log10((Swr - 1.0) / (Swr + 1.0));
            return double.IsFinite(rl) ? Math.Min(rl, 60.0) : 60.0;
        }
    }

    /// <summary>True when RF is present (per the state flag, with a power fallback).</summary>
    public bool IsTransmitting => StateFlag == 1 || ForwardPowerW > 0.0;

    public override string ToString() =>
        $"{ForwardPowerW:0.0}W SWR {Swr:0.00} Z {ZOhms:0.0}∠{PhaseDeg:0.0}° ({ResistanceOhms:0.0}{ReactanceOhms:+0.0;-0.0}j) {Dbm:0.0}dBm";
}
