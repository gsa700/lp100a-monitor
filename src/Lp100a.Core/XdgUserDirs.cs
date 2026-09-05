namespace Lp100a.Core;

/// <summary>
/// Reads a directory out of <c>~/.config/user-dirs.dirs</c>, the freedesktop file that says where a
/// user's Documents, Desktop and so on actually are. Ported from the W2 monitor.
///
/// This exists because <c>~/Documents</c> is an assumption, not a fact. The directory is localised —
/// <c>~/Documentos</c>, <c>~/Documents</c>, <c>~/ドキュメント</c> — and a user can point it anywhere
/// or switch it off. .NET's <c>SpecialFolder.MyDocuments</c> does not consult this file on Linux; it
/// returns the home directory, which would put the transmission log at the top of <c>$HOME</c>.
///
/// Pure parsing, given the file's contents: the format is quoted, <c>$HOME</c>-relative and
/// comment-bearing, which is three ways to get it subtly wrong on a machine that isn't in front of
/// you, so it is tested rather than eyeballed.
/// </summary>
public static class XdgUserDirs
{
    /// <summary>Key naming the documents directory.</summary>
    public const string DocumentsKey = "XDG_DOCUMENTS_DIR";

    /// <summary>
    /// Resolve <paramref name="key"/> from the contents of <c>user-dirs.dirs</c>. Returns null when
    /// the key is absent, empty, or set to the home directory itself.
    /// </summary>
    /// <param name="home">Value to substitute for <c>$HOME</c>.</param>
    /// <remarks>
    /// A key set to <c>"$HOME/"</c> means "this user has no such directory" by convention. Callers
    /// choose their own fallback for that — for the log it is <c>~/Documents</c>, created on demand,
    /// rather than scattering a CSV into the top of someone's home directory.
    /// </remarks>
    public static string? Resolve(string? contents, string key, string home)
    {
        if (string.IsNullOrEmpty(contents)) return null;

        string? found = null;
        foreach (var raw in contents.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0 || line[..eq].Trim() != key) continue;

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            found = value;   // last assignment wins, matching how the shell would read the file
        }

        if (string.IsNullOrWhiteSpace(found)) return null;

        var path = found.StartsWith("$HOME", StringComparison.Ordinal)
            ? home + found["$HOME".Length..]
            : found;

        path = path.TrimEnd('/');
        if (path.Length == 0) return null;

        // "$HOME/" collapses to the home directory, which is the convention for "no such directory".
        return string.Equals(path, home.TrimEnd('/'), StringComparison.Ordinal) ? null : path;
    }
}
