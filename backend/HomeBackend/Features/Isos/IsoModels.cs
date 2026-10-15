namespace HomeBackend.Features.Isos;

/// <summary>An image on the host: an ISO for the CD drive, or a qcow2 disk image a new VM's disk is made from.</summary>
/// <param name="UsedBy">VMs that have it in their CD drive; it can't be deleted while any do.</param>
/// <param name="DiskBytes">A qcow2's disk size, the least a VM's disk made from it can have; null for an ISO.</param>
public sealed record IsoFile(string Name, long SizeBytes, DateTimeOffset Modified, IReadOnlyList<string> UsedBy, long? DiskBytes = null);

/// <summary>A download in progress, or one that failed (until it's dismissed).</summary>
/// <param name="TotalBytes">From Content-Length; null when the server doesn't say.</param>
/// <param name="Error">Why it failed; null while it runs.</param>
public sealed record IsoDownload(string Name, string Url, long ReceivedBytes, long? TotalBytes, string? Error);

/// <summary>Answer of <c>GET /api/isos</c>.</summary>
public sealed record IsoList(IReadOnlyList<IsoFile> Files, IReadOnlyList<IsoDownload> Downloads, long? FreeBytes);

/// <summary>Body of <c>POST /api/isos</c>: what to download, and optionally the file name to save it as.</summary>
public sealed record IsoDownloadRequest(string Url, string? Name);
