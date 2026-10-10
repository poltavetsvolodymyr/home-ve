using HomeBackend.Configuration;
using HomeBackend.Features.Auth;
using HomeBackend.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HomeBackend.Tests.Integration;

public class StartupTests
{
    [Fact]
    public async Task Without_a_password_on_real_data_it_starts_waiting_for_the_setup()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "home-backend-tests-" + Guid.NewGuid().ToString("N"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["HomeBackend:DataDir"] = dataDir });
        builder.AddHomeBackend();
        await using var app = builder.Build();

        app.PrepareSetup();

        Assert.True(app.Services.GetRequiredService<PasswordStore>().NeedsSetup);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}\n$", File.ReadAllText(Path.Combine(dataDir, "setup-code")));
        Directory.Delete(dataDir, recursive: true);
    }

    [Fact]
    public void Mock_data_without_a_hash_takes_admin()
    {
        var mock = Store(new HomeBackendOptions { Mock = true });

        Assert.True(mock.Verify("admin"));
        Assert.False(mock.Verify("Admin"));
        Assert.False(mock.NeedsSetup);
        Assert.False(Store(new HomeBackendOptions()).Verify("admin"));
    }

    private static PasswordStore Store(HomeBackendOptions options) => new(Options.Create(options));

    [Fact]
    public void Defaults_fill_only_what_the_config_leaves_empty()
    {
        var options = new HomeBackendOptions { AllowedNetworks = ["10.0.0.0/8"] }.WithDefaults();

        Assert.Equal(["10.0.0.0/8"], options.AllowedNetworks);
        Assert.Equal(["http://127.0.0.1:5000"], options.Urls);
        Assert.Equal("/etc/vm", options.VmConfigDir);
    }

    [Fact]
    public void Without_a_config_every_private_network_gets_in_and_no_disk_group_is_guessed()
    {
        var options = new HomeBackendOptions().WithDefaults();

        Assert.Equal(["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128"], options.AllowedNetworks);
        Assert.Equal("", options.DiskGroup);
        Assert.Equal("home", new HomeBackendOptions { Mock = true }.WithDefaults().DiskGroup);
    }
}
