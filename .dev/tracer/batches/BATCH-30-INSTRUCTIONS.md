# BATCH-30 Instructions — TRC-P6-003 & TRC-P6-004

## Context

You are implementing Phase 6 trace DTO / endpoint tasks in `d:\Work\Tracer`:
- **TRC-P6-003**: Trace DTOs + `TraceDtoMapper` + 5 unit tests
- **TRC-P6-004**: `TraceEndpoints` + DI wiring + 8 unit tests

Build command: `dotnet build Tracer.sln -c Release --no-incremental`
Test command: `dotnet test tests\Tracer.Tests.Unit -c Release --no-build`

**Constraints:**
- `TreatWarningsAsErrors=true` — zero warnings
- `Nullable=enable`, `LangVersion=12`
- Use `DtoMappers.ToHex(EventId)` / `DtoMappers.ToHex(TraceId)` already in `Tracer.WebApi.Contracts.Mapping.DtoMappers` for ID formatting
- `JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)` pattern from existing DTOs in `Dtos.cs`

---

## TASK 1: TRC-P6-003 — Trace DTOs

### 1a. Create `src/Tracer.WebApi/Contracts/Dto/TraceDtos.cs`

```csharp
using System.Text.Json.Serialization;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record TraceTreeDto
{
    public required string TraceId { get; init; }
    public required IReadOnlyList<TraceNodeDto> Nodes { get; init; }
    public required IReadOnlyList<TraceEdgeDto> Edges { get; init; }
    public required IReadOnlyList<string> RootEventIds { get; init; }
    public required IReadOnlyList<string> LeafEventIds { get; init; }
    public required TraceSummaryDto Summary { get; init; }
}

public sealed record TraceNodeDto
{
    public required string EventId { get; init; }
    public required string TraceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentEventId { get; init; }
    public required DateTimeOffset PublishWallclock { get; init; }
    public required string PublisherNode { get; init; }
    public required string Topic { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Severity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NotableLabel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayloadJson { get; init; }
}

public sealed record TraceEdgeDto
{
    public required string ParentEventId { get; init; }
    public required string ChildEventId { get; init; }
    public required double LatencyMs { get; init; }
}

public sealed record TraceSummaryDto
{
    public required string TraceId { get; init; }
    public required int TotalEvents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalEventsAvailable { get; init; }
    public required bool Truncated { get; init; }
    public required double TotalSpanMs { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required int RootCount { get; init; }
    public required int LeafCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? FirstEventUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastEventUtc { get; init; }
}
```

### 1b. Create `src/Tracer.WebApi/Contracts/Mapping/TraceDtoMapper.cs`

```csharp
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Contracts.Mapping;

public static class TraceDtoMapper
{
    public static TraceTreeDto Map(TraceTree tree)
    {
        var nodes    = tree.Nodes.Select(MapNode).ToList();
        var edges    = tree.Edges.Select(MapEdge).ToList();
        var rootIds  = tree.Roots.Select(n => DtoMappers.ToHex(n.Event.EventId)).ToList();
        var leafIds  = tree.Leaves.Select(n => DtoMappers.ToHex(n.Event.EventId)).ToList();

        return new TraceTreeDto
        {
            TraceId      = tree.TraceId.ToString("X16"),
            Nodes        = nodes,
            Edges        = edges,
            RootEventIds = rootIds,
            LeafEventIds = leafIds,
            Summary      = Map(tree.Summary),
        };
    }

    public static TraceNodeDto MapNode(TraceNode node)
    {
        var ev = node.Event;
        return new TraceNodeDto
        {
            EventId        = DtoMappers.ToHex(ev.EventId),
            TraceId        = DtoMappers.ToHex(ev.TraceId),
            ParentEventId  = ev.ParentEventId.HasValue
                                 ? DtoMappers.ToHex(ev.ParentEventId.Value)
                                 : null,
            PublishWallclock = ev.PublishWallclock.ToDateTimeOffset(),
            PublisherNode  = ev.PublisherNode.Value,
            Topic          = ev.Topic.Value,
            EntityId       = ev.EntityId?.Value,
            Severity       = ev.Severity?.ToString(),
            NotableLabel   = ev.NotableLabel,
            PayloadJson    = ev.PayloadJson,
        };
    }

    public static TraceEdgeDto MapEdge(TraceEdge edge) => new()
    {
        ParentEventId = DtoMappers.ToHex(edge.ParentEventId),
        ChildEventId  = DtoMappers.ToHex(edge.ChildEventId),
        LatencyMs     = edge.LatencyMs,
    };

    public static TraceSummaryDto Map(TraceSummary summary) => new()
    {
        TraceId                = summary.TraceId.ToString("X16"),
        TotalEvents            = summary.TotalEvents,
        TotalEventsAvailable   = summary.Truncated ? summary.TotalEventsAvailable : null,
        Truncated              = summary.Truncated,
        TotalSpanMs            = summary.TotalSpanMs,
        ParticipatingNodes     = summary.ParticipatingNodes,
        RootCount              = summary.RootCount,
        LeafCount              = summary.LeafCount,
        FirstEventUtc          = summary.FirstEventUtc,
        LastEventUtc           = summary.LastEventUtc,
    };
}
```

