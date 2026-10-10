using System.Net;
using System.Net.Sockets;

namespace HomeBackend.Features.Isos;

/// <summary>
/// Connecting to a host with several addresses the way browsers do (RFC 8305, "Happy Eyeballs"): IPv6 and IPv4
/// addresses take turns, each next one is tried <see cref="AttemptDelay"/> after the previous without waiting
/// for it, and the first that connects wins. A host whose IPv6 is broken in a way that makes connections hang
/// (an address, but no way out) then still gets through over IPv4 at once.
/// </summary>
public static class HappyEyeballs
{
    public static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>For <see cref="SocketsHttpHandler.ConnectCallback"/>.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var (host, port) = (context.DnsEndPoint.Host, context.DnsEndPoint.Port);
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        var socket = await ConnectAsync(host, addresses, port, DefaultTimeout, ct);
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>The first socket that connects; an <see cref="IOException"/> saying what each address did when none does.</summary>
    public static Task<Socket> ConnectAsync(string host, IReadOnlyList<IPAddress> addresses, int port, TimeSpan timeout, CancellationToken ct) =>
        ConnectAsync(host, addresses, (address, token) => ConnectSocketAsync(address, port, token), timeout, ct);

    /// <summary>The race itself, with <paramref name="connect"/> making one connection (tests pass their own).</summary>
    public static async Task<T> ConnectAsync<T>(string host, IReadOnlyList<IPAddress> addresses,
        Func<IPAddress, CancellationToken, Task<T>> connect, TimeSpan timeout, CancellationToken ct) where T : class, IDisposable
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var order = Interleave(addresses);
        var errors = new List<string>();
        var pending = new List<Task<T?>>();
        var next = 0;
        try
        {
            while (next < order.Count || pending.Count > 0)
            {
                if (cts.IsCancellationRequested) next = order.Count; // out of time: no new attempts
                if (next < order.Count) pending.Add(TryAsync(order[next++], connect, errors, cts.Token));
                // the next address after the delay, or now if one fails before that
                var waits = pending.Cast<Task>().ToList();
                if (next < order.Count) waits.Add(Task.Delay(AttemptDelay, cts.Token));
                await Task.WhenAny(waits);
                foreach (var done in pending.Where(t => t.IsCompleted).ToList())
                {
                    pending.Remove(done);
                    if (done.Result is { } connection) return connection;
                }
            }
        }
        finally
        {
            // the others give up at once; any that still connects is closed
            cts.Cancel();
            foreach (var t in pending) _ = t.ContinueWith(x => x.Result?.Dispose(), TaskScheduler.Default);
        }
        ct.ThrowIfCancellationRequested();
        string why;
        lock (errors) why = errors.Count > 0 ? string.Join("; ", errors) : "no address";
        throw new IOException($"could not connect to {host}: {why}");
    }

    private static async Task<Socket> ConnectSocketAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<T?> TryAsync<T>(IPAddress address, Func<IPAddress, CancellationToken, Task<T>> connect,
        List<string> errors, CancellationToken ct) where T : class
    {
        try
        {
            return await connect(address, ct);
        }
        catch (Exception ex)
        {
            var family = address.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
            var reason = ex is OperationCanceledException ? "no answer" : (ex as SocketException)?.SocketErrorCode.ToString() ?? ex.Message;
            lock (errors) errors.Add($"{family} {address}: {reason}");
            return null;
        }
    }

    /// <summary>IPv6 first, then the families take turns: 6, 4, 6, 4...</summary>
    private static List<IPAddress> Interleave(IReadOnlyList<IPAddress> addresses)
    {
        var v6 = new Queue<IPAddress>(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6));
        var v4 = new Queue<IPAddress>(addresses.Where(a => a.AddressFamily != AddressFamily.InterNetworkV6));
        var order = new List<IPAddress>(addresses.Count);
        while (v6.Count > 0 || v4.Count > 0)
        {
            if (v6.TryDequeue(out var a)) order.Add(a);
            if (v4.TryDequeue(out var b)) order.Add(b);
        }
        return order;
    }
}
