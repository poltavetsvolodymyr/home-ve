using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using HomeBackend.Features.Auth;
using HomeBackend.Features.Update;
using HomeBackend.Infrastructure;
using HomeBackend.Security;

namespace HomeBackend.Installer;

/// <summary>What the installer page sends to install: the form, as typed.</summary>
/// <param name="Disk">Kernel name from <see cref="Machine.Disks"/>, e.g. sda; everything on it is erased.</param>
/// <param name="BackupGb">The backup volume (/var/backups/vm); 0: none.</param>
/// <param name="Network">dhcp or static; static takes <paramref name="Address"/> (192.168.1.10/24),
/// <paramref name="Gateway"/> and <paramref name="Dns"/> (addresses split by spaces or commas).</param>
/// <param name="Channel">stable or dev, as Update has them.</param>
/// <param name="WebPassword">The web UI's: with it set here, the host skips its first-run setup.</param>
public sealed record InstallRequest(
    string? Disk, int RootGb, int SwapGb, int BackupGb, string? Hostname, string? Timezone,
    string? Nic, string? Network, string? Address, string? Gateway, string? Dns,
    string? User, string? Channel, string? RootPassword, string? UserPassword, string? WebPassword);

/// <param name="State">idle, running, done or failed.</param>
/// <param name="Step">The step running now (or the last one), 1-based; 0 before the first.</param>
/// <param name="Current">What that step does, e.g. "partitioning /dev/sda".</param>
/// <param name="Log">The last lines the install wrote.</param>
public sealed record InstallStatus(string State, int Step, int Steps, string? Current, IReadOnlyList<string> Log);

/// <summary>
/// The form checked against the machine, as the two files home-ve-install reads: the settings, and the secrets
/// (the passwords, and the web password's hash instead of the password itself).
/// </summary>
public static partial class InstallPlan
{
    public const int MinRootGb = 8;
    public const int MinPoolGb = 8;
    // the BIOS boot, EFI and /boot partitions, and room for LVM's own data
    private const int PartitionsGb = 2;

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex HostnamePattern();

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial Regex UserPattern();

    [GeneratedRegex("^[A-Za-z0-9_+-]+(/[A-Za-z0-9_+-]+)*$")]
    private static partial Regex TimezonePattern();

    // names the system has, or the installed host makes, itself
    private static readonly HashSet<string> TakenUsers =
    [
        "root", "daemon", "bin", "sys", "sync", "games", "man", "lp", "mail", "news", "uucp", "proxy", "www-data",
        "backup", "list", "irc", "nobody", "messagebus", "sshd", "home-backend", "libvirt-qemu", "libvirt-dnsmasq",
    ];

    /// <summary>Null when the form is fine, else what is wrong with it, for the page to show.</summary>
    public static string? Validate(InstallRequest r, Machine machine, Func<string, bool> timezoneExists)
    {
        var disk = machine.Disks.FirstOrDefault(d => d.Name == r.Disk);
        if (disk is null) return "Choose the disk to install onto.";
        if (disk.InUse) return $"{disk.Name} is in use (the installer's own stick?): choose another disk.";

        if (r.RootGb < MinRootGb) return $"The system needs at least {MinRootGb} GB.";
        if (r.SwapGb is < 0 or > 128) return "Swap: 0 to 128 GB.";
        if (r.BackupGb < 0) return "The backup volume can't be smaller than 0 GB.";
        var diskGb = disk.SizeBytes >> 30;
        var left = diskGb - PartitionsGb - r.RootGb - r.SwapGb - r.BackupGb;
        if (left < MinPoolGb) return $"That leaves {Math.Max(left, 0)} GB for the VMs, of {diskGb} GB: at least {MinPoolGb} are needed.";

        if (r.Hostname is null || !HostnamePattern().IsMatch(r.Hostname))
            return "The host name: small letters, digits and dashes, not at the start or end.";
        if (r.Timezone is null || !TimezonePattern().IsMatch(r.Timezone) || !timezoneExists(r.Timezone))
            return "Choose the time zone.";

        var nic = machine.Nics.FirstOrDefault(n => n.Name == r.Nic);
        if (nic is null) return "Choose the network card.";
        if (nic.Wireless) return "A Wi-Fi card can't carry the VMs' bridge: choose a wired one.";
        if (r.Network == "static")
        {
            if (!IsAddressWithPrefix(r.Address)) return "The address with its prefix, e.g. 192.168.1.10/24.";
            if (!IsIPv4(r.Gateway)) return "The gateway, e.g. 192.168.1.1.";
            if (DnsServers(r.Dns).Any(d => !IPAddress.TryParse(d, out _))) return "DNS servers: addresses, e.g. 192.168.1.1 1.1.1.1.";
        }
        else if (r.Network != "dhcp") return "Choose DHCP or a fixed address.";

        if (r.User is null || !UserPattern().IsMatch(r.User) || TakenUsers.Contains(r.User) || r.User.StartsWith("systemd-"))
            return "The user name: small letters, digits, - and _, starting with a letter, and not one the system has (root…).";
        if (!UpdateChannel.IsValid(r.Channel)) return "Choose the update channel.";

        return PasswordError(r.RootPassword, "root's") ?? PasswordError(r.UserPassword, $"{r.User}'s")
            ?? PasswordError(r.WebPassword, "The web UI's");
    }

