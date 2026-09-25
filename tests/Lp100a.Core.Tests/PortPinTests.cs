using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class PortPinTests
{
    private const string Meter = "usb-FTDI_FT232R_USB_UART_ABSCDI99-if00-port0";
    private const string Shunt = "usb-VictronEnergy_BV_VE_Direct_cable_VEAUI3T2-if00-port0";

    // ---- ForSave: what may replace the saved pin ----

    [Fact]
    public void NoConnectionThisRunLeavesTheSavedPinAlone()
    {
        // The 1.0.0 bug, exactly: meter unplugged, the list showed the shunt's port, the app closed.
        var (port, serial) = PortPin.ForSave(null, _ => Shunt, "/dev/ttyUSB3", Meter);
        Assert.Equal("/dev/ttyUSB3", port);
        Assert.Equal(Meter, serial);
    }

    [Fact]
    public void AConnectionReplacesThePin()
    {
        var (port, serial) = PortPin.ForSave("/dev/ttyUSB5", p => p == "/dev/ttyUSB5" ? Meter : null, "/dev/ttyUSB3", Meter);
        Assert.Equal("/dev/ttyUSB5", port);
        Assert.Equal(Meter, serial);
    }

    [Fact]
    public void ConnectingToADifferentDeviceOnPurposeRepinsToIt()
    {
        // A deliberate Connect is the user's call; the pin follows it.
        var (port, serial) = PortPin.ForSave("/dev/ttyUSB0", _ => Shunt, "/dev/ttyUSB3", Meter);
        Assert.Equal("/dev/ttyUSB0", port);
        Assert.Equal(Shunt, serial);
    }

    [Fact]
    public void DeviceGoneBySaveTimeKeepsTheSerialButTakesThePort()
    {
        // Connected earlier, unplugged mid-session: the identity is not thrown away.
        var (port, serial) = PortPin.ForSave("COM4", _ => null, "COM1", "ABSCDI99A");
        Assert.Equal("COM4", port);
        Assert.Equal("ABSCDI99A", serial);
    }

    [Fact]
    public void FirstEverConnectionWithNothingSavedPinsIt()
    {
        var (port, serial) = PortPin.ForSave("COM4", _ => "ABSCDI99A", null, null);
        Assert.Equal("COM4", port);
        Assert.Equal("ABSCDI99A", serial);
    }

    [Fact]
    public void NothingSavedAndNoConnectionStaysNothing()
    {
        var (port, serial) = PortPin.ForSave(null, _ => "X", null, null);
        Assert.Null(port);
        Assert.Null(serial);
    }

    // ---- Reselect: what the Setup list shows selected ----

    [Fact]
    public void KeepsTheCurrentSelectionWhenStillPresent() =>
        Assert.Equal("/dev/ttyUSB3", PortPin.Reselect("/dev/ttyUSB3", ["/dev/ttyUSB0", "/dev/ttyUSB3"]));

    [Fact]
    public void SelectsNothingWhenTheCurrentPortIsGone() =>
        Assert.Null(PortPin.Reselect("/dev/ttyUSB3", ["/dev/ttyUSB0", "/dev/ttyUSB1"]));

    [Fact]
    public void NeverGuessesOnAFreshStart() =>
        Assert.Null(PortPin.Reselect(null, ["COM1", "COM3", "COM4"]));

    [Fact]
    public void EmptyListSelectsNothing() =>
        Assert.Null(PortPin.Reselect("COM4", []));

    [Fact]
    public void MatchesWindowsNamesCaseInsensitivelyAndReturnsTheListsSpelling() =>
        Assert.Equal("COM4", PortPin.Reselect("com4", ["COM3", "COM4"]));
}
