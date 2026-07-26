using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class IdTimerTests
{
    // Times of day matter here: the reminder fires on wall-clock marks (:00, :10, :20 …),
    // not on a stopwatch started at the last ID.
    private static DateTime At(int hour, int minute, int second = 0) =>
        new(2026, 1, 1, hour, minute, second);

    [Fact]
    public void IdleUntilTheFirstTransmission()
    {
        var t = new IdTimer();
        t.Observe(transmitting: false, At(12, 3));
        Assert.False(t.IsArmed);
        Assert.Equal(IdTimerState.Idle, t.State);
    }

    [Fact]
    public void CountsDownToTheNextWallClockMark()
    {
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 3));       // QSO starts at :03
        Assert.True(t.IsArmed);
        Assert.Equal(IdTimerState.Running, t.State);
        Assert.Equal(TimeSpan.FromMinutes(7), t.Remaining);   // :03 -> :10, not a fresh 10 minutes
    }

    [Fact]
    public void MarkAlreadyPastWhenTheQsoStartedIsNotOurs()
    {
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 10, 30));   // started just after the :10 mark
        Assert.Equal(IdTimerState.Running, t.State);     // not instantly overdue
        Assert.Equal(TimeSpan.FromSeconds(570), t.Remaining);   // 9m30s to :20
    }

    [Fact]
    public void WarnsInTheRunUpToTheMark()
    {
        var t = new IdTimer(warnBeforeSeconds: 60);
        t.Observe(transmitting: true, At(12, 3));
        t.Observe(transmitting: false, At(12, 9, 10));
        Assert.Equal(IdTimerState.Due, t.State);          // inside the last minute
        Assert.Equal(TimeSpan.FromSeconds(50), t.Remaining);
    }

    [Fact]
    public void GoesOverdueAtTheMarkAndKeepsCounting()
    {
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 3));
        t.Observe(transmitting: false, At(12, 10, 0));
        Assert.Equal(IdTimerState.Overdue, t.State);
        Assert.Equal(TimeSpan.Zero, t.Overdue);

        t.Observe(transmitting: false, At(12, 12, 30));
        Assert.Equal(IdTimerState.Overdue, t.State);
        Assert.Equal(TimeSpan.FromSeconds(150), t.Overdue);   // 2m30s past :10
    }

    [Fact]
    public void KeepsRunningWhileReceiving()
    {
        // The whole point of wall clock: it doesn't pause or reset just because we're listening.
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 1));
        t.Observe(transmitting: false, At(12, 5));
        Assert.Equal(IdTimerState.Running, t.State);
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining);
    }

    [Fact]
    public void UnkeyingDoesNotRestartTheCount()
    {
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 2));
        t.Observe(transmitting: false, At(12, 4));
        t.Observe(transmitting: true, At(12, 6));      // new over, same communication
        Assert.Equal(TimeSpan.FromMinutes(4), t.Remaining);   // still counting to :10
    }

    [Fact]
    public void IdentifyingAnswersTheMarkAndMovesToTheNext()
    {
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 3));
        t.Observe(transmitting: false, At(12, 11));    // overdue at :10
        Assert.Equal(IdTimerState.Overdue, t.State);

        t.MarkIdentified(At(12, 11, 30));
        Assert.Equal(IdTimerState.Running, t.State);
        Assert.Equal(TimeSpan.FromSeconds(510), t.Remaining);   // 8m30s to :20

        t.Observe(transmitting: false, At(12, 15));
        Assert.Equal(IdTimerState.Running, t.State);            // stays clear until :20
    }

    [Fact]
    public void IdentifyingJustBeforeAMarkAlsoSatisfiesThatMark()
    {
        // Giving your call at :09:30 shouldn't be met with "ID now" thirty seconds later.
        var t = new IdTimer(warnBeforeSeconds: 60);
        t.Observe(transmitting: true, At(12, 3));
        t.MarkIdentified(At(12, 9, 30));

        Assert.Equal(TimeSpan.FromSeconds(630), t.Remaining);   // counts on to :20, not :10
        t.Observe(transmitting: false, At(12, 10, 5));
        Assert.NotEqual(IdTimerState.Overdue, t.State);
        t.Observe(transmitting: false, At(12, 20, 1));
        Assert.Equal(IdTimerState.Overdue, t.State);            // the :20 mark does still count
    }

    [Fact]
    public void MarksAreAbsolute_SoStationsAgreeRegardlessOfWhenTheyStarted()
    {
        var early = new IdTimer();
        var late = new IdTimer();
        early.Observe(transmitting: true, At(9, 1));
        late.Observe(transmitting: true, At(9, 8));

        early.Observe(transmitting: false, At(9, 10, 0));
        late.Observe(transmitting: false, At(9, 10, 0));
        Assert.Equal(IdTimerState.Overdue, early.State);
        Assert.Equal(IdTimerState.Overdue, late.State);
    }

    [Fact]
    public void CustomIntervalUsesItsOwnMarks()
    {
        var t = new IdTimer(intervalMinutes: 15);       // :00 :15 :30 :45
        t.Observe(transmitting: true, At(12, 5));
        Assert.Equal(TimeSpan.FromMinutes(10), t.Remaining);
        t.Observe(transmitting: false, At(12, 15, 1));
        Assert.Equal(IdTimerState.Overdue, t.State);
    }

    [Fact]
    public void MarksRollOverTheHour()
    {
        var t = new IdTimer();
        t.Observe(transmitting: true, At(12, 55));
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining);     // to 13:00
        t.Observe(transmitting: false, At(13, 0, 30));
        Assert.Equal(IdTimerState.Overdue, t.State);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Overdue);
    }

    [Fact]
    public void DisarmsAfterALongSilence()
    {
        var t = new IdTimer(idleDisarmMinutes: 15);
        t.Observe(transmitting: true, At(12, 3));
        t.Observe(transmitting: false, At(12, 18, 1));
        Assert.False(t.IsArmed);
        Assert.Equal(IdTimerState.Idle, t.State);
    }

    [Fact]
    public void IdleDisarmMustOutlastTheIntervalSoOverdueCanBeSeen()
    {
        // Regression: with disarm == interval, a quiet stretch disarmed the timer at the very
        // moment it should have gone overdue, so the reminder could never appear.
        var t = new IdTimer(intervalMinutes: 10, idleDisarmMinutes: 15);
        t.Observe(transmitting: true, At(12, 5));
        t.Observe(transmitting: false, At(12, 12));
        Assert.Equal(IdTimerState.Overdue, t.State);
        Assert.True(t.IsArmed);
    }

    [Fact]
    public void IdentifyingCountsAsActivitySoItDoesNotImmediatelyDisarm()
    {
        var t = new IdTimer(idleDisarmMinutes: 15);
        t.Observe(transmitting: true, At(12, 0));
        t.MarkIdentified(At(12, 14));
        t.Observe(transmitting: false, At(12, 20));   // 6 min after the ID, not 20 after the over
        Assert.True(t.IsArmed);
    }
}
