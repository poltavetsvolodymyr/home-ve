using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Update;

/// <summary>A pretend update for development: runs for a few seconds and prints what update.sh would.</summary>
public sealed class MockUpdateRunner : IUpdateRunner
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(6);
    private static readonly string[] Lines =
    [
        "channel stable: v0.4.0 -> v0.5.0",
        "2a90a19 install.sh: no more taking PasswordHash out of config.json",
        "==> downloading https://github.com/poltavetsvolodymyr/home-ve/releases/download/v0.5.0/home-ve-v0.5.0-linux-x64.tar.gz",
        "==> build of 2a90a19 in place (v0.5.0/home-ve-v0.5.0-linux-x64.tar.gz)",
        "==> version v0.5.0",
        "==> VM tools",
        "==> frontend -> /var/www/home",
        "Done. The web UI: https://192.168.178.2/ (docs/deployment.md).",
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

    public string? ReadVersion() => "v0.5.0";

    public Task StartAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_started is not { } s || DateTimeOffset.UtcNow - s >= Duration) _started = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }
}
