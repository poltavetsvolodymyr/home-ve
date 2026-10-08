using HomeBackend.Features.Logs;
using HomeBackend.Features.Update;
using HomeBackend.Infrastructure;

namespace HomeBackend.Features.Offsite;

/// <summary>
/// vm-offsite.service runs deploy/offsite/vm-offsite as root. The backend may only restart it (polkit rule in
/// deploy/vm/50-home-backend.rules), reads its state and journal, and sees whether rclone.conf exists: the
/// directory is root's 0711, so the file can be found but not read.
/// </summary>
public sealed class SystemctlOffsiteRunner(IJournalSource journal) : IOffsiteRunner
{
    public const string Unit = "vm-offsite.service";
    public const string RcloneConf = "/etc/vm-offsite/rclone.conf";
    public const int LogLines = 300;

    private static readonly string[] ShowArguments =
        ["show", "--no-pager", "--timestamp=unix", "--property=ActiveState,SubState,Result,ExecMainStartTimestamp,ExecMainExitTimestamp", Unit];

    public async Task<OffsiteStatus> ReadAsync(CancellationToken ct)
    {
        var configured = File.Exists(RcloneConf);
        var (state, started, finished) = UpdateUnit.ParseShow(await ProcessRunner.RunAsync("systemctl", ShowArguments, ct));
        if (started is null) return new OffsiteStatus(configured, state, null, null, []);

        // only this run's lines (a second of slack for clock rounding)
        var log = (await journal.ReadAsync(Unit, LogLines, ct))
            .Where(l => l.Time >= started.Value.AddSeconds(-1))
            .ToList();
        return new OffsiteStatus(configured, state, started, finished, log);
    }

    /// <summary>restart: the unit stays "active (exited)" after a run (RemainAfterExit), where a start would do nothing.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var (state, _, _) = UpdateUnit.ParseShow(await ProcessRunner.RunAsync("systemctl", ShowArguments, ct));
        if (state != "running") await ProcessRunner.RunAsync("systemctl", ["restart", "--no-block", Unit], ct);
    }
}
