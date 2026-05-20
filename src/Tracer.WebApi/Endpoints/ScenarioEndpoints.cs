using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class ScenarioEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/scenario/notables", async (
            [Microsoft.AspNetCore.Mvc.FromQuery] string sessionId,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
            [Microsoft.AspNetCore.Mvc.FromQuery] string? before,
            ScenarioQueryService svc,
            CancellationToken ct) =>
        {
            var effectiveLimit = limit ?? DefaultLimit;
            if (effectiveLimit < 1 || effectiveLimit > MaxLimit)
                return Results.BadRequest(new { error = $"limit must be between 1 and {MaxLimit}" });

            DateTimeOffset? beforeTime = null;
            if (before is not null)
            {
                if (!ulong.TryParse(before, System.Globalization.NumberStyles.HexNumber, null, out var beforeId))
                    return Results.BadRequest(new { error = "before must be a valid event ID (hex)" });
                beforeTime = await svc.GetEventTimestampAsync(beforeId, ct);
            }

            var notables = await svc.GetNotablesAsync(sessionId, effectiveLimit, beforeTime, ct);
            return Results.Ok(notables);
        }).WithName("GetScenarioNotables").WithOpenApi();

        app.MapGet("/api/scenario/phases", async (
            [Microsoft.AspNetCore.Mvc.FromQuery] string sessionId,
            ScenarioQueryService svc,
            CancellationToken ct) =>
        {
            var phases = await svc.GetPhasesAsync(sessionId, ct);
            return Results.Ok(phases);
        }).WithName("GetScenarioPhases").WithOpenApi();

        app.MapGet("/api/scenario/state", async (
            [Microsoft.AspNetCore.Mvc.FromQuery] string sessionId,
            ScenarioQueryService svc,
            CancellationToken ct) =>
        {
            var state = await svc.GetCurrentStateAsync(sessionId, ct);
            return state is null ? Results.NotFound() : Results.Ok(state);
        }).WithName("GetScenarioState").WithOpenApi();
    }
}
