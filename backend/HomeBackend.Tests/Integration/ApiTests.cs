using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HomeBackend.Tests.Integration;

public class ApiTests(TestApp app) : IClassFixture<TestApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> SignedIn(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/host")]
    [InlineData("/api/vms")]
    [InlineData("/api/vms/router")]
    [InlineData("/api/vms/router/logs")]
    [InlineData("/api/bridges")]
    public async Task Everything_but_login_needs_a_session(string path)
    {
        var response = await app.CreateClient().GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_refused()
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { password = "nope" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Session_lasts_until_logout()
    {
        var client = await SignedIn(app.CreateClient());

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        Assert.Equal("admin", me.GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("/api/host", "system")]
    [InlineData("/api/host", "cpuPercent")]
    [InlineData("/api/host", "cpuTemperature")]
    [InlineData("/api/vms/router", "config")]
    public async Task Objects_come_in_camel_case(string path, string property)
    {
        var client = await SignedIn(app.CreateClient());
        var json = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        Assert.True(json.TryGetProperty(property, out _), $"{path} has no '{property}'");
    }

    [Fact]
    public async Task Vms_list_their_state_and_config()
    {
        var client = await SignedIn(app.CreateClient());
        var vms = await client.GetFromJsonAsync<JsonElement>("/api/vms", Ct);

        var router = vms.EnumerateArray().Single(v => v.GetProperty("name").GetString() == "router");
        Assert.Equal("running", router.GetProperty("state").GetString());
        Assert.Equal(2, router.GetProperty("config").GetProperty("cpus").GetInt32());
        Assert.Equal("br-wan", router.GetProperty("config").GetProperty("nets")[1].GetProperty("bridge").GetString());
    }

    [Fact]
    public async Task Start_brings_a_vm_up()
    {
        var client = await SignedIn(app.CreateClient());
        var response = await client.PostAsync("/api/vms/test/start", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var vm = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("running", vm.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("/api/vms/router/explode", HttpStatusCode.BadRequest)]
    [InlineData("/api/vms/nope/start", HttpStatusCode.NotFound)]
    public async Task Unknown_actions_and_vms_are_refused(string path, HttpStatusCode expected)
    {
        var client = await SignedIn(app.CreateClient());
        Assert.Equal(expected, (await client.PostAsync(path, null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Settings_are_saved_and_checked()
    {
        var client = await SignedIn(app.CreateClient());
        var nets = new[] { new { bridge = "br-lan", mac = "bc:24:11:3a:91:0e" } };

        var ok = await client.PutAsJsonAsync("/api/vms/test/config", new { cpus = 4, memoryMb = 8192, nets, autostart = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var vm = await ok.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(4, vm.GetProperty("config").GetProperty("cpus").GetInt32());
        Assert.Equal("BC:24:11:3A:91:0E", vm.GetProperty("config").GetProperty("nets")[0].GetProperty("mac").GetString());
        Assert.Equal("/dev/home/test", vm.GetProperty("config").GetProperty("disk").GetString()); // untouched

        var noBridge = await client.PutAsJsonAsync("/api/vms/test/config",
            new { cpus = 2, memoryMb = 2048, nets = new[] { new { bridge = "br-dmz", mac = "BC:24:11:3A:91:0E" } }, autostart = false }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noBridge.StatusCode);

        var tooSmall = await client.PutAsJsonAsync("/api/vms/test/config", new { cpus = 0, memoryMb = 2048, nets, autostart = false }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
    }

    [Fact]
    public async Task A_vm_log_is_its_unit_journal()
    {
        var client = await SignedIn(app.CreateClient());
        var lines = await client.GetFromJsonAsync<JsonElement>("/api/vms/router/logs?lines=20", Ct);
        Assert.Equal(20, lines.GetArrayLength());
        Assert.True(lines[0].TryGetProperty("priority", out _));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/vms/nope/logs", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_console_wants_a_websocket()
    {
        var client = await SignedIn(app.CreateClient());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/vms/router/console", Ct)).StatusCode);
    }

    [Fact]
    public async Task Bridges_are_listed_for_the_settings_form()
    {
        var client = await SignedIn(app.CreateClient());
        Assert.Equal(["br-lan", "br-wan"], (await client.GetFromJsonAsync<string[]>("/api/bridges", Ct))!);
    }

    [Fact]
    public async Task Unknown_api_path_is_404()
    {
        var client = await SignedIn(app.CreateClient());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/nope", Ct)).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await app.CreateClient().GetAsync("/api/auth/me", Ct);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Clients_outside_the_allowed_networks_get_no_answer_at_all()
    {
        // another network: the connection is dropped, not even a status code
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => app.CreateClient("192.168.179.20").GetAsync("/api/auth/me", Ct));
    }

    [Fact]
    public async Task Eleventh_login_attempt_within_five_minutes_is_rate_limited()
    {
        var client = app.CreateClient("192.168.178.10");
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { password = "nope" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct)).StatusCode);

        // per client IP: someone else can still log in
        await SignedIn(app.CreateClient("192.168.178.11"));
    }
}
