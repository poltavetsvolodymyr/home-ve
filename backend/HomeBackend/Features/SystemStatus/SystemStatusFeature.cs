using HomeBackend.Configuration;
using HomeBackend.Infrastructure;

namespace HomeBackend.Features.SystemStatus;

/// <summary>
/// Machine status: hostname, OS, uptime, load, memory, disk, CPU usage and CPU temperature.
/// Has no endpoints of its own; the Host feature shows it.
/// </summary>
public static class SystemStatusFeature
{
    public static IServiceCollection AddSystemStatusFeature(this IServiceCollection services, HomeBackendOptions options)
    {
        services.AddDataSource<ISystemSource, LinuxSystemSource, MockSystemSource>(options.Mock);
        services.AddSingleton<CpuMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<CpuMonitor>());

        services.AddDataSource<ICpuTemperatureSource, HwmonCpuTemperatureSource, MockCpuTemperatureSource>(options.Mock);
        services.AddSingleton<CpuTemperatureMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<CpuTemperatureMonitor>());
        return services;
    }
}
