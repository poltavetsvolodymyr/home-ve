using HomeBackend.Api;
using HomeBackend.Configuration;
using HomeBackend.Features.Isos;
using HomeBackend.Features.Logs;
using HomeBackend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Vms;

/// <summary>
/// The VMs: list, state and usage, the buttons, the settings, the log and the console, creating and deleting.
/// Pages: VMs, VM, New VM. Endpoints under <c>/api/vms</c>, plus <c>GET /api/bridges</c>.
/// </summary>
public static class VmsFeature
{
    public const int DefaultLogLines = 200;

    public static IServiceCollection AddVmsFeature(this IServiceCollection services, bool useMockData)
    {
        services.AddDataSource<IVmHost, LinuxVmHost, MockVmHost>(useMockData);
        services.AddSingleton<VmMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<VmMonitor>());
        return services;
    }

    public static RouteGroupBuilder MapVmsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/vms", (VmMonitor monitor) => monitor.Current);
        api.MapPost("/vms", Create);
        api.MapDelete("/vms/{name}", Delete);
        api.MapGet("/vms/{name}", Results<Ok<VmInfo>, NotFound> (string name, VmMonitor monitor) =>
            monitor.Find(name) is { } vm ? TypedResults.Ok(vm) : TypedResults.NotFound());
        api.MapPost("/vms/{name}/{action}", RunAction);
        api.MapPut("/vms/{name}/config", SaveConfig);
        api.MapGet("/vms/{name}/logs", ReadLogs);
        api.Map("/vms/{name}/console", (HttpContext http, string name, IOptions<HomeBackendOptions> options, VmMonitor monitor) =>
            VmConsole.HandleAsync(http, name, options, monitor));
        api.MapGet("/bridges", (IVmHost host) => host.ReadBridges().ToArray());
        return api;
    }

    private static async Task<Results<Ok<VmInfo>, NotFound, BadRequest<ErrorResponse>, ProblemHttpResult>> RunAction(
        string name, string action, IVmHost host, VmMonitor monitor, CancellationToken ct)
    {
        if (!Enum.TryParse<VmAction>(action, ignoreCase: true, out var a) || !Enum.IsDefined(a))
            return TypedResults.BadRequest(new ErrorResponse($"unknown action '{action}'"));
        if (monitor.Find(name) is null) return TypedResults.NotFound();

        try { await host.RunAsync(name, a, ct); }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }

        await monitor.RefreshAsync(ct);
        return monitor.Find(name) is { } vm ? TypedResults.Ok(vm) : TypedResults.NotFound();
    }

    /// <summary>
    /// CPUs, memory, network cards and autostart; the disk stays as it is. Applies at the next start of the VM,
    /// like changing hardware on a real machine.
    /// </summary>
    private static async Task<Results<Ok<VmInfo>, NotFound, BadRequest<ErrorResponse>>> SaveConfig(
        string name, VmSettingsRequest request, IVmHost host, IsoStore isos, VmMonitor monitor, CancellationToken ct)
    {
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();

        var config = vm.Config with
        {
            Cpus = request.Cpus,
            MemoryMb = request.MemoryMb,
            Nets = request.Nets.Select(n => new VmNet(n.Bridge, n.Mac.ToUpperInvariant())).ToList(),
            Autostart = request.Autostart,
            Cdrom = string.IsNullOrEmpty(request.Cdrom) ? null : request.Cdrom,
        };
        if (Check(config, host, isos) is { } error) return TypedResults.BadRequest(new ErrorResponse(error));

        host.WriteConfig(config);
        await monitor.RefreshAsync(ct);
        return monitor.Find(name) is { } saved ? TypedResults.Ok(saved) : TypedResults.NotFound();
    }

    /// <summary>What both a new VM and changed settings must pass: the file rules, bridges that exist, an ISO that's there.</summary>
    private static string? Check(VmConfig config, IVmHost host, IsoStore isos)
    {
        if (VmConfigFile.Validate(config) is { } error) return error;
        var bridges = host.ReadBridges();
        if (config.Nets.FirstOrDefault(n => !bridges.Contains(n.Bridge)) is { } missing)
            return $"there is no bridge '{missing.Bridge}' on the host";
        if (config.Cdrom is { } iso && !isos.Exists(iso)) return $"there is no ISO image {iso}";
        return null;
    }

    /// <summary>
    /// A new VM is just a new .conf: its disk, /dev/&lt;group&gt;/&lt;name&gt;, is created by vm-run (as root) at the
    /// first start, with DISK_SIZE. A volume of that name left over from a deleted VM is used as it is.
    /// </summary>
    private static async Task<Results<Created<VmInfo>, Conflict<ErrorResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>> Create(
        VmCreateRequest request, IVmHost host, IsoStore isos, VmMonitor monitor, IOptions<HomeBackendOptions> options, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (!VmConfigFile.IsValidName(name))
            return TypedResults.BadRequest(new ErrorResponse("a name is 1–12 lowercase letters, digits or dashes, starting with a letter"));
        if (VmConfigFile.ReservedNames.Contains(name))
            return TypedResults.BadRequest(new ErrorResponse($"'{name}' is reserved"));
        if (host.ConfigExists(name)) return TypedResults.Conflict(new ErrorResponse($"there already is a VM {name}"));

        var config = new VmConfig(name, request.Cpus, request.MemoryMb, $"/dev/{options.Value.DiskGroup}/{name}",
            request.Nets.Select(n => new VmNet(n.Bridge, n.Mac.ToUpperInvariant())).ToList(), request.Autostart,
            request.DiskSizeGb, string.IsNullOrEmpty(request.Cdrom) ? null : request.Cdrom);
        if (config.DiskSizeGb is null) return TypedResults.BadRequest(new ErrorResponse("a new VM needs a disk size"));
        if (Check(config, host, isos) is { } error) return TypedResults.BadRequest(new ErrorResponse(error));

        host.WriteConfig(config);
        if (request.Start)
        {
            await monitor.RefreshAsync(ct);
            try { await host.RunAsync(name, VmAction.Start, ct); }
            catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
        }
        await monitor.RefreshAsync(ct);
        var created = monitor.Find(name) ?? new VmInfo(name, "stopped", null, null, null, config);
        return TypedResults.Created($"/api/vms/{name}", created);
    }

    /// <summary>
    /// Only a stopped VM. With <c>?disk=true</c> its disk goes too, first (if that fails, the VM is still there to retry);
    /// only a disk named after the VM, in the usual group, is ever deleted.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, Conflict<ErrorResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>> Delete(
        string name, bool? disk, IVmHost host, VmMonitor monitor, IOptions<HomeBackendOptions> options, CancellationToken ct)
    {
        await monitor.RefreshAsync(ct);
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();
        if (vm.State is not ("stopped" or "failed")) return TypedResults.Conflict(new ErrorResponse($"{name} is {vm.State}: shut it down first"));

        if (disk == true)
        {
            if (vm.Config.Disk != $"/dev/{options.Value.DiskGroup}/{name}")
                return TypedResults.BadRequest(new ErrorResponse($"{vm.Config.Disk} isn't named after the VM: delete it by hand"));
            try { await host.RemoveDiskAsync(name, ct); }
            catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
        }
        host.DeleteConfig(name);
        await monitor.RefreshAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<LogEntry>>, NotFound, ProblemHttpResult>> ReadLogs(
        string name, int? lines, IJournalSource journal, VmMonitor monitor, CancellationToken ct)
    {
        // only known VMs: the unit name goes straight into journalctl's argv
        if (monitor.Find(name) is null) return TypedResults.NotFound();
        try { return TypedResults.Ok(await journal.ReadAsync(SystemctlVmUnits.Unit(name), Math.Clamp(lines ?? DefaultLogLines, 10, 1000), ct)); }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }
}

/// <summary>Body of <c>PUT /api/vms/{name}/config</c>. The disk stays as it is.</summary>
/// <param name="Cdrom">ISO file name for the CD drive; null or empty to eject.</param>
public sealed record VmSettingsRequest(int Cpus, int MemoryMb, IReadOnlyList<VmNet> Nets, bool Autostart, string? Cdrom = null);

/// <summary>Body of <c>POST /api/vms</c>.</summary>
/// <param name="DiskSizeGb">Size of the new disk, which vm-run creates at the first start.</param>
/// <param name="Start">Start it right away (to install from the ISO in the console).</param>
public sealed record VmCreateRequest(string Name, int Cpus, int MemoryMb, int? DiskSizeGb, IReadOnlyList<VmNet> Nets,
    bool Autostart, string? Cdrom, bool Start);
