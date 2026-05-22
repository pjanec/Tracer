using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;
using Tracer.WebApi.Util;

namespace Tracer.WebApi.Endpoints;

public static class GapEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/gaps", HandleAsync)
            .WithName("GetGaps")
            .WithOpenApi();
    }

    public static async Task<IResult> HandleAsync(
        [FromServices] IServiceProvider sp,
        [FromServices] GapDetectionService svc,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? topic = null,
        [FromQuery] string? publisherNode = null,
        [FromQuery] string? subscriberNode = null,
        [FromQuery] int limit = 500)
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

        var query = new GapDetectionQuery
        {
            From = WallclockTime.FromDateTimeOffset(from.Value),
            To = WallclockTime.FromDateTimeOffset(to.Value),
            Topic = topic,
            PublisherNode = publisherNode,
            SubscriberNode = subscriberNode,
            Limit = Math.Clamp(limit, 1, 5000),
        };

        var result = await svc.GetGapsAsync(query, ct);
        return Results.Ok(GapDtoMapper.Map(result));
    }
}
