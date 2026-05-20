using System.Collections.Generic;
using System.Text.Json;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Contracts.Mapping;
using Tracer.WebApi.Lifecycle;
using Tracer.WebApi.Streaming;

namespace Tracer.WebApi.Endpoints;

public static class SseEndpoints
{
    private static readonly JsonSerializerOptions _sseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
            var filter = new SseFilter
            {
                NotablesOnly = notablesOnly ?? true,
                SessionId = sessionId,
            };

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
                    var json = JsonSerializer.Serialize(dto, _sseJsonOptions);
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

        // GET /api/live/events — SSE stream of all (optionally filtered) events
        app.MapGet("/api/live/events", async (
            HttpContext context,
            SseConnectionManager connectionManager,
            SseStreamingOptions options,
            CancellationToken ct,
            [Microsoft.AspNetCore.Mvc.FromQuery] string? sessionId,
            [Microsoft.AspNetCore.Mvc.FromQuery] string[]? topic,
            [Microsoft.AspNetCore.Mvc.FromQuery] string[]? node,
            [Microsoft.AspNetCore.Mvc.FromQuery] string? traceId,
            [Microsoft.AspNetCore.Mvc.FromQuery] string[]? entityId,
            [Microsoft.AspNetCore.Mvc.FromQuery] string[]? playerId,
            [Microsoft.AspNetCore.Mvc.FromQuery] string[]? severity,
            [Microsoft.AspNetCore.Mvc.FromQuery] bool notablesOnly = false) =>
        {
            var filter = new SseFilter
            {
                SessionId = sessionId,
                Topics = topic?.Length > 0 ? new HashSet<string>(topic, StringComparer.Ordinal) : null,
                Nodes = node?.Length > 0 ? new HashSet<string>(node, StringComparer.Ordinal) : null,
                TraceId = traceId,
                EntityIds = entityId?.Length > 0 ? new HashSet<string>(entityId, StringComparer.Ordinal) : null,
                PlayerIds = playerId?.Length > 0 ? new HashSet<string>(playerId, StringComparer.Ordinal) : null,
                Severities = severity?.Length > 0 ? new HashSet<string>(severity, StringComparer.Ordinal) : null,
                NotablesOnly = notablesOnly,
            };

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
                    var dto = DtoMappers.ToDto(ev);
                    var json = JsonSerializer.Serialize(dto, _sseJsonOptions);
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
        }).WithName("GetLiveEvents").WithOpenApi();

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

