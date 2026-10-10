using System.Net;
using HomeBackend.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace HomeBackend.Tests.Integration;

/// <summary>
/// The real app with mock data on a random loopback port: same middleware, endpoints and JSON as on
/// the router, minus nginx. Requests carry X-Forwarded-For like nginx sends it.
/// </summary>
public sealed class TestApp : IAsyncLifetime
{
    /// <summary>false: no mock data and no password, so the app waits for its first-run setup.</summary>
    public bool Mock { get; init; } = true;

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "home-backend-tests-" + Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private int _lastClientHost = 100;

    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Where the "running" mock VMs keep their sockets (vm-&lt;name&gt;/vnc.sock), for console tests.</summary>
    public string RuntimeDir => Path.Combine(_dataDir, "run");

    /// <summary>The ISO directory; tests drop files into it.</summary>
    public string IsoDir => Path.Combine(_dataDir, "iso");

    /// <summary>HomeBackend:DataDir: the cookie keys, the update channel.</summary>
    public string DataDir => _dataDir;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HomeBackend:Mock"] = Mock ? "true" : "false",
            // nothing of the machine running the tests: no VM configs
            ["HomeBackend:VmConfigDir"] = Path.Combine(_dataDir, "vm"),
            ["HomeBackend:Urls:0"] = "http://127.0.0.1:0",
            ["HomeBackend:DataDir"] = _dataDir,
            ["HomeBackend:VmRuntimeDir"] = RuntimeDir,
            ["HomeBackend:IsoDir"] = IsoDir,
        });
        builder.AddHomeBackend();

        _app = builder.Build();
        _app.PrepareSetup();
        _app.UseHomeBackend();
        await _app.StartAsync();
        BaseAddress = new Uri(_app.Urls.Single());
    }

    /// <summary>
    /// A browser-like client with its own cookie jar, coming from <paramref name="clientIp"/> or else from
    /// a LAN address of its own (the login rate limit counts per address).
    /// </summary>
    public HttpClient CreateClient(string? clientIp = null)
    {
        clientIp ??= $"192.168.178.{Interlocked.Increment(ref _lastClientHost)}";
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = BaseAddress };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { /* best effort */ }
    }
}
