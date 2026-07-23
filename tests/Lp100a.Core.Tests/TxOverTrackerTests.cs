using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class TxOverTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    private static Lp100Reading R(double power, double swr = 1.0, double z = 50, double phase = 0, int range = 0)
        => new() { ForwardPowerW = power, Swr = swr, ZOhms = z, PhaseDeg = phase, PowerRange = range };

    [Fact]
    public void EmitsOverOnConfirmedKeyUp_WithPeakSampledValues()
    {
        var t = new TxOverTracker();

        Assert.Null(t.Observe(R(0), null, T0));                       // idle
        Assert.Null(t.Observe(R(100, swr: 1.5), null, T0.AddSeconds(1))); // rising edge -> start
        Assert.Null(t.Observe(R(120, swr: 1.3, z: 40, phase: 10, range: 1), null, T0.AddSeconds(2))); // new peak
        Assert.Null(t.Observe(R(80, swr: 1.2), null, T0.AddSeconds(3))); // lastTx = T0+3
        Assert.Null(t.Observe(R(0), null, T0.AddSeconds(3.5)));        // low, but within hang -> alive
        Assert.True(t.InOver);

        var over = t.Observe(R(0), null, T0.AddSeconds(5.1));         // low past hang -> key-up
        Assert.NotNull(over);
        Assert.False(t.InOver);
        Assert.Equal(2, over!.DurationSeconds);                       // lastTx(3) - start(1)
        Assert.Equal(120, over.PeakForwardW);
        Assert.Equal(1.5, over.MaxSwr);
        Assert.Equal(1, over.PowerRange);                             // sampled at peak
        // Impedance sampled at the peak-power tick (z=40, phase=10°), not the last tick.
        Assert.Equal(40 * Math.Cos(10 * Math.PI / 180), over.ResistanceOhms, 6);
        Assert.Equal(40 * Math.Sin(10 * Math.PI / 180), over.ReactanceOhms, 6);
    }

    [Fact]
    public void ReadDropoutDoesNotEndOrResetOver()
    {
        var t = new TxOverTracker();
        Assert.Null(t.Observe(R(100), null, T0));                     // start

        // A dropout (null reading) even far past the hang time must NOT end the over.
        Assert.Null(t.Observe(null, null, T0.AddSeconds(10)));
        Assert.True(t.InOver);

        Assert.Null(t.Observe(R(100), null, T0.AddSeconds(11)));      // TX resumes, lastTx = 11
        var over = t.Observe(R(0), null, T0.AddSeconds(14));          // low past hang -> end
        Assert.NotNull(over);
        Assert.Equal(11, over!.DurationSeconds);                      // spans the dropout
    }

    [Fact]
    public void DisconnectEndsOverImmediately()
    {
        var t = new TxOverTracker();
        Assert.Null(t.Observe(R(100), null, T0));
        Assert.Null(t.Observe(R(120), null, T0.AddSeconds(2)));

        var over = t.Observe(null, null, T0.AddSeconds(2.2), connected: false);
        Assert.NotNull(over);
        Assert.Equal(2, over!.DurationSeconds);
    }

    [Fact]
    public void OverShorterThanMinDurationIsDropped()
    {
        var t = new TxOverTracker();
        Assert.Null(t.Observe(R(100), null, T0));                     // single tick, lastTx == start
        var over = t.Observe(null, null, T0.AddSeconds(0.5), connected: false);
        Assert.Null(over);                                           // duration 0 < 1 s
        Assert.False(t.InOver);
    }

    [Fact]
    public void TimedOutFlagSetPastTimeout()
    {
        var t = new TxOverTracker(timeoutSeconds: 3);
        Assert.Null(t.Observe(R(100), null, T0));
        Assert.Null(t.Observe(R(100), null, T0.AddSeconds(5)));       // lastTx = 5, duration 5 >= 3
        var over = t.Observe(R(0), null, T0.AddSeconds(8));
        Assert.NotNull(over);
        Assert.True(over!.TimedOut);
    }

    [Fact]
    public void TracksMaxSwrAndSwrAtPeakPower()
    {
        var t = new TxOverTracker();
        Assert.Null(t.Observe(R(50, swr: 2.0), null, T0));
        Assert.Null(t.Observe(R(100, swr: 1.4), null, T0.AddSeconds(1)));   // peak power here
        Assert.Null(t.Observe(R(80, swr: 1.8), null, T0.AddSeconds(2)));
        var over = t.Observe(R(0), null, T0.AddSeconds(5));
        Assert.NotNull(over);
        Assert.Equal(2.0, over!.MaxSwr);      // worst anywhere in the over
        Assert.Equal(1.4, over.SwrAtPeak);    // the value at max forward power
    }

    [Fact]
    public void RampTransientDoesNotDominateSwrAtPeak()
    {
        // Regression for 0.9.7-beta: the meter reports ~1.00 while power ramps up and decays,
        // because there's too little power to measure reflection. A running MINIMUM latched onto
        // that on every over (125/125 logged rows read exactly 1.00). Sampling at peak power is
        // immune, so the real operating SWR survives.
        var t = new TxOverTracker();
        Assert.Null(t.Observe(R(0.5, swr: 1.00), null, T0));                 // key-up ramp
        Assert.Null(t.Observe(R(1000, swr: 1.24), null, T0.AddSeconds(1)));  // real operating point
        Assert.Null(t.Observe(R(0.4, swr: 1.00), null, T0.AddSeconds(2)));   // key-down decay
        var over = t.Observe(R(0), null, T0.AddSeconds(5));
        Assert.NotNull(over);
        Assert.Equal(1.24, over!.SwrAtPeak);
        Assert.NotEqual(1.00, over.SwrAtPeak);
    }

    [Fact]
    public void CapturesLatestCatFrequencyDuringOver()
    {
        var t = new TxOverTracker();
        Assert.Null(t.Observe(R(100), 14.074, T0));
        Assert.Null(t.Observe(R(100), 14.076, T0.AddSeconds(1)));     // freq nudges during over
        Assert.Null(t.Observe(R(100), null, T0.AddSeconds(2)));       // CAT dropout keeps last
        var over = t.Observe(R(0), null, T0.AddSeconds(5));
        Assert.NotNull(over);
        Assert.Equal(14.076, over!.FreqMhz);
    }
}
