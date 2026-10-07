using System.Net;

namespace HomeBackend.Security;

public static class NetworkAllowlist
{
    /// <summary>
    /// Hard boundary: a request from outside <paramref name="allowedNetworks"/> (HomeBackend:AllowedNetworks)
    /// gets no response at all, the connection is just dropped. Runs after the forwarded-headers middleware,
    /// so behind nginx it checks the real client address.
    /// </summary>
    public static IApplicationBuilder UseNetworkAllowlist(this IApplicationBuilder app, IEnumerable<string> allowedNetworks)
    {
        var networks = allowedNetworks.Select(IPNetwork.Parse).ToArray();
        return app.Use(async (ctx, next) =>
        {
            if (!IsAllowed(ctx.Connection.RemoteIpAddress, networks))
            {
                ctx.Abort();
                return;
            }
            await next();
        });
    }

    public static bool IsAllowed(IPAddress? ip, IReadOnlyList<IPNetwork> networks)
    {
        if (ip is { IsIPv4MappedToIPv6: true }) ip = ip.MapToIPv4();
        return ip is not null && networks.Any(n => n.Contains(ip));
    }
}
