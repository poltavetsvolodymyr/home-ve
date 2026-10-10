using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HomeBackend.Features.Isos;

namespace HomeBackend.Tests.Features;

/// <summary>The race with a stand-in for the network: which address hangs, refuses or answers is up to the test.</summary>
public class HappyEyeballsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IPAddress V6 = IPAddress.Parse("2001:db8::1"), V4 = IPAddress.Parse("192.0.2.1"), V4b = IPAddress.Parse("192.0.2.2");

    private sealed class Connection(IPAddress address) : IDisposable
    {
        public IPAddress Address { get; } = address;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static Func<IPAddress, CancellationToken, Task<Connection>> Network(Func<IPAddress, CancellationToken, Task> behaviour) =>
        async (a, ct) =>
        {
            await behaviour(a, ct);
            return new Connection(a);
        };

    private static Task Hang(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

    [Fact]
    public async Task A_hanging_IPv6_does_not_hold_up_IPv4()
    {
        var watch = Stopwatch.StartNew();
        var c = await HappyEyeballs.ConnectAsync("host", [V6, V4],
            Network((a, ct) => a.Equals(V6) ? Hang(ct) : Task.CompletedTask), TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(V4, c.Address);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task IPv6_goes_first_when_both_answer() =>
        Assert.Equal(V6, (await HappyEyeballs.ConnectAsync("host", [V4, V6], Network((_, _) => Task.CompletedTask),
            TimeSpan.FromSeconds(10), Ct)).Address);

    [Fact]
    public async Task A_refusal_moves_on_at_once_without_waiting_for_the_delay()
    {
        var watch = Stopwatch.StartNew();
        var c = await HappyEyeballs.ConnectAsync("host", [V4, V4b],
            Network((a, _) => a.Equals(V4) ? throw new SocketException((int)SocketError.ConnectionRefused) : Task.CompletedTask),
            TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(V4b, c.Address);
        Assert.True(watch.Elapsed < HappyEyeballs.AttemptDelay, $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task When_nothing_connects_the_error_says_what_each_address_did()
    {
        var ex = await Assert.ThrowsAsync<IOException>(() => HappyEyeballs.ConnectAsync("host", [V6, V4],
            Network((a, ct) => a.Equals(V6) ? throw new SocketException((int)SocketError.NetworkUnreachable) : Hang(ct)),
            TimeSpan.FromSeconds(1), Ct));

        Assert.Equal("could not connect to host: IPv6 2001:db8::1: NetworkUnreachable; IPv4 192.0.2.1: no answer", ex.Message);
    }

    [Fact]
    public async Task A_late_winner_is_closed_when_another_already_won()
    {
        var late = new TaskCompletionSource();
        Connection? loser = null;
        var c = await HappyEyeballs.ConnectAsync("host", [V6, V4], async (a, ct) =>
        {
            if (a.Equals(V6))
            {
                await late.Task; // finishes only after V4 has won, ignoring the cancellation
                return loser = new Connection(a);
            }
            return new Connection(a);
        }, TimeSpan.FromSeconds(10), Ct);
        late.SetResult();
        for (var i = 0; i < 50 && loser is not { Disposed: true }; i++) await Task.Delay(20, Ct);

        Assert.Equal(V4, c.Address);
        Assert.True(loser?.Disposed);
    }

    [Fact]
    public async Task A_real_connection_over_loopback()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            using var socket = await HappyEyeballs.ConnectAsync("localhost", [IPAddress.Loopback],
                ((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromSeconds(5), Ct);
            Assert.True(socket.Connected);
        }
        finally { server.Stop(); }
    }
}
