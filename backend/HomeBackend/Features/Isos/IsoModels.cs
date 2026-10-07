namespace HomeBackend.Features.Isos;

/// <summary>An ISO image on the host.</summary>
/// <param name="UsedBy">VMs that have it in their CD drive; it can't be deleted while any do.</param>
public sealed record IsoFile(string Name, long SizeBytes, DateTimeOffset Modified, IReadOnlyList<string> UsedBy);

/// <summary>A download in progress, or one that failed (until it's dismissed).</summary>
/// <param name="TotalBytes">From Content-Length; null when the server doesn't say.</param>
/// <param name="Error">Why it failed; null while it runs.</param>
public sealed record IsoDownload(string Name, string Url, long ReceivedBytes, long? TotalBytes, string? Error);

/// <summary>Answer of <c>GET /api/isos</c>.</summary>
public sealed record IsoList(IReadOnlyList<IsoFile> Files, IReadOnlyList<IsoDownload> Downloads, long? FreeBytes);

/// <summary>Body of <c>POST /api/isos</c>: what to download, and optionally the file name to save it as.</summary>
public sealed record IsoDownloadRequest(string Url, string? Name);
