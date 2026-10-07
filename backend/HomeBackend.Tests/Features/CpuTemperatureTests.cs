using HomeBackend.Features.SystemStatus;

namespace HomeBackend.Tests.Features;

public class CpuTemperatureTests
{
    [Fact]
    public void Picks_the_hottest_socket_and_prefers_tdie()
    {
        Assert.Equal(55, CpuSensors.PickCpu(
        [
            new("coretemp", "Package id 0", 50, 100),
            new("coretemp", "Package id 1", 55, 100),
            new("coretemp", "Core 0", 70, 100),
        ])!.Celsius);

        Assert.Equal("Tdie", CpuSensors.PickCpu(
        [
            new("k10temp", "Tctl", 72, null),
            new("k10temp", "Tdie", 52, null),
        ])!.Label);
    }

    [Fact]
    public void Without_a_cpu_chip_there_is_no_cpu_temperature() =>
        Assert.Null(CpuSensors.PickCpu([new("nvme", "Composite", 40, 85), new("acpitz", null, 28, null)]));

    [Fact]
    public void Reads_sysfs_hwmon_on_bare_metal()
    {
        var root = Directory.CreateTempSubdirectory("hwmon-").FullName;
        try
        {
            WriteChip(root, "hwmon0", "nvme", ("temp1", "38850", "Composite", null));
            WriteChip(root, "hwmon1", "coretemp", ("temp1", "47000", "Package id 0", "100000"), ("temp2", "45000", "Core 0", "100000"));

            var sensors = HwmonCpuTemperatureSource.ReadSensors(root);

            Assert.Equal(3, sensors.Count);
            Assert.Equal(new TemperatureSensor("coretemp", "Package id 0", 47, 100), CpuSensors.PickCpu(sensors));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_hwmon_folder_means_no_sensors() =>
        Assert.Empty(HwmonCpuTemperatureSource.ReadSensors("/nonexistent/hwmon"));

    private static void WriteChip(string root, string dir, string name, params (string Sensor, string Input, string? Label, string? Crit)[] sensors)
    {
        var chip = Directory.CreateDirectory(Path.Combine(root, dir)).FullName;
        File.WriteAllText(Path.Combine(chip, "name"), name + "\n");
        foreach (var s in sensors)
        {
            File.WriteAllText(Path.Combine(chip, $"{s.Sensor}_input"), s.Input + "\n");
            if (s.Label is not null) File.WriteAllText(Path.Combine(chip, $"{s.Sensor}_label"), s.Label + "\n");
            if (s.Crit is not null) File.WriteAllText(Path.Combine(chip, $"{s.Sensor}_crit"), s.Crit + "\n");
        }
    }
}
