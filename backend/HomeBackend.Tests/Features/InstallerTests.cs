using System.Net;
using System.Net.Http.Json;
using HomeBackend.Installer;
using Microsoft.AspNetCore.Builder;

namespace HomeBackend.Tests.Features;

public class InstallerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Disks_from_lsblk_skip_cd_drives_and_mark_the_installer_stick()
    {
        var disks = LinuxMachineSource.ParseDisks("""
            {"blockdevices": [
              {"name": "loop0", "type": "loop", "size": 512000000, "ro": true, "rm": false, "rota": false, "mountpoints": ["/run/live/rootfs/filesystem.squashfs"]},
              {"name": "sda", "type": "disk", "size": 64424509440, "model": "QEMU HARDDISK   ", "serial": "QM00001", "tran": "sata", "ro": false, "rm": false, "rota": true, "mountpoints": [null]},
              {"name": "sdb", "type": "disk", "size": "15376000000", "model": "Ultra", "tran": "usb", "ro": "0", "rm": "1", "rota": "0", "mountpoints": [null],
               "children": [{"name": "sdb1", "type": "part", "size": 15376000000, "mountpoints": ["/run/live/medium"]}]},
              {"name": "sr0", "type": "rom", "size": 409600000, "ro": false, "rm": true, "rota": false, "mountpoints": [null]},
              {"name": "nvme0n1", "type": "disk", "size": 0, "ro": false, "rm": false, "rota": false, "mountpoints": [null]}
            ]}
            """);

        Assert.Equal(
            [
                new Disk("sda", 64_424_509_440, "QEMU HARDDISK", "QM00001", "sata", false, true, false),
                new Disk("sdb", 15_376_000_000, "Ultra", null, "usb", true, false, true),
            ],
            disks);
    }

    [Fact]
    public void Cpu_and_memory_from_proc()
    {
        Assert.Equal("AMD Ryzen 7 5700U", LinuxMachineSource.CpuModel("processor\t: 0\nmodel name\t: AMD Ryzen 7 5700U\nflags\t: x"));
        Assert.Equal(32_768_000L * 1024, LinuxMachineSource.MemoryBytes("MemTotal:       32768000 kB\nMemFree: 1 kB"));
        Assert.Null(LinuxMachineSource.CpuModel(null));
    }

    [Fact]
    public async Task Nothing_opens_without_the_code_from_the_screen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "home-ve-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "code"), "ABCD-EFGH\n", Ct);
        await using var app = InstallerHost.Build(
            ["--Installer:Mock=true", "--Installer:Urls:0=http://127.0.0.1:0", $"--Installer:CodeFile={dir}/code", $"--Installer:WwwDir={dir}"]);
        await app.StartAsync(Ct);
        using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(app.Urls.Single()) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/installer/machine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/installer/session", new { code = "AAAA-AAAA" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/installer/session", new { code = "abcdefgh" }, Ct)).StatusCode);

        var machine = await client.GetFromJsonAsync<Machine>("/api/installer/machine", Ct);
        Assert.Equal(3, machine!.Disks.Count);
        Assert.Contains(machine.Disks, d => d.InUse);

        await app.StopAsync(Ct);
        Directory.Delete(dir, recursive: true);
    }
}
