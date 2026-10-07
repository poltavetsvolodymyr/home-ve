using System.Globalization;
using HomeBackend.Infrastructure;

namespace HomeBackend.Features.SystemStatus;

/// <summary>
/// The CPU's own sensors under /sys/class/hwmon (coretemp, k10temp, …). The host runs on bare metal, so
/// they're right here.
/// </summary>
public sealed class HwmonCpuTemperatureSource : ICpuTemperatureSource
{
    private const string SysClassHwmon = "/sys/class/hwmon";

    public string Source => "local";

    public Task<CpuTemperature> ReadAsync(CancellationToken ct)
    {
        var cpu = CpuSensors.PickCpu(ReadSensors(SysClassHwmon));
        return Task.FromResult(cpu is null
            ? new CpuTemperature(null, null, Source, "no CPU sensor: load k10temp or coretemp")
            : new CpuTemperature(cpu.Celsius, cpu.CriticalCelsius, Source, null));
    }

    /// <summary>
    /// Every <c>temp*_input</c> of every chip in <paramref name="root"/>. Each chip is a folder with a
    /// <c>name</c> file; values are in millidegrees, <c>temp*_label</c> and <c>temp*_crit</c> are optional.
    /// </summary>
    public static List<TemperatureSensor> ReadSensors(string root)
    {
        var sensors = new List<TemperatureSensor>();
        if (!Directory.Exists(root)) return sensors;

        foreach (var chipDir in Directory.EnumerateDirectories(root).Order())
        {
            var chip = LinuxFiles.ReadText(Path.Combine(chipDir, "name"));
            if (string.IsNullOrEmpty(chip)) continue;

            foreach (var input in Directory.EnumerateFiles(chipDir, "temp*_input").Order())
            {
                var prefix = input[..^"_input".Length]; // …/temp1
                if (ReadMillidegrees(input) is not { } celsius) continue;
                sensors.Add(new TemperatureSensor(chip, LinuxFiles.ReadText(prefix + "_label"), celsius, ReadMillidegrees(prefix + "_crit")));
            }
        }
        return sensors;
    }

    private static double? ReadMillidegrees(string path) =>
        double.TryParse(LinuxFiles.ReadText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out var milli) ? milli / 1000 : null;
}
