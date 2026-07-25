using System.Globalization;
using System.Text;

namespace Lp100a.Core;

/// <summary>
/// Reads the TX log CSV back into <see cref="TxLogEntry"/> rows for the in-app log viewer.
///
/// Columns are resolved **by header name**, not by position. The schema has already changed once
/// (MinSWR became SWR_at_peak, same column count) and will change again for dual-channel, so
/// index-based parsing could silently mislabel a column in a log written by another version.
/// Unknown columns are ignored and missing ones read as null.
///
/// Splitting on ',' is sufficient because <see cref="TxOverRecord.ToCsvRow"/> writes only
/// invariant-culture fixed-point numbers and fixed vocabulary — no field can contain a comma or a
/// quote. If that ever stops being true, this needs a real CSV parser.
/// </summary>
public static class TxLogReader
{
    /// <summary>Read every row, oldest first. A missing file reads as empty, not an error.</summary>
    public static IReadOnlyList<TxLogEntry> Read(string path)
    {
        if (!File.Exists(path)) return Array.Empty<TxLogEntry>();
        return Parse(File.ReadAllLines(path, Encoding.UTF8));
    }

    /// <summary>Parse pre-read lines (header first). Exposed for testing without touching disk.</summary>
    public static IReadOnlyList<TxLogEntry> Parse(IReadOnlyList<string> lines)
    {
        var header = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (header is null) return Array.Empty<TxLogEntry>();

        // Strip a UTF-8 BOM so the first column name still matches.
        var index = BuildIndex(header.TrimStart('﻿'));
        var rows = new List<TxLogEntry>();

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split(',');
            rows.Add(new TxLogEntry
            {
                Start = Time(Cell(cells, index, "Timestamp")),
                FreqMhz = Num(Cell(cells, index, "Freq_MHz")),
                DurationSeconds = Int(Cell(cells, index, "Duration_s")),
                PeakForwardW = Num(Cell(cells, index, "PeakFwd_W")),
                MaxSwr = Num(Cell(cells, index, "MaxSWR")),
                SwrAtPeak = Num(Cell(cells, index, "SWR_at_peak")),
                MinReturnLossDb = Num(Cell(cells, index, "MinReturnLoss_dB")),
                ResistanceOhms = Num(Cell(cells, index, "R_ohm")),
                ReactanceOhms = Num(Cell(cells, index, "X_ohm")),
                PhaseDeg = Num(Cell(cells, index, "Phase_deg")),
                PowerRange = Int(Cell(cells, index, "Range")),
                TimedOut = Flag(Cell(cells, index, "TimedOut")),
            });
        }
        return rows;
    }

    private static Dictionary<string, int> BuildIndex(string header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = header.Split(',');
        for (var i = 0; i < names.Length; i++)
            map[names[i].Trim()] = i;      // a duplicated name keeps the last; not expected
        return map;
    }

    private static string? Cell(string[] cells, Dictionary<string, int> index, string column) =>
        index.TryGetValue(column, out var i) && i < cells.Length ? cells[i].Trim() : null;

    private static double? Num(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? Int(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? Time(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var v) ? v : null;

    private static bool? Flag(string? s) => s switch
    {
        "yes" => true,
        "no" => false,
        _ => null,
    };
}
