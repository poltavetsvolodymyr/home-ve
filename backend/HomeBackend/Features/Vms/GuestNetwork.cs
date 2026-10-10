using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace HomeBackend.Features.Vms;

/// <summary>An address the guest has on one of its network interfaces, e.g. 192.168.178.119/24 on br0.</summary>
public sealed record GuestAddress(string Interface, string Address, int Prefix);

/// <summary>A network the host itself is on, e.g. 192.168.178.2/24 on br0.</summary>
public readonly record struct HostNetwork(IPAddress Address, int Prefix)
{
    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != Address.AddressFamily) return false;
        var a = address.GetAddressBytes();
        var n = Address.GetAddressBytes();
        // whole bytes of the prefix, then the bits left in the next one
        var bytes = Prefix / 8;
        for (var i = 0; i < bytes; i++)
            if (a[i] != n[i]) return false;
        var bits = Prefix % 8;
        if (bits == 0) return true;
        var mask = (byte)(0xFF << (8 - bits));
        return (a[bytes] & mask) == (n[bytes] & mask);
    }
}

/// <summary>
/// The guest agent's answer to guest-network-get-interfaces, which deploy/vm/vm-guest-net leaves in
/// /run/vm-guest-net/&lt;name&gt;.json. It comes from inside the guest, so nothing in it is trusted: every
/// address is parsed, odd names are dropped, and the list is capped.
/// </summary>
public static class GuestNetwork
{
    public const string Dir = "/run/vm-guest-net";

    /// <summary>Older than this, the file is from a collector that stopped: not shown.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    private const int MaxAddresses = 32;
    private const int MaxNameLength = 32;

    /// <summary>
    /// The addresses worth showing, IPv4 first: no loopback, no link-local (fe80::, 169.254.x.x), each one
    /// once. Null when the text isn't an answer at all.
    /// </summary>
    public static IReadOnlyList<GuestAddress>? Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 }); }
        catch (JsonException) { return null; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("return", out var interfaces)
                || interfaces.ValueKind != JsonValueKind.Array)
                return null;

            var found = new List<(GuestAddress Address, bool V4)>();
            foreach (var nic in interfaces.EnumerateArray())
            {
                if (nic.ValueKind != JsonValueKind.Object
                    || !nic.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String
                    || !nic.TryGetProperty("ip-addresses", out var addresses) || addresses.ValueKind != JsonValueKind.Array)
                    continue;
                var name = n.GetString()!;
                if (!IsPlainName(name)) continue;

                foreach (var a in addresses.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object
                        || !a.TryGetProperty("ip-address", out var ip) || ip.ValueKind != JsonValueKind.String
                        || !IPAddress.TryParse(ip.GetString(), out var address))
                        continue;
                    if (!IsWorthShowing(address)) continue;

                    var v4 = address.AddressFamily == AddressFamily.InterNetwork;
                    var max = v4 ? 32 : 128;
                    var prefix = a.TryGetProperty("prefix", out var p) && p.TryGetInt32(out var bits) && bits >= 0 && bits <= max ? bits : max;
                    // the text IPAddress writes, not the guest's (no zone ids, no odd spellings)
                    var entry = new GuestAddress(name, address.ToString(), prefix);
                    if (!found.Exists(f => f.Address == entry)) found.Add((entry, v4));
                }
            }

            return found.OrderBy(f => f.V4 ? 0 : 1).Select(f => f.Address).Take(MaxAddresses).ToList();
        }
    }

    /// <summary>
    /// The addresses in a network the host has on a bridge the VM is plugged into first: those it is reached at
    /// from the host and the network behind it (the guest's docker0 or VPN aren't). Otherwise as they were:
    /// IPv4 first, then in the guest's own order.
    /// </summary>
    public static IReadOnlyList<GuestAddress> Prefer(IReadOnlyList<GuestAddress> addresses, IReadOnlyList<HostNetwork> networks) =>
        networks.Count == 0
            ? addresses
            : addresses.OrderBy(a => IPAddress.TryParse(a.Address, out var ip) && networks.Any(n => n.Contains(ip)) ? 0 : 1).ToList();

    private static bool IsWorthShowing(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !address.IsIPv6LinkLocal && !address.IsIPv4MappedToIPv6;
        var b = address.GetAddressBytes();
        return b.Length == 4 && !(b[0] == 169 && b[1] == 254);
    }

    // interface names as Linux and Windows have them: letters, digits and a little punctuation
    private static bool IsPlainName(string name) =>
        name.Length is > 0 and <= MaxNameLength
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '@' or ':' or ' ' or '(' or ')' or '#' or '*');
}
