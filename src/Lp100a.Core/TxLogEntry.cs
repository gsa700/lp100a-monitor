namespace Lp100a.Core;

/// <summary>
/// One row read back from the TX log CSV. Every field is nullable: a column can be legitimately
/// blank (frequency with no CAT source) or absent entirely (a log written by an older schema), and
/// a viewer should show a gap rather than a fabricated zero.
///
/// This is the read-side mirror of <see cref="TxOverRecord"/>. They are deliberately separate types:
/// the writer's fields are non-null values produced by the tracker, while anything parsed off disk
/// is untrusted and may be missing.
/// </summary>
public sealed record TxLogEntry
{
    public DateTime? Start { get; init; }
    public double? FreqMhz { get; init; }
    public int? DurationSeconds { get; init; }
    public double? PeakForwardW { get; init; }
    public double? MaxSwr { get; init; }
    public double? SwrAtPeak { get; init; }
    public double? MinReturnLossDb { get; init; }
    public double? ResistanceOhms { get; init; }
    public double? ReactanceOhms { get; init; }
    public double? PhaseDeg { get; init; }
    public int? PowerRange { get; init; }
    public bool? TimedOut { get; init; }

    /// <summary>Autorange scale as shown on the meter.</summary>
    public string RangeText => PowerRange switch
    {
        0 => "High",
        1 => "Mid",
        2 => "Low",
        _ => "",
    };

    public string TimedOutText => TimedOut switch
    {
        true => "yes",
        false => "",      // only the exceptions are worth ink in a table
        _ => "",
    };

    /// <summary>Load impedance as "48.9 + j5.7 Ω", or blank if it wasn't recorded.</summary>
    public string RxText =>
        ResistanceOhms is { } r && ReactanceOhms is { } x
            ? $"{r:0.0} {(x >= 0 ? "+" : "−")} j{Math.Abs(x):0.0}"
            : "";
}
