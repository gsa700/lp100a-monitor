using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Lp100a.Core;

namespace Lp100a.App.Services;

/// <summary>Outcome of a relocation run, for a one-line notice if anything didn't move.</summary>
/// <param name="Moved">Files copied, verified and removed from the old directory.</param>
/// <param name="Left">Files left in the old directory because the destination already had them.</param>
/// <param name="Failures">Files that could not be moved, with why. Their originals are untouched.</param>
public readonly record struct RelocationResult(int Moved, int Left, IReadOnlyList<string> Failures)
{
    public bool DidAnything => Moved > 0 || Left > 0 || Failures.Count > 0;
}

/// <summary>
/// Moves the transmission log and its archives from the old app-data directory to Documents. Runs
/// once per start, before the logger opens the file; a run with nothing in the old directory costs
/// one directory listing.
/// </summary>
/// <remarks>
/// Copy, verify, then delete — never move-and-hope. The log is the one thing in this app nothing
/// can reconstruct, so each file is copied, both copies are hashed, and only a byte-identical
/// destination earns the deletion of the original. Any mismatch or error leaves the original where
/// it was, discards the partial copy, and is reported rather than swallowed. The decision of *which*
/// files and what to do about a name clash is <see cref="TxLogRelocationPlan"/>, in Core, so it is
/// tested; this class is the IO.
/// </remarks>
public static class TxLogRelocator
{
    public static RelocationResult Run(string sourceDir, string destinationDir, string logFileName)
    {
        var failures = new List<string>();
        int moved = 0, left = 0;

        if (!Directory.Exists(sourceDir)) return new RelocationResult(0, 0, failures);

        string[] names;
        try
        {
            names = Directory.EnumerateFiles(sourceDir).Select(Path.GetFileName).OfType<string>().ToArray();
        }
        catch (IOException ex) { return new RelocationResult(0, 0, [$"{sourceDir}: {ex.Message}"]); }
        catch (UnauthorizedAccessException ex) { return new RelocationResult(0, 0, [$"{sourceDir}: {ex.Message}"]); }

        var plan = TxLogRelocationPlan.Plan(logFileName, names,
            existsAtDestination: n => File.Exists(Path.Combine(destinationDir, n)));
        if (plan.Count == 0) return new RelocationResult(0, 0, failures);

        try { Directory.CreateDirectory(destinationDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RelocationResult(0, 0, [$"{destinationDir}: {ex.Message}"]);
        }

        foreach (var step in plan)
        {
            if (step.Action == RelocationAction.LeaveBecauseDestinationExists) { left++; continue; }

            var src = Path.Combine(sourceDir, step.FileName);
            var dst = Path.Combine(destinationDir, step.FileName);
            try
            {
                File.Copy(src, dst, overwrite: false);
                if (!SameBytes(src, dst))
                {
                    TryDelete(dst);
                    failures.Add($"{step.FileName}: copy did not match the original; left in place");
                    continue;
                }
                File.Delete(src);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A half-written destination must not be mistaken for the real thing on the next run,
                // where the plan would then leave the original behind as a "clash".
                TryDelete(dst);
                failures.Add($"{step.FileName}: {ex.Message}");
            }
        }

        return new RelocationResult(moved, left, failures);
    }

    private static bool SameBytes(string a, string b)
    {
        using var ha = SHA256.Create();
        using var hb = SHA256.Create();
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return ha.ComputeHash(fa).AsSpan().SequenceEqual(hb.ComputeHash(fb));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
