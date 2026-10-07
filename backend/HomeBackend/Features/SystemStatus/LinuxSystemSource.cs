using HomeBackend.Infrastructure;

namespace HomeBackend.Features.SystemStatus;

/// <summary>Reads /proc and /etc. Needs no root.</summary>
public sealed class LinuxSystemSource : ISystemSource
{
    public CpuTimes ReadCpuTimes() =>
        ProcParsers.ParseCpuTimes(File.ReadLines("/proc/stat").First());

    public SystemInfo ReadSystem()
    {
        var mem = ProcParsers.ParseMemInfo(File.ReadLines("/proc/meminfo"));
        var root = new DriveInfo("/");
        var load = ProcParsers.ParseLoadAverage(File.ReadAllText("/proc/loadavg"));

        return new SystemInfo(
            Hostname: LinuxFiles.ReadText("/etc/hostname") ?? Environment.MachineName,
            Os: ReadOsName(),
            Kernel: LinuxFiles.ReadText("/proc/sys/kernel/osrelease") ?? "",
            UptimeSeconds: LinuxFiles.ReadUptimeSeconds(),
            LoadAverage: load,
            CpuCount: Environment.ProcessorCount,
            MemoryTotal: mem.GetValueOrDefault("MemTotal"),
            MemoryAvailable: mem.GetValueOrDefault("MemAvailable"),
            DiskTotal: root.TotalSize,
            DiskFree: root.AvailableFreeSpace);
    }

    private static string ReadOsName()
    {
        var name = File.Exists("/etc/os-release") ? ProcParsers.ParsePrettyName(File.ReadLines("/etc/os-release")) : null;
        return name ?? "Linux";
    }
}