    private static string? PasswordError(string? password, string whose) =>
        password is null || password.Length < PasswordStore.MinLength ? $"{Capital(whose)} password: at least {PasswordStore.MinLength} characters."
        : password.Length > PasswordStore.MaxLength ? $"{Capital(whose)} password is too long."
        : password.Any(char.IsControl) ? $"{Capital(whose)} password can't have line breaks or tabs."
        : null;

    private static string Capital(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static bool IsIPv4(string? s) =>
        s is not null && IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString() == s;

    private static bool IsAddressWithPrefix(string? s) =>
        s?.Split('/') is [var ip, var prefix] && IsIPv4(ip) && int.TryParse(prefix, out var p) && p is >= 1 and <= 32 && prefix == p.ToString();

    private static IEnumerable<string> DnsServers(string? dns) =>
        (dns ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The settings file: KEY=value per line, for a request <see cref="Validate"/> passed.</summary>
    public static string Settings(InstallRequest r)
    {
        var lines = new List<string>
        {
            $"DISK=/dev/{r.Disk}", $"ROOT_GB={r.RootGb}", $"SWAP_GB={r.SwapGb}", $"BACKUP_GB={r.BackupGb}",
            $"HOSTNAME={r.Hostname}", $"TIMEZONE={r.Timezone}", $"NIC={r.Nic}", $"NET={r.Network}",
        };
        if (r.Network == "static")
            lines.AddRange([$"ADDRESS={r.Address}", $"GATEWAY={r.Gateway}", $"DNS={string.Join(' ', DnsServers(r.Dns))}"]);
        lines.AddRange([$"USER={r.User}", $"CHANNEL={r.Channel}"]);
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The secrets file; the web password goes as its hash, the way the host keeps it.</summary>
    public static string Secrets(InstallRequest r) =>
        $"ROOT_PASSWORD={r.RootPassword}\nUSER_PASSWORD={r.UserPassword}\nWEB_PASSWORD_HASH={PasswordHash.Create(r.WebPassword!)}\n";

    /// <summary>The status from what the install wrote: its steps are the lines "==> [n/N] what".</summary>
    public static InstallStatus Status(string state, IReadOnlyList<string> log, int tail = 40)
    {
        int step = 0, steps = 0;
        string? current = null;
        foreach (var line in log)
            if (StepLine().Match(line) is { Success: true } m)
                (step, steps, current) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value);
        return new InstallStatus(state, step, steps, current, log.Count > tail ? log.Skip(log.Count - tail).ToList() : log);
    }

    [GeneratedRegex(@"^==> \[(\d+)/(\d+)\] (.*)$")]
    private static partial Regex StepLine();
}

public enum InstallRunState { NotStarted, Running, Succeeded, Failed }

/// <summary>Runs home-ve-install and tells how it goes.</summary>
public interface IInstallRunner
{
    Task StartAsync(string settingsFile, string secretsFile, CancellationToken ct);
    Task<InstallRunState> StateAsync(CancellationToken ct);
    IReadOnlyList<string> Log();
    Task RebootAsync(CancellationToken ct);
}

/// <summary>
/// home-ve-install as the transient unit home-ve-install (systemd-run): it goes on if this service restarts, its
/// output goes to a file the page reads, and after it ends the unit stays, as "exited" or "failed".
/// </summary>
public sealed class SystemdInstallRunner(string logFile) : IInstallRunner
{
    public const string Unit = "home-ve-install";
    public const string Script = "/usr/local/sbin/home-ve-install";

    public async Task StartAsync(string settingsFile, string secretsFile, CancellationToken ct)
    {
        File.WriteAllText(logFile, "");
        await ProcessRunner.RunAsync("systemd-run",
        [
            $"--unit={Unit}", "--remain-after-exit", "--quiet",
            $"--property=StandardOutput=append:{logFile}", $"--property=StandardError=append:{logFile}",
            Script, settingsFile, secretsFile,
        ], ct);
    }

    public async Task<InstallRunState> StateAsync(CancellationToken ct)
    {
        var show = await ProcessRunner.RunAsync("systemctl", ["show", Unit, "--property=ActiveState,SubState"], ct);
        var p = show.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('=', 2)).Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0], kv => kv[1].Trim());
        return (p.GetValueOrDefault("ActiveState"), p.GetValueOrDefault("SubState")) switch
        {
            ("failed", _) => InstallRunState.Failed,
            ("active", "exited") => InstallRunState.Succeeded,
            ("active" or "activating", _) => InstallRunState.Running,
            _ => InstallRunState.NotStarted,
        };
    }

    public IReadOnlyList<string> Log() =>
        File.Exists(logFile) ? File.ReadAllText(logFile).Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];

    public Task RebootAsync(CancellationToken ct) => ProcessRunner.RunAsync("systemctl", ["reboot"], ct);
}

