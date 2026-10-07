using HomeBackend.Api;
using HomeBackend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBackend.Features.Update;

/// <summary>
/// Updating home-ve (backend, frontend, VM tools) from the UI: <c>/opt/home-ve/deploy/update.sh</c>, run by systemd as home-update.service.
/// Page: Settings. Endpoints: <c>GET /api/host/update</c> (last run and its log), <c>POST /api/host/update</c> (start).
/// </summary>
public static class UpdateFeature
{
    public static IServiceCollection AddUpdateFeature(this IServiceCollection services, bool useMockData) =>
        services.AddDataSource<IUpdateRunner, SystemctlUpdateRunner, MockUpdateRunner>(useMockData);

    public static RouteGroupBuilder MapUpdateEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/host/update", ReadAsync);
        api.MapPost("/host/update", StartAsync);
        return api;
    }

    private static async Task<Results<Ok<UpdateStatus>, ProblemHttpResult>> ReadAsync(IUpdateRunner runner, CancellationToken ct)
    {
        try { return TypedResults.Ok(await runner.ReadAsync(ct)); }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }

    /// <summary>Starting it while it runs changes nothing: the one run goes on.</summary>
    private static async Task<Results<Ok<UpdateStatus>, ProblemHttpResult>> StartAsync(IUpdateRunner runner, CancellationToken ct)
    {
        try
        {
            await runner.StartAsync(ct);
            return TypedResults.Ok(await runner.ReadAsync(ct));
        }
        catch (Exception ex) when (HostCommandFailure.Is(ex)) { return HostCommandFailure.ToProblem(ex); }
    }
}
