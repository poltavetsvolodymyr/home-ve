using System.Net;
using System.Net.Http.Json;

namespace HomeBackend.Tests.Integration;

/// <summary>A host without a password: nothing but the first-run setup, which takes the setup code.</summary>
public sealed class SetupApiTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly TestApp _app = new() { Mock = false };

    public ValueTask InitializeAsync() => _app.InitializeAsync();
    public ValueTask DisposeAsync() => _app.DisposeAsync();

    private string Code => File.ReadAllText(Path.Combine(_app.DataDir, "setup-code")).Trim();

    [Fact]
    public async Task Before_the_setup_nothing_else_opens()
    {
        var client = _app.CreateClient();

        Assert.True((await client.GetFromJsonAsync<SetupStatusDto>("/api/auth/setup", Ct))!.Needed);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/vms", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { password = "" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Wrong_code_or_short_password_sets_nothing()
    {
        var client = _app.CreateClient();

        var wrong = await client.PostAsJsonAsync("/api/auth/setup", new { code = "AAAA-AAAA", password = "secret-password" }, Ct);
        var shortOne = await client.PostAsJsonAsync("/api/auth/setup", new { code = Code, password = "short" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortOne.StatusCode);
        Assert.True((await client.GetFromJsonAsync<SetupStatusDto>("/api/auth/setup", Ct))!.Needed);
    }

    [Fact]
    public async Task The_setup_signs_in_and_after_it_only_the_password_does()
    {
        var client = _app.CreateClient();

        var setup = await client.PostAsJsonAsync("/api/auth/setup", new { code = Code, password = "secret-password" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/vms", Ct)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<SetupStatusDto>("/api/auth/setup", Ct))!.Needed);

        var other = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Conflict,
            (await other.PostAsJsonAsync("/api/auth/setup", new { code = "AAAA-AAAA", password = "taken-over!" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await other.PostAsJsonAsync("/api/auth/login", new { password = "secret-password" }, Ct)).StatusCode);
    }

    private sealed record SetupStatusDto(bool Needed);
}
