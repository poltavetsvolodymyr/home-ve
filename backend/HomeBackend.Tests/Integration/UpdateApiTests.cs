using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HomeBackend.Tests.Integration;

/// <summary>A fixture of its own: starting the (mock) update changes what the other tests would see.</summary>
public class UpdateApiTests(TestApp app) : IClassFixture<TestApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_needs_a_session()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/host/update", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/host/update", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/host/update/channel", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/host/update/channel", new { channel = "dev" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_channel_is_stable_until_switched_and_takes_only_stable_or_dev()
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct);

        var initial = await client.GetFromJsonAsync<JsonElement>("/api/host/update/channel", Ct);
        Assert.Equal("stable", initial.GetProperty("channel").GetString());
        Assert.False(string.IsNullOrEmpty(initial.GetProperty("version").GetString()));

        var switched = await client.PutAsJsonAsync("/api/host/update/channel", new { channel = "dev" }, Ct);
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal("dev", (await switched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("channel").GetString());
        // what update.sh reads, as root
        Assert.Equal("dev\n", await File.ReadAllTextAsync(Path.Combine(app.DataDir, "update-channel"), Ct));

        var refused = await client.PutAsJsonAsync("/api/host/update/channel", new { channel = "main; rm -rf /" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("dev", (await client.GetFromJsonAsync<JsonElement>("/api/host/update/channel", Ct)).GetProperty("channel").GetString());
    }

    [Fact]
    public async Task Starting_the_update_shows_it_running()
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct);

        var before = await client.GetFromJsonAsync<JsonElement>("/api/host/update", Ct);
        Assert.Equal("never", before.GetProperty("state").GetString());

        var started = await client.PostAsync("/api/host/update", null, Ct);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var status = await started.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("running", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, status.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Array, status.GetProperty("log").ValueKind);
    }
}
