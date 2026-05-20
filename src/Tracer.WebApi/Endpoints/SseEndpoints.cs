using System.Text.Json;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Contracts.Mapping;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Streaming;

namespace Tracer.WebApi.Endpoints;

public static class SseEndpoints
{
    public static void Map(WebApplication app)
    {
        // GET /api/live/notables — SSE stream of notable events
        app.MapGet("/api/live/notables", async (
            HttpContext context,
            bool? notablesOnly,
            string? sessionId,
            SseConnectionManager connectionManager,
            SseStreamingOptions options,
            CancellationToken ct) =>
        {
            var filter = new SseFilter(
                NotablesOnly: notablesOnly ?? true,
                SessionId: sessionId);

            var connection = connectionManager.TryRegister(filter);
            if (connection is null)
                return Results.StatusCode(503);

            var response = context.Response;
            response.Headers["Content-Type"] = "text/event-stream";
            response.Headers["Cache-Control"] = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";

            var requestAborted = context.RequestAborted;

            try
            {
                // Heartbeat task
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
                var heartbeatTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(options.HeartbeatInterval, cts.Token);
                            await response.WriteAsync(": keepalive\n\n", cts.Token);
                            await response.Body.FlushAsync(cts.Token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch { break; }
                    }
                }, cts.Token);

                await foreach (var ev in connection.ReadAsync(requestAborted))
                {
                    var dto = DtoMappers.ToNotableDto(ev);
                    var json = JsonSerializer.Serialize(dto);
                    await response.WriteAsync($"data: {json}\n\n", requestAborted);
                    await response.Body.FlushAsync(requestAborted);
                }

                cts.Cancel();
                try { await heartbeatTask; } catch { }
            }
            catch (OperationCanceledException) { }
            finally
            {
                connectionManager.Deregister(connection.Id);
            }

            return Results.Empty;
        }).WithName("GetLiveNotables").WithOpenApi();

        // GET /api/live/status — live ingestion/streaming status
        app.MapGet("/api/live/status", (
            ILiveStatusProvider statusProvider,
            SseConnectionManager connectionManager) =>
        {
            var lastEvent = statusProvider.LastEventUtc;
            var isHealthy = lastEvent.HasValue &&
                (DateTimeOffset.UtcNow - lastEvent.Value).TotalSeconds <= 60;

            var dto = new LiveStatusDto
            {
                IngestionHealthy = isHealthy,
                IngestedTotal = statusProvider.IngestedTotal,
                DroppedTotal = statusProvider.DroppedTotal,
                ActiveSseClients = connectionManager.ActiveCount,
                LastEventUtc = lastEvent,
            };
            return Results.Ok(dto);
        }).WithName("GetLiveStatus").WithOpenApi();
    }
}

