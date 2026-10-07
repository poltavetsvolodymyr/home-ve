using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBackend.Api;

/// <summary>
/// systemctl or journalctl failing (not installed, non-zero exit) is the host's problem, not a bug here:
/// such endpoints answer 502 Bad Gateway with the error text as the problem detail.
/// </summary>
public static class HostCommandFailure
{
    public static bool Is(Exception ex) => ex is InvalidOperationException or Win32Exception;

    public static ProblemHttpResult ToProblem(Exception ex) =>
        TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
}
