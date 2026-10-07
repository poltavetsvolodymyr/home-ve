using HomeBackend.Features.Vms;

namespace HomeBackend.Tests.Features;

public class VmUnitsTests
{
    [Fact]
    public void Reads_one_block_per_unit_in_order()
    {
        const string output = """
            ActiveState=active
            SubState=running
            ActiveEnterTimestampMonotonic=60000000
            MainPID=812

            ActiveState=inactive
            SubState=dead
            ActiveEnterTimestampMonotonic=0
            MainPID=0
            """;
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        var units = SystemctlVmUnits.ParseShow(output.ReplaceLineEndings("\n"), 2, uptimeSeconds: 3600, now);

        Assert.Equal(new VmUnitState("active", "running", now.AddSeconds(-3540), 812), units[0]);
        Assert.Equal(new VmUnitState("inactive", "dead", null, null), units[1]);
    }

    [Theory]
    [InlineData("active", "running")]
    [InlineData("activating", "starting")]
    [InlineData("deactivating", "stopping")]
    [InlineData("failed", "failed")]
    [InlineData("inactive", "stopped")]
    public void States_have_plain_names(string active, string state) =>
        Assert.Equal(state, SystemctlVmUnits.StateOf(new VmUnitState(active, "", null, null)));

    [Fact]
    public void Poweroff_terms_qemu_so_the_vm_ends_stopped_not_failed() =>
        Assert.Equal(
            [["kill", "--signal=TERM", "vm@test.service"], ["stop", "--no-block", "vm@test.service"]],
            SystemctlVmUnits.ActionCommands("test", VmAction.Poweroff));

    [Fact]
    public void Cpu_time_is_counted_after_the_command_name()
    {
        // a command name with spaces and a parenthesis must not shift the fields
        const string stat = "812 (qemu (x) 86) S 1 812 812 0 -1 4194560 120 0 0 0 1500 300 0 0 20 0 9 0 6000 3000000000 300000";
        Assert.Equal(1800UL, ProcPid.ParseCpuTicks(stat));
        Assert.Equal(300000L, ProcPid.ParseResidentPages("700000 300000 2000 500 0 400000 0"));
    }
}
