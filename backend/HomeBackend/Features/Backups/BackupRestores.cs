namespace HomeBackend.Features.Backups;

/// <summary>
/// Restores run in the background: <c>systemctl start vm-restore@…</c> waits for the unit, which can take many
/// minutes. vm-restore@ has no RemainAfterExit (each instance is one backup), so its result is kept here, in
/// memory: after a backend restart the page shows no last restore, the journal still has it.
/// </summary>
public sealed class BackupRestores(IBackupHost host, ILogger<BackupRestores> log)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BackupJob> _jobs = [];

    public BackupJob Read(string name)
    {
        lock (_lock) return _jobs.GetValueOrDefault(name) ?? new BackupJob("never", null, null);
    }

    public bool IsRunning(string name) => Read(name).State == "running";

    /// <summary>False when one is already running for that VM.</summary>
    public bool Start(string name, string id)
    {
        var job = new BackupJob("running", DateTimeOffset.UtcNow, null, id);
        lock (_lock)
        {
            if (_jobs.GetValueOrDefault(name)?.State == "running") return false;
            _jobs[name] = job;
        }
        _ = Task.Run(async () =>
        {
            BackupJob done;
            try
            {
                await host.RestoreAsync(name, id, CancellationToken.None);
                done = job with { State = "succeeded", FinishedAt = DateTimeOffset.UtcNow };
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "restore of {Vm} from {Id} failed", name, id);
                done = job with { State = "failed", FinishedAt = DateTimeOffset.UtcNow, Error = ex.Message };
            }
            lock (_lock) _jobs[name] = done;
        });
        return true;
    }
}
