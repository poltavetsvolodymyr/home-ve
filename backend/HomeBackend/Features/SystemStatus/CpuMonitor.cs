namespace HomeBackend.Features.SystemStatus;

/// <summary>
/// Samples the CPU counters every <see cref="Interval"/>: usage is a rate, so it needs two readings
/// some time apart, and requests shouldn't wait for that.
/// </summary>
public sealed class CpuMonitor(ISystemSource source, ILogger<CpuMonitor> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Lock _lock = new();
    private CpuTimes? _last;
    private double _cpuPercent;

    /// <summary>CPU usage over the last interval, 0–100.</summary>
    public double CpuPercent { get { lock (_lock) return _cpuPercent; } }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try { Sample(); }
                catch (Exception ex) { log.LogWarning(ex, "CPU sampling failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        // the timer throws when the app stops; that's not a failure
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void Sample()
    {
        var cpu = source.ReadCpuTimes();
        lock (_lock)
        {
            if (_last is { } prev && cpu.Total > prev.Total)
                _cpuPercent = 100.0 * (1 - (double)(cpu.Idle - prev.Idle) / (cpu.Total - prev.Total));
            _last = cpu;
        }
    }
}
