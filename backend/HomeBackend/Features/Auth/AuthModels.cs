namespace HomeBackend.Features.Auth;

public sealed record LoginRequest(string? Password);

public sealed record MeResponse(string? Name);

/// <summary>Whether the first-run setup is still to be done (there is no password yet).</summary>
public sealed record SetupStatus(bool Needed);

/// <summary>The setup code from the host (<c>/var/lib/home-backend/setup-code</c>) and the first password.</summary>
public sealed record SetupRequest(string? Code, string? Password);
