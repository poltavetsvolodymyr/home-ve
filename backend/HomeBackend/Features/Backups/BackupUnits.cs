using System.Globalization;
using System.Text.RegularExpressions;

namespace HomeBackend.Features.Backups;

/// <summary>Unit and file names; the id is checked before it goes anywhere near a path or an argv.</summary>
public static partial class BackupUnits
{
    public static string Backup(string name) => $"vm-backup@{name}.service";

    public static string Restore(string name, string id) => $"vm-restore@{name}:{id}.service";

    public static string Delete(string name, string id) => $"vm-backup-delete@{name}:{id}.service";

    public static string[] ShowArguments(string name) =>
        ["show", "--no-pager", "--timestamp=unix", "--property=ActiveState,SubState,Result,ExecMainStartTimestamp,ExecMainExitTimestamp", Backup(name)];

    [GeneratedRegex(@"^\d{8}-\d{6}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string id) => IdPattern().IsMatch(id) && TimeOf(id) is not null;

    /// <summary>The time in an id (the host's local time, as <c>date</c> wrote it).</summary>
    public static DateTimeOffset? TimeOf(string id) =>
        DateTime.TryParseExact(id, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t)
            ? new DateTimeOffset(t)
            : null;

    /// <summary>The id in a file name <c>&lt;vm&gt;-&lt;id&gt;.img.zst</c>; null for anything else.</summary>
    public static string? IdOf(string name, string fileName) =>
        fileName.StartsWith(name + "-", StringComparison.Ordinal) && fileName.EndsWith(".img.zst", StringComparison.Ordinal)
            && fileName[(name.Length + 1)..^".img.zst".Length] is var id && IsValidId(id)
            ? id
            : null;
}