**Important notes:**
- `EntityId` property: check what property `EntityId?` struct exposes. Look at `src/Tracer.Core/Identity/EntityId.cs`. If it exposes `.Value`, use `ev.EntityId?.Value`. If it's different, adjust.
- `TopicName.Value`: check `src/Tracer.Core/Identity/TopicName.cs` for the property name.
- `DtoMappers.ToHex(EventId)` and `DtoMappers.ToHex(TraceId)` already exist in `Tracer.WebApi.Contracts.Mapping.DtoMappers`.
- The `ParentEventId` on `EventRecord` is `EventId?` (nullable struct). Use `.HasValue` and `.Value`.

### 1c. Create `tests/Tracer.Tests.Unit/WebApi/TraceDtoMapperTests.cs`

```csharp
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Mapping;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class TraceDtoMapperTests
{
    private static readonly WallclockTime BaseTime =
        WallclockTime.FromUnixNanoseconds(
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentId = 0,
        string node = "node-a") =>
        new EventRecord
        {
            SequenceNumber   = eventId,
            PublishWallclock = BaseTime,
            ReceiveWallclock = BaseTime,
            PublisherNode    = new AgentId(node),
            SubscriberNode   = new AgentId(node),
            Topic            = new TopicName("test.topic"),
            EventId          = new EventId(eventId),
            TraceId          = new TraceId(traceId),
            ParentEventId    = parentId != 0 ? new EventId(parentId) : null,
            PayloadJson      = "{}",
        };

    private static TraceTree MakeTree(ulong traceId, params EventRecord[] events)
    {
        var nodes = events.Select(e => new TraceNode(e)).ToList();
        var nodeById = nodes.ToDictionary(n => n.Event.EventId.Value);
        var edges = new List<TraceEdge>();
        foreach (var node in nodes)
        {
            var parentId = node.Event.ParentEventId?.Value ?? 0;
            if (parentId == 0 || !nodeById.TryGetValue(parentId, out var parent)) continue;
            edges.Add(new TraceEdge(parent.Event.EventId, node.Event.EventId, 5.0));
        }
        var childSet = new HashSet<ulong>(edges.Select(e => e.ChildEventId.Value));
        var parentSet = new HashSet<ulong>(edges.Select(e => e.ParentEventId.Value));
        return new TraceTree
        {
            TraceId  = traceId,
            Nodes    = nodes,
            Edges    = edges,
            Roots    = nodes.Where(n => !childSet.Contains(n.Event.EventId.Value)).ToList(),
            Leaves   = nodes.Where(n => !parentSet.Contains(n.Event.EventId.Value)).ToList(),
            Summary  = new TraceSummary
            {
                TraceId            = traceId,
                TotalEvents        = events.Length,
                Truncated          = false,
                TotalSpanMs        = 0,
                ParticipatingNodes = new[] { "node-a" },
                RootCount          = 1,
                LeafCount          = 1,
            },
        };
    }

    [Fact]
    public void MapTraceTree_AllNodesProjected_EventIdIsUppercaseHex16()
    {
        ulong traceId  = 0xA1B2C3D4E5F60001UL;
        ulong event1Id = 0x00000000000000FFUL;
        ulong event2Id = 0x0000000000001000UL;

        var tree = MakeTree(traceId,
            MakeEvent(event1Id, traceId),
            MakeEvent(event2Id, traceId, event1Id));

        var dto = TraceDtoMapper.Map(tree);

        dto.Nodes.Should().HaveCount(2);
        foreach (var node in dto.Nodes)
        {
            node.EventId.Should().HaveLength(16, "EventId must be 16 chars");
            node.EventId.Should().MatchRegex("^[0-9A-F]{16}$", "EventId must be uppercase hex");
        }

        dto.Nodes.Should().Contain(n => n.EventId == "00000000000000FF");
        dto.Nodes.Should().Contain(n => n.EventId == "0000000000001000");
    }

    [Fact]
    public void MapTraceTree_RootNodes_HaveNullParentEventId()
    {
        ulong traceId  = 0xBBBBBBBBBBBBBBBBUL;
        ulong rootId   = 0x0000000000000001UL;
        ulong childId  = 0x0000000000000002UL;

        var tree = MakeTree(traceId,
            MakeEvent(rootId, traceId, parentId: 0),
            MakeEvent(childId, traceId, parentId: rootId));

        var dto = TraceDtoMapper.Map(tree);

        var rootDto  = dto.Nodes.Single(n => n.EventId == rootId.ToString("X16"));
        var childDto = dto.Nodes.Single(n => n.EventId == childId.ToString("X16"));

        rootDto.ParentEventId.Should().BeNull("root node has no parent");
        childDto.ParentEventId.Should().NotBeNull("child node has a parent");
        childDto.ParentEventId.Should().Be(rootId.ToString("X16"));
    }

    [Fact]
    public void MapTraceEdge_LatencyMs_RoundTripsAsDouble()
    {
        ulong traceId  = 0xCCCCCCCCCCCCCCCCUL;
        ulong parentId = 0x0000000000000001UL;
        ulong childId  = 0x0000000000000002UL;
        const double expectedLatency = 123.456789;

        var nodes = new[]
        {
            new TraceNode(MakeEvent(parentId, traceId)),
            new TraceNode(MakeEvent(childId, traceId, parentId)),
        };
        var edge = new TraceEdge(new EventId(parentId), new EventId(childId), expectedLatency);
        var tree = new TraceTree
        {
            TraceId  = traceId,
            Nodes    = nodes,
            Edges    = [edge],
            Roots    = [nodes[0]],
            Leaves   = [nodes[1]],
            Summary  = new TraceSummary
            {
                TraceId            = traceId,
                TotalEvents        = 2,
                Truncated          = false,
                TotalSpanMs        = 0,
                ParticipatingNodes = new[] { "node-a" },
                RootCount          = 1,
                LeafCount          = 1,
            },
        };

        var dto = TraceDtoMapper.Map(tree);

        dto.Edges.Should().HaveCount(1);
        dto.Edges[0].LatencyMs.Should().Be(expectedLatency, "latency must not be rounded");
    }

    [Fact]
    public void MapTraceSummary_WhenTruncated_TotalEventsAvailableIsNonNull()
    {
        var summary = new TraceSummary
        {
            TraceId            = 0xDDDDDDDDDDDDDDDDUL,
            TotalEvents        = 100,
            TotalEventsAvailable = 500,
            Truncated          = true,
            TotalSpanMs        = 1000,
            ParticipatingNodes = new[] { "node-a" },
            RootCount          = 1,
            LeafCount          = 5,
        };

        var dto = TraceDtoMapper.Map(summary);

        dto.Truncated.Should().BeTrue();
        dto.TotalEventsAvailable.Should().NotBeNull("truncated traces must include TotalEventsAvailable");
        dto.TotalEventsAvailable.Should().Be(500);
    }

    [Fact]
    public void MapTraceSummary_WhenNotTruncated_TotalEventsAvailableIsNull()
    {
        var summary = new TraceSummary
        {
            TraceId            = 0xEEEEEEEEEEEEEEEEUL,
            TotalEvents        = 50,
            TotalEventsAvailable = null,
            Truncated          = false,
            TotalSpanMs        = 500,
            ParticipatingNodes = new[] { "node-a" },
            RootCount          = 1,
            LeafCount          = 3,
        };

        var dto = TraceDtoMapper.Map(summary);

        dto.Truncated.Should().BeFalse();
        dto.TotalEventsAvailable.Should().BeNull("non-truncated traces must not include TotalEventsAvailable");
    }
}
```

