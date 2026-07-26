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
/// Station-identification reminder. §97.119 wants an ID at least every ten minutes *during a
/// communication*, so unlike the transmit-timeout this is a WALL-CLOCK timer: it keeps running
/// while you're receiving and does not reset on key-up. Only identifying resets it.
///
/// It arms on the first transmission (a communication has begun) and disarms itself once you've
/// been quiet long enough that the QSO is plainly over, so it doesn't nag at an idle station.
///
/// The app can't hear you identify, so <see cref="MarkIdentified"/> is driven by the operator.
/// Pure and clock-injected, like <see cref="TxOverTracker"/> — the caller passes the time in.
/// </summary>
public sealed class IdTimer
{
    private readonly TimeSpan _interval;
    private readonly TimeSpan _warnBefore;
    private readonly TimeSpan _idleDisarmAfter;

    private bool _armed;
    private DateTime _since;      // last ID, or when the communication started
    private DateTime _lastTx;

    /// <param name="intervalMinutes">Identify at least this often. Default 10 (§97.119).</param>
    /// <param name="warnBeforeSeconds">Start warning this long before it's due. Default 60 s.</param>
    /// <param name="idleDisarmMinutes">
    /// Disarm after this long with no transmission. Must be LONGER than the interval: at equal
    /// values a quiet stretch disarms the timer at the very moment it would come due, so the
    /// overdue reminder could never appear. Default 15 against a 10-minute interval.
    /// </param>
    public IdTimer(double intervalMinutes = 10, double warnBeforeSeconds = 60,
        double idleDisarmMinutes = 15)
    {
        _interval = TimeSpan.FromMinutes(intervalMinutes);
        _warnBefore = TimeSpan.FromSeconds(warnBeforeSeconds);
        _idleDisarmAfter = TimeSpan.FromMinutes(idleDisarmMinutes);
    }

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
                // First transmission of a communication: the clock starts here.
                _armed = true;
                _since = now;
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

        var elapsed = now - _since;
        if (elapsed >= _interval)
        {
            Remaining = TimeSpan.Zero;
            Overdue = elapsed - _interval;
            State = IdTimerState.Overdue;
        }
        else
        {
            Remaining = _interval - elapsed;
            Overdue = TimeSpan.Zero;
            State = Remaining <= _warnBefore ? IdTimerState.Due : IdTimerState.Running;
        }
    }

    /// <summary>
    /// The operator identified: restart the ten minutes. Stays armed, because the communication
    /// is still in progress and the next ID is due an interval from now.
    /// </summary>
    public void MarkIdentified(DateTime now)
    {
        _since = now;
        // Identifying means you keyed up and gave your call, so it counts as activity: without
        // this the idle clock keeps running from the previous over and can disarm the timer
        // moments after you've told it the QSO is very much alive.
        _lastTx = now;
        _armed = true;   // identifying before the first over still starts the communication
        Remaining = _interval;
        Overdue = TimeSpan.Zero;
        State = IdTimerState.Running;
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
