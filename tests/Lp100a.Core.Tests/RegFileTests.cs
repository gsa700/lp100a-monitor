using Lp100a.Core;

namespace Lp100a.Core.Tests;

/// <summary>
/// The escaping rules matter more than they look: <c>reg import</c> rejects a malformed file
/// wholesale, so one bad value doesn't lose itself — it loses the whole installed-apps entry, which
/// is the user's only ordinary route to uninstalling the app.
/// </summary>
public class RegFileTests
{
    private const string Key = @"HKEY_CURRENT_USER\Software\Test\Lp100aMonitor";

    [Fact]
    public void FileStartsWithTheRequiredHeaderAndKey()
    {
        var text = RegFile.Build(Key, [RegFile.Sz("DisplayName", "LP-100A Monitor")]);
        var lines = text.Split("\r\n");

        Assert.Equal(RegFile.Header, lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal($"[{Key}]", lines[2]);
    }

    [Fact]
    public void UsesCrlfAndEndsWithABlankLine()
    {
        var text = RegFile.Build(Key, [RegFile.Sz("A", "b")]);
        Assert.DoesNotContain(text.Replace("\r\n", ""), "\n");
        Assert.EndsWith("\r\n\r\n", text);
    }

    [Fact]
    public void StringValueIsQuoted()
    {
        var text = RegFile.Build(Key, [RegFile.Sz("Publisher", "David Erickson (AB0R)")]);
        Assert.Contains("\"Publisher\"=\"David Erickson (AB0R)\"", text);
    }

    [Fact]
    public void WindowsPathBackslashesAreDoubled()
    {
        // The single most likely thing to appear in these values, and the single most likely thing
        // to break the file: an unescaped path turns \n or \t inside it into an escape sequence.
        var text = RegFile.Build(Key,
            [RegFile.Sz("DisplayIcon", @"C:\Users\Someone\AppData\Local\Programs\LP-100A Monitor\Lp100aMonitor.exe")]);

        Assert.Contains(@"C:\\Users\\Someone\\AppData\\Local\\Programs\\LP-100A Monitor\\Lp100aMonitor.exe", text);
    }

    [Fact]
    public void EmbeddedQuotesAreEscaped()
    {
        // UninstallString is always of the form "path" --uninstall, so this is the normal case.
        var text = RegFile.Build(Key, [RegFile.Sz("UninstallString", "\"C:\\App\\x.exe\" --uninstall")]);
        Assert.Contains("\"UninstallString\"=\"\\\"C:\\\\App\\\\x.exe\\\" --uninstall\"", text);
    }

    [Fact]
    public void NewlinesAreFlattenedRatherThanEmitted()
    {
        // A raw newline would end the line early and make the remainder a malformed entry, taking
        // the whole import with it. Flattening keeps the damage inside the one value.
        var text = RegFile.Build(Key, [RegFile.Sz("DisplayName", "Line one\r\nline two")]);

        Assert.Contains("\"DisplayName\"=\"Line one  line two\"", text);
        var body = text.Substring(text.IndexOf("\"DisplayName\"", StringComparison.Ordinal));
        Assert.Equal("\"DisplayName\"=\"Line one  line two\"", body.Split("\r\n")[0]);
    }

    [Fact]
    public void ValueNamesAreEscapedToo()
    {
        var text = RegFile.Build(Key, [RegFile.Sz("odd\"name", "v")]);
        Assert.Contains("\"odd\\\"name\"=\"v\"", text);
    }

    [Theory]
    [InlineData(1, "dword:00000001")]
    [InlineData(0, "dword:00000000")]
    [InlineData(255, "dword:000000ff")]
    [InlineData(104857, "dword:00019999")]
    public void DwordIsAlwaysEightLowercaseHexDigits(long value, string expected)
    {
        // reg import is strict about the width: a short one is rejected, not zero-padded for you.
        var text = RegFile.Build(Key, [RegFile.Dword("EstimatedSize", value)]);
        Assert.Contains($"\"EstimatedSize\"={expected}", text);
    }

    [Fact]
    public void EveryValueGetsItsOwnLine()
    {
        var text = RegFile.Build(Key,
        [
            RegFile.Sz("DisplayName", "LP-100A Monitor"),
            RegFile.Sz("DisplayVersion", "0.9.19-beta"),
            RegFile.Dword("NoModify", 1),
        ]);

        var valueLines = text.Split("\r\n").Where(l => l.StartsWith('"')).ToArray();
        Assert.Equal(3, valueLines.Length);
    }

    [Fact]
    public void AKeyWithNoValuesStillCreatesTheKey()
    {
        var text = RegFile.Build(Key, []);
        Assert.Contains($"[{Key}]", text);
        Assert.Equal(RegFile.Header, text.Split("\r\n")[0]);
    }

    [Fact]
    public void TheLongHiveNameSurvivesUnescaped()
    {
        // The key line is not a quoted value, so its backslashes must NOT be doubled — doing so
        // would write a key literally named HKEY_CURRENT_USER\\Software\\...
        var text = RegFile.Build(Key, [RegFile.Sz("A", "b")]);
        Assert.Contains(@"[HKEY_CURRENT_USER\Software\Test\Lp100aMonitor]", text);
        Assert.DoesNotContain(@"HKEY_CURRENT_USER\\Software", text);
    }
}
