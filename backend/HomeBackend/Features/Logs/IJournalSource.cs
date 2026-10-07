namespace HomeBackend.Features.Logs;

/// <summary>Reads the systemd journal.</summary>
public interface IJournalSource
{
    /// <summary>The newest <paramref name="lines"/> entries, newest first.</summary>
    /// <param name="unit">A systemd unit name, "kernel" for the kernel ring, or null for the whole journal.</param>
    Task<IReadOnlyList<LogEntry>> ReadAsync(string? unit, int lines, CancellationToken ct);
}
