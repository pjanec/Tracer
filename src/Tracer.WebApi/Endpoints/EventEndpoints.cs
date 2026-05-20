using System.Text.RegularExpressions;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class EventEndpoints
{
    private static readonly Regex HexPattern = new Regex("^[0-9a-fA-F]{16}$", RegexOptions.Compiled);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/events/{eventId}", async (
            string eventId,
            EventLookupService svc,
            CancellationToken ct) =>
        {
            if (!HexPattern.IsMatch(eventId))
                return Results.BadRequest(new { error = "eventId must be a 16-character hexadecimal string" });

            var ev = await svc.GetByIdAsync(eventId, ct);
            return ev is null ? Results.NotFound() : Results.Ok(ev);
        }).WithName("GetEvent").WithOpenApi();
    }
}