---

## TASK 2: TRC-P6-004 — Trace API Endpoints

### 2a. Create `src/Tracer.WebApi/Endpoints/TraceEndpoints.cs`

The routes are:
- `GET /api/traces/{traceId}` → summary only (returns `TraceSummaryDto`)
- `GET /api/traces/{traceId}/tree` → full tree (returns `TraceTreeDto`)
- `GET /api/events/{eventId}/trace` → trace tree via event (returns `TraceTreeDto`)
- `GET /api/events/{eventId}/ancestors` → ancestor chain (returns `TraceTreeDto`)
- `GET /api/events/{eventId}/descendants` → descendants (returns `TraceTreeDto`)

IDs: parsed as 16-char uppercase hex. Invalid input → 400 ProblemDetails.
`maxEvents` clamped to `[1, 5000]`. `maxDepth` clamped to `[1, 100]`. `maxNodes` clamped to `[1, 5000]`.

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tracer.Core.Identity;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Contracts.Mapping;
using Tracer.WebApi.Queries;

namespace Tracer.WebApi.Endpoints;

public static class TraceEndpoints
{
    private static readonly Regex HexPattern =
        new Regex("^[0-9a-fA-F]{16}$", RegexOptions.Compiled);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/traces/{traceId}",              HandleGetTraceSummaryAsync)
           .WithName("GetTraceSummary").WithOpenApi();

