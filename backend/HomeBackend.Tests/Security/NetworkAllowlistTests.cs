using System.Net;
using HomeBackend.Security;

namespace HomeBackend.Tests.Security;

public class NetworkAllowlistTests
{
    private static readonly IPNetwork[] Lan = [IPNetwork.Parse("192.168.178.0/24"), IPNetwork.Parse("127.0.0.0/8")];

    [Theory]
    [InlineData("192.168.178.20", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::ffff:192.168.178.20", true)] // IPv4 on a dual-stack socket
    [InlineData("192.168.179.20", false)]       // Fritz!Box guest network
    [InlineData("10.0.0.1", false)]
    [InlineData("::1", false)]
    public void Allows_only_configured_networks(string ip, bool allowed) =>
        Assert.Equal(allowed, NetworkAllowlist.IsAllowed(IPAddress.Parse(ip), Lan));

    [Fact]
    public void Unknown_address_is_refused() =>
        Assert.False(NetworkAllowlist.IsAllowed(null, Lan));
}
