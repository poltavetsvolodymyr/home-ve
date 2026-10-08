using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Backups;

/// <summary>
/// The backup volume and the root units that do the work (deploy/vm/vm-backup, vm-restore, vm-backup-delete).
/// The backend only lists the files and starts the units: the files themselves are root's.
/// </summary>
public interface IBackupHost
{
    bool IsMounted();

    (long Free, long Total)? ReadVolume();

    /// <summary>A VM's backups, newest first.</summary>
    IReadOnlyList<VmBackup> List(string name);

    /// <summary>The last run of vm-backup@&lt;name&gt;.service.</summary>
    Task<BackupJob> ReadBackupJobAsync(string name, CancellationToken ct);

    /// <summary>Starts a backup and returns at once.</summary>
    Task StartBackupAsync(string name, CancellationToken ct);

    /// <summary>Writes a backup onto the stopped VM's disk; returns when it's done (it can take a while).</summary>
    Task RestoreAsync(string name, string id, CancellationToken ct);

    Task DeleteAsync(string name, string id, CancellationToken ct);

    /// <summary>Journal lines of a run that started at <paramref name="since"/>, newest first.</summary>
    Task<IReadOnlyList<LogEntry>> ReadLogAsync(string unit, DateTimeOffset since, CancellationToken ct);
}
