using HomeBackend.Features.SystemStatus;

namespace HomeBackend.Tests.Features;

public class ProcParsersTests
{
    [Fact]
    public void Cpu_times_count_iowait_as_idle()
    {
        // user nice system idle iowait irq softirq steal guest guest_nice
        var cpu = ProcParsers.ParseCpuTimes("cpu  100 5 50 800 20 3 2 1 0 0");

        Assert.Equal(981UL, cpu.Total); // the first eight columns
        Assert.Equal(820UL, cpu.Idle);  // idle + iowait
    }

    [Fact]
    public void Meminfo_values_are_bytes()
    {
        var mem = ProcParsers.ParseMemInfo(["MemTotal:        2040108 kB", "MemAvailable:    1656536 kB", "HugePages_Total:       0"]);

        Assert.Equal(2040108L * 1024, mem["MemTotal"]);
        Assert.Equal(1656536L * 1024, mem["MemAvailable"]);
        Assert.Equal(0, mem["HugePages_Total"]);
    }

    [Fact]
    public void Load_average_is_the_first_three_numbers() =>
        Assert.Equal([0.08, 0.02, 0.01], ProcParsers.ParseLoadAverage("0.08 0.02 0.01 1/123 4567\n"));

    [Fact]
    public void Pretty_name_comes_without_quotes()
    {
        string[] osRelease = ["NAME=\"Debian GNU/Linux\"", "PRETTY_NAME=\"Debian GNU/Linux 13 (trixie)\"", "ID=debian"];

        Assert.Equal("Debian GNU/Linux 13 (trixie)", ProcParsers.ParsePrettyName(osRelease));
        Assert.Null(ProcParsers.ParsePrettyName(["ID=debian"]));
    }
}
