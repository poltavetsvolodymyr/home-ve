using System.Globalization;

namespace HomeBackend.Features.Vms;

/// <summary>Parsers for /proc/&lt;pid&gt;/stat and statm. Pure functions, unit-tested.</summary>
public static class ProcPid
{
    /// <summary>
    /// utime + stime in clock ticks. The command name (field 2) is in parentheses and may contain spaces or
    /// parentheses itself, so fields are counted from the last ')': utime and stime are fields 14 and 15.
    /// </summary>
    public static ulong ParseCpuTicks(string stat)
    {
        var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        // rest[0] is field 3 (state)
        return ulong.Parse(rest[11], CultureInfo.InvariantCulture) + ulong.Parse(rest[12], CultureInfo.InvariantCulture);
    }

    /// <summary>Resident pages, the second field of statm.</summary>
    public static long ParseResidentPages(string statm) =>
        long.Parse(statm.Split(' ')[1], CultureInfo.InvariantCulture);
}
