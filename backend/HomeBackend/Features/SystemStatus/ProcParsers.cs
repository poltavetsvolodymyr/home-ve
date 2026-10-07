using System.Globalization;

namespace HomeBackend.Features.SystemStatus;

/// <summary>Parsers for /proc and /etc file formats. Pure functions, so they're unit-tested without a Linux box.</summary>
public static class ProcParsers
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>First line of /proc/stat: <c>cpu  user nice system idle iowait irq softirq steal guest guest_nice</c>.</summary>
    public static CpuTimes ParseCpuTimes(string statCpuLine)
    {
        var f = statCpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(ulong.Parse).ToArray();
        ulong total = 0;
        foreach (var v in f) total += v;
        return new CpuTimes(total, f[3] + f[4]);
    }

    /// <summary>/proc/meminfo lines (<c>MemTotal:  2040108 kB</c>) to bytes by key.</summary>
    public static Dictionary<string, long> ParseMemInfo(IEnumerable<string> lines) =>
        lines
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => long.Parse(p[1].Trim().Split(' ')[0], Inv) * 1024);

    /// <summary>The 1, 5 and 15 minute averages from /proc/loadavg.</summary>
    public static double[] ParseLoadAverage(string loadavg) =>
        loadavg.Split(' ').Take(3).Select(s => double.Parse(s, Inv)).ToArray();

    /// <summary>PRETTY_NAME from /etc/os-release, or null when it's not there.</summary>
    public static string? ParsePrettyName(IEnumerable<string> osReleaseLines) =>
        osReleaseLines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))?["PRETTY_NAME=".Length..].Trim('"');
}
