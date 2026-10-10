using System.Net;
using System.Net.Sockets;
using HomeBackend.Configuration;
using HomeBackend.Features.Isos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HomeBackend.Tests.Features;

/// <summary>A server that goes quiet fails the download instead of leaving it hanging.</summary>
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
