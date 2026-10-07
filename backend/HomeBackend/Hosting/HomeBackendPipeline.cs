using HomeBackend.Configuration;
using HomeBackend.Features.Auth;
using HomeBackend.Features.Host;
using HomeBackend.Features.Isos;
using HomeBackend.Features.Update;
using HomeBackend.Features.Vms;
using HomeBackend.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace HomeBackend.Hosting;

public static class HomeBackendPipeline
{
    /// <summary>The UI never runs without a password, except with mock data during development.</summary>
    public static bool HasPasswordOrMockData(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<HomeBackendOptions>>().Value;
        if (!string.IsNullOrEmpty(options.PasswordHash) || options.Mock) return true;

        app.Logger.LogCritical("HomeBackend:PasswordHash is not set. Run: home-backend set-password /etc/home-backend/config.json");
        return false;
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
            .MapVmsEndpoints()
            .MapIsoEndpoints();

        return app;
    }
}