        app.MapGet("/api/traces/{traceId}/tree",         HandleGetTraceTreeAsync)
           .WithName("GetTraceTree").WithOpenApi();

        app.MapGet("/api/events/{eventId}/trace",        HandleGetTraceByEventAsync)
           .WithName("GetTraceByEvent").WithOpenApi();

        app.MapGet("/api/events/{eventId}/ancestors",    HandleAncestorsAsync)
           .WithName("GetEventAncestors").WithOpenApi();

        app.MapGet("/api/events/{eventId}/descendants",  HandleDescendantsAsync)
           .WithName("GetEventDescendants").WithOpenApi();
    }

    internal static async Task<Results<Ok<TraceSummaryDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceSummaryAsync(
            string traceId,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(traceId))
            return TypedResults.Problem(BadHexDetail("traceId"), statusCode: 400);

        var id = ulong.Parse(traceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var tree = await traces.GetTraceTreeAsync(id, maxEvents: 1, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree.Summary));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceTreeAsync(
            string traceId,
            [FromQuery] int? maxEvents,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(traceId))
            return TypedResults.Problem(BadHexDetail("traceId"), statusCode: 400);

        var id  = ulong.Parse(traceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var cap = Math.Clamp(maxEvents ?? 1000, 1, 5000);
        var tree = await traces.GetTraceTreeAsync(id, cap, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceByEventAsync(
            string eventId,
            [FromQuery] int? maxEvents,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(eventId))
            return TypedResults.Problem(BadHexDetail("eventId"), statusCode: 400);

        var id  = ulong.Parse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var cap = Math.Clamp(maxEvents ?? 1000, 1, 5000);
        var tree = await traces.GetTraceTreeForEventAsync(new EventId(id), cap, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleAncestorsAsync(
            string eventId,
            [FromQuery] int? maxDepth,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(eventId))
            return TypedResults.Problem(BadHexDetail("eventId"), statusCode: 400);

        var id    = ulong.Parse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var depth = Math.Clamp(maxDepth ?? 50, 1, 100);
        var tree  = await traces.GetAncestorTreeAsync(new EventId(id), depth, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    internal static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleDescendantsAsync(
            string eventId,
            [FromQuery] int? maxDepth,
            [FromQuery] int? maxNodes,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!HexPattern.IsMatch(eventId))
            return TypedResults.Problem(BadHexDetail("eventId"), statusCode: 400);

        var id    = ulong.Parse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var depth = Math.Clamp(maxDepth ?? 30, 1, 100);
        var nodes = Math.Clamp(maxNodes ?? 1000, 1, 5000);
        var tree  = await traces.GetDescendantTreeAsync(new EventId(id), depth, nodes, ct);
        if (tree is null) return TypedResults.NotFound();

        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    private static string BadHexDetail(string field) =>
        $"{field} must be a 16-character hexadecimal string";
}
```

**Note on `HandleGetTraceSummaryAsync`**: Calling `GetTraceTreeAsync(id, maxEvents: 1, ct)` with `maxEvents=1` fetches only 1 event from DuckDB (just to confirm the trace exists). This avoids a separate count query. It's a lightweight existence check. The `TraceSummaryDto` it returns will show `TotalEvents=1` and `Truncated=true` if more events exist — this is acceptable for the summary-only endpoint (the client wanting full stats can call `/tree`). If this design feels wrong, use `maxEvents: 5000` to get more accurate stats.

### 2b. Wire `TraceQueryService` into `ObserverHostBuilder`

In `src/Tracer.Observer/ObserverHostBuilder.cs`, add `TraceQueryService` registration and `TraceEndpoints.Map(app)` call.

**Find the DI registration block** (around line 154-158):
```csharp
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
```
Add after them:
```csharp
        builder.Services.AddSingleton<EventAggregationService>();
        builder.Services.AddSingleton<TraceQueryService>();
```
(Note: `EventAggregationService` might already be there — check first. Only add `TraceQueryService` if missing.)

**Find the endpoint registration block** (around line 221-227):
```csharp
        HealthEndpoints.Map(app);
        SessionEndpoints.Map(app);
        EventEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        SseEndpoints.Map(app);
        BundleEndpoints.Map(app);
```
Add:
```csharp
        TraceEndpoints.Map(app);
```

### 2c. Wire `TraceQueryService` into `OfflineViewerHostBuilder`

In `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs`, similarly add registration and endpoint.

**Find the DI registration block** (around line 59-64):
```csharp
        builder.Services.AddSingleton<SessionQueryService>();
        builder.Services.AddSingleton<ScenarioQueryService>();
        builder.Services.AddSingleton<TopologyQueryService>();
        builder.Services.AddSingleton<EventLookupService>();
        builder.Services.AddSingleton<EventQueryService>();
        builder.Services.AddSingleton<EventAggregationService>();
```
Add after:
```csharp
        builder.Services.AddSingleton<TraceQueryService>();
```

**Find the endpoint registration block** (around line 92-97):
```csharp
        SessionEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        EventEndpoints.Map(app);
        SseEndpoints.Map(app);
        BundleOpenEndpoints.Map(app);
```
Add:
```csharp
        TraceEndpoints.Map(app);
```

### 2d. Wire `TraceQueryService` into `WebApiFixture`

In `src/Tracer.TestHarness/Observer/WebApiFixture.cs`, add `TraceQueryService` registration and endpoint mapping:

**Find the DI registration block** (around line 60-63):
```csharp
        builder.Services.AddSingleton<EventLookupService>();
        builder.Services.AddSingleton<EventQueryService>();
        builder.Services.AddSingleton<EventAggregationService>();
```
Add after:
```csharp
        builder.Services.AddSingleton<TraceQueryService>();
```

**Find the endpoint registration block** (around line 75-82):
```csharp
        HealthEndpoints.Map(app);
        SessionEndpoints.Map(app);
        TopologyEndpoints.Map(app);
        ScenarioEndpoints.Map(app);
        EventEndpoints.Map(app);
        SseEndpoints.Map(app);
```
Add:
```csharp
        TraceEndpoints.Map(app);
```

**Note**: `WebApiFixture` uses `NullIntervalSetTracker` → `LiveMultiIntervalReader` is not initialized. Tests against `WebApiFixture` that need data should use `ObserverFixture` instead.

---

## TASK 3: Unit Tests

### 3a. Create `tests/Tracer.Tests.Unit/WebApi/TraceEndpointsTests.cs`

Use `ObserverFixture` (via HTTP client) for data-dependent tests and `WebApiFixture` for pure routing/validation tests.

```csharp
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class TraceEndpointsTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 900_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentId = 0,
        DateTimeOffset? at = null) =>
        new EventRecord
        {
            SequenceNumber   = eventId,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode    = new AgentId("trace-node"),
            SubscriberNode   = new AgentId("trace-node"),
            Topic            = new TopicName("trace.endpoint.test"),
            EventId          = new EventId(eventId),
            TraceId          = new TraceId(traceId),
            ParentEventId    = parentId != 0 ? new EventId(parentId) : null,
            PayloadJson      = "{}",
        };

    public TraceEndpointsTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetTraceTree_ValidHexTraceId_Returns200WithNodesAndEdges()
    {
        // 5-event trace: root + 4 children
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 4; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId, BaseTime.AddSeconds(i + 1)));
        await _fixture.PushAsync(events);

        var url = $"/api/traces/{traceId:X16}/tree";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("nodes").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetTraceTree_InvalidHexId_Returns400ProblemDetails()
    {
        var response = await _fixture.Client.GetAsync("/api/traces/ZZZZZZZZZZZZZZZZ/tree");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("traceId");
    }

    [Fact]
    public async Task GetTraceTree_UnknownTraceId_Returns404()
    {
        // A trace ID that was never ingested
        var unknownTraceId = ulong.MaxValue - 42;
        var url = $"/api/traces/{unknownTraceId:X16}/tree";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTraceTree_MaxEventsExceeds5000_ClampedTo5000AndNoError()
    {
        // Seed a trace with 10 events; requesting 99999 should not fail
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 9; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId));
        await _fixture.PushAsync(events);

        var url = $"/api/traces/{traceId:X16}/tree?maxEvents=99999";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("nodes").GetArrayLength().Should().BeLessThanOrEqualTo(5000);
    }

    [Fact]
    public async Task GetAncestors_ValidEventId_Returns200WithAncestorChain()
    {
        // 3-level chain: root → mid → leaf
        var traceId = _nextId++;
        var rootId = _nextId++;
        var midId  = _nextId++;
        var leafId = _nextId++;
        await _fixture.PushAsync(new[]
        {
            MakeEvent(rootId, traceId, 0),
            MakeEvent(midId,  traceId, rootId, BaseTime.AddSeconds(1)),
            MakeEvent(leafId, traceId, midId,  BaseTime.AddSeconds(2)),
        });

        var url = $"/api/events/{leafId:X16}/ancestors";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tree = doc.RootElement;

        // Roots of an ancestor-only walk are the top-most events (the root)
        var rootEventIds = tree.GetProperty("rootEventIds").EnumerateArray().Select(x => x.GetString()).ToList();
        rootEventIds.Should().Contain(rootId.ToString("X16"),
            "the topmost ancestor should appear in rootEventIds");
    }

    [Fact]
    public async Task GetDescendants_ValidEventId_Returns200WithDescendantTree()
    {
        // root → [child1, child2]
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var child1  = _nextId++;
        var child2  = _nextId++;
        await _fixture.PushAsync(new[]
        {
            MakeEvent(rootId,  traceId, 0),
            MakeEvent(child1, traceId, rootId, BaseTime.AddSeconds(1)),
            MakeEvent(child2, traceId, rootId, BaseTime.AddSeconds(2)),
        });

        var url = $"/api/events/{rootId:X16}/descendants";
        var response = await _fixture.Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tree = doc.RootElement;

        // The leaf event IDs should have no outgoing edges in the response
        var leafEventIds = tree.GetProperty("leafEventIds").EnumerateArray()
            .Select(x => x.GetString()).ToHashSet();
        var edgeParents = tree.GetProperty("edges").EnumerateArray()
            .Select(e => e.GetProperty("parentEventId").GetString()).ToHashSet();

        leafEventIds.Intersect(edgeParents).Should().BeEmpty(
            "leaf events should not appear as parent in any edge");
    }

    [Fact]
    public async Task GetTraceTree_Under100Events_RespondsBefore300ms()
    {
        // Seed 50 events
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 49; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId));
        await _fixture.PushAsync(events);

        var url = $"/api/traces/{traceId:X16}/tree";

        // Warm up connection
        await _fixture.Client.GetAsync(url);

        var sw = Stopwatch.StartNew();
        var response = await _fixture.Client.GetAsync(url);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(300,
            "a 50-event trace tree should respond within 300 ms");
    }

    [Fact]
    public async Task GetAncestors_10DeepChain_WalkExpandsBefore200ms()
    {
        // 10-deep chain
        var traceId = _nextId++;
        var ids = new ulong[10];
        for (int i = 0; i < 10; i++) ids[i] = _nextId++;

        var events = new List<EventRecord> { MakeEvent(ids[0], traceId, 0) };
        for (int i = 1; i < 10; i++)
            events.Add(MakeEvent(ids[i], traceId, ids[i - 1], BaseTime.AddSeconds(i)));
        await _fixture.PushAsync(events);

        var leafId = ids[9];
        var url = $"/api/events/{leafId:X16}/ancestors?maxDepth=10";

        // Warm up
        await _fixture.Client.GetAsync(url);

        var sw = Stopwatch.StartNew();
        var response = await _fixture.Client.GetAsync(url);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "a 10-deep ancestor walk should respond within 200 ms");
    }
}
```

---

## Verification

```powershell
cd d:\Work\Tracer
dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 5
dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~TraceDtoMapperTests|FullyQualifiedName~TraceEndpointsTests" 2>&1 | Select-Object -Last 10
dotnet test tests\Tracer.Tests.Unit -c Release --no-build 2>&1 | Select-Object -Last 4
```

All 336+ existing unit tests must still pass. The new test classes (TraceDtoMapperTests: 5 tests, TraceEndpointsTests: 8 tests) must all pass. Total expected: 349+.

---

## Return in your report

1. All files created/modified (with paths)
2. Any fixes made (e.g. property name corrections for EntityId, TopicName)
3. Full build output (last 5 lines)
4. Test results for new test classes
5. Total unit test count
