using System.Net.Sockets;
using System.Net.WebSockets;
using HomeBackend.Configuration;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Vms;

/// <summary>
/// The VM's screen in the browser. QEMU serves VNC on /run/vm-&lt;name&gt;/vnc.sock; noVNC on the page speaks VNC
/// over a WebSocket. This passes bytes between the two, nothing more: VNC itself stays unencrypted, but it only
/// ever travels inside the host and then through nginx's HTTPS.
/// </summary>
public static class VmConsole
{
    public static async Task HandleAsync(HttpContext http, string name, IOptions<HomeBackendOptions> options, VmMonitor monitor)
    {
        if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = 400; return; }
        if (monitor.Find(name) is not { State: "running" }) { http.Response.StatusCode = 404; return; }

        var path = Path.Combine(options.Value.VmRuntimeDir, $"vm-{name}", "vnc.sock");
        using var vnc = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await vnc.ConnectAsync(new UnixDomainSocketEndPoint(path), http.RequestAborted);
        }
        catch (SocketException)
        {
            http.Response.StatusCode = 502; // the VM runs but has no VNC socket (yet)
            return;
        }

        // noVNC asks for the "binary" subprotocol
        var protocol = http.WebSockets.WebSocketRequestedProtocols.Contains("binary") ? "binary" : null;
        using var ws = await http.WebSockets.AcceptWebSocketAsync(protocol);

        // Each direction ends on its own: the VM closing its socket closes the WebSocket properly, the
        // browser closing the WebSocket closes the socket. Cancelling a WebSocket receive would abort it
        // without a close frame, so nothing here is cancelled except when the request itself goes away.
        var toBrowser = PumpAsync(vnc, ws, http.RequestAborted);
        var toVm = PumpAsync(ws, vnc, http.RequestAborted);

        if (await Task.WhenAny(toBrowser, toVm) == toBrowser)
        {
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "the VM closed the console", CancellationToken.None); }
                catch (WebSocketException) { /* the browser is gone already */ }
            }
            // the browser answers the close; give it a moment, then stop waiting
            await Task.WhenAny(toVm, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        else
        {
            vnc.Shutdown(SocketShutdown.Both);
            await toBrowser;
            if (ws.State == WebSocketState.CloseReceived)
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
    }

    private static async Task PumpAsync(Socket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var n = await from.ReceiveAsync(buffer, SocketFlags.None, ct);
                if (n == 0) return;
                await to.SendAsync(buffer.AsMemory(0, n), WebSocketMessageType.Binary, endOfMessage: true, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or WebSocketException) { }
    }

    private static async Task PumpAsync(WebSocket from, Socket to, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var r = await from.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                await to.SendAsync(buffer.AsMemory(0, r.Count), SocketFlags.None, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or WebSocketException) { }
    }
}
