using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class TxLogRelocationPlanTests
{
    private const string Log = "TXlog.csv";

    [Theory]
    [InlineData("TXlog.csv")]
    [InlineData("TXlog_20260904221500.csv")]      // an archive from Clear log or a schema change
    [InlineData("TXlog_20260904221500-2.csv")]    // same second, uniquified
    [InlineData("txlog.csv")]                     // Windows file names are case-insensitive
    public void TheLiveLogAndItsArchivesAreFamily(string name) =>
        Assert.True(TxLogRelocationPlan.IsLogFamily(name, Log));

    [Theory]
    [InlineData("config.json")]                   // settings stay in app data
    [InlineData("TXlogbook.csv")]                 // shares the stem but isn't an archive
    [InlineData("TXlog_.csv")]                    // underscore with nothing after it
    [InlineData("TXlog_notes.csv")]               // underscore but not a stamp
    [InlineData("TXlog.csv.bak")]
    [InlineData("other.csv")]
    public void UnrelatedFilesAreNot(string name) =>
        Assert.False(TxLogRelocationPlan.IsLogFamily(name, Log));

    [Fact]
    public void ThePatternFollowsTheLogsNameRatherThanBeingSpelledOut()
    {
        // Rename the log and the archives are still recognised — nothing is left behind.
        Assert.True(TxLogRelocationPlan.IsLogFamily("overs_20260904221500.csv", "overs.csv"));
        Assert.False(TxLogRelocationPlan.IsLogFamily("TXlog_20260904221500.csv", "overs.csv"));
    }

    [Fact]
    public void PlansAMoveForEveryFamilyFileAndIgnoresTheRest()
    {
        var plan = TxLogRelocationPlan.Plan(Log,
            ["config.json", "TXlog.csv", "TXlog_20260801120000.csv", "notes.txt"],
            existsAtDestination: _ => false);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, s => Assert.Equal(RelocationAction.Move, s.Action));
        Assert.Contains(plan, s => s.FileName == "TXlog.csv");
        Assert.Contains(plan, s => s.FileName == "TXlog_20260801120000.csv");
    }

    [Fact]
    public void AFileAlreadyAtTheDestinationIsLeftAloneNotOverwritten()
    {
        // Two copies is a nuisance a person can sort out. One overwritten copy is data gone.
        var plan = TxLogRelocationPlan.Plan(Log,
            ["TXlog.csv", "TXlog_20260801120000.csv"],
            existsAtDestination: name => name == "TXlog.csv");

        var live = Assert.Single(plan, s => s.FileName == "TXlog.csv");
        Assert.Equal(RelocationAction.LeaveBecauseDestinationExists, live.Action);

        var archive = Assert.Single(plan, s => s.FileName == "TXlog_20260801120000.csv");
        Assert.Equal(RelocationAction.Move, archive.Action);
    }

    [Fact]
    public void NothingToMoveIsAnEmptyPlan() =>
        Assert.Empty(TxLogRelocationPlan.Plan(Log, ["config.json"], _ => false));
}
