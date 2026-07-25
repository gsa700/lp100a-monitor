using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class TxLogReaderTests
{
    private static readonly string Header = TxOverRecord.CsvHeader;

    [Fact]
    public void ReadsAWrittenRowBackFaithfully()
    {
        // Round-trip against the real writer format rather than a hand-typed string, so a change to
        // ToCsvRow that the reader doesn't follow fails here.
        var over = new TxOverRecord
        {
            Start = new DateTime(2026, 7, 24, 16, 8, 4),
            FreqMhz = 14.074,
            DurationSeconds = 39,
            PeakForwardW = 1189.5,
            MaxSwr = 1.16,
            SwrAtPeak = 1.09,
            ResistanceOhms = 48.9,
            ReactanceOhms = 5.7,
            PhaseDeg = 6.7,
            PowerRange = 0,
            TimedOut = false,
        };

        var row = TxLogReader.Parse(new[] { Header, over.ToCsvRow() }).Single();

        Assert.Equal(over.Start, row.Start);
        Assert.Equal(14.074, row.FreqMhz!.Value, 4);
        Assert.Equal(39, row.DurationSeconds);
        Assert.Equal(1189.5, row.PeakForwardW!.Value, 3);
        Assert.Equal(1.16, row.MaxSwr!.Value, 3);
        Assert.Equal(1.09, row.SwrAtPeak!.Value, 3);
        Assert.Equal(48.9, row.ResistanceOhms!.Value, 3);
        Assert.Equal(5.7, row.ReactanceOhms!.Value, 3);
        Assert.Equal(0, row.PowerRange);
        Assert.False(row.TimedOut);
        Assert.Equal("High", row.RangeText);
        Assert.Equal("48.9 + j5.7", row.RxText);
    }

    [Fact]
    public void BlankFrequencyStaysNull()
    {
        var over = new TxOverRecord { Start = new DateTime(2026, 1, 1), FreqMhz = null, PeakForwardW = 100 };
        var row = TxLogReader.Parse(new[] { Header, over.ToCsvRow() }).Single();
        Assert.Null(row.FreqMhz);
    }

    [Fact]
    public void ColumnsAreResolvedByNameNotPosition()
    {
        // The whole point of name-based lookup: a log whose columns sit in a different order (or
        // that carries an extra column a later version added) must still read correctly.
        var lines = new[]
        {
            "PeakFwd_W,Timestamp,Channel,Freq_MHz",
            "1189.5,2026-07-24 16:08:04,A,14.0740",
        };
        var row = TxLogReader.Parse(lines).Single();

        Assert.Equal(1189.5, row.PeakForwardW!.Value, 3);
        Assert.Equal(new DateTime(2026, 7, 24, 16, 8, 4), row.Start);
        Assert.Equal(14.074, row.FreqMhz!.Value, 4);
        Assert.Null(row.MaxSwr);      // absent column -> null, not zero
    }

    [Fact]
    public void OlderSchemaDoesNotMislabelTheRenamedColumn()
    {
        // 0.9.7 wrote MinSWR where 0.9.8+ writes SWR_at_peak — same position, same column count.
        // Position-based parsing would silently show the old value under the new name.
        var lines = new[]
        {
            "Timestamp,Freq_MHz,Duration_s,PeakFwd_W,MaxSWR,MinSWR,MinReturnLoss_dB,R_ohm,X_ohm,Phase_deg,Range,TimedOut",
            "2026-07-20 17:34:47,,2,1097.3,1.19,1.00,21.2,56.4,3.9,4.0,0,no",
        };
        var row = TxLogReader.Parse(lines).Single();

        Assert.Equal(1097.3, row.PeakForwardW!.Value, 3);
        Assert.Equal(1.19, row.MaxSwr!.Value, 3);
        Assert.Null(row.SwrAtPeak);   // that column genuinely isn't in this file
    }

    [Fact]
    public void HeaderBomIsIgnored()
    {
        var row = TxLogReader.Parse(new[] { "﻿" + Header, "2026-07-24 16:08:04,,5,100.0,1.10,1.05,26.4,50.0,1.0,1.1,1,no" }).Single();
        Assert.Equal(new DateTime(2026, 7, 24, 16, 8, 4), row.Start);   // needs the BOM stripped to match "Timestamp"
        Assert.Equal("Mid", row.RangeText);
    }

    [Fact]
    public void SkipsBlankLinesAndTolerateShortRows()
    {
        var lines = new[] { Header, "", "2026-07-24 16:08:04,,5", "" };
        var row = TxLogReader.Parse(lines).Single();
        Assert.Equal(5, row.DurationSeconds);
        Assert.Null(row.PeakForwardW);   // truncated row -> nulls, no exception
    }

    [Fact]
    public void EmptyOrHeaderOnlyFileReadsAsNoRows()
    {
        Assert.Empty(TxLogReader.Parse(Array.Empty<string>()));
        Assert.Empty(TxLogReader.Parse(new[] { Header }));
    }

    [Fact]
    public void MissingFileReadsAsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "lp100a-does-not-exist-" + Guid.NewGuid().ToString("N") + ".csv");
        Assert.Empty(TxLogReader.Read(path));
    }

    [Fact]
    public void NegativeReactanceRendersWithMinusSign()
    {
        var over = new TxOverRecord { Start = new DateTime(2026, 1, 1), ResistanceOhms = 48.2, ReactanceOhms = -6.1 };
        var row = TxLogReader.Parse(new[] { Header, over.ToCsvRow() }).Single();
        Assert.Equal("48.2 − j6.1", row.RxText);
    }
}
