using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Util;

namespace Tracer.WebApi.Endpoints;

public static class TopologyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/topology", async (
            TopologyQueryService svc,
            CancellationToken ct) =>
        {
            var topology = await svc.GetAsync(ct);
            return Results.Ok(topology);
        }).WithName("GetTopology").WithOpenApi();

        app.MapGet("/api/topology/network", HandleNetworkAsync)
            .WithName("GetNetworkTopology")
            .WithOpenApi();
    }

    public static async Task<IResult> HandleNetworkAsync(
        [FromServices] IServiceProvider sp,
        [FromServices] NetworkTopologyService svc,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(svc);
        var gate = BundleModeGate.CheckBundleOrLive(sp);
        if (gate is not null) return gate;

        if (from is null || to is null)
            return Results.Problem(new ProblemDetails
            {
                Title = "Missing parameters",
                Detail = "from and to query parameters are required.",
                Status = StatusCodes.Status400BadRequest
            });

        var result = await svc.GetAsync(
            WallclockTime.FromDateTimeOffset(from.Value),
            WallclockTime.FromDateTimeOffset(to.Value),
            ct);

        return Results.Ok(NetworkTopologyDtoMapper.Map(result));
    }
}
