using System.Net;
using System.Net.Sockets;
using HomeBackend.Configuration;
using HomeBackend.Features.Isos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeBackend.Tests.Features;

/// <summary>A server that goes quiet fails the download instead of leaving it hanging; .xz links are unpacked.</summary>
public sealed class IsoDownloadTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "home-iso-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);
    private readonly IsoStore _store;

    public IsoDownloadTests()
    {
        _server.Start();
        _store = new IsoStore(Options.Create(new HomeBackendOptions { IsoDir = _dir }), NullLogger<IsoStore>.Instance,
            stallTimeout: TimeSpan.FromSeconds(1));
    }

    private string Url => $"http://127.0.0.1:{((IPEndPoint)_server.LocalEndpoint).Port}/test.iso";

    [Fact]
    public async Task A_server_that_never_answers_fails_the_download()
    {
        using var accepted = Accept(_ => Task.Delay(Timeout.Infinite, Ct));

        Assert.Null(_store.StartDownload(Url, null));

        Assert.Equal("the server sent nothing for 1 s", await ErrorAsync());
    }

    [Fact]
    public async Task A_server_that_stops_in_the_middle_fails_the_download()
    {
        using var accepted = Accept(async stream =>
        {
            var head = "HTTP/1.1 200 OK\r\nContent-Length: 1000000\r\n\r\n"u8.ToArray();
            await stream.WriteAsync(head, Ct);
            await stream.WriteAsync(new byte[1000], Ct);
            await Task.Delay(Timeout.Infinite, Ct);
        });

        Assert.Null(_store.StartDownload(Url, null));

        Assert.Equal("the server sent nothing for 1 s", await ErrorAsync());
        Assert.False(File.Exists(Path.Combine(_dir, ".test.iso.part"))); // the half file is gone
    }

    [Fact]
    public async Task A_missing_file_says_404_at_once()
    {
        using var accepted = Accept(async stream =>
        {
            await stream.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), Ct);
            await Task.Delay(Timeout.Infinite, Ct);
        });

        Assert.Null(_store.StartDownload(Url, null));

        Assert.Equal("the server answered 404 NotFound", await ErrorAsync());
    }

    [Fact]
    public async Task A_refused_connection_says_so()
    {
        var port = ((IPEndPoint)_server.LocalEndpoint).Port;
        _server.Stop();

        Assert.Null(_store.StartDownload($"http://127.0.0.1:{port}/test.iso", null));

        Assert.Equal("could not connect to 127.0.0.1: IPv4 127.0.0.1: ConnectionRefused", await ErrorAsync());
    }

    [Fact]
    public async Task An_xz_link_lands_unpacked_under_the_name_without_xz()
    {
        var qcow2 = new byte[1 << 20];
        Random.Shared.NextBytes(qcow2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(qcow2, 0x514649fb);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(qcow2.AsSpan(24), 32L << 30);
        var packed = await XzAsync(qcow2);
        using var accepted = Serve(packed);

        Assert.Null(_store.StartDownload(UrlOf("haos_ova-16.2.qcow2.xz"), null));

        Assert.Null(await FinishedAsync());
        Assert.Equal(qcow2, await File.ReadAllBytesAsync(Path.Combine(_dir, "haos_ova-16.2.qcow2"), Ct));
        Assert.Equal(32L << 30, _store.List([]).Files.Single().DiskBytes);
    }

    [Fact]
    public async Task A_qcow2_that_isnt_one_is_not_kept()
    {
        using var accepted = Serve(new byte[4096]);

        Assert.Null(_store.StartDownload(UrlOf("fake.qcow2"), null));

        Assert.Equal("not a qcow2 image", await FinishedAsync());
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_broken_xz_says_what_xz_said()
    {
        using var accepted = Serve(new byte[4096]);

        Assert.Null(_store.StartDownload(UrlOf("broken.iso.xz"), null));

        Assert.StartsWith("xz: ", await FinishedAsync());
        Assert.Empty(Directory.GetFiles(_dir));
    }

    private string UrlOf(string file) => $"http://127.0.0.1:{((IPEndPoint)_server.LocalEndpoint).Port}/{file}";

    /// <summary>Answers one request with <paramref name="body"/>.</summary>
    private IDisposable Serve(byte[] body) => Accept(async stream =>
    {
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), Ct);
        await stream.WriteAsync(body, Ct);
        await Task.Delay(Timeout.Infinite, Ct);
    });

    private static async Task<byte[]> XzAsync(byte[] data)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("xz", "-c") { RedirectStandardInput = true, RedirectStandardOutput = true };
        using var xz = System.Diagnostics.Process.Start(psi)!;
        var output = new MemoryStream();
        var read = xz.StandardOutput.BaseStream.CopyToAsync(output, Ct);
        await xz.StandardInput.BaseStream.WriteAsync(data, Ct);
        xz.StandardInput.Close();
        await read;
        await xz.WaitForExitAsync(Ct);
        return output.ToArray();
    }

    /// <summary>The download's error once it's over, or null when it went through.</summary>
    private async Task<string?> FinishedAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            if (_store.List([]).Downloads.SingleOrDefault() is not { } d) return null;
            if (d.Error is { } error) return error;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("the download didn't finish");
    }

    /// <summary>Answers one connection with <paramref name="serve"/>; disposing closes it.</summary>
    private IDisposable Accept(Func<NetworkStream, Task> serve)
    {
        var client = new TaskCompletionSource<TcpClient>();
        _ = Task.Run(async () =>
        {
            var c = await _server.AcceptTcpClientAsync(Ct);
            client.SetResult(c);
            try { await serve(c.GetStream()); } catch (OperationCanceledException) { }
        }, Ct);
        return new Closer(client.Task);
    }

    private sealed class Closer(Task<TcpClient> client) : IDisposable
    {
        public void Dispose()
        {
            if (client.IsCompletedSuccessfully) client.Result.Dispose();
        }
    }

    private async Task<string?> ErrorAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            if (_store.List([]).Downloads.SingleOrDefault()?.Error is { } error) return error;
            await Task.Delay(100, Ct);
        }
        return null;
    }

    public void Dispose()
    {
        _store.Dispose();
        _server.Stop();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
