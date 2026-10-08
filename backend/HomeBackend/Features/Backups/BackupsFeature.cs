using HomeBackend.Api;
using HomeBackend.Features.Logs;
using HomeBackend.Features.Vms;
using HomeBackend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBackend.Features.Backups;

/// <summary>
/// A VM's backups: list, back up now, restore, delete. The work is done by root units (deploy/vm/vm-backup and
/// the rest); the nightly run is vm-backup-all.timer. Page: VM, tab Backups. Endpoints under <c>/api/vms/{name}/backups</c>.
/// </summary>
public static class BackupsFeature
{
    public static IServiceCollection AddBackupsFeature(this IServiceCollection services, bool useMockData)
    {
        services.AddDataSource<IBackupHost, LinuxBackupHost, MockBackupHost>(useMockData);
        services.AddSingleton<BackupRestores>();
        return services;
    }

    public static RouteGroupBuilder MapBackupsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/vms/{name}/backups", ReadAsync);
        api.MapPost("/vms/{name}/backups", BackUpAsync);
        api.MapPost("/vms/{name}/backups/{id}/restore", RestoreAsync);
        api.MapDelete("/vms/{name}/backups/{id}", DeleteAsync);
        return api;
    }

    private static async Task<Results<Ok<VmBackups>, NotFound, ProblemHttpResult>> ReadAsync(
        string name, IBackupHost host, BackupRestores restores, VmMonitor monitor, CancellationToken ct)
    {
        // only known VMs: the name goes into paths and unit names
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();
        try { return TypedResults.Ok(await BuildAsync(vm, host, restores, ct)); }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }

    /// <summary>Starting it while one runs is refused rather than restarting it: a restart would cut the running one off.</summary>
    private static async Task<Results<Ok<VmBackups>, NotFound, Conflict<ErrorResponse>, ProblemHttpResult>> BackUpAsync(
        string name, IBackupHost host, BackupRestores restores, VmMonitor monitor, CancellationToken ct)
    {
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();
        if (!host.IsMounted()) return TypedResults.Conflict(new ErrorResponse("the backup volume is not mounted"));
        if (restores.IsRunning(name)) return TypedResults.Conflict(new ErrorResponse($"a restore of {name} is running"));
        try
        {
            if ((await host.ReadBackupJobAsync(name, ct)).State == "running")
                return TypedResults.Conflict(new ErrorResponse($"a backup of {name} is already running"));
            await host.StartBackupAsync(name, ct);
            return TypedResults.Ok(await BuildAsync(vm, host, restores, ct));
        }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }

    /// <summary>Only onto a stopped VM, and not while a backup of it runs (it would copy a half-written disk).</summary>
    private static async Task<Results<Ok<VmBackups>, NotFound, Conflict<ErrorResponse>, ProblemHttpResult>> RestoreAsync(
        string name, string id, IBackupHost host, BackupRestores restores, VmMonitor monitor, CancellationToken ct)
    {
        await monitor.RefreshAsync(ct);
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();
        if (!BackupUnits.IsValidId(id) || host.List(name).All(b => b.Id != id)) return TypedResults.NotFound();
        if (vm.State is not ("stopped" or "failed")) return TypedResults.Conflict(new ErrorResponse($"{name} is {vm.State}: shut it down first"));
        try
        {
            if ((await host.ReadBackupJobAsync(name, ct)).State == "running")
                return TypedResults.Conflict(new ErrorResponse($"a backup of {name} is running"));
            if (!restores.Start(name, id)) return TypedResults.Conflict(new ErrorResponse($"a restore of {name} is already running"));
            return TypedResults.Ok(await BuildAsync(vm, host, restores, ct));
        }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }

    private static async Task<Results<Ok<VmBackups>, NotFound, Conflict<ErrorResponse>, ProblemHttpResult>> DeleteAsync(
        string name, string id, IBackupHost host, BackupRestores restores, VmMonitor monitor, CancellationToken ct)
    {
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();
        if (!BackupUnits.IsValidId(id) || host.List(name).All(b => b.Id != id)) return TypedResults.NotFound();
        if (restores.Read(name) is { State: "running", Id: var restoring } && restoring == id)
            return TypedResults.Conflict(new ErrorResponse("this backup is being restored"));
        try
        {
            await host.DeleteAsync(name, id, ct);
            return TypedResults.Ok(await BuildAsync(vm, host, restores, ct));
        }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }

    private static async Task<VmBackups> BuildAsync(VmInfo vm, IBackupHost host, BackupRestores restores, CancellationToken ct)
    {
        var name = vm.Name;
        var backup = await host.ReadBackupJobAsync(name, ct);
        var restore = restores.Read(name);

        var log = new List<LogEntry>();
        if (backup.StartedAt is { } b) log.AddRange(await host.ReadLogAsync(BackupUnits.Backup(name), b, ct));
        if (restore is { StartedAt: { } r, Id: { } id }) log.AddRange(await host.ReadLogAsync(BackupUnits.Restore(name, id), r, ct));

        var volume = host.ReadVolume();
        return new VmBackups(host.IsMounted(), vm.Config.Backup, host.List(name), backup, restore,
            log.OrderByDescending(l => l.Time).ToList(), volume?.Free, volume?.Total);
    }
}
