namespace Lp100a.Core;

/// <summary>
/// Reads a value out of <c>reg.exe query</c> output. Pure string work so it can be tested without a
/// registry, and Windows-only in practice but not in dependency.
///
/// This exists to answer a question the app could not previously ask: not "is there an entry?" but
/// "did the entry I just wrote actually land?". Checking only that *some* entry exists is what let a
/// silent no-op import look like a successful install for three releases — an orphaned entry from a
/// previous version supplied the name the check was looking for.
///
/// The output format is one value per line: some leading spaces, the name, whitespace, the type
/// token, whitespace, then the data. The data may itself contain spaces (every path does), so it is
/// everything after the type token rather than the next whitespace-delimited field.
/// </summary>
public static class RegQuery
{
    /// <summary>
    /// The data of <paramref name="name"/>, or null if the output doesn't contain it. An empty
    /// string means the value exists and is empty, which is not the same as absent.
    /// </summary>
    public static string? Value(string output, string name)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r').TrimStart();
            if (line.Length == 0) continue;

            // The key path is echoed above its values and can contain the name as a path segment.
            if (line.StartsWith("HKEY", StringComparison.OrdinalIgnoreCase)) continue;

            if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;

            var after = line[name.Length..];
            // Guard against a prefix match: "DisplayName" must not answer for "DisplayNameEx".
            if (after.Length == 0 || !char.IsWhiteSpace(after[0])) continue;
            after = after.TrimStart();

            var typeEnd = after.IndexOfAny([' ', '\t']);
            if (typeEnd < 0)
            {
                // Type present with no data — an empty value prints as just the type.
                return after.StartsWith("REG_", StringComparison.OrdinalIgnoreCase) ? "" : null;
            }

            if (!after[..typeEnd].StartsWith("REG_", StringComparison.OrdinalIgnoreCase)) continue;
            return after[typeEnd..].Trim();
        }

        return null;
    }
}
