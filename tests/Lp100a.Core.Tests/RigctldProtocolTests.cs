using Lp100a.Core;

namespace Lp100a.Core.Tests;

public class RigctldProtocolTests
{
    // --- frequency ---

    [Theory]
    [InlineData("14074000\n", 14.074)]
    [InlineData("3573000\n", 3.573)]
    [InlineData("  50313000  \n", 50.313)]
    [InlineData("1836600\n", 1.8366)]
    public void ParsesBareFrequencyLine(string response, double expectedMhz)
    {
        Assert.True(RigctldProtocol.TryParseFrequencyMhz(response, out var mhz));
        Assert.Equal(expectedMhz, mhz, 6);
    }

    [Fact]
    public void ParsesFrequencyFromChattyResponse()
    {
        // Extended/verbose backends can prefix the value; fall back to the long digit run.
        Assert.True(RigctldProtocol.TryParseFrequencyMhz("Frequency: 14074000\nRPRT 0\n", out var mhz));
        Assert.Equal(14.074, mhz, 6);
    }

    [Theory]
    [InlineData("RPRT -1\n")]     // Hamlib error: must not be read as 1 Hz
    [InlineData("RPRT -11\n")]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData(null)]
    [InlineData("0\n")]           // implausible; not a real dial frequency
    public void RejectsNonFrequencyResponses(string? response)
    {
        Assert.False(RigctldProtocol.TryParseFrequencyMhz(response, out _));
    }

    // --- PTT ---

    [Theory]
    [InlineData("0\n", false)]
    [InlineData("1\n", true)]
    [InlineData("2\n", true)]    // non-zero is a PTT type, all meaning "keyed"
    [InlineData(" 1 \n", true)]
    public void ParsesPtt(string response, bool expected)
    {
        Assert.True(RigctldProtocol.TryParsePtt(response, out var tx));
        Assert.Equal(expected, tx);
    }

    [Theory]
    [InlineData("RPRT -11\n")]   // backend can't report PTT -> unknown, NOT "receiving"
    [InlineData("")]
    [InlineData(null)]
    public void UnreportablePttFails(string? response)
    {
        Assert.False(RigctldProtocol.TryParsePtt(response, out _));
    }

    // --- endpoint ---

    [Theory]
    [InlineData("127.0.0.1:4532", "127.0.0.1", 4532)]
    [InlineData("localhost", "localhost", 4532)]        // default port
    [InlineData("10.0.1.7:4533", "10.0.1.7", 4533)]
    [InlineData("  shack.local:4532  ", "shack.local", 4532)]
    [InlineData("host:", "host", 4532)]                 // blank port -> default
    public void ParsesEndpoint(string text, string expectedHost, int expectedPort)
    {
        Assert.True(RigctldProtocol.TryParseEndpoint(text, out var host, out var port));
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(":4532")]        // no host
    [InlineData("host:abc")]     // non-numeric port
    [InlineData("host:99999")]   // out of range
    public void RejectsBadEndpoint(string? text)
    {
        Assert.False(RigctldProtocol.TryParseEndpoint(text, out _, out _));
    }
}
