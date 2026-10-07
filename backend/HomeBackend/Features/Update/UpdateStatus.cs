using HomeBackend.Features.Logs;

namespace HomeBackend.Features.Update;

/// <summary>The last run of home-update.service (deploy/update.sh as root) and what it printed.</summary>
/// <param name="State">never (not run since the host booted), running, succeeded or failed.</param>
/// <param name="Log">That run's journal lines, newest first; empty when it never ran.</param>
public sealed record UpdateStatus(string State, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<LogEntry> Log);
