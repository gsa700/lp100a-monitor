namespace Lp100a.Core;

/// <summary>
/// Decides when a serial link has gone dead so the reader can drop it and reconnect. Ported from
/// the W2 monitor, where the same class recovers a meter across a USB replug; the thresholds here
/// are the LP-100A's own, for the reason below.
///
/// Two failure modes, deliberately weighted differently:
///
/// <list type="bullet">
/// <item><description>
/// <see cref="Fault"/> — a hard port error. The device is gone (asleep, unplugged, renumbered) and
/// the handle is invalid. Acted on immediately: there is nothing ambiguous about it.
/// </description></item>
/// <item><description>
/// <see cref="RecordCycle"/> — silence. The port is healthy but nothing is coming back. On the W2
/// that means the device has gone, because a W2 answers every query. **An LP-100A can be silent for
/// a perfectly good reason: it is on a screen other than Watts.** Reconnecting cannot fix that, so
/// the silence threshold is set long (seconds, not milliseconds) and is only a backstop for a handle
/// that survives a resume but never delivers again. Shortening it would make a meter parked on the
/// wrong screen reconnect over and over.
/// </description></item>
/// </list>
///
/// Pure and clock-free so it unit-tests deterministically — the reader feeds it one bool per poll
/// cycle and flags a hard fault directly.
/// </summary>
public sealed class LinkHealth
{
    private readonly int _threshold;
    private int _consecutiveFailures;

    /// <param name="deadCycleThreshold">
    /// Consecutive polls with no frame decoded before the link is declared lost. The reader derives
    /// this from a duration rather than passing a raw count, so the grace window doesn't silently
    /// change if the poll interval is ever retuned.
    /// </param>
    public LinkHealth(int deadCycleThreshold)
    {
        if (deadCycleThreshold < 1) deadCycleThreshold = 1;
        _threshold = deadCycleThreshold;
    }

    /// <summary>True once the link is considered lost; stays latched until <see cref="Reset"/>.</summary>
    public bool IsLost { get; private set; }

    /// <summary>Consecutive silent cycles seen so far (for diagnostics and tests).</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>Record one poll cycle. <paramref name="anyData"/> = at least one frame decoded.</summary>
    public void RecordCycle(bool anyData)
    {
        if (anyData)
        {
            _consecutiveFailures = 0;
            IsLost = false;
        }
        else if (++_consecutiveFailures >= _threshold)
        {
            IsLost = true;
        }
    }

    /// <summary>A hard port error (I/O error, port closed): the link is lost immediately.</summary>
    public void Fault() => IsLost = true;

    /// <summary>Clear all state — call after a fresh (re)connect.</summary>
    public void Reset()
    {
        _consecutiveFailures = 0;
        IsLost = false;
    }
}
