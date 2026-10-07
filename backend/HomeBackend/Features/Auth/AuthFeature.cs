using System.Security.Claims;
using System.Threading.RateLimiting;
using HomeBackend.Configuration;
using HomeBackend.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Auth;

/// <summary>
/// Password login with a cookie session (7 days, sliding), at most 10 attempts per 5 minutes per client IP.
/// Page: Login. Endpoints: <c>POST /api/auth/login</c>, <c>POST /api/auth/logout</c>, <c>GET /api/auth/me</c>.
/// </summary>
public static class AuthFeature
{
    private const string LoginRateLimit = "login";

    /// <param name="keyRingDirectory">Where the cookie encryption keys live, so sessions survive restarts.</param>
    public static IServiceCollection AddAuthFeature(this IServiceCollection services, string keyRingDirectory)
    {
        Directory.CreateDirectory(keyRingDirectory);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingDirectory))
            .SetApplicationName("home-backend");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(c =>
            {
                c.Cookie.Name = "home-backend";
                c.Cookie.HttpOnly = true;
                c.Cookie.SameSite = SameSiteMode.Strict;
                c.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // plain http inside the LAN
                c.ExpireTimeSpan = TimeSpan.FromDays(7);
                c.SlidingExpiration = true;
                // an API, not a page: answer 401/403 instead of redirecting
                c.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                c.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            });
        services.AddAuthorization();

        services.AddRateLimiter(r =>
        {
            r.RejectionStatusCode = 429;
            r.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
        });
        return services;
    }

    /// <summary>Maps onto the <c>/api/auth</c> group: login and logout are open, <c>me</c> needs a session.</summary>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/login", Login).RequireRateLimiting(LoginRateLimit);
        // a lambda on purpose: a method taking just HttpContext binds to the RequestDelegate overload,
        // which would drop the 204 result
        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return TypedResults.NoContent();
        });
        auth.MapGet("/me", (ClaimsPrincipal user) => new MeResponse(user.Identity?.Name)).RequireAuthorization();
        return auth;
    }

    /// <summary>
    /// Checks against the PBKDF2 hash from the config. With mock data and no hash configured,
    /// the password is "admin", so the UI can be developed without any setup.
    /// </summary>
    public static bool IsPasswordCorrect(string? password, HomeBackendOptions options) =>
        string.IsNullOrEmpty(options.PasswordHash)
            ? options.Mock && password == "admin"
            : PasswordHash.Verify(password ?? "", options.PasswordHash);

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> Login(
        LoginRequest request, HttpContext http, IOptions<HomeBackendOptions> options, ILoggerFactory loggers)
    {
        if (!IsPasswordCorrect(request.Password, options.Value))
        {
            loggers.CreateLogger(typeof(AuthFeature)).LogWarning("Failed login from {Ip}", http.Connection.RemoteIpAddress);
            return TypedResults.Unauthorized();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
        return TypedResults.NoContent();
    }
}
