using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Offsite;

/// <summary>A pretend upload for development: runs for a few seconds and prints what vm-offsite would.</summary>
public sealed class MockOffsiteRunner : IOffsiteRunner
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);
    private static readonly string[] Lines =
    [
        "archiving the host's settings into _host/host-20261008-034512.tar.zst",
        "local 8204 MiB, offsite 7391 MiB before the upload",
        "_host/host-20261008-034512.tar.zst: Copied (new)",
        "router/router-20261008-033512.img.zst: Copied (new)",
        "router/router-20261001-033407.img.zst: Moved into backup dir",
        "done: the offsite store holds 8152 MiB",
    ];

    private readonly Lock _lock = new();
    private DateTimeOffset? _started;

    public Task<OffsiteStatus> ReadAsync(CancellationToken ct)
    {
        DateTimeOffset? started;
        lock (_lock) started = _started;
        if (started is not { } s) return Task.FromResult(new OffsiteStatus(true, "never", null, null, []));

        var elapsed = DateTimeOffset.UtcNow - s;
        var done = elapsed >= Duration;
        var shown = done ? Lines.Length : (int)(Lines.Length * elapsed / Duration);
        IReadOnlyList<LogEntry> log = Lines.Take(shown)
            .Select((m, i) => new LogEntry(s.AddSeconds(i * Duration.TotalSeconds / Lines.Length), 6, "vm-offsite", m))
            .Reverse()
            .ToList();
        return Task.FromResult(new OffsiteStatus(true, done ? "succeeded" : "running", s, done ? s + Duration : null, log));
    }

    public Task StartAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_started is not { } s || DateTimeOffset.UtcNow - s >= Duration) _started = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }
}
