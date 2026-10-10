using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using HomeBackend.Infrastructure;

namespace HomeBackend.Installer;

/// <summary>A disk the system can go onto.</summary>
/// <param name="Name">Kernel name, e.g. sda, nvme0n1: the device is /dev/&lt;name&gt;.</param>
/// <param name="Transport">sata, nvme, usb, virtio… as lsblk tells it; null when it doesn't know.</param>
/// <param name="InUse">Something on it is mounted (the stick the installer runs from): not offered.</param>
public sealed record Disk(string Name, long SizeBytes, string? Model, string? Serial, string? Transport, bool Removable,
    bool Rotational, bool InUse);

/// <summary>A network card.</summary>
/// <param name="Link">A cable is in and the other end is up.</param>
/// <param name="Wireless">Wi-Fi: it can't go into a bridge.</param>
/// <param name="Addresses">IPv4 addresses it has now (from DHCP), e.g. 192.168.178.57/24.</param>
public sealed record Nic(string Name, string Mac, bool Link, bool Wireless, string? Driver, IReadOnlyList<string> Addresses);

public sealed record Machine(bool Uefi, string? Cpu, int Cpus, long MemoryBytes, IReadOnlyList<Disk> Disks, IReadOnlyList<Nic> Nics);

/// <summary>What the installer page shows of the machine: its firmware, CPU, memory, disks and network cards.</summary>
public interface IMachineSource
{
    Task<Machine> ReadAsync(CancellationToken ct);
}

public sealed class LinuxMachineSource : IMachineSource
{
    public async Task<Machine> ReadAsync(CancellationToken ct)
    {
        var lsblk = await ProcessRunner.RunAsync("lsblk",
            ["--json", "--bytes", "-o", "NAME,TYPE,SIZE,MODEL,SERIAL,TRAN,RM,RO,ROTA,MOUNTPOINTS"], ct);
        return new Machine(
            Uefi: Directory.Exists("/sys/firmware/efi"),
            Cpu: CpuModel(LinuxFiles.ReadText("/proc/cpuinfo")),
            Cpus: Environment.ProcessorCount,
            MemoryBytes: MemoryBytes(LinuxFiles.ReadText("/proc/meminfo")),
            Disks: ParseDisks(lsblk),
            Nics: ReadNics());
    }

    /// <summary>
    /// The whole disks in <c>lsblk --json --bytes</c> output: no loop devices, CD drives, read-only or empty ones.
    /// A disk with anything mounted below it (the installer's own stick) is marked in use.
    /// </summary>
    public static IReadOnlyList<Disk> ParseDisks(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<Disk>();
        foreach (var d in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
        {
            if (Text(d, "type") != "disk" || Flag(d, "ro")) continue;
            var size = Number(d, "size");
            if (size <= 0) continue;
            result.Add(new Disk(Text(d, "name") ?? "?", size, Text(d, "model")?.Trim(), Text(d, "serial")?.Trim(),
                Text(d, "tran"), Flag(d, "rm"), Flag(d, "rota"), HasMount(d)));
        }
        return result;
    }

    private static bool HasMount(JsonElement e)
    {
        if (e.TryGetProperty("mountpoints", out var m) && m.ValueKind == JsonValueKind.Array
            && m.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 }))
            return true;
        return e.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array && c.EnumerateArray().Any(HasMount);
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // older lsblk writes numbers and flags as strings ("1", "0"), newer as JSON numbers and booleans
    private static long Number(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number ? v.GetInt64()
        : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var n) ? n : 0;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.String && v.GetString() == "1")
            || (v.ValueKind == JsonValueKind.Number && v.GetInt32() == 1));

    public static string? CpuModel(string? cpuinfo) =>
        cpuinfo?.Split('\n').FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim();

    public static long MemoryBytes(string? meminfo)
    {
        var line = meminfo?.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal:"));
        var kb = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return long.TryParse(kb, out var v) ? v * 1024 : 0;
    }

    // the physical cards: those with a device behind them (not lo, bridges, tunnels)
    private static List<Nic> ReadNics()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces().ToDictionary(
            n => n.Name,
            n => (IReadOnlyList<string>)n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => $"{a.Address}/{a.PrefixLength}").ToList());
        var result = new List<Nic>();
        if (!Directory.Exists("/sys/class/net")) return result;
        foreach (var dir in Directory.EnumerateDirectories("/sys/class/net").Order())
        {
            if (!Directory.Exists(Path.Combine(dir, "device"))) continue;
            var name = Path.GetFileName(dir);
            var driver = new FileInfo(Path.Combine(dir, "device", "driver")).LinkTarget is { } t ? Path.GetFileName(t) : null;
            result.Add(new Nic(name, LinuxFiles.ReadText(Path.Combine(dir, "address")) ?? "", LinuxFiles.ReadText(Path.Combine(dir, "carrier")) == "1",
                Directory.Exists(Path.Combine(dir, "wireless")), driver, addresses.GetValueOrDefault(name) ?? []));
        }
        return result;
    }
}

/// <summary>A made-up machine for working on the installer page without one.</summary>
public sealed class MockMachineSource : IMachineSource
{
    public Task<Machine> ReadAsync(CancellationToken ct) => Task.FromResult(new Machine(
        Uefi: true, Cpu: "AMD Ryzen 7 5700U with Radeon Graphics", Cpus: 16, MemoryBytes: 32L << 30,
        Disks:
        [
            new("nvme0n1", 1_000_204_886_016, "Samsung SSD 980 PRO 1TB", "S5GXNF0R123456", "nvme", false, false, false),
            new("sda", 4_000_787_030_016, "WDC WD40EFZX-68AWUN0", "WD-WX12D1234567", "sata", false, true, false),
            new("sdb", 15_376_000_000, "SanDisk Ultra", "4C530001", "usb", true, false, true),
        ],
        Nics:
        [
            new("enp1s0", "bc:24:11:3a:91:0e", true, false, "r8169", ["192.168.178.57/24"]),
            new("enp2s0", "bc:24:11:3a:91:0f", false, false, "igc", []),
            new("wlp3s0", "f4:4e:e3:12:34:56", false, true, "iwlwifi", []),
        ]));
}