/// <summary>A made-up install for working on the page: the steps one after another, nothing touched.</summary>
public sealed class MockInstallRunner(TimeProvider time, TimeSpan stepTime) : IInstallRunner
{
    private static readonly string[] Steps =
    [
        "erasing /dev/nvme0n1", "partitioning /dev/nvme0n1", "LVM: root 32 GB, swap 8 GB, backups 200 GB, the rest for the VMs",
        "Debian 13 from deb.debian.org (the longest step)", "the system's settings: disks, name, time zone, network, users", "the kernel and the boot loader (UEFI)", "home-ve (stable channel)",
        "finishing",
    ];

    private DateTimeOffset? _started;

    public Task StartAsync(string settingsFile, string secretsFile, CancellationToken ct)
    {
        File.Delete(secretsFile);
        _started = time.GetUtcNow();
        return Task.CompletedTask;
    }

    private int Done => _started is { } s ? (int)((time.GetUtcNow() - s) / stepTime) : -1;

    public Task<InstallRunState> StateAsync(CancellationToken ct) => Task.FromResult(
        Done < 0 ? InstallRunState.NotStarted : Done < Steps.Length ? InstallRunState.Running : InstallRunState.Succeeded);

    public IReadOnlyList<string> Log()
    {
        var lines = new List<string>();
        for (var i = 0; i < Math.Min(Done + 1, Steps.Length); i++)
        {
            lines.Add($"==> [{i + 1}/{Steps.Length}] {Steps[i]}");
            lines.Add("  (mock: nothing done)");
        }
        if (Done >= Steps.Length) lines.Add("==> done: take the installer's stick out and restart");
        return lines;
    }

    public Task RebootAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>One install per boot: the form checked, its files written, the runner started.</summary>
public sealed class InstallService(IInstallRunner runner, IMachineSource machine, InstallerOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public enum Outcome { Started, Invalid, AlreadyStarted }

    public async Task<(Outcome, string?)> StartAsync(InstallRequest request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (await runner.StateAsync(ct) != InstallRunState.NotStarted) return (Outcome.AlreadyStarted, "The install has started already.");
            var error = InstallPlan.Validate(request, await machine.ReadAsync(ct), TimezoneExists);
            if (error is not null) return (Outcome.Invalid, error);

            Directory.CreateDirectory(options.RunDir);
            var settings = Path.Combine(options.RunDir, "settings");
            var secrets = Path.Combine(options.RunDir, "secrets");
            File.WriteAllText(settings, InstallPlan.Settings(request));
            WritePrivate(secrets, InstallPlan.Secrets(request));
            await runner.StartAsync(settings, secrets, ct);
            return (Outcome.Started, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InstallStatus> StatusAsync(CancellationToken ct)
    {
        var state = await runner.StateAsync(ct) switch
        {
            InstallRunState.Running => "running",
            InstallRunState.Succeeded => "done",
            InstallRunState.Failed => "failed",
            _ => "idle",
        };
        return InstallPlan.Status(state, runner.Log());
    }

    /// <summary>False while installing: a restart then would leave half a system.</summary>
    public async Task<bool> RebootAsync(CancellationToken ct)
    {
        if (await runner.StateAsync(ct) == InstallRunState.Running) return false;
        await runner.RebootAsync(ct);
        return true;
    }

    private bool TimezoneExists(string zone) =>
        !Directory.Exists(options.ZoneInfoDir) || File.Exists(Path.Combine(options.ZoneInfoDir, zone));

    private static void WritePrivate(string path, string text)
    {
        var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.Delete(path);
        using var file = new FileStream(path, fileOptions);
        file.Write(Encoding.UTF8.GetBytes(text));
    }
}
