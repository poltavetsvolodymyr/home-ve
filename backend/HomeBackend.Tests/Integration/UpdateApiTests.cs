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
