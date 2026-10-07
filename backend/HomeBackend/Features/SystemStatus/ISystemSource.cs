namespace HomeBackend.Features.SystemStatus;

/// <summary>Reads machine-level facts: hostname, OS, uptime, load, memory, disk, temperature, CPU counters.</summary>
public interface ISystemSource
{
    SystemInfo ReadSystem();
    CpuTimes ReadCpuTimes();
}
