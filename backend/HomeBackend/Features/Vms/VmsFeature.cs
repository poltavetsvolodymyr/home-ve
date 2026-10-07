using HomeBackend.Api;
using HomeBackend.Configuration;
using HomeBackend.Features.Logs;
using HomeBackend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Vms;

/// <summary>
/// The VMs: list, state and usage, the buttons, the settings, the log and the console. Pages: Overview, VM.
/// Endpoints under <c>/api/vms</c>, plus <c>GET /api/bridges</c>.
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
        string name, VmSettingsRequest request, IVmHost host, VmMonitor monitor, CancellationToken ct)
    {
        if (monitor.Find(name) is not { } vm) return TypedResults.NotFound();

        var config = vm.Config with
        {
            Cpus = request.Cpus,
            MemoryMb = request.MemoryMb,
            Nets = request.Nets.Select(n => new VmNet(n.Bridge, n.Mac.ToUpperInvariant())).ToList(),
            Autostart = request.Autostart,
        };
        if (VmConfigFile.Validate(config) is { } error) return TypedResults.BadRequest(new ErrorResponse(error));
        var bridges = host.ReadBridges();
        if (config.Nets.FirstOrDefault(n => !bridges.Contains(n.Bridge)) is { } missing)
            return TypedResults.BadRequest(new ErrorResponse($"there is no bridge '{missing.Bridge}' on the host"));

        host.WriteConfig(config);
        await monitor.RefreshAsync(ct);
        return monitor.Find(name) is { } saved ? TypedResults.Ok(saved) : TypedResults.NotFound();
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

/// <summary>Body of <c>PUT /api/vms/{name}/config</c>.</summary>
public sealed record VmSettingsRequest(int Cpus, int MemoryMb, IReadOnlyList<VmNet> Nets, bool Autostart);
