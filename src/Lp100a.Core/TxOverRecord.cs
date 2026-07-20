using System.Globalization;

namespace Lp100a.Core;

/// <summary>
/// One completed transmission ("over"): the aggregated result of a single key-down, emitted by
/// <see cref="TxOverTracker"/> and written as one CSV row by <see cref="TxLogWriter"/>.
///
/// Impedance and range are sampled at the moment of peak forward power — the most meaningful
/// point in the over. Frequency comes from an external CAT source and is null when none is bound.
/// </summary>
public sealed record TxOverRecord
{
    /// <summary>Local start time of the over.</summary>
    public DateTime Start { get; init; }

    /// <summary>Operating frequency (MHz) from the bound CAT radio, or null if unbound.</summary>
    public double? FreqMhz { get; init; }

    public int DurationSeconds { get; init; }
    public double PeakForwardW { get; init; }
    public double MaxSwr { get; init; }
    public double MinSwr { get; init; }

    /// <summary>Resistive part of the load at peak power, R = |Z|·cos(phase).</summary>
    public double ResistanceOhms { get; init; }

    /// <summary>Reactive part of the load at peak power. +inductive / -capacitive.</summary>
    public double ReactanceOhms { get; init; }

    /// <summary>Phase at peak power (deg).</summary>
    public double PhaseDeg { get; init; }

    /// <summary>Autorange scale at peak power (0=High, 1=Mid, 2=Low).</summary>
    public int PowerRange { get; init; }

    public bool TimedOut { get; init; }

    /// <summary>Return loss (dB) at the worst (max) SWR of the over. Capped at 60 dB, matching
    /// <see cref="Lp100Reading.ReturnLossDb"/>.</summary>
    public double MinReturnLossDb
    {
        get
        {
            if (MaxSwr <= 1.0) return 60.0;
            var rl = -20.0 * Math.Log10((MaxSwr - 1.0) / (MaxSwr + 1.0));
            return double.IsFinite(rl) ? Math.Min(rl, 60.0) : 60.0;
        }
    }

    /// <summary>CSV header matching <see cref="ToCsvRow"/>. <see cref="TxLogWriter"/> archives any
    /// existing log whose first line differs from this, so the schema can evolve safely.</summary>
    public const string CsvHeader =
        "Timestamp,Freq_MHz,Duration_s,PeakFwd_W,MaxSWR,MinSWR,MinReturnLoss_dB,R_ohm,X_ohm,Phase_deg,Range,TimedOut";

    /// <summary>One CSV row (invariant culture) with columns matching <see cref="CsvHeader"/>.</summary>
    public string ToCsvRow()
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(',',
            Start.ToString("yyyy-MM-dd HH:mm:ss", inv),
            FreqMhz is { } f ? f.ToString("N4", inv) : "",
            DurationSeconds.ToString(inv),
            PeakForwardW.ToString("N1", inv),
            MaxSwr.ToString("N2", inv),
            MinSwr.ToString("N2", inv),
            MinReturnLossDb.ToString("N1", inv),
            ResistanceOhms.ToString("N1", inv),
            ReactanceOhms.ToString("N1", inv),
            PhaseDeg.ToString("N1", inv),
            PowerRange.ToString(inv),
            TimedOut ? "yes" : "no");
    }
}
