namespace Lp100a.Core;

/// <summary>What to do with one file found in the old log directory.</summary>
public enum RelocationAction
{
    /// <summary>Copy to the new directory, verify, then delete the original.</summary>
    Move,
    /// <summary>
    /// A file of the same name is already at the destination. Leave both alone: overwriting could
    /// destroy the newer copy, and deleting the old one could destroy the only complete one. A
    /// person can reconcile two files; nothing can reconcile a missing one.
    /// </summary>
    LeaveBecauseDestinationExists,
}

/// <summary>One step of a relocation plan.</summary>
public readonly record struct RelocationStep(string FileName, RelocationAction Action);

/// <summary>
/// Decides which files in the old data directory belong to the transmission log and what should
/// happen to each — pure, so the rules that could lose data are tested rather than trusted.
///
/// The log moved from the per-user app-data directory to Documents in v1.0.0-beta2, because it is
/// operating history rather than app state: the thing you open in a spreadsheet, keep for years,
/// and expect to be backed up. App data is none of those. Moving it also puts it out of reach of
/// uninstall by construction, which is a better guarantee than a dialog.
///
/// The log "family" is the live file plus the archives <see cref="TxLogWriter"/> sets aside on a
/// clear or a schema change: <c>TXlog.csv</c>, <c>TXlog_20260904221500.csv</c>,
/// <c>TXlog_20260904221500-2.csv</c>. The pattern is derived from the live file's name so a
/// rename can't quietly leave archives behind — the same rule the uninstaller used.
/// </summary>
public static class TxLogRelocationPlan
{
    /// <summary>
    /// Whether <paramref name="fileName"/> is the live log or one of its archives, given the live
    /// log's file name. <c>config.json</c> and any unrelated CSV are not.
    /// </summary>
    public static bool IsLogFamily(string fileName, string logFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(logFileName);
        var ext = Path.GetExtension(logFileName);

        if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return false;
        var name = fileName[..^ext.Length];

        if (string.Equals(name, stem, StringComparison.OrdinalIgnoreCase)) return true;

        // Archives are <stem>_<14 digit stamp> with an optional -<n> uniquifier. Requiring the
        // underscore and a digit keeps a hypothetical "TXlogbook.csv" out of the family.
        return name.Length > stem.Length + 1
            && name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
            && char.IsDigit(name[stem.Length + 1]);
    }

    /// <summary>
    /// Plan the relocation of every log-family file among <paramref name="sourceFileNames"/>.
    /// Files that already exist at the destination are left in place rather than overwritten.
    /// </summary>
    public static IReadOnlyList<RelocationStep> Plan(
        string logFileName,
        IEnumerable<string> sourceFileNames,
        Func<string, bool> existsAtDestination)
    {
        var steps = new List<RelocationStep>();
        foreach (var name in sourceFileNames)
        {
            if (!IsLogFamily(name, logFileName)) continue;
            steps.Add(new RelocationStep(name,
                existsAtDestination(name)
                    ? RelocationAction.LeaveBecauseDestinationExists
                    : RelocationAction.Move));
        }
        return steps;
    }
}
