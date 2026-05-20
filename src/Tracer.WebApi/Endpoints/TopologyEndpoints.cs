using Tracer.WebApi.Queries;

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
    }
}
