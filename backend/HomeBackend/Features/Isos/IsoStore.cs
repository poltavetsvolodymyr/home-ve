using System.Net;
using HomeBackend.Configuration;
using HomeBackend.Features.Vms;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Isos;

/// <summary>
/// The ISO directory: list, delete, and downloads by URL that run in the background (a phone can't upload
/// a 700 MB file comfortably, but it can paste a link). A download goes to <c>.&lt;name&gt;.part</c> and is
/// renamed when complete, so a half file never shows up as an image.
/// </summary>
public sealed class IsoStore(IOptions<HomeBackendOptions> options, ILogger<IsoStore> log) : IDisposable
{
    public const long MaxBytes = 32L << 30;

    private static readonly HttpClient Http = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "home-ve" } },
    };

    private readonly string _dir = options.Value.IsoDir;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Download> _downloads = [];
    private readonly CancellationTokenSource _stopping = new();

    private sealed class Download(string name, string url)
    {
        public string Name { get; } = name;
        public string Url { get; } = url;
        public long Received;
        public long? Total;
        public string? Error;
        public CancellationTokenSource Cancel { get; } = new();
        public IsoDownload ToModel() => new(Name, Url, Interlocked.Read(ref Received), Total, Error);
    }

    public string PathOf(string name) => Path.Combine(_dir, name);

    public bool Exists(string name) => VmConfigFile.IsValidIsoName(name) && File.Exists(PathOf(name));

    public IsoList List(IReadOnlyList<VmConfig> vms)
    {
        var files = Directory.Exists(_dir)
            ? new DirectoryInfo(_dir).EnumerateFiles("*.iso")
                .Where(f => VmConfigFile.IsValidIsoName(f.Name))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new IsoFile(f.Name, f.Length, f.LastWriteTimeUtc,
                    vms.Where(v => v.Cdrom == f.Name).Select(v => v.Name).ToList()))
                .ToList()
            : [];
        List<IsoDownload> downloads;
        lock (_lock) downloads = _downloads.Values.Select(d => d.ToModel()).OrderBy(d => d.Name).ToList();
        return new IsoList(files, downloads, FreeBytes());
    }

    /// <summary>Starts a download; the error message when it can't start.</summary>
    public string? StartDownload(string url, string? name)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return "the link must start with https:// or http://";
        name = string.IsNullOrWhiteSpace(name) ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)) : name.Trim();
        if (!VmConfigFile.IsValidIsoName(name))
            return $"'{name}' is not an ISO file name (letters, digits, . _ + -, ending in .iso): give it a name";
        if (File.Exists(PathOf(name))) return $"{name} is already there";

        var download = new Download(name, uri.ToString());
        lock (_lock)
        {
            if (_downloads.TryGetValue(name, out var existing) && existing.Error is null) return $"{name} is already downloading";
            _downloads[name] = download;
        }
        _ = Task.Run(() => RunAsync(download, uri));
        return null;
    }

    /// <summary>Deletes an image, or cancels/dismisses a download of that name. False when there is neither.</summary>
    public bool Delete(string name)
    {
        if (!VmConfigFile.IsValidIsoName(name)) return false;
        Download? download;
        lock (_lock)
        {
            if (_downloads.Remove(name, out download)) download.Cancel.Cancel();
        }
        if (File.Exists(PathOf(name)))
        {
            File.Delete(PathOf(name));
            return true;
        }
        return download is not null;
    }

    private async Task RunAsync(Download d, Uri uri)
    {
        var part = Path.Combine(_dir, $".{d.Name}.part");
        try
        {
            Directory.CreateDirectory(_dir);
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(d.Cancel.Token, _stopping.Token);
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct.Token);
            response.EnsureSuccessStatusCode();

            d.Total = response.Content.Headers.ContentLength;
            if (d.Total > MaxBytes) throw new InvalidOperationException($"bigger than {MaxBytes >> 30} GiB");
            if (d.Total > FreeBytes()) throw new InvalidOperationException("not enough free space on the host");

            await using (var source = await response.Content.ReadAsStreamAsync(ct.Token))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int n;
                while ((n = await source.ReadAsync(buffer, ct.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), ct.Token);
                    if (Interlocked.Add(ref d.Received, n) > MaxBytes) throw new InvalidOperationException($"bigger than {MaxBytes >> 30} GiB");
                }
            }
            if (d.Total is { } total && d.Received != total) throw new IOException($"got {d.Received} of {total} bytes");

            File.Move(part, PathOf(d.Name));
            lock (_lock) _downloads.Remove(d.Name);
            log.LogInformation("Downloaded {Name} ({Bytes} bytes) from {Url}", d.Name, d.Received, d.Url);
        }
        catch (Exception ex)
        {
            TryDelete(part);
            if (d.Cancel.IsCancellationRequested || _stopping.IsCancellationRequested) return;
            d.Error = ex is HttpRequestException { StatusCode: { } code } ? $"the server answered {(int)code} {code}" : ex.Message;
            log.LogWarning("Downloading {Name} from {Url} failed: {Error}", d.Name, d.Url, d.Error);
        }
    }

    private long? FreeBytes()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            return new DriveInfo(_dir).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
