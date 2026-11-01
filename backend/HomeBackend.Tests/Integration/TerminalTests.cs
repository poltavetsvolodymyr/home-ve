using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace HomeBackend.Tests.Integration;

public class TerminalTests(TestApp app) : IClassFixture<TestApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_tabs_share_the_one_console_connection()
    {
        // a stand-in for QEMU's serial console: takes one client, like QEMU, and echoes what it gets in brackets
        using var listener = Listen("router");
        var server = Task.Run(async () =>
        {
            using var vm = await listener.AcceptAsync(Ct);
            var buffer = new byte[64];
            var n = await vm.ReceiveAsync(buffer, SocketFlags.None, Ct);
            await vm.SendAsync(Encoding.ASCII.GetBytes($"[{Encoding.ASCII.GetString(buffer, 0, n)}]"), SocketFlags.None, Ct);
        }, Ct);

        using var phone = await OpenAsync("router", "192.168.178.70");
        using var laptop = await OpenAsync("router", "192.168.178.71");
        await phone.SendAsync("ls\r"u8.ToArray(), WebSocketMessageType.Binary, true, Ct);

        Assert.Equal("[ls\r]", await ReceiveText(phone));
        Assert.Equal("[ls\r]", await ReceiveText(laptop));
        await server;
        // the VM hung up: both tabs are told, and the next tab opens a new connection
        Assert.Equal("the VM closed the console", await ClosedWith(phone));
        Assert.Equal("the VM closed the console", await ClosedWith(laptop));
    }

    [Fact]
    public async Task A_tab_starts_with_the_end_of_the_console_log()
    {
        Directory.CreateDirectory(app.ConsoleLogDir);
        // more than the tail: it starts after the first line break in it, not in the middle of a line
        var log = new string('x', 70 * 1024) + "\nboot ok\r\nlogin: ";
        await File.WriteAllTextAsync(Path.Combine(app.ConsoleLogDir, "router.console.log"), log, Ct);
        using var listener = Listen("router");
        var server = listener.AcceptAsync(Ct).AsTask();

        using var ws = await OpenAsync("router", "192.168.178.72");
        Assert.Equal("boot ok\r\nlogin: ", await ReceiveText(ws));
        (await server).Dispose();
        await ClosedWith(ws);
        File.Delete(Path.Combine(app.ConsoleLogDir, "router.console.log"));
    }

    [Fact]
    public async Task A_running_vm_without_its_console_socket_is_a_bad_gateway()
    {
        File.Delete(Path.Combine(app.RuntimeDir, "vm-router", "console.sock"));
        var e = await Assert.ThrowsAsync<WebSocketException>(() => OpenAsync("router", "192.168.178.73"));
        Assert.Contains("502", e.Message);
    }

    [Fact]
    public async Task A_stopped_vm_has_no_terminal()
    {
        var e = await Assert.ThrowsAsync<WebSocketException>(() => OpenAsync("test", "192.168.178.74"));
        Assert.Contains("404", e.Message);
    }

    private Socket Listen(string vm)
    {
        var dir = Directory.CreateDirectory(Path.Combine(app.RuntimeDir, $"vm-{vm}")).FullName;
        var path = Path.Combine(dir, "console.sock");
        File.Delete(path);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen();
        return listener;
    }

    private async Task<ClientWebSocket> OpenAsync(string vm, string clientIp)
    {
        var cookies = new CookieContainer();
        using (var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = app.BaseAddress })
        {
            http.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct)).StatusCode);
        }
        var ws = new ClientWebSocket();
        ws.Options.Cookies = cookies;
        ws.Options.SetRequestHeader("X-Forwarded-For", clientIp);
        try
        {
            await ws.ConnectAsync(new Uri(app.BaseAddress.ToString().Replace("http", "ws") + $"api/vms/{vm}/terminal"), Ct);
        }
        catch
        {
            ws.Dispose();
            throw;
        }
        return ws;
    }

    /// <summary>Waits for the backend to close the WebSocket; its reason.</summary>
    private static async Task<string?> ClosedWith(ClientWebSocket ws)
    {
        var buffer = new byte[64];
        while ((await ws.ReceiveAsync(buffer, Ct)).MessageType != WebSocketMessageType.Close) { }
        return ws.CloseStatusDescription;
    }

    private static async Task<string> ReceiveText(ClientWebSocket ws)
    {
        var buffer = new byte[64];
        var r = await ws.ReceiveAsync(buffer, Ct);
        return Encoding.ASCII.GetString(buffer, 0, r.Count);
    }
}
