using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HomeBackend.Features.Vms;

/// <summary>
/// The <c>.conf</c> format, shared with deploy/vm/vm-run (which runs as root and checks every value the same way):
/// <code>
/// CPUS=2
/// MEMORY=2048
/// DISK=/dev/home/router
/// NET=br-lan BC:24:11:14:4D:CD
/// NET=br-wan BC:24:11:C7:E4:7B
/// AUTOSTART=yes
/// DISK_SIZE=20          (GiB; vm-run creates the disk with it when the volume doesn't exist yet)
/// CDROM=debian-13.iso   (a file in the ISO directory, in the VM's CD drive)
/// BACKUP=no             (left out of the nightly backups; without the line it's backed up)
/// </code>
/// Plain KEY=VALUE lines, no shell: nothing in the file is ever executed.
/// </summary>
public static partial class VmConfigFile
{
    public const int MaxCpus = 64, MinMemoryMb = 128, MaxMemoryMb = 256 * 1024, MaxNets = 8, MaxDiskSizeGb = 4096;

    /// <summary>
    /// Taken by the host's own volumes in the group (vm-run would refuse them anyway, they aren't thin),
    /// and "new" by the New VM page's address (/vms/new).
    /// </summary>
    public static readonly string[] ReservedNames = ["root", "swap", "data", "new"];

    /// <summary>An ISO file name: no paths, no hidden files.</summary>
    public static bool IsValidIsoName(string? name) => name is not null && IsoPattern().IsMatch(name);

    /// <summary>A VM name: also part of the unit name, the tap names (name-n0) and paths.</summary>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <summary>Reads a file's lines; unknown keys and comments are skipped. Throws <see cref="FormatException"/> on bad values.</summary>
    public static VmConfig Parse(string name, IEnumerable<string> lines)
    {
        int? cpus = null, memory = null;
        string? disk = null, cdrom = null;
        int? diskSize = null;
        var autostart = false;
        var backup = true;
        var nets = new List<VmNet>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) throw new FormatException($"not KEY=VALUE: {line}");
            var (key, value) = (line[..eq], line[(eq + 1)..]);

            switch (key)
            {
                case "CPUS": cpus = ParseInt(key, value); break;
                case "MEMORY": memory = ParseInt(key, value); break;
                case "DISK": disk = value; break;
                case "AUTOSTART": autostart = value == "yes"; break;
                case "BACKUP": backup = value != "no"; break;
                case "DISK_SIZE": diskSize = ParseInt(key, value); break;
                case "CDROM": cdrom = value.Length == 0 ? null : value; break;
                case "NET":
                    var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) throw new FormatException($"NET needs a bridge and a MAC: {value}");
                    nets.Add(new VmNet(parts[0], parts[1].ToUpperInvariant()));
                    break;
            }
        }

        var config = new VmConfig(name,
            cpus ?? throw new FormatException("CPUS is missing"),
            memory ?? throw new FormatException("MEMORY is missing"),
            disk ?? throw new FormatException("DISK is missing"),
            nets, autostart, diskSize, cdrom, backup);
        if (Validate(config) is { } error) throw new FormatException(error);
        return config;
    }

    /// <summary>The file content for a config. Always valid input for <see cref="Parse"/>.</summary>
    public static string Format(VmConfig c)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# VM \"{c.Name}\", run by vm@{c.Name}.service. After editing: systemctl restart vm@{c.Name}\n");
        sb.Append(CultureInfo.InvariantCulture, $"CPUS={c.Cpus}\n");
        sb.Append(CultureInfo.InvariantCulture, $"MEMORY={c.MemoryMb}\n");
        sb.Append(CultureInfo.InvariantCulture, $"DISK={c.Disk}\n");
        foreach (var n in c.Nets) sb.Append(CultureInfo.InvariantCulture, $"NET={n.Bridge} {n.Mac}\n");
        sb.Append(CultureInfo.InvariantCulture, $"AUTOSTART={(c.Autostart ? "yes" : "no")}\n");
        if (c.DiskSizeGb is { } size) sb.Append(CultureInfo.InvariantCulture, $"DISK_SIZE={size}\n");
        if (c.Cdrom is { } iso) sb.Append(CultureInfo.InvariantCulture, $"CDROM={iso}\n");
        if (!c.Backup) sb.Append("BACKUP=no\n");
        return sb.ToString();
    }

    /// <summary>Why a config can't be used, or null when it can. The same rules as vm-run's.</summary>
    public static string? Validate(VmConfig c)
    {
        if (!IsValidName(c.Name)) return "a name is 1–12 lowercase letters, digits or dashes, starting with a letter";
        if (c.Cpus is < 1 or > MaxCpus) return $"CPUs must be 1–{MaxCpus}";
        if (c.MemoryMb is < MinMemoryMb or > MaxMemoryMb) return $"memory must be {MinMemoryMb}–{MaxMemoryMb} MiB";
        if (!DiskPattern().IsMatch(c.Disk)) return "the disk must be an LVM volume, /dev/<group>/<volume>";
        if (c.DiskSizeGb is < 1 or > MaxDiskSizeGb) return $"the disk must be 1–{MaxDiskSizeGb} GiB";
        if (c.Cdrom is not null && !IsValidIsoName(c.Cdrom)) return $"'{c.Cdrom}' is not an ISO file name";
        if (c.Nets.Count > MaxNets) return $"at most {MaxNets} network cards";
        foreach (var n in c.Nets)
        {
            if (!BridgePattern().IsMatch(n.Bridge)) return $"'{n.Bridge}' is not a bridge name";
            if (!MacPattern().IsMatch(n.Mac)) return $"'{n.Mac}' is not a MAC address";
            // bit 0 of the first byte set = multicast, which a NIC can't have
            if ((Convert.ToInt32(n.Mac[..2], 16) & 1) == 1) return $"{n.Mac} is a multicast address";
        }
        if (c.Nets.Select(n => n.Mac).Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Nets.Count) return "two cards have the same MAC";
        return null;
    }

    private static int ParseInt(string key, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException($"{key} is not a number: {value}");

    [GeneratedRegex("^[a-z][a-z0-9-]{0,11}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^/dev/[a-z0-9][a-z0-9_.-]*/[a-z0-9][a-z0-9_.-]*$")]
    private static partial Regex DiskPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,14}$")]
    private static partial Regex BridgePattern();

    [GeneratedRegex("^[0-9A-F]{2}(:[0-9A-F]{2}){5}$")]
    private static partial Regex MacPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,123}\.iso$")]
    private static partial Regex IsoPattern();
}
