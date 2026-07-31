using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class SerialErrorsTests
{
    [Fact]
    public void LinuxAccessDeniedExplainsTheDialoutGroup()
    {
        var msg = SerialErrors.Describe(new UnauthorizedAccessException(), "/dev/ttyUSB0", isLinux: true);
        Assert.Contains("dialout", msg);
        Assert.Contains("/dev/ttyUSB0", msg);
    }

    [Fact]
    public void WindowsAccessDeniedBlamesAnotherApp()
    {
        var msg = SerialErrors.Describe(new UnauthorizedAccessException(), "COM4", isLinux: false);
        Assert.Contains("COM4", msg);
        Assert.Contains("another app", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dialout", msg);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AccessDeniedMidReconnectIsCalmOnEitherPlatform(bool isLinux)
    {
        // The device is still re-enumerating after a resume or a replug. Telling the operator to
        // edit their group membership, or that another app stole the port, would be wrong and
        // alarming — the reader is about to succeed on its own.
        var msg = SerialErrors.Describe(
            new UnauthorizedAccessException(), "COM4", isLinux, reconnecting: true);

        Assert.Contains("reconnecting", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dialout", msg);
        Assert.DoesNotContain("another app", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstConnectDenialStillGetsTheFullHint()
    {
        // The calm wording is only for reconnects; a genuine first-connect denial is a real setup
        // problem and must keep saying how to fix it.
        var msg = SerialErrors.Describe(
            new UnauthorizedAccessException(), "/dev/ttyUSB0", isLinux: true, reconnecting: false);
        Assert.Contains("dialout", msg);
    }

    [Fact]
    public void MissingPortAsksAboutTheCable()
    {
        var msg = SerialErrors.Describe(new FileNotFoundException(), "COM7", isLinux: false);
        Assert.Contains("COM7", msg);
        Assert.Contains("LP-100A", msg);
    }

    [Fact]
    public void IoErrorMentionsPowerAndCable()
    {
        var msg = SerialErrors.Describe(new IOException("boom"), "COM7", isLinux: false);
        Assert.Contains("COM7", msg);
        Assert.Contains("powered", msg);
    }

    [Fact]
    public void UnknownErrorFallsBackToItsMessage()
    {
        var msg = SerialErrors.Describe(new InvalidOperationException("something odd"), "COM1", isLinux: false);
        Assert.Contains("something odd", msg);
    }

    [Fact]
    public void ReconnectingOnlySoftensAccessDenied()
    {
        // A missing port mid-reconnect is still worth naming plainly — it is not the transient
        // permissions case the softening exists for.
        var msg = SerialErrors.Describe(
            new FileNotFoundException(), "COM7", isLinux: false, reconnecting: true);
        Assert.Contains("LP-100A", msg);
    }
}
