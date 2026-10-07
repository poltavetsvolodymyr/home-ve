namespace HomeBackend.Features.SystemStatus;

/// <summary>Machine facts shown on the Overview page. Serialized as-is into the API (camelCase).</summary>
/// <param name="LoadAverage">Runnable processes averaged over 1, 5 and 15 minutes; equal to CpuCount = every core busy.</param>
public sealed record SystemInfo(
    string Hostname,
    string Os,
    string Kernel,
    double UptimeSeconds,
    double[] LoadAverage,
    int CpuCount,
    long MemoryTotal,
    long MemoryAvailable,
    long DiskTotal,
    long DiskFree);

/// <summary>Cumulative CPU time counters from /proc/stat; usage is the idle share between two samples.</summary>
public readonly record struct CpuTimes(ulong Total, ulong Idle);
