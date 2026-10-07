using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace HomeBackend.Tests.Integration;

public class ConsoleTests(TestApp app) : IClassFixture<TestApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bytes_pass_both_ways_between_the_websocket_and_the_vnc_socket()
    {
        // a stand-in for QEMU: greets like a VNC server and answers "pong" to "ping"
        var dir = Directory.CreateDirectory(Path.Combine(app.RuntimeDir, "vm-router")).FullName;
        var path = Path.Combine(dir, "vnc.sock");
        File.Delete(path);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen();
        var server = Task.Run(async () =>
        {
            using var vm = await listener.AcceptAsync(Ct);
            await vm.SendAsync("RFB 003.008\n"u8.ToArray(), SocketFlags.None, Ct);
            var buffer = new byte[16];
            var n = await vm.ReceiveAsync(buffer, SocketFlags.None, Ct);
            if (Encoding.ASCII.GetString(buffer, 0, n) == "ping") await vm.SendAsync("pong"u8.ToArray(), SocketFlags.None, Ct);
        }, Ct);

        var cookies = new CookieContainer();
        using (var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = app.BaseAddress })
        {
            http.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.178.60");
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct)).StatusCode);
        }

        using var ws = new ClientWebSocket();
        ws.Options.Cookies = cookies;
        ws.Options.SetRequestHeader("X-Forwarded-For", "192.168.178.60");
        ws.Options.AddSubProtocol("binary");
        await ws.ConnectAsync(new Uri(app.BaseAddress.ToString().Replace("http", "ws") + "api/vms/router/console"), Ct);
        Assert.Equal("binary", ws.SubProtocol);

        Assert.Equal("RFB 003.008\n", await ReceiveText(ws));
        await ws.SendAsync("ping"u8.ToArray(), WebSocketMessageType.Binary, true, Ct);
        Assert.Equal("pong", await ReceiveText(ws));
        await server;
    }

    [Fact]
    public async Task A_stopped_vm_has_no_console()
    {
        var cookies = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = app.BaseAddress };
        http.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.178.61");
        await http.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct);

        using var ws = new ClientWebSocket();
        ws.Options.Cookies = cookies;
        ws.Options.SetRequestHeader("X-Forwarded-For", "192.168.178.61");
        var e = await Assert.ThrowsAsync<WebSocketException>(() =>
            ws.ConnectAsync(new Uri(app.BaseAddress.ToString().Replace("http", "ws") + "api/vms/test/console"), Ct));
        Assert.Contains("404", e.Message);
    }

    private static async Task<string> ReceiveText(ClientWebSocket ws)
    {
        var buffer = new byte[64];
        var r = await ws.ReceiveAsync(buffer, Ct);
        return Encoding.ASCII.GetString(buffer, 0, r.Count);
    }
}
