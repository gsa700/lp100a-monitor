using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class XdgUserDirsTests
{
    private const string Home = "/home/ab0r";

    private static string? Documents(string? contents) =>
        XdgUserDirs.Resolve(contents, XdgUserDirs.DocumentsKey, Home);

    [Fact]
    public void ReadsTheOrdinaryFormRaspberryPiOsWrites() =>
        Assert.Equal("/home/ab0r/Documents", Documents("XDG_DOCUMENTS_DIR=\"$HOME/Documents\"\n"));

    [Fact]
    public void ReadsALocalisedDirectory() =>
        // The whole reason this isn't hardcoded to ~/Documents.
        Assert.Equal("/home/ab0r/Documentos", Documents("XDG_DOCUMENTS_DIR=\"$HOME/Documentos\"\n"));

    [Fact]
    public void ReadsAnAbsolutePathOutsideHome() =>
        Assert.Equal("/mnt/nas/docs", Documents("XDG_DOCUMENTS_DIR=\"/mnt/nas/docs\"\n"));

    [Fact]
    public void PicksTheRightKeyOutOfAFullFile()
    {
        var file = """
            # This file is written by xdg-user-dirs-update
            XDG_DOWNLOAD_DIR="$HOME/Downloads"
            XDG_DESKTOP_DIR="$HOME/Desktop"
            XDG_DOCUMENTS_DIR="$HOME/Documents"
            """;
        Assert.Equal("/home/ab0r/Documents", Documents(file));
        Assert.Equal("/home/ab0r/Downloads", XdgUserDirs.Resolve(file, "XDG_DOWNLOAD_DIR", Home));
    }

    [Fact]
    public void IgnoresACommentedOutKey() =>
        Assert.Null(Documents("#XDG_DOCUMENTS_DIR=\"$HOME/Documents\"\n"));

    [Fact]
    public void DoesNotMatchAKeyThatMerelyEndsWithTheName() =>
        Assert.Null(Documents("MY_XDG_DOCUMENTS_DIR=\"$HOME/Nope\"\n"));

    [Theory]
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME/\"")]   // the convention for "this user has no such directory"
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME\"")]
    [InlineData("XDG_DOCUMENTS_DIR=\"\"")]
    [InlineData("XDG_DOCUMENTS_DIR=")]
    public void NoDirectoryMeansNull(string line) =>
        // The caller falls back to ~/Documents; what it must never do is write into $HOME itself.
        Assert.Null(Documents(line));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nothing relevant here\n")]
    public void MissingOrIrrelevantContentIsNull(string? contents) =>
        Assert.Null(Documents(contents));

    [Fact]
    public void TolerantOfWhitespaceAndCrlf() =>
        Assert.Equal("/home/ab0r/Documents", Documents("  XDG_DOCUMENTS_DIR = \"$HOME/Documents\" \r\n"));

    [Fact]
    public void TrailingSlashIsTrimmedSoPathsCompareEqual() =>
        Assert.Equal("/home/ab0r/Documents", Documents("XDG_DOCUMENTS_DIR=\"$HOME/Documents/\"\n"));

    [Fact]
    public void LastAssignmentWins() =>
        Assert.Equal("/home/ab0r/Second",
            Documents("XDG_DOCUMENTS_DIR=\"$HOME/First\"\nXDG_DOCUMENTS_DIR=\"$HOME/Second\"\n"));
}
