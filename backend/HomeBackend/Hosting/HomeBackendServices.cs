using HomeBackend.Api;
using HomeBackend.Configuration;
using HomeBackend.Features.Auth;
using HomeBackend.Features.Backups;
using HomeBackend.Features.Isos;
using HomeBackend.Features.Logs;
using HomeBackend.Features.Offsite;
using HomeBackend.Features.SystemStatus;
using HomeBackend.Features.Update;
using HomeBackend.Features.Vms;

namespace HomeBackend.Hosting;

public static class HomeBackendServices
{
    /// <summary>Configuration and every service of the app. Each feature registers its own pieces.</summary>
    public static WebApplicationBuilder AddHomeBackend(this WebApplicationBuilder builder)
    {
        // the host's own settings (password hash, networks) win over everything else
        builder.Configuration.AddJsonFile(HomeBackendOptions.ConfigFile, optional: true, reloadOnChange: false);

        var section = builder.Configuration.GetSection(HomeBackendOptions.Section);
        builder.Services.AddOptions<HomeBackendOptions>().Bind(section).PostConfigure(o => o.WithDefaults());

        // a few settings shape the host itself, so they're read right away
        var options = (section.Get<HomeBackendOptions>() ?? new()).WithDefaults();
        builder.WebHost.UseUrls(options.Urls);

        builder.Services.ConfigureHttpJsonOptions(j => j.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        builder.Services
            .AddAuthFeature(keyRingDirectory: options.DataDir)
            .AddSystemStatusFeature(options)
            .AddVmsFeature(options.Mock)
            .AddIsosFeature()
            .AddLogsFeature(options.Mock)
            .AddUpdateFeature(options.Mock)
            .AddOffsiteFeature(options.Mock)
            .AddBackupsFeature(options.Mock);

        return builder;
    }
}
