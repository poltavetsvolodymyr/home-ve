using HomeBackend.Configuration;
using HomeBackend.Features.Logs;
using HomeBackend.Features.Update;
using HomeBackend.Infrastructure;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Backups;

/// <summary>
/// The backup directories are root:home-backend 0750 and the files root's 0600: the backend can list and stat
/// them but not read them. Backing up, restoring and deleting go through the root units, which polkit lets
/// home-backend start (deploy/vm/50-home-backend.rules).
/// </summary>
public sealed class LinuxBackupHost(IOptions<HomeBackendOptions> options, IJournalSource journal) : IBackupHost
{
    public const int LogLines = 200;

    private readonly string _dir = options.Value.BackupDir.TrimEnd('/');

    /// <summary>Whether the directory is a mount point of its own (field 5 of mountinfo), not just a folder on /.</summary>
    public bool IsMounted()
    {
        try
        {
            return File.ReadLines("/proc/self/mountinfo")
                .Select(l => l.Split(' '))
                .Any(f => f.Length > 4 && f[4] == _dir);
        }
        catch (IOException) { return false; }
    }

    public (long Free, long Total)? ReadVolume()
    {
        if (!Directory.Exists(_dir)) return null;
        try
        {
            var drive = new DriveInfo(_dir);
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public IReadOnlyList<VmBackup> List(string name)
    {
        var dir = Path.Combine(_dir, name);
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles($"{name}-*.img.zst")
                .Select(f => (File: f, Id: BackupUnits.IdOf(name, f.Name)))
                .Where(x => x.Id is not null)
                .Select(x => new VmBackup(x.Id!, BackupUnits.TimeOf(x.Id!)!.Value, x.File.Length))
                .OrderByDescending(b => b.Id, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException) { return []; }
    }

    public async Task<BackupJob> ReadBackupJobAsync(string name, CancellationToken ct)
    {
        var (state, started, finished) = UpdateUnit.ParseShow(await ProcessRunner.RunAsync("systemctl", BackupUnits.ShowArguments(name), ct));
        return new BackupJob(state, started, finished);
    }

    /// <summary>restart: the unit stays "active (exited)" after a run (RemainAfterExit), where a start would do nothing.</summary>
    public Task StartBackupAsync(string name, CancellationToken ct) =>
        ProcessRunner.RunAsync("systemctl", ["restart", "--no-block", BackupUnits.Backup(name)], ct);

    /// <summary>systemctl start waits for the oneshot and fails when it does; up to the unit's own 2 h limit.</summary>
    public Task RestoreAsync(string name, string id, CancellationToken ct) =>
        ProcessRunner.RunAsync("systemctl", ["start", BackupUnits.Restore(name, id)], ct, timeoutMs: (int)TimeSpan.FromHours(2.1).TotalMilliseconds);

    public Task DeleteAsync(string name, string id, CancellationToken ct) =>
        ProcessRunner.RunAsync("systemctl", ["start", BackupUnits.Delete(name, id)], ct);

    /// <summary>The journal keeps earlier runs too: only this one's lines (a second of slack for clock rounding).</summary>
    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(string unit, DateTimeOffset since, CancellationToken ct) =>
        (await journal.ReadAsync(unit, LogLines, ct)).Where(l => l.Time >= since.AddSeconds(-1)).ToList();
}
