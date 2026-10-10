using System.Security.Claims;
using System.Threading.RateLimiting;
using HomeBackend.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBackend.Features.Auth;

/// <summary>
/// Password login with a cookie session (7 days, sliding), at most 10 attempts per 5 minutes per client IP.
/// Before there is a password, the first-run setup sets one (<see cref="PasswordStore"/>).
/// Pages: Login, Setup. Endpoints: <c>POST /api/auth/login</c>, <c>POST /api/auth/logout</c>, <c>GET /api/auth/me</c>,
/// <c>GET</c> and <c>POST /api/auth/setup</c>.
/// </summary>
public static class AuthFeature
{
    private const string LoginRateLimit = "login";

    /// <param name="keyRingDirectory">Where the cookie encryption keys live, so sessions survive restarts.</param>
    public static IServiceCollection AddAuthFeature(this IServiceCollection services, string keyRingDirectory)
    {
        Directory.CreateDirectory(keyRingDirectory);
        services.AddSingleton<PasswordStore>();
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
        auth.MapGet("/setup", (PasswordStore store) => new SetupStatus(store.NeedsSetup));
        auth.MapPost("/setup", SetUp).RequireRateLimiting(LoginRateLimit);
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

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> Login(
        LoginRequest request, HttpContext http, PasswordStore store, ILoggerFactory loggers)
    {
        if (!store.Verify(request.Password))
        {
            loggers.CreateLogger(typeof(AuthFeature)).LogWarning("Failed login from {Ip}", http.Connection.RemoteIpAddress);
            return TypedResults.Unauthorized();
        }

        await SignInAsync(http);
        return TypedResults.NoContent();
    }

    /// <summary>The first password, with the setup code; signs in right away.</summary>
    private static async Task<Results<NoContent, BadRequest<ErrorResponse>, Conflict<ErrorResponse>>> SetUp(
        SetupRequest request, HttpContext http, PasswordStore store, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger(typeof(AuthFeature));
        if (!store.NeedsSetup) return TypedResults.Conflict(new ErrorResponse("The password is set already: sign in with it."));
        if (request.Password is not { Length: >= PasswordStore.MinLength and <= PasswordStore.MaxLength } password)
            return TypedResults.BadRequest(new ErrorResponse($"The password needs {PasswordStore.MinLength} to {PasswordStore.MaxLength} characters."));

        try
        {
            if (!store.TrySetUp(request.Code, password))
            {
                log.LogWarning("Wrong setup code from {Ip}", http.Connection.RemoteIpAddress);
                return TypedResults.BadRequest(new ErrorResponse("That is not the setup code."));
            }
        }
        catch (InvalidOperationException)
        {
            // someone else was quicker, a moment ago
            return TypedResults.Conflict(new ErrorResponse("The password is set already: sign in with it."));
        }

        log.LogWarning("Password set by the first-run setup from {Ip}", http.Connection.RemoteIpAddress);
        await SignInAsync(http);
        return TypedResults.NoContent();
    }

    private static Task SignInAsync(HttpContext http)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }
}
