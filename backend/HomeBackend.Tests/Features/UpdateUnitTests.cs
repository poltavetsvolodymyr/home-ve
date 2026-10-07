using HomeBackend.Features.Update;

namespace HomeBackend.Tests.Features;

public class UpdateUnitTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1791378000);

    private static (string, DateTimeOffset?, DateTimeOffset?) Parse(string output) =>
        UpdateUnit.ParseShow(output.ReplaceLineEndings("\n"));

    [Fact]
    public void Never_ran_since_boot()
    {
        Assert.Equal(("never", null, null), Parse("""
            ActiveState=inactive
            Result=success
            ExecMainStartTimestamp=
            ExecMainExitTimestamp=
            """));
    }

    [Fact]
    public void Running_has_no_end_yet()
    {
        Assert.Equal(("running", Start, null), Parse("""
            ActiveState=activating
            SubState=start
            Result=success
            ExecMainStartTimestamp=@1791378000
            ExecMainExitTimestamp=
            """));
    }

    [Fact]
    public void Finished_runs_say_how_they_ended()
    {
        // RemainAfterExit=yes: a successful run stays active (exited)
        Assert.Equal(("succeeded", Start, Start.AddSeconds(42)), Parse("""
            ActiveState=active
            SubState=exited
            Result=success
            ExecMainStartTimestamp=@1791378000
            ExecMainExitTimestamp=@1791378042
            """));
        Assert.Equal(("failed", Start, Start.AddSeconds(3)), Parse("""
            ActiveState=failed
            Result=exit-code
            ExecMainStartTimestamp=@1791378000
            ExecMainExitTimestamp=@1791378003
            """));
    }
}
