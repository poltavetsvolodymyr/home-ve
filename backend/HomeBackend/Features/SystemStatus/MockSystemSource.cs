using HomeBackend.Infrastructure;

namespace HomeBackend.Features.SystemStatus;

/// <summary>Plausible fake host data for development without the host.</summary>
public sealed class MockSystemSource : ISystemSource
{
    private readonly Lock _lock = new();
    private ulong _cpuTotal, _cpuIdle;

    public CpuTimes ReadCpuTimes()
    {
        lock (_lock)
        {
            _cpuTotal += 400;
            _cpuIdle += (ulong)Random.Shared.Next(330, 395);
            return new CpuTimes(_cpuTotal, _cpuIdle);
        }
    }

    public SystemInfo ReadSystem() => new(
        "home", "Debian GNU/Linux 13 (trixie)", "6.12.111+deb13-amd64",
        (DateTimeOffset.UtcNow - MockClock.BootTime).TotalSeconds,
        [0.08 + Random.Shared.NextDouble() * 0.1, 0.06, 0.05],
        16, 30L << 30, (long)(26.5 * (1L << 30)) + Random.Shared.Next(1 << 24),
        40L << 30, 37L << 30);
}
