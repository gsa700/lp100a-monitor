using System.Text;
using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class TxLogWriterTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public TxLogWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lp100a-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "TXlog.csv");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static TxOverRecord Over(int seconds) => new()
    {
        Start = new DateTime(2026, 7, 20, 0, 0, 0).AddSeconds(seconds),
        DurationSeconds = seconds,
        PeakForwardW = 100,
        MaxSwr = 1.2,
        SwrAtPeak = 1.1,
        PowerRange = 0,
    };

    [Fact]
    public void WritesHeaderThenAppendsRows()
    {
        var w = new TxLogWriter(_path);
        w.Append(Over(1));
        w.Append(Over(2));

        var lines = File.ReadAllLines(_path, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
        Assert.Equal(TxOverRecord.CsvHeader, lines[0]);
        Assert.Equal(3, lines.Length);   // header + 2 rows
    }

    [Fact]
    public void RollingCapKeepsNewestRows()
    {
        var w = new TxLogWriter(_path, maxRows: 3);
        for (var i = 1; i <= 5; i++) w.Append(Over(i));

        var lines = File.ReadAllLines(_path, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
        Assert.Equal(4, lines.Length);   // header + 3 data rows

        // Duration_s is column index 2; the newest three overs (3s, 4s, 5s) survive, in order.
        var durations = lines.Skip(1).Select(l => int.Parse(l.Split(',')[2])).ToArray();
        Assert.Equal(new[] { 3, 4, 5 }, durations);
    }

    [Fact]
    public void ArchiveMovesTheLogAsideAndLeavesNoActiveLog()
    {
        var w = new TxLogWriter(_path, clock: () => new DateTime(2026, 7, 25, 9, 15, 0));
        w.Append(Over(1));
        w.Append(Over(2));

        var archived = w.Archive();

        Assert.NotNull(archived);
        Assert.False(File.Exists(_path), "the active log should be gone after archiving");
        Assert.True(File.Exists(archived!), "the archive should exist");
        Assert.Equal(Path.Combine(_dir, "TXlog_20260725091500.csv"), archived);
        // The rows are preserved, not destroyed: header + the two overs.
        Assert.Equal(3, File.ReadAllLines(archived!).Where(l => l.Length > 0).ToArray().Length);
    }

    [Fact]
    public void ArchiveOnEmptyLogIsANoOp()
    {
        var w = new TxLogWriter(_path);
        Assert.Null(w.Archive());          // nothing written yet
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void AppendAfterArchiveStartsAFreshLog()
    {
        var w = new TxLogWriter(_path, clock: () => new DateTime(2026, 7, 25, 9, 15, 0));
        w.Append(Over(1));
        w.Archive();
        w.Append(Over(9));

        var lines = File.ReadAllLines(_path, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
        Assert.Equal(TxOverRecord.CsvHeader, lines[0]);
        Assert.Equal(2, lines.Length);                          // header + only the new over
        Assert.Equal(9, int.Parse(lines[1].Split(',')[2]));      // Duration_s of the new row
    }

    [Fact]
    public void TwoArchivesInTheSameSecondDoNotOverwriteEachOther()
    {
        // The stamp is per-second, so without uniquifying, clearing twice quickly would destroy
        // the first archive — exactly the data loss archiving is meant to prevent.
        var frozen = new DateTime(2026, 7, 25, 9, 15, 0);
        var w = new TxLogWriter(_path, clock: () => frozen);

        w.Append(Over(1));
        var first = w.Archive();
        w.Append(Over(2));
        var second = w.Archive();

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first!));
        Assert.True(File.Exists(second!));
        Assert.Equal(1, int.Parse(File.ReadAllLines(first!)[1].Split(',')[2]));
        Assert.Equal(2, int.Parse(File.ReadAllLines(second!)[1].Split(',')[2]));
    }

    [Fact]
    public void MismatchedHeaderIsArchivedAsideNotCorrupted()
    {
        File.WriteAllText(_path, "Old,Schema,Header" + Environment.NewLine + "junk,row,here" + Environment.NewLine, Encoding.UTF8);

        var w = new TxLogWriter(_path, clock: () => new DateTime(2026, 7, 20, 8, 30, 0));
        w.Append(Over(1));

        var lines = File.ReadAllLines(_path, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
        Assert.Equal(TxOverRecord.CsvHeader, lines[0]);      // fresh file with the current schema
        Assert.Equal(2, lines.Length);                       // header + the one new row

        var archived = Path.Combine(_dir, "TXlog_20260720083000.csv");
        Assert.True(File.Exists(archived), "old log should be archived aside");
        Assert.Contains("Old,Schema,Header", File.ReadAllText(archived));
    }
}
