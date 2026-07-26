using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class IdTimerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void StartsIdleAndArmsOnFirstTransmission()
    {
        var t = new IdTimer();
        t.Observe(transmitting: false, T0);
        Assert.False(t.IsArmed);
        Assert.Equal(IdTimerState.Idle, t.State);

        t.Observe(transmitting: true, T0.AddSeconds(5));
        Assert.True(t.IsArmed);
        Assert.Equal(IdTimerState.Running, t.State);
        Assert.Equal(TimeSpan.FromMinutes(10), t.Remaining);
    }

    [Fact]
    public void KeepsRunningWhileReceiving()
    {
        // The ten minutes are wall clock during a communication, NOT accumulated key-down time —
        // this is the whole difference from the transmit-timeout.
        var t = new IdTimer();
        t.Observe(true, T0);                       // arm
        t.Observe(false, T0.AddMinutes(4));        // listening to the other station
        Assert.Equal(TimeSpan.FromMinutes(6), t.Remaining);
        Assert.Equal(IdTimerState.Running, t.State);
    }

    [Fact]
    public void KeyUpDoesNotResetIt()
    {
        var t = new IdTimer();
        t.Observe(true, T0);
        t.Observe(false, T0.AddMinutes(3));
        t.Observe(true, T0.AddMinutes(5));         // a new over, mid-QSO
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining);
    }

    [Fact]
    public void WarnsBeforeDueThenGoesOverdue()
    {
        var t = new IdTimer();
        t.Observe(true, T0);

        t.Observe(false, T0.AddMinutes(8));
        Assert.Equal(IdTimerState.Running, t.State);

        t.Observe(false, T0.AddSeconds(9 * 60 + 15));   // inside the 60 s warning window
        Assert.Equal(IdTimerState.Due, t.State);

        t.Observe(false, T0.AddSeconds(10 * 60 + 30));
        Assert.Equal(IdTimerState.Overdue, t.State);
        Assert.Equal(TimeSpan.Zero, t.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Overdue);
    }

    [Fact]
    public void IdentifyingRestartsTheIntervalButStaysArmed()
    {
        var t = new IdTimer();
        t.Observe(true, T0);
        t.Observe(false, T0.AddMinutes(11));
        Assert.Equal(IdTimerState.Overdue, t.State);

        t.MarkIdentified(T0.AddMinutes(11));
        Assert.True(t.IsArmed);                     // the QSO is still going
        Assert.Equal(IdTimerState.Running, t.State);
        Assert.Equal(TimeSpan.FromMinutes(10), t.Remaining);

        t.Observe(false, T0.AddMinutes(16));
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining);
    }

    [Fact]
    public void DisarmsAfterALongQuietSpell()
    {
        // The QSO ended; an idle station shouldn't be nagged to identify.
        var t = new IdTimer(idleDisarmMinutes: 10);
        t.Observe(true, T0);
        t.Observe(false, T0.AddMinutes(9));
        Assert.True(t.IsArmed);

        t.Observe(false, T0.AddMinutes(10.5));
        Assert.False(t.IsArmed);
        Assert.Equal(IdTimerState.Idle, t.State);
    }

    [Fact]
    public void ReArmsOnTheNextCommunication()
    {
        var t = new IdTimer(idleDisarmMinutes: 10);
        t.Observe(true, T0);
        t.Observe(false, T0.AddMinutes(11));       // disarms
        Assert.False(t.IsArmed);

        t.Observe(true, T0.AddMinutes(20));        // new QSO: full interval again
        Assert.True(t.IsArmed);
        Assert.Equal(TimeSpan.FromMinutes(10), t.Remaining);
    }

    [Fact]
    public void HonoursACustomInterval()
    {
        var t = new IdTimer(intervalMinutes: 5, warnBeforeSeconds: 30);
        t.Observe(true, T0);
        t.Observe(false, T0.AddMinutes(4));
        Assert.Equal(IdTimerState.Running, t.State);
        t.Observe(false, T0.AddSeconds(4 * 60 + 40));
        Assert.Equal(IdTimerState.Due, t.State);
        t.Observe(false, T0.AddMinutes(5));
        Assert.Equal(IdTimerState.Overdue, t.State);
    }
}
