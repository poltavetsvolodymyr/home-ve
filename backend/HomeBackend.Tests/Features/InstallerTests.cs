using System.Net;
using System.Net.Http.Json;
using HomeBackend.Installer;
using Microsoft.AspNetCore.Builder;

namespace HomeBackend.Tests.Features;

public class InstallerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Disks_from_lsblk_skip_cd_drives_and_floppies_and_mark_the_installer_stick()
    {
        var disks = LinuxMachineSource.ParseDisks("""
            {"blockdevices": [
              {"name": "loop0", "type": "loop", "size": 512000000, "ro": true, "rm": false, "rota": false, "mountpoints": ["/run/live/rootfs/filesystem.squashfs"]},
              {"name": "sda", "type": "disk", "size": 64424509440, "model": "QEMU HARDDISK   ", "serial": "QM00001", "tran": "sata", "ro": false, "rm": false, "rota": true, "mountpoints": [null]},
              {"name": "sdb", "type": "disk", "size": "15376000000", "model": "Ultra", "tran": "usb", "ro": "0", "rm": "1", "rota": "0", "mountpoints": [null],
               "children": [{"name": "sdb1", "type": "part", "size": 15376000000, "mountpoints": ["/run/live/medium"]}]},
              {"name": "sr0", "type": "rom", "size": 409600000, "ro": false, "rm": true, "rota": false, "mountpoints": [null]},
              {"name": "nvme0n1", "type": "disk", "size": 0, "ro": false, "rm": false, "rota": false, "mountpoints": [null]},
              {"name": "fd0", "type": "disk", "size": 4096, "ro": false, "rm": true, "rota": true, "mountpoints": [null]}
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
        // this test's client is on loopback too: here it counts as a phone
        await using var app = InstallerHost.Build(
            ["--Installer:Mock=true", "--Installer:Urls:0=http://127.0.0.1:0", $"--Installer:CodeFile={dir}/code", $"--Installer:WwwDir={dir}",
             "--Installer:TrustLoopback=false"]);
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

    [Fact]
    public async Task The_machines_own_screen_needs_no_code_and_learns_how_a_phone_gets_in()
    {
        var dir = Path.Combine(Path.GetTempPath(), "home-ve-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "code"), "abcdefgh\n", Ct);
        await using var app = InstallerHost.Build(
            ["--Installer:Mock=true", "--Installer:Urls:0=http://127.0.0.1:0", $"--Installer:CodeFile={dir}/code", $"--Installer:WwwDir={dir}"]);
        await app.StartAsync(Ct);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/installer/machine", Ct)).StatusCode);
        var local = await client.GetFromJsonAsync<LocalAccess>("/api/installer/local", Ct);
        Assert.Equal("ABCD-EFGH", local!.Code);

        await app.StopAsync(Ct);
        Directory.Delete(dir, recursive: true);
    }
}

public class InstallPlanTests
{
    private static readonly Machine Machine = new MockMachineSource().ReadAsync(CancellationToken.None).Result;

    private static InstallRequest Valid() => new(
        Disk: "nvme0n1", RootGb: 32, SwapGb: 8, BackupGb: 200, Hostname: "home", Timezone: "Europe/Berlin",
        Nic: "enp1s0", Network: "dhcp", Address: null, Gateway: null, Dns: null,
        User: "volodymyr", Channel: "stable", RootPassword: "root-pass=", UserPassword: "user pass", WebPassword: "web-password");

    private static string? Check(InstallRequest r) => InstallPlan.Validate(r, Machine, zone => zone == "Europe/Berlin");

    [Fact]
    public void A_filled_in_form_passes() => Assert.Null(Check(Valid()));

    [Theory]
    [InlineData("sdb")] // the installer's stick
    [InlineData("sdz")]
    [InlineData(null)]
    public void Only_a_free_disk_of_this_machine(string? disk) => Assert.NotNull(Check(Valid() with { Disk = disk }));

    [Fact]
    public void The_sizes_must_leave_room_for_the_VMs()
    {
        Assert.NotNull(Check(Valid() with { RootGb = 4 }));
        Assert.NotNull(Check(Valid() with { BackupGb = 900 }));
        Assert.NotNull(Check(Valid() with { SwapGb = -1 }));
        Assert.Null(Check(Valid() with { BackupGb = 0 }));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("-home")]
    [InlineData("home.lan")]
    [InlineData("")]
    public void Host_names_are_plain(string name) => Assert.NotNull(Check(Valid() with { Hostname = name }));

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Mars/Olympus")]
    public void Only_time_zones_the_system_has(string zone) => Assert.NotNull(Check(Valid() with { Timezone = zone }));

    [Fact]
    public void The_bridge_needs_a_wired_card_of_this_machine()
    {
        Assert.NotNull(Check(Valid() with { Nic = "wlp3s0" }));
        Assert.NotNull(Check(Valid() with { Nic = "eth9" }));
        Assert.Null(Check(Valid() with { Nic = "enp2s0" }));
    }

    [Fact]
    public void A_fixed_address_needs_its_prefix_and_a_gateway()
    {
        var fixedAddress = Valid() with { Network = "static", Address = "192.168.178.2/24", Gateway = "192.168.178.1", Dns = "192.168.178.1, 1.1.1.1" };
        Assert.Null(Check(fixedAddress));
        Assert.NotNull(Check(fixedAddress with { Address = "192.168.178.2" }));
        Assert.NotNull(Check(fixedAddress with { Address = "192.168.178.2/33" }));
        Assert.NotNull(Check(fixedAddress with { Gateway = "router" }));
        Assert.NotNull(Check(fixedAddress with { Dns = "1.1.1.1\nX=1" }));
        Assert.NotNull(Check(Valid() with { Network = "pppoe" }));
        Assert.Contains("DNS=192.168.178.1 1.1.1.1\n", InstallPlan.Settings(fixedAddress));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("home-backend")]
    [InlineData("systemd-network")]
    [InlineData("Admin")]
    [InlineData("1user")]
    public void The_user_is_a_new_one(string user) => Assert.NotNull(Check(Valid() with { User = user }));

    [Fact]
    public void Passwords_are_long_enough_and_one_line()
    {
        Assert.NotNull(Check(Valid() with { RootPassword = "short" }));
        Assert.NotNull(Check(Valid() with { UserPassword = null }));
        Assert.NotNull(Check(Valid() with { WebPassword = "web-password\nCHANNEL=dev" }));
        Assert.NotNull(Check(Valid() with { Channel = "main" }));
    }

    [Fact]
    public void The_files_for_the_script()
    {
        var r = Valid();
        Assert.Equal(
            "DISK=/dev/nvme0n1\nROOT_GB=32\nSWAP_GB=8\nBACKUP_GB=200\nHOSTNAME=home\nTIMEZONE=Europe/Berlin\nNIC=enp1s0\nNET=dhcp\n" +
            "USER=volodymyr\nCHANNEL=stable\n",
            InstallPlan.Settings(r));
        var secrets = InstallPlan.Secrets(r).Split('\n');
        Assert.Equal("ROOT_PASSWORD=root-pass=", secrets[0]);
        Assert.Equal("USER_PASSWORD=user pass", secrets[1]);
        Assert.True(HomeBackend.Security.PasswordHash.Verify("web-password", secrets[2]["WEB_PASSWORD_HASH=".Length..]));
    }

    [Fact]
    public void The_progress_is_the_last_step_line()
    {
        var status = InstallPlan.Status("running", ["==> [1/8] erasing /dev/sda", "noise", "==> [2/8] partitioning /dev/sda", "more"], tail: 2);
        Assert.Equal((2, 8, "partitioning /dev/sda"), (status.Step, status.Steps, status.Current));
        Assert.Equal(["==> [2/8] partitioning /dev/sda", "more"], status.Log);
        Assert.Equal((0, null), (InstallPlan.Status("idle", []).Step, InstallPlan.Status("idle", []).Current));
    }
}

public class InstallApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_install_from_the_form_then_the_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "home-ve-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await using var app = InstallerHost.Build(
            ["--Installer:Mock=true", "--Installer:Urls:0=http://127.0.0.1:0", $"--Installer:CodeFile={dir}/code", $"--Installer:WwwDir={dir}",
             $"--Installer:RunDir={dir}/run", $"--Installer:ZoneInfoDir={dir}/none", "--Installer:MockStepSeconds=60"]);
        await app.StartAsync(Ct);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        Assert.Equal("idle", (await client.GetFromJsonAsync<InstallStatus>("/api/installer/install", Ct))!.State);
        var form = new
        {
            disk = "nvme0n1", rootGb = 32, swapGb = 8, backupGb = 200, hostname = "home", timezone = "Europe/Berlin", nic = "enp1s0",
            network = "dhcp", user = "volodymyr", channel = "dev", rootPassword = "root-password", userPassword = "user-password",
            webPassword = "web-password",
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/installer/install", form with { disk = "sdb" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/installer/install", form, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/installer/install", form, Ct)).StatusCode);

        var status = await client.GetFromJsonAsync<InstallStatus>("/api/installer/install", Ct);
        Assert.Equal(("running", 1, 8), (status!.State, status.Step, status.Steps));
        Assert.Contains("CHANNEL=dev", await File.ReadAllTextAsync($"{dir}/run/settings", Ct));
        Assert.False(File.Exists($"{dir}/run/secrets")); // the runner takes it, as the script deletes it
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/installer/reboot", null, Ct)).StatusCode);

        await app.StopAsync(Ct);
        Directory.Delete(dir, recursive: true);
    }
}
