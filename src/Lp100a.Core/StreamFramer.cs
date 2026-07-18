using System.Text;

namespace Lp100a.Core;

/// <summary>
/// Reassembles LP-100A frames from a raw byte/char stream. Frames are delimited by
/// a leading ';' and have NO trailing CR/LF, so we split on ';' and hold any partial
/// trailing fragment until the next chunk completes it.
/// </summary>
public sealed class StreamFramer
{
    // Cap on the retained partial fragment. A real LP-100A frame is well under this; the cap
    // only trips on a stream that never sends ';' (e.g. the wrong COM port), so the fragment
    // buffer can't grow without bound.
    private const int MaxTail = 4096;

    private readonly StringBuilder _acc = new();

    /// <summary>Feed a chunk of decoded text; returns any complete frame bodies (';' stripped).</summary>
    public List<string> Feed(string chunk)
    {
        var frames = new List<string>();
        _acc.Append(chunk);

        var s = _acc.ToString();
        var parts = s.Split(';');

        // Everything except the last part is a complete frame body.
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var body = parts[i].Trim();
            if (body.Length > 0) frames.Add(body);
        }

        // Keep the (possibly partial) tail for next time — unless it has grown past the cap
        // without a delimiter, in which case drop it so a non-LP-100A stream can't accumulate.
        _acc.Clear();
        var tail = parts[^1];
        if (tail.Length <= MaxTail) _acc.Append(tail);
        return frames;
    }

    public void Reset() => _acc.Clear();
}
