using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class TriggerEvalEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/scenario/triggers", HandleAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<TriggerEvaluationListDto>, NotFound>> HandleAsync(
        [FromQuery] string sessionId,
        [FromServices] SessionQueryService sessions,
        [FromServices] TriggerEvalService triggerEvalService,
        CancellationToken ct,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? triggerId = null,
        [FromQuery] string? result = null,
        [FromQuery] int limit = 1000)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(triggerEvalService);

        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null)
            return TypedResults.NotFound();

        TriggerResult? parsedResult = null;
        if (result is not null)
        {
            if (result.Equals("fired", StringComparison.OrdinalIgnoreCase))
                parsedResult = TriggerResult.Fired;
            else if (result.Equals("not-fired", StringComparison.OrdinalIgnoreCase))
                parsedResult = TriggerResult.NotFired;
            // anything else → null (no 400)
        }

        var clampedLimit = Math.Clamp(limit, 1, 5000);

        var fromTime = WallclockTime.FromDateTimeOffset(from ?? session.StartUtc);
        var toTime = WallclockTime.FromDateTimeOffset(
            to ?? session.EndUtc ?? DateTimeOffset.UtcNow);

        var queryResult = await triggerEvalService.ListAsync(
            sessionId, fromTime, toTime, triggerId, parsedResult, clampedLimit, ct);

        return TypedResults.Ok(TriggerEvalDtoMapper.Map(queryResult));
    }
}
