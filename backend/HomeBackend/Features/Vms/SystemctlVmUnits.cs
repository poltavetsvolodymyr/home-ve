namespace HomeBackend.Features.Vms;

/// <summary>Builds the <c>systemctl</c> command lines for VM units and parses <c>systemctl show</c>.</summary>
public static class SystemctlVmUnits
{
    public static string Unit(string name) => $"vm@{name}.service";

    /// <summary>Root oneshot that deletes a stopped VM's disk (deploy/vm/vm-disk-remove).</summary>
    public static string DiskRemoveUnit(string name) => $"vm-disk-remove@{name}.service";

    public static string[] ShowArguments(IReadOnlyList<string> names) =>
        ["show", "--no-pager", "--property=ActiveState,SubState,ActiveEnterTimestampMonotonic,MainPID", .. names.Select(Unit)];

    /// <summary>
    /// The commands behind a button. Everything but start returns at once (<c>--no-block</c>): a shutdown can take
    /// the guest a while, and the page follows the state anyway. Poweroff sends QEMU SIGTERM: it drops the guest at
    /// once, like pulling the plug, but exits cleanly with 0, so the unit ends up stopped rather than failed (SIGKILL
    /// counts as a failure). The stop after it is for a QEMU that doesn't react: systemd kills it after the timeout.
    /// </summary>
    public static string[][] ActionCommands(string name, VmAction action) => action switch
    {
        VmAction.Start => [["start", Unit(name)]],
        VmAction.Shutdown => [["stop", "--no-block", Unit(name)]],
        VmAction.Reboot => [["restart", "--no-block", Unit(name)]],
        VmAction.Poweroff => [["kill", "--signal=TERM", Unit(name)], ["stop", "--no-block", Unit(name)]],
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// One block of <c>Key=Value</c> lines per unit, in the order asked for, separated by blank lines.
    /// ActiveEnterTimestampMonotonic counts microseconds since boot; the uptime turns it into a date.
    /// </summary>
    public static List<VmUnitState> ParseShow(string output, int count, double uptimeSeconds, DateTimeOffset now)
    {
        var blocks = output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var result = new List<VmUnitState>(count);
        for (var i = 0; i < count; i++)
        {
            var kv = (i < blocks.Length ? blocks[i] : "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);

            var active = kv.GetValueOrDefault("ActiveState", "inactive");
            DateTimeOffset? since = null;
            if (active == "active" && ulong.TryParse(kv.GetValueOrDefault("ActiveEnterTimestampMonotonic"), out var mono) && mono > 0)
                since = now.AddSeconds(-(uptimeSeconds - mono / 1e6));

            result.Add(new VmUnitState(
                active,
                kv.GetValueOrDefault("SubState", ""),
                since,
                int.TryParse(kv.GetValueOrDefault("MainPID"), out var pid) && pid > 0 ? pid : null));
        }
        return result;
    }

    /// <summary>The state word the pages use.</summary>
    public static string StateOf(VmUnitState unit) => unit.ActiveState switch
    {
        "active" => "running",
        "activating" or "reloading" => "starting",
        "deactivating" => "stopping",
        "failed" => "failed",
        _ => "stopped",
    };
}
