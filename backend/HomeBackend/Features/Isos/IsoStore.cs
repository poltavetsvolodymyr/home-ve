using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using HomeBackend.Configuration;
using HomeBackend.Features.Vms;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Isos;

/// <summary>
/// The ISO directory, which also holds qcow2 disk images: list, delete, and downloads by URL that run in the
/// background (a phone can't upload a 700 MB file comfortably, but it can paste a link). A download goes to
/// <c>.&lt;name&gt;.part</c> and is renamed when complete, so a half file never shows up as an image. A link
/// to an <c>.xz</c> file is unpacked on the way (disk images are mostly published so: haos_ova-16.2.qcow2.xz).
/// A server that goes quiet (no answer, or no data in the middle of the file) fails the download after
/// <paramref name="stallTimeout"/> instead of leaving it hanging forever.
/// </summary>
public sealed class IsoStore(IOptions<HomeBackendOptions> options, ILogger<IsoStore> log, TimeSpan? stallTimeout = null) : IDisposable
{
    public const long MaxBytes = 32L << 30;
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _stallTimeout = stallTimeout ?? DefaultStallTimeout;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = HappyEyeballs.ConnectAsync,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", "home-ve" } },
    };

    private readonly string _dir = options.Value.IsoDir;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Download> _downloads = [];
    private readonly CancellationTokenSource _stopping = new();

    private sealed class Download(string name, string url, bool unpack)
    {
        public string Name { get; } = name;
        public string Url { get; } = url;
        public bool Unpack { get; } = unpack;
        public long Received;
        public long? Total;
        public string? Error;
        public CancellationTokenSource Cancel { get; } = new();
        public IsoDownload ToModel() => new(Name, Url, Interlocked.Read(ref Received), Total, Error);
    }

    public string PathOf(string name) => Path.Combine(_dir, name);

    /// <summary>An ISO or a qcow2 file name.</summary>
    public static bool IsValidName(string? name) => VmConfigFile.IsValidIsoName(name) || VmConfigFile.IsValidImageName(name);

    public bool Exists(string name) => IsValidName(name) && File.Exists(PathOf(name));

    /// <summary>The disk size a qcow2 holds, from its header; null when the file isn't one.</summary>
    public long? DiskBytesOf(string name) => IsValidName(name) ? Qcow2DiskBytes(PathOf(name)) : null;

    /// <summary>
    /// The qcow2 header: "QFIû", then at byte 24 the disk size, big-endian. Only to show it and to size the
    /// VM's disk; vm-run reads the file again with qemu-img, as home-backend, when it writes it onto the disk.
    /// </summary>
    public static long? Qcow2DiskBytes(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[32];
            file.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0x514649fb) return null;
            var size = BinaryPrimitives.ReadInt64BigEndian(header[24..]);
            return size > 0 ? size : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IsoList List(IReadOnlyList<VmConfig> vms)
    {
        var files = Directory.Exists(_dir)
            ? new DirectoryInfo(_dir).EnumerateFiles()
                .Where(f => IsValidName(f.Name))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new IsoFile(f.Name, f.Length, f.LastWriteTimeUtc,
                    vms.Where(v => v.Cdrom == f.Name).Select(v => v.Name).ToList(),
                    VmConfigFile.IsValidImageName(f.Name) ? Qcow2DiskBytes(f.FullName) : null))
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
        var linked = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        var unpack = linked.EndsWith(".xz", StringComparison.OrdinalIgnoreCase);
        name = string.IsNullOrWhiteSpace(name) ? linked : name.Trim();
        // the name is the unpacked file's
        if (unpack && name.EndsWith(".xz", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
        if (!IsValidName(name))
            return $"'{name}' is not an image file name (letters, digits, . _ + -, ending in .iso or .qcow2): give it a name";
        if (File.Exists(PathOf(name))) return $"{name} is already there";

        var download = new Download(name, uri.ToString(), unpack);
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
        if (!IsValidName(name)) return false;
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
            // restarted before every wait for the server: fires only when it sends nothing for that long
            using var stall = new CancellationTokenSource(_stallTimeout);
            using var ct = CancellationTokenSource.CreateLinkedTokenSource(d.Cancel.Token, _stopping.Token, stall.Token);
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct.Token);
            response.EnsureSuccessStatusCode();

            d.Total = response.Content.Headers.ContentLength;
            if (d.Total > MaxBytes) throw new InvalidOperationException($"bigger than {MaxBytes >> 30} GiB");
            if (d.Total > FreeBytes()) throw new InvalidOperationException("not enough free space on the host");

            await using (var source = await response.Content.ReadAsStreamAsync(ct.Token))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                if (d.Unpack) await UnpackAsync(source, file, d, stall, ct.Token);
                else await ReceiveAsync(source, file, d, stall, ct.Token);
            }
            if (d.Total is { } total && d.Received != total) throw new IOException($"got {d.Received} of {total} bytes");
            if (VmConfigFile.IsValidImageName(d.Name) && Qcow2DiskBytes(part) is null)
                throw new InvalidDataException("not a qcow2 image");

            File.Move(part, PathOf(d.Name));
            lock (_lock) _downloads.Remove(d.Name);
            log.LogInformation("Downloaded {Name} ({Bytes} bytes) from {Url}", d.Name, d.Received, d.Url);
        }
        catch (Exception ex)
        {
            TryDelete(part);
            if (d.Cancel.IsCancellationRequested || _stopping.IsCancellationRequested) return;
            d.Error = ex switch
            {
                HttpRequestException { StatusCode: { } code } => $"the server answered {(int)code} {code}",
                OperationCanceledException => $"the server sent nothing for {_stallTimeout.TotalSeconds:0} s",
                // connecting failed: HappyEyeballs says what each address did
                HttpRequestException { InnerException: IOException inner } => inner.Message,
                _ => ex.Message,
            };
            log.LogWarning("Downloading {Name} from {Url} failed: {Error}", d.Name, d.Url, d.Error);
        }
    }

    /// <summary>The server's bytes into <paramref name="sink"/>, counted for the progress bar.</summary>
    private async Task ReceiveAsync(Stream source, Stream sink, Download d, CancellationTokenSource stall, CancellationToken ct)
    {
        var buffer = new byte[1 << 16];
        int n;
        while (true)
        {
            stall.CancelAfter(_stallTimeout);
            if ((n = await source.ReadAsync(buffer, ct)) == 0) break;
            await sink.WriteAsync(buffer.AsMemory(0, n), ct);
            if (Interlocked.Add(ref d.Received, n) > MaxBytes) throw new InvalidOperationException($"bigger than {MaxBytes >> 30} GiB");
        }
    }

    /// <summary>
    /// The server's bytes through <c>xz -dc</c> into <paramref name="file"/>. The progress bar counts what comes
    /// from the server (that's what Content-Length tells), the limit what lands on the disk.
    /// </summary>
    private async Task UnpackAsync(Stream source, Stream file, Download d, CancellationTokenSource stall, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("xz")
        {
            ArgumentList = { "-dc" },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["LC_ALL"] = "C";
        using var xz = Process.Start(psi) ?? throw new InvalidOperationException("failed to start xz");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var errors = xz.StandardError.ReadToEndAsync(stop.Token);
        var feed = Task.Run(async () =>
        {
            await using var stdin = xz.StandardInput.BaseStream;
            await ReceiveAsync(source, stdin, d, stall, stop.Token);
        }, stop.Token);
        try
        {
            var stdout = xz.StandardOutput.BaseStream;
            var buffer = new byte[1 << 16];
            long written = 0;
            int n;
            while ((n = await stdout.ReadAsync(buffer, stop.Token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), stop.Token);
                if ((written += n) > MaxBytes) throw new InvalidOperationException($"bigger than {MaxBytes >> 30} GiB unpacked");
            }
            // a server that stopped in the middle shows here, before xz's "unexpected end of input"
            await feed;
            await xz.WaitForExitAsync(stop.Token);
            if (xz.ExitCode != 0) throw new InvalidDataException($"xz: {(await errors).Trim()}");
        }
        finally
        {
            await stop.CancelAsync();
            try { xz.Kill(); } catch (InvalidOperationException) { /* already gone */ }
            try { await feed; } catch (Exception) when (feed.IsFaulted || feed.IsCanceled) { /* the error that matters is already on its way */ }
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
