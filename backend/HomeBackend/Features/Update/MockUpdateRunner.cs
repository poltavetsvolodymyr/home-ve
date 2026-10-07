using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Update;

/// <summary>A pretend update for development: runs for a few seconds and prints what update.sh would.</summary>
public sealed class MockUpdateRunner : IUpdateRunner
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);
    private static readonly string[] Lines =
    [
        "From github.com:poltavetsvolodymyr/home-ve",
        "   b53fc33..4a74ac7  main       -> origin/main",
        "Updating b53fc33..4a74ac7",
        "4a74ac7 No underline on links; card hover only with a real pointer",
        "==> VM tools",
        "==> frontend -> /var/www/home",
        "Done. The backend listens on 127.0.0.1:5000; nginx serves the UI (docs/deployment.md).",
    ];

    private readonly Lock _lock = new();
    private DateTimeOffset? _started;

    public Task<UpdateStatus> ReadAsync(CancellationToken ct)
    {
        DateTimeOffset? started;
        lock (_lock) started = _started;
        if (started is not { } s) return Task.FromResult(new UpdateStatus("never", null, null, []));

        var elapsed = DateTimeOffset.UtcNow - s;
        var done = elapsed >= Duration;
        var shown = done ? Lines.Length : (int)(Lines.Length * elapsed / Duration);
        IReadOnlyList<LogEntry> log = Lines.Take(shown)
            .Select((m, i) => new LogEntry(s.AddSeconds(i * Duration.TotalSeconds / Lines.Length), 6, "update.sh", m))
            .Reverse()
            .ToList();
        return Task.FromResult(new UpdateStatus(done ? "succeeded" : "running", s, done ? s + Duration : null, log));
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
