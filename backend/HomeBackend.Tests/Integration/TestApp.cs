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
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "home-backend-tests-" + Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private int _lastClientHost = 100;

    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Where the "running" mock VMs keep their sockets (vm-&lt;name&gt;/vnc.sock), for console tests.</summary>
    public string RuntimeDir => Path.Combine(_dataDir, "run");

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HomeBackend:Mock"] = "true",
            ["HomeBackend:Urls:0"] = "http://127.0.0.1:0",
            ["HomeBackend:DataDir"] = _dataDir,
            ["HomeBackend:VmRuntimeDir"] = RuntimeDir,
        });
        builder.AddHomeBackend();

        _app = builder.Build();
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
