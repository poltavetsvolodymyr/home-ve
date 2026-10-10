namespace HomeBackend.Features.Vms;

/// <summary>Two fake VMs for development without the host: the router (running) and a stopped test VM.</summary>
public sealed class MockVmHost : IVmHost
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, VmConfig> _configs = new()
    {
        ["router"] = new("router", 2, 2048, "/dev/home/router",
            [new("br-lan", "BC:24:11:14:4D:CD"), new("br-wan", "BC:24:11:C7:E4:7B")], Autostart: true),
        ["test"] = new("test", 2, 4096, "/dev/home/test", [new("br-lan", "BC:24:11:3A:91:0E")], Autostart: false),
    };
    private readonly Dictionary<string, (string State, DateTimeOffset? Since, int? Pid)> _units = new()
    {
        ["router"] = ("active", DateTimeOffset.UtcNow.AddHours(-30), 812),
        ["test"] = ("inactive", null, null),
    };
    private readonly Dictionary<int, double> _cpu = [];

    public IReadOnlyList<VmConfig> ReadConfigs()
    {
        lock (_lock) return _configs.Values.OrderBy(c => c.Name).ToList();
    }

    public void WriteConfig(VmConfig config)
    {
        lock (_lock) _configs[config.Name] = config;
    }

    public bool ConfigExists(string name)
    {
        lock (_lock) return _configs.ContainsKey(name);
    }

    public void DeleteConfig(string name)
    {
        lock (_lock)
        {
            _configs.Remove(name);
            _units.Remove(name);
        }
    }

    public Task RemoveDiskAsync(string name, CancellationToken ct) => Task.Delay(500, ct);

    public Task<IReadOnlyList<VmUnitState>> ReadUnitsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<VmUnitState> list = names
                .Select(n => _units.TryGetValue(n, out var u) ? new VmUnitState(u.State, "", u.Since, u.Pid) : new VmUnitState("inactive", "dead", null, null))
                .ToList();
            return Task.FromResult(list);
        }
    }

    public async Task RunAsync(string name, VmAction action, CancellationToken ct)
    {
        // a short "starting"/"stopping" phase, like the real thing
        var (during, after) = action switch
        {
            VmAction.Start or VmAction.Reboot => ("activating", "active"),
            _ => ("deactivating", "inactive"),
        };
        Set(name, during);
        await Task.Delay(action == VmAction.Poweroff ? 100 : 1500, ct);
        Set(name, after);
    }

    private void Set(string name, string state)
    {
        lock (_lock)
            _units[name] = state == "active"
                ? (state, DateTimeOffset.UtcNow, Random.Shared.Next(1000, 9999))
                : (state, null, null);
    }

    public (double CpuSeconds, long RssBytes)? ReadProcess(int pid)
    {
        lock (_lock)
        {
            _cpu[pid] = _cpu.GetValueOrDefault(pid) + Random.Shared.NextDouble() * 0.2;
            return (_cpu[pid], (long)(1.2 * (1L << 30)) + Random.Shared.Next(1 << 26));
        }
    }

    // the host is on br-lan (so the router's address there comes first); br-wan is only a cable for the router
    public IReadOnlyDictionary<string, IReadOnlyList<HostNetwork>> ReadHostNetworks() =>
        new Dictionary<string, IReadOnlyList<HostNetwork>>
        {
            ["br-lan"] = [new(System.Net.IPAddress.Parse("192.168.178.2"), 24)],
            ["br-wan"] = [],
        };

    // the router says where it is; the test VM has no guest agent
    public (string Json, DateTimeOffset Written)? ReadGuestNetwork(string name) =>
        name == "router"
            ? ("""
               {"return": [
                 {"name": "lo", "ip-addresses": [{"ip-address-type": "ipv4", "ip-address": "127.0.0.1", "prefix": 8}]},
                 {"name": "ppp0", "ip-addresses": [{"ip-address-type": "ipv4", "ip-address": "203.0.113.7", "prefix": 32}]},
                 {"name": "br-lan", "hardware-address": "bc:24:11:14:4d:cd", "ip-addresses": [
                   {"ip-address-type": "ipv4", "ip-address": "192.168.178.1", "prefix": 24},
                   {"ip-address-type": "ipv6", "ip-address": "fd00::1", "prefix": 64},
                   {"ip-address-type": "ipv6", "ip-address": "fe80::be24:11ff:fe14:4dcd", "prefix": 64}]}
               ], "id": 1}
               """, DateTimeOffset.UtcNow)
            : null;

    public IReadOnlyList<string> ReadBridges() => ["br-lan", "br-wan"];
}
