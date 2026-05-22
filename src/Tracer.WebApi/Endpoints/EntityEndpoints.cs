using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.Parquet;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class EntityEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/entities", HandleListAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/summary", HandleSummaryAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/events", HandleEventsAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/slow-state", HandleSlowStateAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/fast-state/topics", HandleFastStateTopicsAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/fast-state/{topic}/schema", HandleFastStateSchemaAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/fast-state/{topic}", HandleFastStateAsync).WithOpenApi();
    }

    internal static async Task<Results<Ok<EntityListDto>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] string sessionId,
        [FromQuery] string? topic,
        [FromQuery] string? playerId,
        [FromServices] EntityDiscoveryService discovery,
        [FromServices] SessionQueryService sessions,
        CancellationToken ct,
        [FromQuery] int limit = 200)
    {
        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null)
            return TypedResults.Problem(new ProblemDetails { Title = "Session not found", Status = 404 });

        var entities = await discovery.DiscoverAsync(
            sessionId,
            WallclockTime.FromDateTimeOffset(session.StartUtc),
            WallclockTime.FromDateTimeOffset(session.EndUtc ?? DateTimeOffset.UtcNow),
            topic,
            playerId,
            Math.Clamp(limit, 1, 5000),
            ct);

        return TypedResults.Ok(new EntityListDto
        {
            Entities = entities.Select(EntityDtoMapper.Map).ToList(),
            Count = entities.Count,
        });
    }

    internal static async Task<Results<Ok<EntitySummaryDto>, NotFound>> HandleSummaryAsync(
        string entityId,
        [FromQuery] string sessionId,
        [FromServices] EntityDiscoveryService discovery,
        [FromServices] SessionQueryService sessions,
        CancellationToken ct)
    {
        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null) return TypedResults.NotFound();

        var entities = await discovery.DiscoverAsync(
            sessionId,
            WallclockTime.FromDateTimeOffset(session.StartUtc),
            WallclockTime.FromDateTimeOffset(session.EndUtc ?? DateTimeOffset.UtcNow),
            null, null, 5000, ct);

        var match = entities.FirstOrDefault(e => e.EntityId == entityId);
        return match is null ? TypedResults.NotFound() : TypedResults.Ok(EntityDtoMapper.Map(match));
    }

    internal static async Task<Ok<EntityEventsDto>> HandleEventsAsync(
        string entityId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromServices] EntityEventsService events,
        CancellationToken ct,
        [FromQuery] int limit = 5000)
    {
        var result = await events.GetEventsAsync(
            entityId,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            Math.Clamp(limit, 1, 5000),
            ct);
        return TypedResults.Ok(EntityEventsDtoMapper.Map(result));
    }

    internal static async Task<Ok<EntitySlowStateDto>> HandleSlowStateAsync(
        string entityId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string[]? topic,
        [FromServices] EntitySlowStateService slowState,
        CancellationToken ct)
    {
        var result = await slowState.GetAsync(
            entityId,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            topic,
            ct);
        return TypedResults.Ok(EntitySlowStateDtoMapper.Map(result));
    }

    internal static Task<Ok<IReadOnlyList<string>>> HandleFastStateTopicsAsync(
        string entityId,
        [FromServices] EntityFastStateService fastState)
    {
        var topics = fastState.GetAvailableTopics(entityId);
        return Task.FromResult(TypedResults.Ok(topics));
    }

    internal static async Task<Results<Ok<FastStateTopicSchemaDto>, NotFound>> HandleFastStateSchemaAsync(
        string entityId,
        string topic,
        [FromServices] EntityFastStateService fastState,
        CancellationToken ct)
    {
        var schema = await fastState.GetSchemaAsync(entityId, topic, ct);
        return schema is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(FastStateSchemaDtoMapper.Map(schema));
    }

    internal static async Task<Results<Ok<EntityFastStateDto>, ProblemHttpResult>> HandleFastStateAsync(
        string entityId,
        string topic,
        [FromQuery] string[]? column,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromServices] EntityFastStateService fastState,
        CancellationToken ct,
        [FromQuery] int maxSamples = 5000)
    {
        if (column is null || column.Length == 0)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Missing column",
                Detail = "At least one column is required.",
                Status = 400,
            });

        if (maxSamples < 10 || maxSamples > 10_000)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "maxSamples out of range",
                Detail = "maxSamples must be between 10 and 10000.",
                Status = 400,
            });

        var result = await fastState.ReadAsync(
            entityId,
            topic,
            column,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            maxSamples,
            ct);

        return TypedResults.Ok(EntityFastStateDtoMapper.Map(result));
    }

    // ── DTO Mappers ───────────────────────────────────────────────────────────

    private static class EntityDtoMapper
    {
        public static EntitySummaryDto Map(EntitySummary e) => new EntitySummaryDto
        {
            EntityId = e.EntityId,
            FirstSeenUtc = e.FirstSeenUtc,
            LastSeenUtc = e.LastSeenUtc,
            EventCount = e.EventCount,
            SamplePlayerId = e.SamplePlayerId,
            Topics = e.Topics,
        };
    }

    private static class EntityEventsDtoMapper
    {
        public static EntityEventsDto Map(EntityEventsResult result) => new EntityEventsDto
        {
            EntityId = result.EntityId,
            Events = result.Events.Select(MapEvent).ToList(),
            Truncated = result.Truncated,
        };

        private static EventDto MapEvent(EventRecord ev) => new EventDto
        {
            EventId = ev.EventId.ToString(),
            TraceId = ev.TraceId.ToString(),
            ParentEventId = ev.ParentEventId?.ToString(),
            SequenceNumber = (long)ev.SequenceNumber,
            OccurredAtUtc = ev.PublishWallclock.ToDateTimeOffset(),
            PublisherNode = ev.PublisherNode.Value,
            SubscriberNode = ev.SubscriberNode.Value,
            Topic = ev.Topic.Value,
            EntityId = ev.EntityId?.Value,
            OwningPlayerId = ev.OwningPlayerId,
            ScenarioPhase = ev.ScenarioPhase,
            Severity = ev.Severity?.ToString(),
            NotableLabel = ev.NotableLabel,
            PayloadJson = ev.PayloadJson,
        };
    }

    private static class EntitySlowStateDtoMapper
    {
        public static EntitySlowStateDto Map(EntitySlowStateResult result) => new EntitySlowStateDto
        {
            EntityId = result.EntityId,
            ByTopic = result.ByTopic.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<SlowStateSampleDto>)kv.Value.Select(MapSample).ToList()),
        };

        private static SlowStateSampleDto MapSample(SlowStateSample s) => new SlowStateSampleDto
        {
            Topic = s.Topic,
            PublishWallclock = s.PublishWallclock.ToDateTimeOffset(),
            PayloadJson = s.PayloadJson,
            TraceId = s.TraceId == 0UL ? null : new TraceId(s.TraceId).ToString(),
        };
    }

    private static class FastStateSchemaDtoMapper
    {
        public static FastStateTopicSchemaDto Map(FastStateTopicSchema schema) => new FastStateTopicSchemaDto
        {
            EntityId = schema.EntityId,
            Topic = schema.Topic,
            Columns = schema.Columns.Select(c => new FastStateColumnDto
            {
                Name = c.Name,
                DuckType = c.DuckType,
                IsNumeric = c.IsNumeric,
            }).ToList(),
        };
    }

    private static class EntityFastStateDtoMapper
    {
        public static EntityFastStateDto Map(EntityFastStateResult result) => new EntityFastStateDto
        {
            EntityId = result.EntityId,
            Topic = result.Topic,
            Columns = result.Columns,
            Samples = result.Samples.Select(s => new FastStateSampleDto
            {
                PublishWallclock = s.PublishWallclock.ToDateTimeOffset(),
                Values = s.Values,
            }).ToList(),
            TotalSamples = result.TotalSamples,
            Downsampled = result.Downsampled,
        };
    }
}
