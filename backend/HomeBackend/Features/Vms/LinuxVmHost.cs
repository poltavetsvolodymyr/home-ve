using HomeBackend.Configuration;
using HomeBackend.Infrastructure;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Vms;

/// <summary>
/// /etc/vm for the configs, systemctl for the units, /proc for usage. Runs as the home-backend user:
/// deploy/install.sh lets it write /etc/vm and (by a polkit rule) start and stop vm@*.service, nothing else.
/// </summary>
public sealed class LinuxVmHost(IOptions<HomeBackendOptions> options, ILogger<LinuxVmHost> log) : IVmHost
{
    // Linux has 100 ticks per second (USER_HZ) on every architecture we care about
    private const double TicksPerSecond = 100;

    private readonly string _dir = options.Value.VmConfigDir;
    private readonly HashSet<string> _reportedBroken = [];

    public IReadOnlyList<VmConfig> ReadConfigs()
    {
        var result = new List<VmConfig>();
        if (!Directory.Exists(_dir)) return result;

        foreach (var file in Directory.EnumerateFiles(_dir, "*.conf").Order())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!VmConfigFile.IsValidName(name)) continue;
            try
            {
                result.Add(VmConfigFile.Parse(name, File.ReadLines(file)));
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                // read every 2 s: say it once per file and message, not every time
                lock (_reportedBroken)
                    if (_reportedBroken.Add($"{file}: {ex.Message}"))
                        log.LogWarning("Skipping {File}: {Problem}", file, ex.Message);
            }
        }
        return result;
    }

    public void WriteConfig(VmConfig config)
    {
        // a temp file in the same directory, then rename: vm-run never reads half a file
        var path = Path.Combine(_dir, config.Name + ".conf");
        var temp = path + ".tmp";
        File.WriteAllText(temp, VmConfigFile.Format(config));
        File.Move(temp, path, overwrite: true);
    }

    public async Task<IReadOnlyList<VmUnitState>> ReadUnitsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        if (names.Count == 0) return [];
        var output = await ProcessRunner.RunAsync("systemctl", SystemctlVmUnits.ShowArguments(names), ct);
        return SystemctlVmUnits.ParseShow(output, names.Count, LinuxFiles.ReadUptimeSeconds(), DateTimeOffset.UtcNow);
    }

    public async Task RunAsync(string name, VmAction action, CancellationToken ct)
    {
        var commands = SystemctlVmUnits.ActionCommands(name, action);
        for (var i = 0; i < commands.Length; i++)
        {
            try
            {
                await ProcessRunner.RunAsync("systemctl", commands[i], ct, timeoutMs: 30_000);
            }
            // the kill before a poweroff fails when QEMU is already gone; the stop still has to happen
            catch (InvalidOperationException) when (action == VmAction.Poweroff && i == 0) { }
        }
    }

    public (double CpuSeconds, long RssBytes)? ReadProcess(int pid)
    {
        var stat = LinuxFiles.ReadText($"/proc/{pid}/stat");
        var statm = LinuxFiles.ReadText($"/proc/{pid}/statm");
        if (stat is null || statm is null) return null;
        return (ProcPid.ParseCpuTicks(stat) / TicksPerSecond, ProcPid.ParseResidentPages(statm) * Environment.SystemPageSize);
    }

    public IReadOnlyList<string> ReadBridges() =>
        Directory.Exists("/sys/class/net")
            ? Directory.EnumerateDirectories("/sys/class/net")
                .Where(d => Directory.Exists(Path.Combine(d, "bridge")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order()
                .ToList()
            : [];
}
