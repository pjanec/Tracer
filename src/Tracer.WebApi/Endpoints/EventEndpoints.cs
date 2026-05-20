using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
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

        app.MapGet("/api/events", HandleListAsync)
            .WithName("ListEvents").WithOpenApi();

        app.MapGet("/api/events/aggregate", HandleAggregateAsync)
            .WithName("AggregateEvents").WithOpenApi();
    }

    internal static async Task<Results<Ok<EventListDto>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] string? sessionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string[]? topic,
        [FromQuery] string[]? node,
        [FromQuery] string? traceId,
        [FromQuery] string[]? entityId,
        [FromQuery] string[]? playerId,
        [FromQuery] string[]? severity,
        [FromServices] EventQueryService eventSvc,
        [FromServices] SessionQueryService sessionSvc,
        CancellationToken ct,
        [FromQuery] bool notablesOnly = false,
        [FromQuery] int limit = 5000,
        [FromQuery] string? orderBy = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return TypedResults.Problem("sessionId is required", statusCode: 400);

        if (limit < 1 || limit > 5000)
            return TypedResults.Problem("limit must be 1..5000", statusCode: 400);

        var sessionRange = await sessionSvc.GetSessionTimeRangeAsync(sessionId, ct);
        if (sessionRange is null)
            return TypedResults.Problem($"Session '{sessionId}' not found", statusCode: 404);

        var sessionFrom = from.HasValue
            ? WallclockTime.FromDateTimeOffset(from.Value)
            : sessionRange.Value.Start;

        var sessionTo = to.HasValue
            ? WallclockTime.FromDateTimeOffset(to.Value)
            : sessionRange.Value.End ?? WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var query = new EventQuery
        {
            SessionId = sessionId,
            From = sessionFrom,
            To = sessionTo,
            Topics = topic?.Length > 0 ? topic : null,
            Nodes = node?.Length > 0 ? node : null,
            TraceId = traceId,
            EntityIds = entityId?.Length > 0 ? entityId : null,
            PlayerIds = playerId?.Length > 0 ? playerId : null,
            Severities = severity?.Length > 0 ? severity : null,
            NotablesOnly = notablesOnly,
            Limit = limit,
            OrderDescending = string.Equals(orderBy, "desc", StringComparison.OrdinalIgnoreCase),
        };

        var result = await eventSvc.ListAsync(query, ct);

        var dto = new EventListDto
        {
            Events = result.Events,
            TotalMatching = result.TotalMatching,
            Returned = result.Returned,
            Truncated = result.Truncated,
        };

        return TypedResults.Ok(dto);
    }

    internal static async Task<Results<Ok<EventAggregateDto>, ProblemHttpResult>> HandleAggregateAsync(
        [FromQuery] string? sessionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? bucketDuration,
        [FromQuery] string? groupBy,
        [FromQuery] string[]? topic,
        [FromQuery] string[]? node,
        [FromQuery] string? traceId,
        [FromQuery] string[]? entityId,
        [FromQuery] string[]? playerId,
        [FromQuery] string[]? severity,
        [FromServices] EventAggregationService aggSvc,
        [FromServices] SessionQueryService sessionSvc,
        CancellationToken ct,
        [FromQuery] bool notablesOnly = false)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return TypedResults.Problem("sessionId is required", statusCode: 400);

        if (string.IsNullOrWhiteSpace(bucketDuration))
            return TypedResults.Problem("bucketDuration is required", statusCode: 400);

        // Validate bucket duration before the session lookup (fail fast on invalid inputs)
        var validBuckets = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            { "100ms", "1s", "5s", "30s", "1m", "5m", "30m", "1h" };
        if (!validBuckets.Contains(bucketDuration))
            return TypedResults.Problem(
                $"Invalid bucketDuration '{bucketDuration}'. Allowed values: {string.Join(", ", validBuckets.OrderBy(x => x))}",
                statusCode: 400);

        if (!from.HasValue || !to.HasValue)
            return TypedResults.Problem("Both 'from' and 'to' query parameters are required for aggregate queries", statusCode: 400);

        var sessionRange = await sessionSvc.GetSessionTimeRangeAsync(sessionId, ct);
        if (sessionRange is null)
            return TypedResults.Problem($"Session '{sessionId}' not found", statusCode: 404);

        var sessionFrom = from.HasValue
            ? WallclockTime.FromDateTimeOffset(from.Value)
            : sessionRange.Value.Start;

        var sessionTo = to.HasValue
            ? WallclockTime.FromDateTimeOffset(to.Value)
            : sessionRange.Value.End ?? WallclockTime.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var groupByEnum = groupBy?.ToLowerInvariant() switch
        {
            "topic"    => AggregateGroupBy.Topic,
            "severity" => AggregateGroupBy.Severity,
            "none"     => AggregateGroupBy.None,
            _          => AggregateGroupBy.Node,
        };

        var query = new AggregateQuery
        {
            SessionId = sessionId,
            From = sessionFrom,
            To = sessionTo,
            BucketDuration = bucketDuration,
            GroupBy = groupByEnum,
            Topics = topic?.Length > 0 ? topic : null,
            Nodes = node?.Length > 0 ? node : null,
            TraceId = traceId,
            EntityIds = entityId?.Length > 0 ? entityId : null,
            PlayerIds = playerId?.Length > 0 ? playerId : null,
            Severities = severity?.Length > 0 ? severity : null,
            NotablesOnly = notablesOnly,
        };

        AggregateResult result;
        try
        {
            result = await aggSvc.AggregateAsync(query, ct);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }

        var dto = new EventAggregateDto
        {
            BucketDuration = result.BucketDuration,
            Buckets = result.Buckets.Select(b => new EventAggregateBucketDto
            {
                BucketStartUtc = b.BucketStartUtc,
                Groups = b.Groups.Select(g => new EventAggregateBucketGroupDto
                {
                    GroupKey = g.GroupKey,
                    Count = g.Count,
                }).ToList(),
                Total = b.Total,
            }).ToList(),
        };

        return TypedResults.Ok(dto);
    }
}

