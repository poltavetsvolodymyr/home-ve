using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HomeBackend.Features.Logs;

/// <summary>Builds the <c>journalctl -o json</c> command line and parses its lines.</summary>
public static class JournalJson
{
    /// <summary>Newest first (<c>-r</c>); <c>-k</c> for the kernel ring, <c>-u</c> for one unit, nothing for everything.</summary>
    public static List<string> Arguments(string? unit, int lines)
    {
        List<string> args = ["--no-pager", "-o", "json", "-r", "-n", lines.ToString(CultureInfo.InvariantCulture)];
        if (unit == "kernel") args.Add("-k");
        else if (unit is not null) args.AddRange(["-u", unit]);
        return args;
    }

    /// <summary>One line of <c>journalctl -o json</c>. Throws <see cref="JsonException"/> for a line that isn't JSON.</summary>
    public static LogEntry ParseLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        var usec = long.Parse(Field(r, "__REALTIME_TIMESTAMP") ?? "0", CultureInfo.InvariantCulture);
        return new LogEntry(
            DateTimeOffset.FromUnixTimeMilliseconds(usec / 1000),
            int.TryParse(Field(r, "PRIORITY"), out var prio) ? prio : 6,
            Field(r, "SYSLOG_IDENTIFIER") ?? Field(r, "_COMM") ?? "",
            Field(r, "MESSAGE") ?? "");
    }

    // journald emits non-UTF-8 fields as byte arrays
    private static string? Field(JsonElement r, string name)
    {
        if (!r.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array => Encoding.UTF8.GetString(v.EnumerateArray().Select(b => (byte)b.GetInt32()).ToArray()),
            _ => v.ToString(),
        };
    }
}
