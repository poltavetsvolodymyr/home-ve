namespace HomeBackend.Features.Update;

/// <summary><c>systemctl show</c> for home-update.service and what its state means for the page.</summary>
public static class UpdateUnit
{
    public const string Name = "home-update.service";

    public static readonly string[] ShowArguments =
        ["show", "--no-pager", "--timestamp=unix", "--property=ActiveState,Result,ExecMainStartTimestamp,ExecMainExitTimestamp", Name];

    public static readonly string[] StartArguments = ["start", "--no-block", Name];

    /// <param name="output">Key=Value lines; timestamps as <c>@&lt;unix seconds&gt;</c>, empty when there is none.</param>
    public static (string State, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt) ParseShow(string output)
    {
        var kv = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1].Trim());

        var started = Timestamp(kv.GetValueOrDefault("ExecMainStartTimestamp"));
        var finished = Timestamp(kv.GetValueOrDefault("ExecMainExitTimestamp"));
        var state = kv.GetValueOrDefault("ActiveState", "inactive") switch
        {
            "activating" or "active" or "reloading" => "running",
            "failed" => "failed",
            _ when started is null => "never",
            _ => kv.GetValueOrDefault("Result") == "success" ? "succeeded" : "failed",
        };
        return (state, started, state == "running" ? null : finished);
    }

    private static DateTimeOffset? Timestamp(string? value) =>
        value is ['@', .. var digits] && long.TryParse(digits.Split('.')[0], out var s) && s > 0
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;
}
