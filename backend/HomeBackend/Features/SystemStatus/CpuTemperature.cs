namespace HomeBackend.Features.SystemStatus;

/// <summary>The CPU temperature tile on the Host page.</summary>
/// <param name="Celsius">null when there is no reading; <paramref name="Problem"/> says why.</param>
/// <param name="CriticalCelsius">The chip's own critical limit when it reports one (Intel does, AMD doesn't).</param>
/// <param name="Source">"local": this machine's sensors.</param>
/// <param name="Problem">Why there is no reading, short enough for the tile. Both null: no reading yet.</param>
public sealed record CpuTemperature(double? Celsius, double? CriticalCelsius, string Source, string? Problem);

/// <summary>One temperature input of a hardware monitoring chip (hwmon), e.g. coretemp's "Package id 0".</summary>
/// <param name="Chip">The hwmon driver: coretemp, k10temp, nvme, acpitz, …</param>
public sealed record TemperatureSensor(string Chip, string? Label, double Celsius, double? CriticalCelsius);
