using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HomeBackend.Tests.Integration;

/// <summary>A fixture of its own: restores and deletions change the mock VMs' backups.</summary>
public class BackupApiTests(TestApp app) : IClassFixture<TestApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> LoginAsync()
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { password = "admin" }, Ct);
        return client;
    }

    [Fact]
    public async Task Backups_need_a_session()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/vms/test/backups", Ct)).StatusCode);
    }

    [Fact]
    public async Task Lists_backups_newest_first()
    {
        var client = await LoginAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/vms/router/backups", Ct);

        Assert.True(list.GetProperty("mounted").GetBoolean());
        Assert.True(list.GetProperty("enabled").GetBoolean());
        var ids = list.GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("id").GetString()).ToList();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.OrderDescending(StringComparer.Ordinal), ids);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/vms/nope/backups", Ct)).StatusCode);
    }

    [Fact]
    public async Task Restore_needs_a_stopped_vm_and_blocks_its_start()
    {
        var client = await LoginAsync();
        var router = await client.GetFromJsonAsync<JsonElement>("/api/vms/router/backups", Ct);
        var routerId = router.GetProperty("backups")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/vms/router/backups/{routerId}/restore", null, Ct)).StatusCode);

        var test = await client.GetFromJsonAsync<JsonElement>("/api/vms/test/backups", Ct);
        var id = test.GetProperty("backups")[0].GetProperty("id").GetString();
        var restore = await client.PostAsync($"/api/vms/test/backups/{id}/restore", null, Ct);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var job = (await restore.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("restore");
        Assert.Equal("running", job.GetProperty("state").GetString());
        Assert.Equal(id, job.GetProperty("id").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/vms/test/start", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/vms/test/backups/{id}/restore", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/vms/test/backups/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Deletes_one_backup()
    {
        var client = await LoginAsync();
        var before = (await client.GetFromJsonAsync<JsonElement>("/api/vms/router/backups", Ct)).GetProperty("backups");
        var last = before[before.GetArrayLength() - 1].GetProperty("id").GetString();

        var deleted = await client.DeleteAsync($"/api/vms/router/backups/{last}", Ct);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var after = (await deleted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("backups");
        Assert.Equal(before.GetArrayLength() - 1, after.GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/vms/router/backups/{last}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/vms/router/backups/..%2Fx", Ct)).StatusCode);
    }

    [Fact]
    public async Task Back_up_now_runs_once()
    {
        var client = await LoginAsync();
        var started = await client.PostAsync("/api/vms/router/backups", null, Ct);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal("running", (await started.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("backup").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/vms/router/backups", null, Ct)).StatusCode);
    }
}
