using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HomeBackend.Tests.Integration;

/// <summary>Creating and deleting VMs and ISO images. A fixture of its own: these tests change the mock host.</summary>
public class VmLifecycleTests(TestApp app) : IClassFixture<TestApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedIn()
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct);
        return client;
    }

    private static object NewVm(string name, int? disk = 10, string? cdrom = null, bool start = false) => new
    {
        name,
        cpus = 1,
        memoryMb = 1024,
        diskSizeGb = disk,
        nets = new[] { new { bridge = "br-lan", mac = "52:54:00:12:34:56" } },
        autostart = false,
        cdrom,
        start,
    };

    [Fact]
    public async Task A_new_vm_gets_a_disk_named_after_it()
    {
        var client = await SignedIn();

        var response = await client.PostAsJsonAsync("/api/vms", NewVm("web"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var vm = await client.GetFromJsonAsync<JsonElement>("/api/vms/web", Ct);
        Assert.Equal("stopped", vm.GetProperty("state").GetString());
        Assert.Equal("/dev/home/web", vm.GetProperty("config").GetProperty("disk").GetString());
        Assert.Equal(10, vm.GetProperty("config").GetProperty("diskSizeGb").GetInt32());
    }

    [Fact]
    public async Task Started_right_away_when_asked()
    {
        var client = await SignedIn();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/vms", NewVm("now", start: true), Ct)).StatusCode);

        var vm = await client.GetFromJsonAsync<JsonElement>("/api/vms/now", Ct);
        Assert.Equal("running", vm.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("router")] // exists
    [InlineData("root")] // the host's own volume
    [InlineData("Web")] // capitals
    public async Task Taken_or_bad_names_are_refused(string name)
    {
        var client = await SignedIn();
        var response = await client.PostAsJsonAsync("/api/vms", NewVm(name), Ct);
        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
    }

    [Fact]
    public async Task A_new_vm_needs_a_disk_size_and_an_iso_that_exists()
    {
        var client = await SignedIn();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/vms", NewVm("nodisk", disk: null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/vms", NewVm("noiso", cdrom: "missing.iso"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_a_stopped_vm_can_be_deleted()
    {
        var client = await SignedIn();
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/vms/router", Ct)).StatusCode);

        await client.PostAsJsonAsync("/api/vms", NewVm("gone"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/vms/gone?disk=true", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/vms/gone", Ct)).StatusCode);
    }

    [Fact]
    public async Task An_iso_in_a_cd_drive_stays_until_ejected()
    {
        var client = await SignedIn();
        Directory.CreateDirectory(app.IsoDir);
        await File.WriteAllTextAsync(Path.Combine(app.IsoDir, "debian.iso"), "not really an ISO", Ct);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/vms", NewVm("inst", cdrom: "debian.iso"), Ct)).StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/api/isos", Ct);
        var iso = list.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "debian.iso");
        Assert.Equal("inst", iso.GetProperty("usedBy")[0].GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/isos/debian.iso", Ct)).StatusCode);

        var eject = new { cpus = 1, memoryMb = 1024, nets = Array.Empty<object>(), autostart = false, cdrom = (string?)null };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/vms/inst/config", eject, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/isos/debian.iso", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("ftp://example.com/a.iso", null)]
    [InlineData("https://example.com/download", null)] // no .iso name in the link and none given
    [InlineData("https://example.com/a.iso", "../a.iso")]
    public async Task Bad_downloads_are_refused_before_they_start(string url, string? name)
    {
        var client = await SignedIn();
        var response = await client.PostAsJsonAsync("/api/isos", new { url, name }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_download_lands_as_an_iso_when_complete()
    {
        // a tiny web server with one 1 MiB "ISO"
        var payload = new byte[1 << 20];
        Random.Shared.NextBytes(payload);
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync(Ct);
            await socket.ReceiveAsync(new byte[4096], Ct);
            var head = $"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await socket.SendAsync(System.Text.Encoding.ASCII.GetBytes(head), Ct);
            await socket.SendAsync(payload, Ct);
        }, Ct);

        var client = await SignedIn();
        var start = await client.PostAsJsonAsync("/api/isos", new { url = $"http://127.0.0.1:{port}/files/tiny-1.0.iso" }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        await server;
        listener.Stop();

        JsonElement list = default;
        for (var i = 0; i < 50; i++)
        {
            list = await client.GetFromJsonAsync<JsonElement>("/api/isos", Ct);
            if (list.GetProperty("downloads").GetArrayLength() == 0) break;
            await Task.Delay(100, Ct);
        }
        var file = list.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "tiny-1.0.iso");
        Assert.Equal(payload.Length, file.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(app.IsoDir, "tiny-1.0.iso"), Ct));
        Assert.Empty(Directory.GetFiles(app.IsoDir, "*.part"));
    }
}
