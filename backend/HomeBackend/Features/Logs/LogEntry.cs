namespace HomeBackend.Features.Logs;

/// <summary>One journal line.</summary>
/// <param name="Priority">syslog priority: 0–3 errors, 4 warning, 5–6 info, 7 debug.</param>
/// <param name="Source">SYSLOG_IDENTIFIER, or the process name when that's missing.</param>
public sealed record LogEntry(DateTimeOffset Time, int Priority, string Source, string Message);
