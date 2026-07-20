using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class TxOverRecordTests
{
    private static TxOverRecord Sample() => new()
    {
        Start = new DateTime(2026, 7, 20, 13, 5, 9),
        FreqMhz = 14.074,
        DurationSeconds = 12,
        PeakForwardW = 105.4,
        MaxSwr = 1.53,
        MinSwr = 1.21,
        ResistanceOhms = 48.2,
        ReactanceOhms = -6.1,
        PhaseDeg = -7.2,
        PowerRange = 1,
        TimedOut = false,
    };

    [Fact]
    public void CsvRowMatchesHeaderColumnCount()
    {
        Assert.Equal(TxOverRecord.CsvHeader.Split(',').Length, Sample().ToCsvRow().Split(',').Length);
    }

    [Fact]
    public void CsvRowFormatsInvariantAndTimedOutFlag()
    {
        var cols = Sample().ToCsvRow().Split(',');
        Assert.Equal("2026-07-20 13:05:09", cols[0]);
        Assert.Equal("14.0740", cols[1]);   // Freq_MHz, N4
        Assert.Equal("12", cols[2]);
        Assert.Equal("105.4", cols[3]);     // PeakFwd_W, N1
        Assert.Equal("1.53", cols[4]);      // MaxSWR, N2
        Assert.Equal("no", cols[^1]);
    }

    [Theory]
    [InlineData(1097.3)]   // kilowatt station: must not gain a thousands-separator comma
    [InlineData(1500.0)]
    public void KilowattPowerStaysOneColumn(double watts)
    {
        var cols = (Sample() with { PeakForwardW = watts }).ToCsvRow().Split(',');
        Assert.Equal(TxOverRecord.CsvHeader.Split(',').Length, cols.Length);
        Assert.Equal(watts.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), cols[3]);
        Assert.DoesNotContain(",", cols[3]);
    }

    [Fact]
    public void NullFrequencyRendersEmptyColumn()
    {
        var cols = (Sample() with { FreqMhz = null }).ToCsvRow().Split(',');
        Assert.Equal("", cols[1]);
    }

    [Fact]
    public void MinReturnLossCapsAtPerfectMatch()
    {
        Assert.Equal(60.0, (Sample() with { MaxSwr = 1.0 }).MinReturnLossDb);
    }

    [Fact]
    public void MinReturnLossDerivedFromMaxSwr()
    {
        var rl = (Sample() with { MaxSwr = 2.0 }).MinReturnLossDb;
        Assert.Equal(-20.0 * Math.Log10(1.0 / 3.0), rl, 6);   // (2-1)/(2+1)
    }
}
