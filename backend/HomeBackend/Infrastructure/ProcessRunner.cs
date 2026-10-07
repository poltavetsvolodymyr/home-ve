using System.Diagnostics;

namespace HomeBackend.Infrastructure;

/// <summary>Runs host programs such as systemctl and journalctl.</summary>
public static class ProcessRunner
{
    /// <summary>Runs a program directly (no shell), returns stdout. Throws on non-zero exit or timeout.</summary>
    public static async Task<string> RunAsync(string file, IEnumerable<string> args, CancellationToken ct, int timeoutMs = 10_000)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["SYSTEMD_COLORS"] = "0";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);

        var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        var output = await stdout;
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} exited with {p.ExitCode}: {(await stderr).Trim()}");
        return output;
    }
}
