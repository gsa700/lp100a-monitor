using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class LinkHealthTests
{
    [Fact]
    public void StartsHealthy()
    {
        var h = new LinkHealth(8);
        Assert.False(h.IsLost);
        Assert.Equal(0, h.ConsecutiveFailures);
    }

    [Fact]
    public void SilentCyclesBelowThresholdDoNotLoseTheLink()
    {
        var h = new LinkHealth(8);
        for (var i = 0; i < 7; i++) h.RecordCycle(anyData: false);
        Assert.False(h.IsLost);
        Assert.Equal(7, h.ConsecutiveFailures);
    }

    [Fact]
    public void LinkIsLostOnTheThresholdCycle()
    {
        var h = new LinkHealth(8);
        for (var i = 0; i < 8; i++) h.RecordCycle(anyData: false);
        Assert.True(h.IsLost);
    }

    [Fact]
    public void OneGoodCycleClearsTheRun()
    {
        var h = new LinkHealth(8);
        for (var i = 0; i < 7; i++) h.RecordCycle(anyData: false);
        h.RecordCycle(anyData: true);
        Assert.Equal(0, h.ConsecutiveFailures);
        Assert.False(h.IsLost);

        // And the count really restarts rather than resuming near the threshold.
        for (var i = 0; i < 7; i++) h.RecordCycle(anyData: false);
        Assert.False(h.IsLost);
    }

    [Fact]
    public void FaultLosesTheLinkImmediately()
    {
        var h = new LinkHealth(8);
        h.Fault();
        Assert.True(h.IsLost);
    }

    [Fact]
    public void LostStaysLatchedUntilReset()
    {
        var h = new LinkHealth(2);
        h.Fault();
        Assert.True(h.IsLost);

        h.Reset();
        Assert.False(h.IsLost);
        Assert.Equal(0, h.ConsecutiveFailures);
    }

    [Fact]
    public void DataAfterLossClearsIt()
    {
        // A silence-declared loss is not latched against real data arriving: the supervisor may see
        // the frame before it acts on IsLost, and it should not tear down a link that just recovered.
        var h = new LinkHealth(2);
        h.RecordCycle(anyData: false);
        h.RecordCycle(anyData: false);
        Assert.True(h.IsLost);

        h.RecordCycle(anyData: true);
        Assert.False(h.IsLost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveThresholdIsClampedToOne(int threshold)
    {
        var h = new LinkHealth(threshold);
        h.RecordCycle(anyData: false);
        Assert.True(h.IsLost);
    }

    [Fact]
    public void SilenceWindowIsLongEnoughToOutlastAMeterOnTheWrongScreen()
    {
        // Guards the LP-100A-specific tuning: a meter parked off its Watts screen is legitimately
        // silent, and the threshold must stay well above the UI's 2 s stale indicator so the reader
        // doesn't sit in a reconnect loop over something reconnecting cannot fix.
        const int silenceTimeoutMs = 6000, pollIntervalMs = 80;
        var threshold = silenceTimeoutMs / pollIntervalMs;

        var h = new LinkHealth(threshold);
        for (var i = 0; i < 2000 / pollIntervalMs; i++) h.RecordCycle(anyData: false);
        Assert.False(h.IsLost);   // still quiet at the 2 s stale mark

        for (var i = 2000 / pollIntervalMs; i < threshold; i++) h.RecordCycle(anyData: false);
        Assert.True(h.IsLost);
    }
}
