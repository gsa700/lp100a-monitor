using Lp100a.Core;

namespace Lp100a.Core.Tests;

/// <summary>
/// Guards the rule that decides whether uninstalling may delete the folder the executable is in.
/// This is a data-loss boundary, not a tidiness preference: the app used to delete that folder
/// unconditionally, so <c>--uninstall</c> from a copy sitting in Downloads would have taken
/// Downloads with it.
/// </summary>
public class UninstallOwnershipTests
{
    [Fact]
    public void AnInstalledCopyOwnsItsDirectory()
    {
        // The app put itself there, so the folder is the app's to remove.
        Assert.True(InstallLayout.OwnsExeDirectory(InstallMode.Installed));
    }

    [Fact]
    public void ALooseCopyDoesNotOwnItsDirectory()
    {
        // The folder is wherever the user dropped the download — routinely Downloads or the Desktop.
        Assert.False(InstallLayout.OwnsExeDirectory(InstallMode.Loose));
    }

    [Fact]
    public void APortableCopyDoesNotOwnItsDirectory()
    {
        // Portable is running on ground it explicitly does not own, which is the whole point of it.
        Assert.False(InstallLayout.OwnsExeDirectory(InstallMode.Portable));
    }

    [Fact]
    public void OnlyInstalledEverOwnsTheDirectory()
    {
        // Written against the enum rather than the three cases above, so a mode added later has to
        // make a deliberate decision here instead of silently defaulting to "safe to delete".
        foreach (var mode in Enum.GetValues<InstallMode>())
        {
            var owns = InstallLayout.OwnsExeDirectory(mode);
            Assert.Equal(mode == InstallMode.Installed, owns);
        }
    }

    [Fact]
    public void ACopyInDownloadsIsLooseAndSoIsNotDeletable()
    {
        // The exact shape of the accident: an executable unzipped straight into Downloads, with no
        // portable marker, is Loose — and Loose must never have its directory removed.
        const string programs = @"C:\Users\Someone\AppData\Local\Programs";
        var mode = InstallLayout.Detect(
            exeDirectory: @"C:\Users\Someone\Downloads",
            portableMarkerPresent: false,
            installDirectory: InstallLayout.InstallDirectoryUnder(programs, windows: true),
            alsoInstalled: InstallLayout.InstalledDirectoriesUnder(programs, windows: true),
            comparison: StringComparison.OrdinalIgnoreCase);

        Assert.Equal(InstallMode.Loose, mode);
        Assert.False(InstallLayout.OwnsExeDirectory(mode));
    }
}
