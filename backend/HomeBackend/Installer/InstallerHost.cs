using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HomeBackend.Api;
using HomeBackend.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace HomeBackend.Installer;

/// <summary>The <c>Installer</c> config section (the live image sets nothing: these defaults are its own).</summary>
public sealed class InstallerOptions
{
    public const string Section = "Installer";

    /// <summary>
    /// HTTPS for the page (passwords go through it), and plain HTTP that only redirects there. Empty: those two on
    /// every address (a default here instead would merge by index with a shorter list from the config).
    /// </summary>
    public string[] Urls { get; set; } = [];

    /// <summary>The built frontend: installer.html and its files.</summary>
    public string WwwDir { get; set; } = "/opt/home-ve/www";

    /// <summary>The code the page asks for, written by the live image at boot and shown on its screen.</summary>
    public string CodeFile { get; set; } = "/run/home-ve-installer/code";

    /// <summary>A made-up machine, and no installing (development).</summary>
    public bool Mock { get; set; }

    /// <summary>
    /// The machine's own screen (the kiosk browser, on http://127.0.0.1) needs no code and no HTTPS: whoever sits in
    /// front of it is at the machine anyway, and the connection doesn't leave it.
    /// </summary>
    public bool TrustLoopback { get; set; } = true;
}

public sealed record InstallerSessionRequest(string? Code);

/// <summary>For the machine's own screen: how a phone gets in.</summary>
/// <param name="Urls">https://&lt;address&gt;/ for each address the machine has now.</param>
public sealed record LocalAccess(string Code, IReadOnlyList<string> Urls);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(InstallerSessionRequest))]
[JsonSerializable(typeof(Machine))]
[JsonSerializable(typeof(LocalAccess))]
[JsonSerializable(typeof(ErrorResponse))]
public partial class InstallerJsonContext : JsonSerializerContext;

/// <summary>
/// <c>home-backend installer</c>: the page of the home-ve installer image, instead of the VM host's web UI. It runs as
/// root on the live system and serves both the frontend (installer.html) and its API under <c>/api/installer</c>.
/// Whoever opens it first types the code from the machine's screen; nothing else opens without it.
/// </summary>
public static class InstallerHost
{
    private const string CodeRateLimit = "code";

    public static int Run(string[] args)
    {
        var app = Build(args);
        app.Run();
        return 0;
    }

    /// <summary>The installer's web app, ready to start; <paramref name="args"/> may set Installer:* (tests do).</summary>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        var section = builder.Configuration.GetSection(InstallerOptions.Section);
        builder.Services.AddOptions<InstallerOptions>().Bind(section);
        var options = section.Get<InstallerOptions>() ?? new();
        if (options.Urls.Length == 0) options.Urls = ["https://0.0.0.0:443", "http://0.0.0.0:80"];

        builder.WebHost.UseUrls(options.Urls);
        // a certificate made now for this boot: the browser warns once, the connection is encrypted. The slim
        // builder leaves HTTPS out unless asked for.
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(k => k.ConfigureHttpsDefaults(h => h.ServerCertificate = SelfSignedCertificate()));
        builder.Services.ConfigureHttpJsonOptions(j => j.SerializerOptions.TypeInfoResolverChain.Insert(0, InstallerJsonContext.Default));

        // sessions only for this boot: the keys stay in memory
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(c =>
        {
            c.Cookie.Name = "home-ve-installer";
            c.Cookie.HttpOnly = true;
            c.Cookie.SameSite = SameSiteMode.Strict;
            c.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            c.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(r =>
        {
            r.RejectionStatusCode = 429;
            r.AddPolicy(CodeRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
        });
        if (options.Mock) builder.Services.AddSingleton<IMachineSource, MockMachineSource>();
        else builder.Services.AddSingleton<IMachineSource, LinuxMachineSource>();

        var app = builder.Build();
        app.UseSecurityHeaders();
        if (options.Urls.Any(u => u.StartsWith("https:", StringComparison.Ordinal)))
            app.Use((ctx, next) =>
            {
                // plain HTTP only points at the HTTPS page, except for the machine's own screen
                if (ctx.Request.IsHttps || IsLocal(ctx, options)) return next();
                ctx.Response.Redirect($"https://{ctx.Request.Host.Host}{ctx.Request.Path}");
                return Task.CompletedTask;
            });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.Use((ctx, next) =>
        {
            // the machine's own screen is signed in as it is
            if (ctx.User.Identity?.IsAuthenticated != true && IsLocal(ctx, options))
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "screen")], "loopback"));
            return next();
        });
        app.UseAuthorization();

        var api = app.MapGroup("/api/installer");
        api.MapPost("/session", CreateSession).RequireRateLimiting(CodeRateLimit);
        api.MapGet("/session", () => TypedResults.NoContent()).RequireAuthorization();
        api.MapGet("/machine", (IMachineSource source, CancellationToken ct) => source.ReadAsync(ct)).RequireAuthorization();
        api.MapGet("/local", Results<Ok<LocalAccess>, NotFound> (HttpContext http, IOptions<InstallerOptions> o) =>
            IsLocal(http, o.Value) ? TypedResults.Ok(new LocalAccess(Format(ReadCode(o.Value)), Addresses())) : TypedResults.NotFound());

        if (Directory.Exists(options.WwwDir))
        {
            var files = new PhysicalFileProvider(options.WwwDir);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, DefaultFileNames = ["installer.html"] });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }

        app.Logger.LogInformation("Installer page on {Urls}", string.Join(", ", options.Urls));
        return app;
    }

    private static async Task<Results<NoContent, BadRequest<ErrorResponse>>> CreateSession(
        InstallerSessionRequest request, HttpContext http, IOptions<InstallerOptions> options)
    {
        var expected = ReadCode(options.Value);
        var given = Normalize(request.Code ?? "");
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given)))
            return TypedResults.BadRequest(new ErrorResponse("That is not the code on the machine's screen."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "installer")], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(new ClaimsPrincipal(identity));
        return TypedResults.NoContent();
    }

    // the mock takes TEST-CODE when the live image's file isn't there
    private static string ReadCode(InstallerOptions options) =>
        File.Exists(options.CodeFile) ? Normalize(File.ReadAllText(options.CodeFile)) : options.Mock ? "TESTCODE" : "";

    private static bool IsLocal(HttpContext ctx, InstallerOptions options) =>
        options.TrustLoopback && ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);

    private static string Format(string code) => code.Length == 8 ? $"{code[..4]}-{code[4..]}" : code;

    private static List<string> Addresses() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => $"https://{a.Address}/")
            .ToList();

    public static string Normalize(string code) =>
        new(code.ToUpperInvariant().Where(c => c is not ('-' or ' ' or '\n' or '\r' or '\t')).ToArray());

    private static X509Certificate2 SelfSignedCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var name = Dns.GetHostName();
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));
        // a key Kestrel can use on Linux: exported and loaded back
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12), null);
    }
}
