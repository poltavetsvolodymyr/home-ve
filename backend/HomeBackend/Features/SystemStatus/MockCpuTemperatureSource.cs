namespace HomeBackend.Features.SystemStatus;

/// <summary>A CPU drifting between 42 and 58 °C, for development without the host.</summary>
public sealed class MockCpuTemperatureSource : ICpuTemperatureSource
{
    private readonly Lock _lock = new();
    private double _celsius = 47;

    public string Source => "local";

    public Task<CpuTemperature> ReadAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _celsius = Math.Clamp(_celsius + (Random.Shared.NextDouble() - 0.5) * 2, 42, 58);
            return Task.FromResult(new CpuTemperature(Math.Round(_celsius, 1), 100, Source, null));
        }
    }
}
