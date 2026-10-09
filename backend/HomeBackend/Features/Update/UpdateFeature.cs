using HomeBackend.Api;
using HomeBackend.Configuration;
using HomeBackend.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Update;

/// <summary>
/// Updating home-ve (backend, frontend, VM tools) from the UI: <c>/opt/home-ve/deploy/update.sh</c>, run by systemd as home-update.service.
/// Page: Settings. Endpoints: <c>GET /api/host/update</c> (last run and its log), <c>POST /api/host/update</c> (start),
/// <c>GET</c>/<c>PUT /api/host/update/channel</c> (stable or dev, and the version running now).
/// </summary>
public static class UpdateFeature
{
    public static IServiceCollection AddUpdateFeature(this IServiceCollection services, bool useMockData) =>
        services.AddDataSource<IUpdateRunner, SystemctlUpdateRunner, MockUpdateRunner>(useMockData);

    public static RouteGroupBuilder MapUpdateEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/host/update", ReadAsync);
        api.MapPost("/host/update", StartAsync);
        api.MapGet("/host/update/channel", ReadChannel);
        api.MapPut("/host/update/channel", WriteChannel);
        return api;
    }

    private static UpdateChannelInfo ReadChannel(IUpdateRunner runner, IOptions<HomeBackendOptions> options) =>
        new(UpdateChannel.Read(options.Value.DataDir), runner.ReadVersion());

    /// <summary>Takes effect at the next update; nothing is switched now.</summary>
    private static Results<Ok<UpdateChannelInfo>, BadRequest<ErrorResponse>> WriteChannel(
        UpdateChannelRequest request, IUpdateRunner runner, IOptions<HomeBackendOptions> options)
    {
        if (!UpdateChannel.IsValid(request.Channel))
            return TypedResults.BadRequest(new ErrorResponse($"the channel is '{UpdateChannel.Stable}' or '{UpdateChannel.Dev}'"));
        UpdateChannel.Write(options.Value.DataDir, request.Channel);
        return TypedResults.Ok(ReadChannel(runner, options));
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
