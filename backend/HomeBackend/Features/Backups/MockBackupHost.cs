using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Backups;

/// <summary>Pretend backups for development: a week of them for every VM; a backup or restore takes a few seconds.</summary>
public sealed class MockBackupHost : IBackupHost
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<VmBackup>> _backups = [];
    private readonly Dictionary<string, DateTimeOffset> _backupStarted = [];
    private readonly Dictionary<string, (DateTimeOffset At, string Id)> _restoreStarted = [];

    public bool IsMounted() => true;

    public (long Free, long Total)? ReadVolume()
    {
        long used;
        lock (_lock) used = _backups.Values.SelectMany(b => b).Sum(b => b.SizeBytes);
        const long total = 49L << 30;
        return (total - used - (200L << 20), total);
    }

    public IReadOnlyList<VmBackup> List(string name)
    {
        lock (_lock) return Backups(name).OrderByDescending(b => b.Id, StringComparer.Ordinal).ToList();
    }

    private List<VmBackup> Backups(string name)
    {
        if (_backups.TryGetValue(name, out var list)) return list;
        var night = DateTime.Today.AddHours(3).AddMinutes(41);
        list = Enumerable.Range(1, 7)
            .Select(d => Make(night.AddDays(-d), (900L << 20) + d * (7L << 20)))
            .ToList();
        _backups[name] = list;
        return list;
    }

    private static VmBackup Make(DateTime local, long size)
    {
        var id = local.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return new VmBackup(id, BackupUnits.TimeOf(id)!.Value, size);
    }

    public Task<BackupJob> ReadBackupJobAsync(string name, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_backupStarted.TryGetValue(name, out var s)) return Task.FromResult(new BackupJob("never", null, null));
            if (DateTimeOffset.UtcNow - s < Duration) return Task.FromResult(new BackupJob("running", s, null));
            var id = s.LocalDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var list = Backups(name);
            if (list.All(b => b.Id != id)) list.Add(Make(s.LocalDateTime, 930L << 20));
            return Task.FromResult(new BackupJob("succeeded", s, s + Duration));
        }
    }

    public Task StartBackupAsync(string name, CancellationToken ct)
    {
        lock (_lock) _backupStarted[name] = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task RestoreAsync(string name, string id, CancellationToken ct)
    {
        lock (_lock) _restoreStarted[name] = (DateTimeOffset.UtcNow, id);
        return Task.Delay(Duration, ct);
    }

    public Task DeleteAsync(string name, string id, CancellationToken ct)
    {
        lock (_lock) Backups(name).RemoveAll(b => b.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(string unit, DateTimeOffset since, CancellationToken ct)
    {
        string[] lines = unit.StartsWith("vm-restore@", StringComparison.Ordinal)
            ? ["the disk as it was is kept as home/test-undo", "restoring the backup onto the disk", "done"]
            : ["guest file systems frozen", "compressing the disk", "done: 930M"];
        var shown = (int)Math.Clamp((DateTimeOffset.UtcNow - since) / Duration * lines.Length, 0, lines.Length);
        IReadOnlyList<LogEntry> log = lines.Take(shown)
            .Select((m, i) => new LogEntry(since.AddSeconds(i * Duration.TotalSeconds / lines.Length), 6, unit.Split('@')[0], m))
            .Reverse()
            .ToList();
        return Task.FromResult(log);
    }
}
