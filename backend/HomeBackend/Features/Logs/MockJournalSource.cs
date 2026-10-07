namespace HomeBackend.Features.Logs;

/// <summary>Fake journal lines for development without the host.</summary>
public sealed class MockJournalSource : IJournalSource
{
    private static readonly string[] Messages =
    [
        "Started vm@router.service - VM router.",
        "qemu-system-x86_64: terminating on signal 15 from pid 1",
        "Stopping vm@router.service - VM router...",
        "vm@router.service: Deactivated successfully.",
        "vm@router.service: Consumed 2h 14min 3.112s CPU time, 1.2G memory peak.",
        "VNC server running on /run/vm-router/vnc.sock",
    ];

    public Task<IReadOnlyList<LogEntry>> ReadAsync(string? unit, int lines, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<LogEntry> list = Enumerable.Range(0, Math.Min(lines, 60))
            .Select(i => new LogEntry(now.AddSeconds(-i * 37), i % 17 == 5 ? 3 : i % 9 == 4 ? 4 : 6, "qemu-system-x86_64", Messages[i % Messages.Length]))
            .ToList();
        return Task.FromResult(list);
    }
}
