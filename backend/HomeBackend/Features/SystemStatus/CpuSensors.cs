namespace HomeBackend.Features.SystemStatus;

/// <summary>Finds the CPU among all the temperature sensors of a machine (NVMe, chipset, ACPI, …).</summary>
public static class CpuSensors
{
    // hwmon drivers that measure the CPU: Intel, AMD (in-kernel and out-of-tree), ARM boards
    private static readonly string[] CpuChips = ["coretemp", "k10temp", "zenpower", "cpu_thermal"];

    /// <summary>
    /// Intel: the hottest "Package id N" (one per socket). AMD: "Tdie" when present, the real die temperature,
    /// else "Tctl" (on older Ryzens it carries an offset). Other CPUs: their hottest sensor.
    /// null when no sensor belongs to a CPU.
    /// </summary>
    public static TemperatureSensor? PickCpu(IEnumerable<TemperatureSensor> sensors)
    {
        var cpu = sensors.Where(s => CpuChips.Contains(s.Chip)).ToList();
        return cpu.Where(s => s.Label?.StartsWith("Package id", StringComparison.Ordinal) == true).MaxBy(s => s.Celsius)
            ?? cpu.Find(s => s.Label == "Tdie")
            ?? cpu.Find(s => s.Label == "Tctl")
            ?? cpu.MaxBy(s => s.Celsius);
    }
}
