using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Backups;

/// <summary>One backup of a VM's disk: <c>&lt;BackupDir&gt;/&lt;vm&gt;/&lt;vm&gt;-&lt;Id&gt;.img.zst</c>.</summary>
/// <param name="Id">The time it was taken, <c>YYYYmmdd-HHMMSS</c> in the host's time zone.</param>
/// <param name="SizeBytes">The compressed file.</param>
public sealed record VmBackup(string Id, DateTimeOffset Time, long SizeBytes);

/// <summary>The last run of vm-backup@ or vm-restore@ for a VM.</summary>
/// <param name="State">never, running, succeeded or failed.</param>
/// <param name="Id">For a restore: which backup.</param>
/// <param name="Error">For a failed restore: what systemctl said.</param>
public sealed record BackupJob(string State, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Id = null, string? Error = null);

/// <summary>Response of <c>GET /api/vms/{name}/backups</c>.</summary>
/// <param name="Mounted">The backup volume is mounted; without it nothing can be backed up.</param>
/// <param name="Enabled">The VM is in the nightly backups (BACKUP in its config).</param>
/// <param name="Backups">Newest first.</param>
/// <param name="Log">The last backup's and the last restore's journal lines, newest first.</param>
public sealed record VmBackups(bool Mounted, bool Enabled, IReadOnlyList<VmBackup> Backups, BackupJob Backup, BackupJob Restore,
    IReadOnlyList<LogEntry> Log, long? FreeBytes, long? TotalBytes);
