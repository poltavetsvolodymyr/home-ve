using HomeBackend.Api;
using HomeBackend.Features.Vms;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HomeBackend.Features.Isos;

/// <summary>
/// ISO images for installing VMs. Page: ISO images. Endpoints: <c>GET /api/isos</c>, <c>POST /api/isos</c>
/// (download by URL), <c>DELETE /api/isos/{name}</c> (delete, or cancel a download).
/// </summary>
public static class IsosFeature
{
    public static IServiceCollection AddIsosFeature(this IServiceCollection services) => services.AddSingleton<IsoStore>();

    public static RouteGroupBuilder MapIsoEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/isos", (IsoStore store, VmMonitor monitor) => store.List(monitor.Current.Select(v => v.Config).ToList()));
        api.MapPost("/isos", Results<Accepted, BadRequest<ErrorResponse>> (IsoDownloadRequest request, IsoStore store) =>
            store.StartDownload(request.Url, request.Name) is { } error
                ? TypedResults.BadRequest(new ErrorResponse(error))
                : TypedResults.Accepted((string?)null));
        api.MapDelete("/isos/{name}", Delete);
        return api;
    }

    private static Results<NoContent, NotFound, Conflict<ErrorResponse>> Delete(string name, IsoStore store, VmMonitor monitor)
    {
        var users = monitor.Current.Where(v => v.Config.Cdrom == name).Select(v => v.Name).ToList();
        if (users.Count > 0)
            return TypedResults.Conflict(new ErrorResponse($"{name} is in the CD drive of {string.Join(", ", users)}: eject it there first"));
        return store.Delete(name) ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
