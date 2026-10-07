using HomeBackend.Features.SystemStatus;

namespace HomeBackend.Features.Host;

/// <summary>The machine itself: CPU, temperature, memory, disk, uptime. Page: Overview. Endpoint: <c>GET /api/host</c>.</summary>
public static class HostFeature
{
    public static RouteGroupBuilder MapHostEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/host", (ISystemSource system, CpuMonitor cpu, CpuTemperatureMonitor temperature) =>
            new HostResponse(system.ReadSystem(), cpu.CpuPercent, temperature.Current));
        return api;
    }
}

/// <param name="CpuPercent">Average over the last couple of seconds, all cores, 0–100.</param>
public sealed record HostResponse(SystemInfo System, double CpuPercent, CpuTemperature CpuTemperature);
