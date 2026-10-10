using HomeBackend.Configuration;
using HomeBackend.Features.Auth;
using HomeBackend.Features.Backups;
using HomeBackend.Features.Host;
using HomeBackend.Features.Isos;
using HomeBackend.Features.Offsite;
using HomeBackend.Features.Update;
using HomeBackend.Features.Vms;
using HomeBackend.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace HomeBackend.Hosting;

public static class HomeBackendPipeline
{
    /// <summary>
    /// Without a password the UI serves nothing but the first-run setup, which takes the setup code: made here,
    /// into a file for root and this service only (the code itself never goes into the log).
    /// </summary>
    public static void PrepareSetup(this WebApplication app)
    {
        var store = app.Services.GetRequiredService<PasswordStore>();
        if (!store.NeedsSetup) return;
        store.EnsureSetupCode();
        app.Logger.LogWarning("No password yet: open the web UI and enter the setup code from {File} (sudo cat {File}). " +
            "Or set it here: home-backend set-password /etc/home-backend/config.json", store.SetupCodeFile, store.SetupCodeFile);
    }

    /// <summary>Middleware, in the order a request passes through it, then every endpoint.</summary>
    public static WebApplication UseHomeBackend(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<HomeBackendOptions>>().Value;
        app.Logger.LogInformation("Listening on {Urls}, allowing {Networks}", string.Join(", ", options.Urls), string.Join(", ", options.AllowedNetworks));

        // API only: nginx serves the frontend from /var/www/home and proxies /api/ here. Take the client
        // IP (and scheme) from its headers, so the network check and the login rate limit see the real
        // client. Only trusted from loopback (the default KnownProxies/KnownNetworks).
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        });
        app.UseNetworkAllowlist(options.AllowedNetworks);
        app.UseSecurityHeaders();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseWebSockets(); // the VM console

        app.MapGroup("/api/auth").MapAuthEndpoints();

        app.MapGroup("/api").RequireAuthorization()
            .MapHostEndpoints()
            .MapUpdateEndpoints()
            .MapOffsiteEndpoints()
            .MapVmsEndpoints()
            .MapBackupsEndpoints()
            .MapIsoEndpoints();

        return app;
    }
}
