namespace HomeBackend.Api;

/// <summary>Body of a 400 response: <c>{"error": "..."}</c>.</summary>
public sealed record ErrorResponse(string Error);
