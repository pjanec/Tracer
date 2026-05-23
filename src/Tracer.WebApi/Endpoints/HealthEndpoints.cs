using Microsoft.AspNetCore.Mvc;
using Tracer.Agent.Upload;
using Tracer.Core.Abstractions;
using Tracer.WebApi.Streaming;

namespace Tracer.WebApi.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", (
            [FromServices] IAgentTransport? transport,
            [FromServices] SseConnectionManager? sseManager,
            [FromServices] UploadIntentDispatcher? uploadDispatcher) =>
        {
            var health = transport?.GetHealth();
            return Results.Ok(new
            {
                status = "ok",
                sharedMemoryDropped = health?.TotalDropped ?? 0L,
                ingestChannelDepth = health?.PendingCount ?? 0,
                sseConnectionsActive = sseManager?.ActiveCount ?? 0,
                intervalsAwaitingUpload = uploadDispatcher?.PendingCount ?? 0,
            });
        })
           .WithName("GetHealth")
           .WithOpenApi();
    }
}

