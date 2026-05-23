using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Abstractions;

namespace Tracer.WebApi.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", ([FromServices] IAgentTransport? transport) =>
        {
            var health = transport?.GetHealth();
            return Results.Ok(new
            {
                status = "ok",
                sharedMemoryDropped = health?.TotalDropped ?? 0L,
                ingestChannelDepth = health?.PendingCount ?? 0,
            });
        })
           .WithName("GetHealth")
           .WithOpenApi();
    }
}

