using HomeBackend.Configuration;
using HomeBackend.Features.Auth;
using HomeBackend.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace HomeBackend.Tests.Integration;

public class StartupTests
{
    [Fact]
    public async Task Refuses_to_run_without_a_password_on_real_data()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "home-backend-tests-" + Guid.NewGuid().ToString("N"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["HomeBackend:DataDir"] = dataDir });
        builder.AddHomeBackend();
        await using var app = builder.Build();

        Assert.False(app.HasPasswordOrMockData());
        Directory.Delete(dataDir, recursive: true);
    }

    [Fact]
    public void Mock_data_without_a_hash_takes_admin()
    {
        var options = new HomeBackendOptions { Mock = true };

        Assert.True(AuthFeature.IsPasswordCorrect("admin", options));
        Assert.False(AuthFeature.IsPasswordCorrect("Admin", options));
        Assert.False(AuthFeature.IsPasswordCorrect("admin", new HomeBackendOptions()));
    }

    [Fact]
    public void Defaults_fill_only_what_the_config_leaves_empty()
    {
        var options = new HomeBackendOptions { AllowedNetworks = ["10.0.0.0/8"] }.WithDefaults();

        Assert.Equal(["10.0.0.0/8"], options.AllowedNetworks);
        Assert.Equal(["http://127.0.0.1:5000"], options.Urls);
        Assert.Equal("/etc/vm", options.VmConfigDir);
    }
}
