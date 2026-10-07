namespace HomeBackend.Features.SystemStatus;

/// <summary>Reads the CPU temperature.</summary>
public interface ICpuTemperatureSource
{
    /// <summary>"local" or "host": where the readings come from.</summary>
    string Source { get; }

    /// <summary>A reading, or one with a <see cref="CpuTemperature.Problem"/>; throws only on bugs.</summary>
    Task<CpuTemperature> ReadAsync(CancellationToken ct);
}
