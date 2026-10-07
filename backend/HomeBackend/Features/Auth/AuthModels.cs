namespace HomeBackend.Features.Auth;

public sealed record LoginRequest(string? Password);

public sealed record MeResponse(string? Name);
