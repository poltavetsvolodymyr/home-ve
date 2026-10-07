using HomeBackend.Features.Vms;

namespace HomeBackend.Tests.Features;

public class VmConfigFileTests
{
    private static readonly string[] RouterConf =
    [
        "# VM \"router\"",
        "CPUS=2",
        "MEMORY=2048",
        "DISK=/dev/home/router",
        "NET=br-lan bc:24:11:14:4d:cd",
        "NET=br-wan BC:24:11:C7:E4:7B",
        "AUTOSTART=yes",
        "",
    ];

    [Fact]
    public void Reads_every_key_and_keeps_the_card_order()
    {
        var c = VmConfigFile.Parse("router", RouterConf);

        Assert.Equal(2, c.Cpus);
        Assert.Equal(2048, c.MemoryMb);
        Assert.Equal("/dev/home/router", c.Disk);
        Assert.Equal([new VmNet("br-lan", "BC:24:11:14:4D:CD"), new VmNet("br-wan", "BC:24:11:C7:E4:7B")], c.Nets);
        Assert.True(c.Autostart);
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        var c = VmConfigFile.Parse("router", RouterConf);
        var again = VmConfigFile.Parse("router", VmConfigFile.Format(c).Split('\n'));

        Assert.Equal(c with { Nets = [] }, again with { Nets = [] });
        Assert.Equal(c.Nets, again.Nets);
    }

    [Theory]
    [InlineData("CPUS=two")]
    [InlineData("CPUS=-1")]
    [InlineData("MEMORY=64")]
    [InlineData("DISK=/dev/nvme0n1")]
    [InlineData("DISK=/dev/home/../root")]
    [InlineData("DISK=$(reboot)")]
    [InlineData("NET=br-lan")]
    [InlineData("NET=br;lan BC:24:11:14:4D:CD")]
    [InlineData("NET=br-lan 01:00:5E:00:00:01")]
    [InlineData("just words")]
    public void Refuses_values_vm_run_would_refuse(string bad)
    {
        string[] lines = ["CPUS=2", "MEMORY=2048", "DISK=/dev/home/router", bad];
        Assert.Throws<FormatException>(() => VmConfigFile.Parse("router", lines));
    }

    [Fact]
    public void A_missing_key_is_an_error() =>
        Assert.Throws<FormatException>(() => VmConfigFile.Parse("router", ["CPUS=2", "MEMORY=2048"]));

    [Fact]
    public void Two_cards_with_one_mac_are_refused() =>
        Assert.NotNull(VmConfigFile.Validate(new VmConfig("x", 1, 512, "/dev/home/x",
            [new("br-lan", "BC:24:11:00:00:01"), new("br-wan", "BC:24:11:00:00:01")], false)));

    [Theory]
    [InlineData("router", true)]
    [InlineData("web-1", true)]
    [InlineData("averylongname", false)]
    [InlineData("Router", false)]
    [InlineData("1st", false)]
    [InlineData("../etc", false)]
    [InlineData("", false)]
    public void Names_fit_units_taps_and_paths(string name, bool valid) =>
        Assert.Equal(valid, VmConfigFile.IsValidName(name));

    [Fact]
    public void Disk_size_and_cd_round_trip()
    {
        var c = VmConfigFile.Parse("web", ["CPUS=1", "MEMORY=1024", "DISK=/dev/home/web", "DISK_SIZE=20", "CDROM=debian-13.1.0-amd64-netinst.iso"]);

        Assert.Equal(20, c.DiskSizeGb);
        Assert.Equal("debian-13.1.0-amd64-netinst.iso", c.Cdrom);
        Assert.Equal(c with { Nets = [] }, VmConfigFile.Parse("web", VmConfigFile.Format(c).Split('\n')) with { Nets = [] });
    }

    [Fact]
    public void Without_disk_size_or_cd_the_keys_stay_out_of_the_file()
    {
        var text = VmConfigFile.Format(VmConfigFile.Parse("router", RouterConf));
        Assert.DoesNotContain("DISK_SIZE", text);
        Assert.DoesNotContain("CDROM", text);
    }

    [Theory]
    [InlineData("../etc/shadow.iso")]
    [InlineData(".hidden.iso")]
    [InlineData("debian.img")]
    [InlineData("a b.iso")]
    public void Cd_must_be_a_plain_iso_file_name(string iso)
    {
        var c = VmConfigFile.Parse("router", RouterConf) with { Cdrom = iso };
        Assert.NotNull(VmConfigFile.Validate(c));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Disk_size_has_limits(int size)
    {
        var c = VmConfigFile.Parse("router", RouterConf) with { DiskSizeGb = size };
        Assert.NotNull(VmConfigFile.Validate(c));
    }
}
