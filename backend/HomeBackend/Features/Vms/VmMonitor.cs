namespace HomeBackend.Features.Vms;

/// <summary>
/// Looks at every VM each <see cref="Interval"/>: config, unit state and, for running ones, CPU and memory.
/// Pages read the latest snapshot instead of running systemctl per request; a button refreshes it at once.
/// </summary>
public sealed class VmMonitor(IVmHost host, ILogger<VmMonitor> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly Dictionary<int, (double CpuSeconds, DateTimeOffset At)> _lastCpu = [];
    // the guest agent's answer is rewritten every 15 s: parsed again only when it changed
    private readonly Dictionary<string, (DateTimeOffset Written, IReadOnlyList<GuestAddress>? Addresses)> _addresses = [];
    private volatile IReadOnlyList<VmInfo> _current = [];

    public IReadOnlyList<VmInfo> Current => _current;

    public VmInfo? Find(string name) => _current.FirstOrDefault(v => v.Name == name);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try { await RefreshAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Reading the VMs failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>Takes a new snapshot now (after a button, so the page shows the new state without waiting).</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        await _refreshing.WaitAsync(ct);
        try
        {
            var configs = host.ReadConfigs();
            var units = await host.ReadUnitsAsync(configs.Select(c => c.Name).ToList(), ct);
            var now = DateTimeOffset.UtcNow;
            IReadOnlyDictionary<string, IReadOnlyList<HostNetwork>>? hostNetworks = null;
            var seen = new HashSet<int>();

            _current = configs.Zip(units, (config, unit) =>
            {
                double? cpu = null;
                long? memory = null;
                IReadOnlyList<GuestAddress>? addresses = null;
                if (unit.ActiveState == "active" && Addresses(config.Name, now) is { } found)
                {
                    // read once per round, and only when some VM has addresses to sort
                    hostNetworks ??= host.ReadHostNetworks();
                    var networks = config.Nets.SelectMany(n => hostNetworks.GetValueOrDefault(n.Bridge) ?? []).ToList();
                    addresses = GuestNetwork.Prefer(found, networks);
                }
                if (unit.ActiveState == "active" && unit.MainPid is { } pid && host.ReadProcess(pid) is { } p)
                {
                    seen.Add(pid);
                    memory = p.RssBytes;
                    if (_lastCpu.TryGetValue(pid, out var last) && now > last.At)
                        cpu = Math.Clamp((p.CpuSeconds - last.CpuSeconds) / (now - last.At).TotalSeconds / config.Cpus * 100, 0, 100);
                    _lastCpu[pid] = (p.CpuSeconds, now);
                }
                return new VmInfo(config.Name, SystemctlVmUnits.StateOf(unit), unit.Since, cpu, memory, config, addresses);
            }).ToList();

            // forget processes that are gone, so a reused PID doesn't inherit an old sample
            foreach (var pid in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) _lastCpu.Remove(pid);
            foreach (var name in _addresses.Keys.Where(n => !configs.Any(c => c.Name == n)).ToList()) _addresses.Remove(name);
        }
        finally
        {
            _refreshing.Release();
        }
    }

    private IReadOnlyList<GuestAddress>? Addresses(string name, DateTimeOffset now)
    {
        if (host.ReadGuestNetwork(name) is not { } answer || now - answer.Written > GuestNetwork.MaxAge) return null;
        if (_addresses.TryGetValue(name, out var known) && known.Written == answer.Written) return known.Addresses;
        var addresses = GuestNetwork.Parse(answer.Json);
        _addresses[name] = (answer.Written, addresses);
        return addresses;
    }
}
