using HomeBackend.Features.Logs;
using HomeBackend.Infrastructure;

namespace HomeBackend.Features.Update;

/// <summary>
/// home-update.service runs deploy/update.sh as root. The backend may only start it (polkit rule in
/// deploy/vm/50-home-backend.rules) and reads its state and journal like any other unit.
/// </summary>
public sealed class SystemctlUpdateRunner(IJournalSource journal) : IUpdateRunner
{
    public const int LogLines = 300;

    public async Task<UpdateStatus> ReadAsync(CancellationToken ct)
    {
        var (state, started, finished) = UpdateUnit.ParseShow(await ProcessRunner.RunAsync("systemctl", UpdateUnit.ShowArguments, ct));
        if (started is null) return new UpdateStatus(state, null, null, []);

        // the journal keeps earlier runs too: only this one's lines (a second of slack for clock rounding)
        var log = (await journal.ReadAsync(UpdateUnit.Name, LogLines, ct))
            .Where(l => l.Time >= started.Value.AddSeconds(-1))
            .ToList();
        return new UpdateStatus(state, started, finished, log);
    }

    /// <summary>A restart while it runs would cut install.sh off halfway, so a running update is left alone.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var (state, _, _) = UpdateUnit.ParseShow(await ProcessRunner.RunAsync("systemctl", UpdateUnit.ShowArguments, ct));
        if (state != "running") await ProcessRunner.RunAsync("systemctl", UpdateUnit.StartArguments, ct);
    }
}
