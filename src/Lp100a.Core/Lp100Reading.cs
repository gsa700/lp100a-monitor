using System.Globalization;

namespace Lp100a.Core;

/// <summary>
/// One decoded LP-100A serial frame.
/// Wire format per the official LP-100A manual (Software, p.20), frame delimited by a
/// leading ';' with NO CR/LF, fields comma-separated:
///   [0] power (W)           [1] Z magnitude (ohms)   [2] phase (deg)
///   [3] alarm setpoint index (0=off,1=1.5,2=2.0,3=2.5,4=3.0,5=User)
///   [4] callsign (6ch, space-padded)
///   [5] power range / autorange scale (0=High, 1=Mid, 2=Low)
///   [6] meter power mode (0=Average, 1=Peak, 2=Tune)
///   [7] dBm                 [8] SWR
/// NOTE: power/dBm/Z/phase only populate when the meter is on the Watts screen.
/// </summary>
public sealed record Lp100Reading
{
    public double ForwardPowerW { get; init; }
    public double ZOhms { get; init; }
    public double PhaseDeg { get; init; }
    public int AlarmIndex { get; init; }   // [3] 0=off,1=1.5,2=2.0,3=2.5,4=3.0,5=User
    public string Callsign { get; init; } = string.Empty;
    public int PowerRange { get; init; }   // [5] 0=High, 1=Mid, 2=Low (autorange scale, NOT a TX flag)
    public int MeterMode { get; init; }    // [6] 0=Average, 1=Peak, 2=Tune
    public double Dbm { get; init; }
    public double Swr { get; init; }

    /// <summary>The meter's active power mode as shown on-screen (Avg/Peak/Tune).</summary>
    public string MeterModeText => MeterMode switch
    {
        0 => "AVG",
        1 => "PEAK",
        2 => "TUNE",
        _ => "—",
    };

    /// <summary>The meter's SWR alarm setpoint as shown on-screen.</summary>
    public string AlarmSetpointText => AlarmIndex switch
    {
        0 => "OFF",
        1 => "1.5",
        2 => "2.0",
        3 => "2.5",
        4 => "3.0",
        5 => "USER",
        _ => "—",
    };

    /// <summary>Numeric SWR trip point for the preset setpoints (1.5–3.0). Null for OFF and
    /// User — the meter does not send the User value over serial, so it can't be echoed.</summary>
    public double? AlarmThreshold => AlarmIndex switch
    {
        1 => 1.5,
        2 => 2.0,
        3 => 2.5,
        4 => 3.0,
        _ => null,
    };

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

    /// <summary>True when RF is present. Field [5] is the autorange scale, not a TX flag,
    /// so transmit is detected purely from forward power being above zero.</summary>
    public bool IsTransmitting => ForwardPowerW > 0.0;

    public override string ToString() =>
        $"{ForwardPowerW:0.0}W SWR {Swr:0.00} Z {ZOhms:0.0}∠{PhaseDeg:0.0}° ({ResistanceOhms:0.0}{ReactanceOhms:+0.0;-0.0}j) {Dbm:0.0}dBm";
}
