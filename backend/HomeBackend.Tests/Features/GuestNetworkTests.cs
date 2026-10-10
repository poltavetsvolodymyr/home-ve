using HomeBackend.Features.Vms;

namespace HomeBackend.Tests.Features;

public class GuestNetworkTests
{
    [Fact]
    public void Shows_the_addresses_ipv4_first_without_loopback_and_link_local()
    {
        var addresses = GuestNetwork.Parse("""
            {"return": [
              {"name": "lo", "ip-addresses": [
                {"ip-address-type": "ipv4", "ip-address": "127.0.0.1", "prefix": 8},
                {"ip-address-type": "ipv6", "ip-address": "::1", "prefix": 128}]},
              {"name": "enp0s3", "hardware-address": "bc:24:11:00:00:01"},
              {"name": "br0", "ip-addresses": [
                {"ip-address-type": "ipv6", "ip-address": "2a02:8109:1234::5", "prefix": 64},
                {"ip-address-type": "ipv6", "ip-address": "fe80::1%br0", "prefix": 64},
                {"ip-address-type": "ipv4", "ip-address": "192.168.178.119", "prefix": 24},
                {"ip-address-type": "ipv4", "ip-address": "169.254.3.4", "prefix": 16}]}
            ], "id": 1791655902}
            """);

        Assert.Equal(
            [new GuestAddress("br0", "192.168.178.119", 24), new GuestAddress("br0", "2a02:8109:1234::5", 64)],
            addresses);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"error": {"class": "CommandNotFound"}, "id": 1}""")]
    [InlineData("""{"return": {}, "id": 1}""")]
    [InlineData("[1, 2]")]
    public void Anything_but_an_answer_is_nothing(string json) =>
        Assert.Null(GuestNetwork.Parse(json));

    [Fact]
    public void Odd_entries_from_the_guest_are_dropped()
    {
        var addresses = GuestNetwork.Parse("""
            {"return": [
              {"name": "<script>", "ip-addresses": [{"ip-address": "10.0.0.1", "prefix": 8}]},
              {"name": "eth0", "ip-addresses": [
                {"ip-address": "not an address", "prefix": 8},
                {"ip-address": 42},
                "a string",
                {"ip-address": "10.0.0.2", "prefix": 99},
                {"ip-address": "10.0.0.2", "prefix": 99}]},
              {"name": 7, "ip-addresses": [{"ip-address": "10.0.0.3"}]},
              {"name": "eth1", "ip-addresses": "10.0.0.4"}
            ]}
            """);

        // a prefix out of range counts as a single address; the same one twice is shown once
        Assert.Equal([new GuestAddress("eth0", "10.0.0.2", 32)], addresses);
    }

    [Fact]
    public void A_guest_with_very_many_addresses_shows_the_first_32()
    {
        var nics = string.Join(',', Enumerable.Range(1, 50).Select(i =>
            $$"""{"name": "veth{{i}}", "ip-addresses": [{"ip-address": "10.1.0.{{i}}", "prefix": 24}]}"""));

        var addresses = GuestNetwork.Parse($$"""{"return": [{{nics}}]}""");

        Assert.Equal(32, addresses!.Count);
        Assert.Equal("10.1.0.1", addresses[0].Address);
    }
}
