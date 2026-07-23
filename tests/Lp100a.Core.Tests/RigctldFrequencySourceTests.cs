using System.Net;
using System.Net.Sockets;
using System.Text;
using Lp100a.Core;

namespace Lp100a.Core.Tests;

/// <summary>
/// End-to-end tests for the rigctld client against a fake daemon on loopback — proves the socket
/// path, not just the parser. Everything runs on an ephemeral port so nothing collides.
/// </summary>
public class RigctldFrequencySourceTests
{
    /// <summary>Minimal stand-in for rigctld: answers "f" with a frequency and "t" with PTT.</summary>
    private sealed class FakeRigctld : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public FakeRigctld(string freqReply, string pttReply)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(() => ServeAsync(freqReply, pttReply, _cts.Token));
        }

        public int Port { get; }

        private async Task ServeAsync(string freqReply, string pttReply, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(ct);
                    using var stream = client.GetStream();
                    var buf = new byte[64];
                    while (!ct.IsCancellationRequested)
                    {
                        var n = await stream.ReadAsync(buf, ct);
                        if (n <= 0) break;
                        var cmd = Encoding.ASCII.GetString(buf, 0, n).Trim();
                        var reply = cmd switch
                        {
                            "f" => freqReply,
                            "t" => pttReply,
                            _ => "RPRT -1\n",
                        };
                        var outBytes = Encoding.ASCII.GetBytes(reply);
                        await stream.WriteAsync(outBytes, ct);
                        await stream.FlushAsync(ct);
                    }
                }
            }
            catch { /* listener torn down */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
            _cts.Dispose();
        }
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return false;
    }

    [Fact]
    public void ReadsFrequencyAndPttFromDaemon()
    {
        using var fake = new FakeRigctld("14074000\n", "1\n");
        using var source = new RigctldFrequencySource("127.0.0.1", fake.Port, pollMs: 100);
        source.Start();

        Assert.True(WaitFor(() => source.FreqMhz is not null), "never read a frequency");
        Assert.Equal(14.074, source.FreqMhz!.Value, 6);
        Assert.True(WaitFor(() => source.IsTransmitting is not null), "never read PTT");
        Assert.True(source.IsTransmitting);
        Assert.True(source.IsConnected);
    }

    [Fact]
    public void PttStaysNullWhenBackendCannotReportIt()
    {
        // A rig with no PTT readback answers RPRT -11. That must read as "unknown", never as
        // "receiving" — the attribution logic has to be able to tell those apart.
        using var fake = new FakeRigctld("7150000\n", "RPRT -11\n");
        using var source = new RigctldFrequencySource("127.0.0.1", fake.Port, pollMs: 100);
        source.Start();

        Assert.True(WaitFor(() => source.FreqMhz is not null), "never read a frequency");
        Assert.Equal(7.15, source.FreqMhz!.Value, 6);
        Assert.Null(source.IsTransmitting);
    }

    [Fact]
    public void ReportsErrorAndStaysNullWhenNothingIsListening()
    {
        // Port 1 on loopback: nothing there. Should surface an error, not throw or hang.
        using var source = new RigctldFrequencySource("127.0.0.1", 1, pollMs: 100);
        source.Start();

        Assert.True(WaitFor(() => source.StatusIsError), "never reported a connection error");
        Assert.Null(source.FreqMhz);
        Assert.False(source.IsConnected);
    }

    [Fact]
    public void StopEndsCleanlyAndClearsReadings()
    {
        using var fake = new FakeRigctld("21205000\n", "0\n");
        var source = new RigctldFrequencySource("127.0.0.1", fake.Port, pollMs: 100);
        source.Start();
        Assert.True(WaitFor(() => source.FreqMhz is not null), "never read a frequency");

        source.Stop();
        Assert.Null(source.FreqMhz);
        Assert.False(source.IsConnected);
        source.Dispose();
    }
}
