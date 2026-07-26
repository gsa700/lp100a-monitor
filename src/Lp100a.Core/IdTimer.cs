namespace Lp100a.Core;

/// <summary>How the ID timer wants to be shown.</summary>
public enum IdTimerState
{
    /// <summary>No communication in progress — nothing to identify for.</summary>
    Idle,
    /// <summary>Running, with time to spare.</summary>
    Running,
    /// <summary>Inside the warning window — identify soon.</summary>
    Due,
    /// <summary>The interval has elapsed — identify now.</summary>
    Overdue,
}

/// <summary>
/// Station-identification reminder, on the WALL CLOCK — the operating custom of identifying "on
/// the tens" (:00, :10, :20 … past the hour) rather than ten minutes after whenever you last did.
/// Boundaries are absolute times of day, so every station running the same interval is prompted at
/// the same moment, and the prompt lands on a round number you can see on any clock in the shack.
/// This also satisfies §97.119's "at least every ten minutes during a communication".
///
/// Unlike the transmit-timeout this keeps running while you're receiving and does not reset on
/// key-up. It arms on the first transmission (a communication has begun) and disarms itself once
/// you've been quiet long enough that the QSO is plainly over, so it doesn't nag an idle station.
///
/// The app can't hear you identify, so <see cref="MarkIdentified"/> is driven by the operator.
/// Pure and clock-injected, like <see cref="TxOverTracker"/> — the caller passes the time in.
/// </summary>
public sealed class IdTimer
{
    private readonly int _intervalMinutes;
    private readonly TimeSpan _warnBefore;
    private readonly TimeSpan _idleDisarmAfter;

    private bool _armed;
    private DateTime _lastTx;
    private DateTime _ackedBoundary = DateTime.MinValue;   // most recent boundary already identified for

    /// <param name="intervalMinutes">
    /// Spacing of the wall-clock marks. Default 10, giving :00/:10/:20/… Values that divide 60
    /// (5, 10, 15, 20, 30) align cleanly to the hour; others simply restart their count each hour.
    /// </param>
    /// <param name="warnBeforeSeconds">Start warning this long before the next mark. Default 60 s.</param>
    /// <param name="idleDisarmMinutes">
    /// Disarm after this long with no transmission. Must be LONGER than the interval: at equal
    /// values a quiet stretch disarms the timer at the very moment it would come due, so the
    /// overdue reminder could never appear. Default 15 against a 10-minute interval.
    /// </param>
    public IdTimer(double intervalMinutes = 10, double warnBeforeSeconds = 60,
        double idleDisarmMinutes = 15)
    {
        _intervalMinutes = Math.Max(1, (int)Math.Round(intervalMinutes));
        _warnBefore = TimeSpan.FromSeconds(warnBeforeSeconds);
        _idleDisarmAfter = TimeSpan.FromMinutes(idleDisarmMinutes);
    }

    /// <summary>The most recent wall-clock mark at or before <paramref name="t"/>.</summary>
    private DateTime PreviousMark(DateTime t)
    {
        var minute = t.Minute - (t.Minute % _intervalMinutes);
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, minute, 0, t.Kind);
    }

    /// <summary>The next wall-clock mark strictly after <paramref name="t"/>.</summary>
    private DateTime NextMark(DateTime t) => PreviousMark(t).AddMinutes(_intervalMinutes);

    public bool IsArmed => _armed;

    /// <summary>Time left before an ID is due. Zero once overdue; zero while idle.</summary>
    public TimeSpan Remaining { get; private set; }

    /// <summary>How long past due, once <see cref="IdTimerState.Overdue"/>. Zero otherwise.</summary>
    public TimeSpan Overdue { get; private set; }

    public IdTimerState State { get; private set; } = IdTimerState.Idle;

    /// <summary>
    /// Advance the timer. Call every tick with the current transmit state — including while
    /// receiving, since the ten minutes run on wall clock, not on key-down time.
    /// </summary>
    public void Observe(bool transmitting, DateTime now)
    {
        if (transmitting)
        {
            if (!_armed)
            {
                // First transmission of a communication: start watching the clock from here.
                _armed = true;
                // Marks already past when the QSO began aren't ours to answer for.
                _ackedBoundary = PreviousMark(now);
            }
            _lastTx = now;
        }
        else if (_armed && now - _lastTx >= _idleDisarmAfter)
        {
            // Long quiet: the QSO is over, so stop counting (and stop nagging).
            Reset();
            return;
        }

        if (!_armed)
        {
            State = IdTimerState.Idle;
            Remaining = TimeSpan.Zero;
            Overdue = TimeSpan.Zero;
            return;
        }

        var mark = PreviousMark(now);
        if (mark > _ackedBoundary)
        {
            // A mark has gone by unanswered — stay overdue until the operator identifies, rather
            // than clearing at the next mark, so a missed ID can't quietly scroll past.
            Remaining = TimeSpan.Zero;
            Overdue = now - mark;
            State = IdTimerState.Overdue;
        }
        else
        {
            Remaining = NextMark(now) - now;
            Overdue = TimeSpan.Zero;
            State = Remaining <= _warnBefore ? IdTimerState.Due : IdTimerState.Running;
        }
    }

    /// <summary>
    /// The operator identified: answer the current mark and count on to the next one. Stays armed,
    /// because the communication is still in progress.
    /// </summary>
    public void MarkIdentified(DateTime now)
    {
        var next = NextMark(now);
        // Identifying in the run-up to a mark satisfies that mark too — otherwise giving your call
        // at :09:30 would be met with a fresh "ID now" thirty seconds later.
        _ackedBoundary = next - now <= _warnBefore ? next : PreviousMark(now);

        // Identifying means you keyed up and gave your call, so it counts as activity: without
        // this the idle clock keeps running from the previous over and can disarm the timer
        // moments after you've told it the QSO is very much alive.
        _lastTx = now;
        _armed = true;   // identifying before the first over still starts the communication

        // Count down to the first mark we haven't already answered, so an early ID doesn't leave
        // the row ticking towards a mark it just satisfied.
        var target = _ackedBoundary >= next ? next.AddMinutes(_intervalMinutes) : next;
        Remaining = target - now;
        Overdue = TimeSpan.Zero;
        State = Remaining <= _warnBefore ? IdTimerState.Due : IdTimerState.Running;
    }

    /// <summary>Disarm completely (communication over, or the feature switched off).</summary>
    public void Reset()
    {
        _armed = false;
        State = IdTimerState.Idle;
        Remaining = TimeSpan.Zero;
        Overdue = TimeSpan.Zero;
    }
}
