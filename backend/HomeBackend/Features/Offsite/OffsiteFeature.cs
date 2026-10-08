using HomeBackend.Api;
using HomeBackend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBackend.Features.Offsite;

/// <summary>
/// The offsite copy of the backups (deploy/offsite/vm-offsite, run by vm-offsite.service as root): its last run
/// and "Upload now". Page: Settings. Endpoints: <c>GET /api/host/offsite</c>, <c>POST /api/host/offsite</c>.
/// </summary>
public static class OffsiteFeature
{
    public static IServiceCollection AddOffsiteFeature(this IServiceCollection services, bool useMockData) =>
        services.AddDataSource<IOffsiteRunner, SystemctlOffsiteRunner, MockOffsiteRunner>(useMockData);

    public static RouteGroupBuilder MapOffsiteEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/host/offsite", ReadAsync);
        api.MapPost("/host/offsite", StartAsync);
        return api;
    }

    private static async Task<Results<Ok<OffsiteStatus>, ProblemHttpResult>> ReadAsync(IOffsiteRunner runner, CancellationToken ct)
    {
        try { return TypedResults.Ok(await runner.ReadAsync(ct)); }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }

    /// <summary>Not set up yet: nothing to start. Starting while one runs changes nothing: the one run goes on.</summary>
    private static async Task<Results<Ok<OffsiteStatus>, Conflict<ErrorResponse>, ProblemHttpResult>> StartAsync(
        IOffsiteRunner runner, CancellationToken ct)
    {
        try
        {
            if (!(await runner.ReadAsync(ct)).Configured)
                return TypedResults.Conflict(new ErrorResponse("the offsite store isn't set up yet (docs/deployment.md)"));
            await runner.StartAsync(ct);
            return TypedResults.Ok(await runner.ReadAsync(ct));
        }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }
}
