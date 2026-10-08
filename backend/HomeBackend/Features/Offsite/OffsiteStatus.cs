using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Offsite;

/// <summary>The last run of vm-offsite.service (deploy/offsite/vm-offsite) and what it printed.</summary>
/// <param name="Configured">/etc/vm-offsite/rclone.conf is there: the offsite store has been set up.</param>
/// <param name="State">never (not run since the host booted), running, succeeded or failed.</param>
/// <param name="Log">That run's journal lines, newest first; empty when it never ran.</param>
public sealed record OffsiteStatus(bool Configured, string State, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    IReadOnlyList<LogEntry> Log);
