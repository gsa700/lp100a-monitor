using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class RegQueryTests
{
    // Real `reg query HKCU\...\Lp100aMonitor` output, four-space separators and all.
    private const string Output =
        "\r\n" +
        "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Lp100aMonitor\r\n" +
        "    DisplayName    REG_SZ    LP-100A Monitor\r\n" +
        "    DisplayVersion    REG_SZ    0.9.21-beta\r\n" +
        "    DisplayIcon    REG_SZ    C:\\Users\\Someone\\AppData\\Local\\Programs\\LP-100A Monitor\\Lp100aMonitor.exe\r\n" +
        "    UninstallString    REG_SZ    \"C:\\Users\\Someone\\AppData\\Local\\Programs\\LP-100A Monitor\\Lp100aMonitor.exe\" --uninstall\r\n" +
        "    NoModify    REG_DWORD    0x1\r\n" +
        "    EstimatedSize    REG_DWORD    0x19999\r\n" +
        "\r\n";

    [Fact]
    public void ReadsASimpleValue() =>
        Assert.Equal("0.9.21-beta", RegQuery.Value(Output, "DisplayVersion"));

    [Fact]
    public void ReadsAPathContainingSpaces()
    {
        // "LP-100A Monitor" has a space in it, so the data is not a single whitespace-delimited field.
        Assert.Equal(
            @"C:\Users\Someone\AppData\Local\Programs\LP-100A Monitor\Lp100aMonitor.exe",
            RegQuery.Value(Output, "DisplayIcon"));
    }

    [Fact]
    public void ReadsAValueContainingQuotesAndArguments() =>
        Assert.Equal(
            "\"C:\\Users\\Someone\\AppData\\Local\\Programs\\LP-100A Monitor\\Lp100aMonitor.exe\" --uninstall",
            RegQuery.Value(Output, "UninstallString"));

    [Fact]
    public void ReadsADword() => Assert.Equal("0x1", RegQuery.Value(Output, "NoModify"));

    [Fact]
    public void MissingValueIsNull() => Assert.Null(RegQuery.Value(Output, "Publisher"));

    [Fact]
    public void EmptyOutputIsNull() => Assert.Null(RegQuery.Value("", "DisplayVersion"));

    [Fact]
    public void TheEchoedKeyPathIsNotMistakenForAValue()
    {
        // The key line ends in "Lp100aMonitor"; asking for that name must not return the path.
        Assert.Null(RegQuery.Value(Output, "Lp100aMonitor"));
    }

    [Fact]
    public void APrefixDoesNotAnswerForALongerName()
    {
        const string o = "    DisplayNameEx    REG_SZ    nope\r\n";
        Assert.Null(RegQuery.Value(o, "DisplayName"));
    }

    [Fact]
    public void ALongerNameDoesNotMatchAShorterValue()
    {
        Assert.Equal("LP-100A Monitor", RegQuery.Value(Output, "DisplayName"));
    }

    [Fact]
    public void AnEmptyValueIsEmptyNotAbsent()
    {
        // Distinguishing these matters: absent means the write never landed, empty means it did.
        const string o = "    DisplayVersion    REG_SZ\r\n";
        Assert.Equal("", RegQuery.Value(o, "DisplayVersion"));
    }

    [Fact]
    public void HandlesLineFeedOnlyOutput()
    {
        const string o = "HKEY_CURRENT_USER\\Software\\X\n    DisplayVersion    REG_SZ    1.2.3\n";
        Assert.Equal("1.2.3", RegQuery.Value(o, "DisplayVersion"));
    }

    [Fact]
    public void NameMatchingIsCaseInsensitive() =>
        Assert.Equal("0.9.21-beta", RegQuery.Value(Output, "displayversion"));

    [Fact]
    public void ErrorOutputYieldsNull()
    {
        const string o = "ERROR: The system was unable to find the specified registry key or value.\r\n";
        Assert.Null(RegQuery.Value(o, "DisplayVersion"));
    }
}
