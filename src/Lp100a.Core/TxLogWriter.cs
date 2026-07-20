using System.Globalization;
using System.Text;

namespace Lp100a.Core;

/// <summary>
/// Appends completed overs to a UTF-8 CSV, keeping a rolling cap of the most recent rows. If an
/// existing log's first line doesn't match the current schema it is archived aside
/// (<c>…_yyyyMMddHHmmss.csv</c>) so a schema change never mixes into or corrupts old data.
///
/// This is thin IO and is not thread-safe; drive it from a single writer. IO exceptions propagate
/// — the caller (which runs this off the serial poll tick) is expected to catch and surface them.
/// </summary>
public sealed class TxLogWriter
{
    private readonly string _path;
    private readonly int _maxRows;
    private readonly Func<DateTime> _clock;

    /// <param name="path">CSV file path.</param>
    /// <param name="maxRows">Rolling cap on data rows (header excluded). Default 2000.</param>
    /// <param name="clock">Time source for the archive-aside suffix. Defaults to <see cref="DateTime.Now"/>.</param>
    public TxLogWriter(string path, int maxRows = 2000, Func<DateTime>? clock = null)
    {
        _path = path;
        _maxRows = Math.Max(1, maxRows);
        _clock = clock ?? (() => DateTime.Now);
    }

    public string Path => _path;

    /// <summary>Append one over, then trim the file to its newest <c>maxRows</c> data rows.</summary>
    public void Append(TxOverRecord over)
    {
        EnsureHeader();
        File.AppendAllText(_path, over.ToCsvRow() + Environment.NewLine, Encoding.UTF8);
        Trim();
    }

    private void EnsureHeader()
    {
        if (File.Exists(_path))
        {
            var first = File.ReadLines(_path, Encoding.UTF8).FirstOrDefault();
            if (first is not null && first != TxOverRecord.CsvHeader)
                ArchiveAside();
        }
        if (!File.Exists(_path))
            File.WriteAllText(_path, TxOverRecord.CsvHeader + Environment.NewLine, Encoding.UTF8);
    }

    private void ArchiveAside()
    {
        var stamp = _clock().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var dir = System.IO.Path.GetDirectoryName(_path) ?? string.Empty;
        var name = System.IO.Path.GetFileNameWithoutExtension(_path);
        var ext = System.IO.Path.GetExtension(_path);
        File.Move(_path, System.IO.Path.Combine(dir, $"{name}_{stamp}{ext}"), overwrite: true);
    }

    private void Trim()
    {
        var lines = File.ReadAllLines(_path, Encoding.UTF8).Where(l => l.Length > 0).ToList();
        if (lines.Count - 1 <= _maxRows) return;   // header + data rows within cap
        var kept = new List<string> { lines[0] };
        kept.AddRange(lines.Skip(lines.Count - _maxRows));
        File.WriteAllLines(_path, kept, Encoding.UTF8);
    }
}
