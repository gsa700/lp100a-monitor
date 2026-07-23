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
