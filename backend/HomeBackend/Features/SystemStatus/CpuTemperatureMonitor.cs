namespace HomeBackend.Features.SystemStatus;

/// <summary>
/// Reads the CPU temperature every <see cref="Interval"/>, so page loads never wait for the host.
/// Logs once when readings stop and once when they're back, not on every failed attempt.
/// </summary>
public sealed class CpuTemperatureMonitor(ICpuTemperatureSource source, ILogger<CpuTemperatureMonitor> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const string CouldNotRead = "could not read";

    private volatile CpuTemperature _current = new(null, null, source.Source, null);

    /// <summary>The latest reading. Before the first one, both Celsius and Problem are null.</summary>
    public CpuTemperature Current => _current;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                var before = _current;
                try
                {
                    _current = await source.ReadAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (before.Problem != CouldNotRead) log.LogWarning(ex, "reading the CPU temperature failed");
                    _current = new CpuTemperature(null, null, source.Source, CouldNotRead);
                }
                LogChange(before, _current);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        // the timer throws when the app stops; that's not a failure
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void LogChange(CpuTemperature before, CpuTemperature now)
    {
        if (now.Problem is not null && now.Problem != before.Problem && now.Problem != CouldNotRead)
            log.LogWarning("No CPU temperature from {Source}: {Problem}", now.Source, now.Problem);
        else if (now.Problem is null && before.Problem is not null)
            log.LogInformation("CPU temperature from {Source} is back", now.Source);
    }
}
