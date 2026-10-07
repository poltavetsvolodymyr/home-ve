using System.Globalization;

namespace HomeBackend.Infrastructure;

/// <summary>Small readers for /proc, /sys and /etc files shared by the Linux data sources.</summary>
public static class LinuxFiles
{
    /// <summary>Trimmed file content, or null when the file can't be read.</summary>
    public static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>A number from a one-value file such as /sys/class/net/*/statistics/rx_bytes; 0 when missing.</summary>
    public static ulong ReadULong(string path) =>
        ulong.TryParse(ReadText(path), out var v) ? v : 0;

    /// <summary>Seconds since boot, from /proc/uptime.</summary>
    public static double ReadUptimeSeconds() =>
        double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture);
}
