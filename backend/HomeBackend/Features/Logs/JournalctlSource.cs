using System.Text.Json;
using HomeBackend.Infrastructure;

namespace HomeBackend.Features.Logs;

/// <summary>Runs <c>journalctl</c>; the service user is in the systemd-journal group, so no root is needed.</summary>
public sealed class JournalctlSource(ILogger<JournalctlSource> log) : IJournalSource
{
    public async Task<IReadOnlyList<LogEntry>> ReadAsync(string? unit, int lines, CancellationToken ct)
    {
        var output = await ProcessRunner.RunAsync("journalctl", JournalJson.Arguments(unit, lines), ct);
        var result = new List<LogEntry>(lines);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                result.Add(JournalJson.ParseLine(line));
            }
            catch (JsonException ex)
            {
                log.LogDebug(ex, "skipping unparsable journal line");
            }
        }
        return result;
    }
}
