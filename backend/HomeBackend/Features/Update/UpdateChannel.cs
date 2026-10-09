namespace HomeBackend.Features.Update;

/// <summary>
/// Which branch Update follows: <c>stable</c> (the latest release) or <c>dev</c> (every commit on main). The backend
/// keeps it in <c>&lt;DataDir&gt;/update-channel</c>; deploy/update.sh, as root, reads it and accepts nothing else
/// than these two words. No file means stable.
/// </summary>
public static class UpdateChannel
{
    public const string Stable = "stable", Dev = "dev";
    public const string FileName = "update-channel";

    /// <summary>What install.sh records: <c>git describe --tags --always</c> of the checkout, e.g. v0.1.0-3-gabc1234.</summary>
    public const string VersionFile = "/etc/home-backend/version";

    public static bool IsValid(string? channel) => channel is Stable or Dev;

    public static string Read(string dataDir)
    {
        try { return File.ReadAllText(Path.Combine(dataDir, FileName)).Trim() is var c && IsValid(c) ? c : Stable; }
        catch (IOException) { return Stable; }
        catch (UnauthorizedAccessException) { return Stable; }
    }

    /// <summary>Temp file + rename, so update.sh never reads half a word.</summary>
    public static void Write(string dataDir, string channel)
    {
        if (!IsValid(channel)) throw new ArgumentException($"no such channel '{channel}'", nameof(channel));
        Directory.CreateDirectory(dataDir);
        var path = Path.Combine(dataDir, FileName);
        var temp = path + ".new";
        File.WriteAllText(temp, channel + "\n");
        File.Move(temp, path, overwrite: true);
    }
}

/// <param name="Channel">stable or dev.</param>
/// <param name="Version">What runs now (<see cref="UpdateChannel.VersionFile"/>); null when unknown.</param>
public sealed record UpdateChannelInfo(string Channel, string? Version);

public sealed record UpdateChannelRequest(string Channel);
