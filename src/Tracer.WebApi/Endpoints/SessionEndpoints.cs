using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/sessions", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            SessionQueryService svc,
            CancellationToken ct) =>
        {
            (DateTimeOffset From, DateTimeOffset To)? range = null;
            if (from.HasValue && to.HasValue)
                range = (from.Value, to.Value);

            var sessions = await svc.ListAsync(range, ct);
            return Results.Ok(sessions);
        }).WithName("GetSessions").WithOpenApi();

        app.MapGet("/api/sessions/{sessionId}", async (
            string sessionId,
            SessionQueryService svc,
            CancellationToken ct) =>
        {
            var session = await svc.GetAsync(sessionId, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        }).WithName("GetSession").WithOpenApi();
    }
}
