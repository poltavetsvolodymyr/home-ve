using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Channels;
using HomeBackend.Configuration;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Vms;

/// <summary>
/// The VM's serial console as a terminal in the browser (the Terminal tab), next to its screen (<see cref="VmConsole"/>).
/// QEMU serves it on /run/vm-&lt;name&gt;/console.sock and takes one client at a time, so this holds that one
/// connection for every tab open on the VM: what the VM writes goes to all of them, what any of them types goes to
/// the VM. The connection is opened by the first tab and closed with the last. A tab starts with the end of the
/// console's log, which QEMU itself writes (deploy/vm/vm-run): the VM's last words before anyone looked.
/// </summary>
public sealed class VmTerminals(IOptions<HomeBackendOptions> options, VmMonitor monitor)
{
    /// <summary>How much of the console's log a tab starts with.</summary>
    public const int LogTailBytes = 64 * 1024;

    /// <summary>Chunks from the VM a tab may fall behind by; a tab further behind (a dead phone) is let go.</summary>
    private const int TabBacklog = 1024;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Session> _open = [];

    public async Task HandleAsync(HttpContext http, string name)
    {
        if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = 400; return; }
        // as for the screen: while QEMU runs, also starting and stopping
        if (monitor.Find(name) is not { State: "running" or "starting" or "stopping" }) { http.Response.StatusCode = 404; return; }

        var tab = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(TabBacklog) { SingleReader = true });
        var session = Join(name, tab);
        if (session is null)
        {
            http.Response.StatusCode = 502; // the VM runs but its console socket isn't there (yet)
            return;
        }
        try
        {
            using var ws = await http.WebSockets.AcceptWebSocketAsync();
            // joined before reading the log: what the VM writes meanwhile may come twice, but none of it is lost
            var tail = ReadLogTail(name);
            if (tail.Length > 0)
                await ws.SendAsync(tail, WebSocketMessageType.Binary, endOfMessage: true, http.RequestAborted);

            var toBrowser = PumpAsync(tab.Reader, ws, http.RequestAborted);
            var toVm = PumpAsync(ws, session, http.RequestAborted);
            if (await Task.WhenAny(toBrowser, toVm) == toBrowser)
            {
                // the VM closed the console (it stopped), or this tab fell too far behind
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "the VM closed the console", CancellationToken.None); }
                    catch (WebSocketException) { /* the browser is gone already */ }
                }
                await Task.WhenAny(toVm, Task.Delay(TimeSpan.FromSeconds(5)));
            }
            else
            {
                tab.Writer.TryComplete();
                await toBrowser;
                if (ws.State == WebSocketState.CloseReceived)
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
        finally
        {
            Leave(name, session, tab);
        }
    }

    /// <summary>Adds the tab to the VM's open connection, or opens one. Null when the console socket isn't there.</summary>
    private Session? Join(string name, Channel<byte[]> tab)
    {
        lock (_gate)
        {
            if (!_open.TryGetValue(name, out var session))
            {
                var path = Path.Combine(options.Value.VmRuntimeDir, $"vm-{name}", "console.sock");
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    // a unix socket connects at once or not at all
                    socket.Connect(new UnixDomainSocketEndPoint(path));
                }
                catch (SocketException)
                {
                    socket.Dispose();
                    return null;
                }
                session = new Session(socket);
                _open[name] = session;
                _ = ReadAsync(name, session);
            }
            session.Tabs.Add(tab);
            return session;
        }
    }

    /// <summary>The last tab to go closes the connection: QEMU can then take another client (socat on the host).</summary>
    private void Leave(string name, Session session, Channel<byte[]> tab)
    {
        tab.Writer.TryComplete();
        lock (_gate)
        {
            session.Tabs.Remove(tab);
            // a connection the VM closed is gone from the list already, maybe replaced by a new one: not this
            if (session.Tabs.Count > 0 || !_open.TryGetValue(name, out var current) || current != session) return;
            _open.Remove(name);
        }
        try { session.Socket.Shutdown(SocketShutdown.Both); }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { /* closed already */ }
    }

    /// <summary>What the VM writes, to every tab. Ends when the VM closes the console or the last tab leaves.</summary>
    private async Task ReadAsync(string name, Session session)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var n = await session.Socket.ReceiveAsync(buffer, SocketFlags.None);
                if (n == 0) break;
                var chunk = buffer[..n];
                lock (_gate)
                {
                    foreach (var tab in session.Tabs)
                        if (!tab.Writer.TryWrite(chunk)) tab.Writer.TryComplete();
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        lock (_gate)
        {
            foreach (var tab in session.Tabs) tab.Writer.TryComplete();
            if (_open.TryGetValue(name, out var current) && current == session) _open.Remove(name);
        }
        session.Socket.Dispose();
    }

    private static async Task PumpAsync(ChannelReader<byte[]> from, WebSocket to, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in from.ReadAllAsync(ct))
                await to.SendAsync(chunk, WebSocketMessageType.Binary, endOfMessage: true, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
    }

    private static async Task PumpAsync(WebSocket from, Session to, CancellationToken ct)
    {
        var buffer = new byte[4 * 1024];
        try
        {
            while (true)
            {
                var r = await from.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                // two tabs typing at once: each send whole, not interleaved
                await to.Writing.WaitAsync(ct);
                try { await to.Socket.SendAsync(buffer.AsMemory(0, r.Count), SocketFlags.None, ct); }
                finally { to.Writing.Release(); }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or WebSocketException or ObjectDisposedException) { }
    }

    /// <summary>
    /// The end of the console's log, from the first line break in it (not from the middle of a line, or of an escape
    /// sequence). Empty when there's no log (an older vm-run, development).
    /// </summary>
    private byte[] ReadLogTail(string name)
    {
        var path = Path.Combine(options.Value.ConsoleLogDir, $"{name}.console.log");
        try
        {
            // QEMU keeps appending to it meanwhile
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, file.Length - LogTailBytes);
            file.Seek(start, SeekOrigin.Begin);
            var tail = new byte[file.Length - start];
            file.ReadExactly(tail);
            if (start == 0) return tail;
            var lineBreak = Array.IndexOf(tail, (byte)'\n');
            return lineBreak < 0 ? [] : tail[(lineBreak + 1)..];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private sealed class Session(Socket socket)
    {
        public Socket Socket { get; } = socket;
        public List<Channel<byte[]>> Tabs { get; } = [];
        public SemaphoreSlim Writing { get; } = new(1, 1);
    }
}
