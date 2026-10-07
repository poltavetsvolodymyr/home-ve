namespace HomeBackend.Features.Vms;

/// <summary>Builds the <c>systemctl</c> command lines for VM units and parses <c>systemctl show</c>.</summary>
public static class SystemctlVmUnits
{
    public static string Unit(string name) => $"vm@{name}.service";

    public static string[] ShowArguments(IReadOnlyList<string> names) =>
        ["show", "--no-pager", "--property=ActiveState,SubState,ActiveEnterTimestampMonotonic,MainPID", .. names.Select(Unit)];

    /// <summary>
    /// The commands behind a button. Everything but start returns at once (<c>--no-block</c>): a shutdown can take
    /// the guest a while, and the page follows the state anyway. Poweroff kills QEMU and then stops the unit, so the
    /// kill isn't taken for a crash and restarted (Restart=on-failure waits 5 s, the stop cancels that).
    /// </summary>
    public static string[][] ActionCommands(string name, VmAction action) => action switch
    {
        VmAction.Start => [["start", Unit(name)]],
        VmAction.Shutdown => [["stop", "--no-block", Unit(name)]],
        VmAction.Reboot => [["restart", "--no-block", Unit(name)]],
        VmAction.Poweroff => [["kill", "--signal=KILL", Unit(name)], ["stop", "--no-block", Unit(name)]],
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
