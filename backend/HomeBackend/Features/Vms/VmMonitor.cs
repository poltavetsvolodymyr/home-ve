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
            var seen = new HashSet<int>();

            _current = configs.Zip(units, (config, unit) =>
            {
                double? cpu = null;
                long? memory = null;
                if (unit.ActiveState == "active" && unit.MainPid is { } pid && host.ReadProcess(pid) is { } p)
                {
                    seen.Add(pid);
                    memory = p.RssBytes;
                    if (_lastCpu.TryGetValue(pid, out var last) && now > last.At)
                        cpu = Math.Clamp((p.CpuSeconds - last.CpuSeconds) / (now - last.At).TotalSeconds / config.Cpus * 100, 0, 100);
                    _lastCpu[pid] = (p.CpuSeconds, now);
                }
                return new VmInfo(config.Name, SystemctlVmUnits.StateOf(unit), unit.Since, cpu, memory, config);
            }).ToList();

            // forget processes that are gone, so a reused PID doesn't inherit an old sample
            foreach (var pid in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) _lastCpu.Remove(pid);
        }
        finally
        {
            _refreshing.Release();
        }
    }
}
