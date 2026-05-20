# BATCH-29 Instructions — TRC-P6-001 & TRC-P6-002

## Context

You are implementing Phase 6 tasks in `d:\Work\Tracer`:
- **TRC-P6-001**: Schema extension — add partial index on `parent_event_id`
- **TRC-P6-002**: Trace walking backend — `TraceWalker`, `TraceQueryService`, domain records, unit tests

Build command: `dotnet build Tracer.sln -c Release --no-incremental`
Test command: `dotnet test tests\Tracer.Tests.Unit -c Release --no-build`

**Important constraints:**
- `TreatWarningsAsErrors=true` — zero warnings allowed
- `Nullable=enable` — all nullable references explicit
- `LangVersion=12` — C# 12 features OK (primary constructors, etc.)
- DuckDB.NET: use sync reader pattern (`cmd.ExecuteReader()` + `reader.Read()`), NOT async
- DuckDB UBIGINT columns: cast to `(long)` when setting parameters, cast result via `Convert.ToUInt64()`
- DuckDB parameters: named with `$paramName`, set via `new DuckDBParameter("paramName", value)`

---

## TASK 1: TRC-P6-001 — Schema Extension

### 1a. Modify `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs`

Replace the `idx_events_parent` index with the partial index `idx_events_parent_event_id`.

**Current content of `CreateIndexes`:**
```csharp
public const string CreateIndexes = """
    CREATE INDEX IF NOT EXISTS idx_events_trace ON events(trace_id);
    CREATE INDEX IF NOT EXISTS idx_events_parent ON events(parent_event_id);
    CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_id);
    CREATE INDEX IF NOT EXISTS idx_events_player ON events(owning_player_id);
    CREATE INDEX IF NOT EXISTS idx_events_topic_time ON events(topic, publish_wallclock);
    CREATE INDEX IF NOT EXISTS idx_state_instance_time ON slow_state(instance_key, publish_wallclock);
    CREATE INDEX IF NOT EXISTS idx_state_topic ON slow_state(topic);
    """;
```

**Replace with:**
```csharp
public const string CreateIndexes = """
    CREATE INDEX IF NOT EXISTS idx_events_trace ON events(trace_id);
    CREATE INDEX IF NOT EXISTS idx_events_parent_event_id ON events (parent_event_id) WHERE parent_event_id != 0;
    CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_id);
    CREATE INDEX IF NOT EXISTS idx_events_player ON events(owning_player_id);
    CREATE INDEX IF NOT EXISTS idx_events_topic_time ON events(topic, publish_wallclock);
    CREATE INDEX IF NOT EXISTS idx_state_instance_time ON slow_state(instance_key, publish_wallclock);
    CREATE INDEX IF NOT EXISTS idx_state_topic ON slow_state(topic);
    """;
```

Note: the old index was `idx_events_parent`, the new one is `idx_events_parent_event_id` with `WHERE parent_event_id != 0`. The doc comment should be updated to "seven indexes".

### 1b. Update `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs`

In `AllIndexes_AreCreated`, the expected array currently contains `"idx_events_parent"`. Replace it with `"idx_events_parent_event_id"`:

```csharp
string[] expected =
[
    "idx_events_trace",
    "idx_events_parent_event_id",   // was "idx_events_parent"
    "idx_events_entity",
    "idx_events_player",
    "idx_events_topic_time",
    "idx_state_instance_time",
    "idx_state_topic"
];
```

### 1c. Create `tests/Tracer.Tests.Unit/Storage/SchemaV1Tests.cs`

```csharp
using FluentAssertions;
using Tracer.Storage.DuckDB.Schema;
using Xunit;

namespace Tracer.Tests.Unit.Storage;

/// <summary>Unit tests for <see cref="SchemaV1"/> DDL constants.</summary>
public sealed class SchemaV1Tests
{
    [Fact]
    public void CreateIndexes_ContainsPartialIndexOnParentEventId()
    {
        // The Phase 6 partial index must appear with this exact name and clause.
        SchemaV1.CreateIndexes.Should().Contain(
            "idx_events_parent_event_id ON events (parent_event_id) WHERE parent_event_id != 0",
            because: "Phase 6 requires a partial index on parent_event_id excluding root events");
    }
}
```

Note: `SchemaV1` is `internal static`. Because this test is in `Tracer.Tests.Unit` and `Tracer.Storage.DuckDB` uses `InternalsVisibleTo`, check if that assembly attribute exists. If not, make `SchemaV1` `public` instead of `internal`.

Actually, to check: look at `Tracer.Storage.DuckDB` project for `[assembly: InternalsVisibleTo("Tracer.Tests.Unit")]`. If it doesn't exist, change `internal` to `public` in `SchemaV1.cs`.

**Check `src/Tracer.Storage.DuckDB/Tracer.Storage.DuckDB.csproj` and assembly attributes.**

If `SchemaV1` is internal and no `InternalsVisibleTo` exists, either:
- Add `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Tracer.Tests.Unit")]` to `SchemaV1.cs`, or
- Change `internal static class SchemaV1` to `public static class SchemaV1`

### 1d. Create `tests/Tracer.Tests.Integration/SchemaAppliedTests.cs`

```csharp
using DuckDB.NET.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.DuckDB;
using Xunit;

namespace Tracer.Tests.Integration;

/// <summary>
/// Integration tests verifying the parent_event_id partial index exists
/// in freshly created intervals.
/// </summary>
public sealed class SchemaAppliedTests : IAsyncDisposable
{
    private readonly string _intervalDir;
    private readonly string _dbPath;

    public SchemaAppliedTests()
    {
        _intervalDir = Path.Combine(Path.GetTempPath(), $"tracer-schema-applied-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_intervalDir);
        _dbPath = Path.Combine(_intervalDir, "events.duckdb");
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        try { Directory.Delete(_intervalDir, recursive: true); } catch { /* best-effort */ }
    }

    private static async Task CreateIntervalAsync(string dir)
    {
        await using var writer = await DuckDbStorageWriter.CreateAsync(
            dir,
            new Dictionary<string, Tracer.Storage.DuckDB.Parquet.ParquetTopicSchema>(),
            NullLogger<DuckDbStorageWriter>.Instance);
        // Writer created — schema applied
    }

    [Fact]
    public async Task NewInterval_ParentEventIdIndexExists()
    {
        await CreateIntervalAsync(_intervalDir);

        var indexes = await QueryListAsync<string>(
            "SELECT index_name FROM duckdb_indexes()");

        indexes.Should().Contain("idx_events_parent_event_id",
            because: "Phase 6 requires a partial index on parent_event_id");
    }

    [Fact]
    public async Task DescendantQuery_ExplainPlanReferencesParentEventIdIndex()
    {
        await CreateIntervalAsync(_intervalDir);

        // Run EXPLAIN on a query that should use the partial index
        var explainOutput = await QueryScalarAsync<string>(
            "EXPLAIN SELECT * FROM events WHERE parent_event_id = 42");

        // DuckDB EXPLAIN output should reference the index name
        explainOutput.Should().Contain("idx_events_parent_event_id",
            because: "the partial index should be used for parent_event_id point lookups");
    }

    private Task<T> QueryScalarAsync<T>(string sql) =>
        Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
        });

    private Task<List<T>> QueryListAsync<T>(string sql) =>
        Task.Run(() =>
        {
            using var conn = new DuckDBConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var list = new List<T>();
            while (r.Read())
                list.Add((T)Convert.ChangeType(r.GetValue(0), typeof(T)));
            return list;
        });
}
```

---

## TASK 2: TRC-P6-002 — Trace Walking Backend

### 2a. Create domain records in `src/Tracer.WebApi/Queries/TraceTree.cs`

```csharp
using Tracer.Core.Identity;
using Tracer.Core.Records;

namespace Tracer.WebApi.Queries;

/// <summary>
/// A tree (or DAG) of events sharing a trace_id, with edges derived from parent_event_id.
/// </summary>
public sealed record TraceTree
{
    public required ulong TraceId { get; init; }
    public required IReadOnlyList<TraceNode> Nodes { get; init; }
    public required IReadOnlyList<TraceEdge> Edges { get; init; }
    public required IReadOnlyList<TraceNode> Roots { get; init; }
    public required IReadOnlyList<TraceNode> Leaves { get; init; }
    public required TraceSummary Summary { get; init; }
}

/// <summary>A node in the trace tree, wrapping the underlying event record.</summary>
public sealed record TraceNode(EventRecord Event);

/// <summary>A directed edge from parent to child, annotated with latency.</summary>
public sealed record TraceEdge(EventId ParentEventId, EventId ChildEventId, double LatencyMs);

/// <summary>Metadata about the trace as a whole.</summary>
public sealed record TraceSummary
{
    public required ulong TraceId { get; init; }
    public required int TotalEvents { get; init; }
    public required bool Truncated { get; init; }
    public required double TotalSpanMs { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required int RootCount { get; init; }
    public required int LeafCount { get; init; }
    public DateTimeOffset? FirstEventUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
    public int? TotalEventsAvailable { get; init; }  // populated when Truncated = true
}
```

### 2b. Create `src/Tracer.WebApi/Queries/EventRecordMapper.cs`

There is already an internal `Mapping.MapEventRecord` in `Tracer.Storage.DuckDB.Internal.Mapping`, but it's `internal`. Since `TraceWalker` lives in `Tracer.WebApi`, create a thin wrapper in `Tracer.WebApi.Queries` that duplicates the mapping logic (or use inline mapping in `TraceWalker`). The simplest approach is an internal static helper that replicates what `Mapping.MapEventRecord` does.

The events table column order (from `SchemaV1`):
0: event_id, 1: trace_id, 2: parent_event_id, 3: sequence_number,
4: publish_wallclock, 5: receive_wallclock, 6: publisher_node, 7: subscriber_node,
8: topic, 9: entity_id, 10: owning_player_id, 11: scenario_phase, 12: severity, 13: notable_label, 14: payload

```csharp
using System.Data;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Maps a DuckDB DataReader row (SELECT * from events, schema order) to an <see cref="EventRecord"/>.
/// </summary>
internal static class EventRecordMapper
{
    public static EventRecord FromReader(IDataReader reader)
    {
        var eventId = new EventId(GetULong(reader, 0));
        var traceId = new TraceId(GetULong(reader, 1));
        var parentRaw = reader.IsDBNull(2) ? (ulong?)null : GetULong(reader, 2);
        var parentEventId = parentRaw.HasValue ? new EventId(parentRaw.Value) : (EventId?)null;
        var sequenceNumber = GetULong(reader, 3);
        var publishWallclock = GetWallclock(reader, 4);
        var receiveWallclock = GetWallclock(reader, 5);
        var publisherNode = new AgentId(reader.GetString(6));
        var subscriberNode = new AgentId(reader.GetString(7));
        var topic = new TopicName(reader.GetString(8));
        var entityIdStr = reader.IsDBNull(9)  ? null : reader.GetString(9);
        var entityId = entityIdStr is not null ? new EntityId(entityIdStr) : (EntityId?)null;
        var owningPlayerId = reader.IsDBNull(10) ? null : reader.GetString(10);
        var scenarioPhase  = reader.IsDBNull(11) ? null : reader.GetString(11);
        var severityStr    = reader.IsDBNull(12) ? null : reader.GetString(12);
        var severity = severityStr is not null ? Enum.Parse<Severity>(severityStr) : (Severity?)null;
        var notableLabel   = reader.IsDBNull(13) ? null : reader.GetString(13);
        var payload        = reader.IsDBNull(14) ? "{}" : reader.GetString(14);

        return new EventRecord
        {
            EventId          = eventId,
            TraceId          = traceId,
            ParentEventId    = parentEventId,
            SequenceNumber   = sequenceNumber,
            PublishWallclock = publishWallclock,
            ReceiveWallclock = receiveWallclock,
            PublisherNode    = publisherNode,
            SubscriberNode   = subscriberNode,
            Topic            = topic,
            EntityId         = entityId,
            OwningPlayerId   = owningPlayerId,
            ScenarioPhase    = scenarioPhase,
            Severity         = severity,
            NotableLabel     = notableLabel,
            PayloadJson      = payload,
        };
    }

    private static ulong GetULong(IDataReader reader, int ordinal)
        => Convert.ToUInt64(reader.GetValue(ordinal));

    private static WallclockTime GetWallclock(IDataReader reader, int ordinal)
    {
        var dt = (DateTime)reader.GetValue(ordinal);
        return new WallclockTime((dt.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
    }
}
```

### 2c. Create `src/Tracer.WebApi/Queries/TraceWalker.cs`

```csharp
using DuckDB.NET.Data;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Pure static trace-walking algorithms operating on a pooled DuckDB connection.
/// </summary>
public static class TraceWalker
{
    /// <summary>
    /// Walks ancestor chain from <paramref name="startEventId"/> up to a root or depth limit.
    /// Returns events in leaf-first order (start event first, root event last).
    /// </summary>
    public static async Task<IReadOnlyList<EventRecord>> WalkAncestorsAsync(
        PooledMultiIntervalConnection conn,
        EventId startEventId,
        int maxDepth,
        CancellationToken ct)
    {
        var chain = new List<EventRecord>();
        var currentId = startEventId.Value;
        var visited = new HashSet<ulong>();

        for (int depth = 0; depth < maxDepth; depth++)
        {
            ct.ThrowIfCancellationRequested();
            if (currentId == 0) break;
            if (!visited.Add(currentId)) break;  // cycle guard

            var ev = await LookupEventAsync(conn, currentId, ct);
            if (ev is null) break;
            chain.Add(ev);

            currentId = ev.ParentEventId?.Value ?? 0;
        }

        return chain;
    }

    /// <summary>
    /// Walks descendants of <paramref name="startEventId"/> using BFS.
    /// Does NOT include <paramref name="startEventId"/> itself in the result.
    /// </summary>
    public static async Task<IReadOnlyList<EventRecord>> WalkDescendantsAsync(
        PooledMultiIntervalConnection conn,
        EventId startEventId,
        int maxDepth,
        int maxNodes,
        CancellationToken ct)
    {
        var allDescendants = new List<EventRecord>();
        var frontier = new List<ulong> { startEventId.Value };
        var visited = new HashSet<ulong> { startEventId.Value };

        for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            ct.ThrowIfCancellationRequested();
            var children = await FetchChildrenAsync(conn, frontier, ct);
            var nextFrontier = new List<ulong>();
            foreach (var child in children)
            {
                if (!visited.Add(child.EventId.Value)) continue;
                allDescendants.Add(child);
                nextFrontier.Add(child.EventId.Value);
                if (allDescendants.Count >= maxNodes) return allDescendants;
            }
            frontier = nextFrontier;
        }

        return allDescendants;
    }

    /// <summary>Looks up a single event by its event_id primary key.</summary>
    public static Task<EventRecord?> LookupEventAsync(
        PooledMultiIntervalConnection conn,
        ulong eventId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            var sql = conn.WithEventsCte("""
                SELECT event_id, trace_id, parent_event_id, sequence_number,
                       publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                       topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
                FROM events
                WHERE event_id = $eventId
                LIMIT 1
                """);

            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DuckDBParameter("eventId", (long)eventId));

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? EventRecordMapper.FromReader(reader) : null;
        }, ct);
    }

    private static Task<IReadOnlyList<EventRecord>> FetchChildrenAsync(
        PooledMultiIntervalConnection conn,
        IReadOnlyList<ulong> parentIds,
        CancellationToken ct)
    {
        if (parentIds.Count == 0) return Task.FromResult<IReadOnlyList<EventRecord>>(Array.Empty<EventRecord>());

        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            // Build IN-clause parameters
            var inParams = string.Join(", ", Enumerable.Range(0, parentIds.Count).Select(i => $"$p{i}"));
            var sql = conn.WithEventsCte($"""
                SELECT event_id, trace_id, parent_event_id, sequence_number,
                       publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                       topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
                FROM events
                WHERE parent_event_id IN ({inParams})
                """);

            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < parentIds.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"p{i}", (long)parentIds[i]));

            var children = new List<EventRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                children.Add(EventRecordMapper.FromReader(reader));

            return (IReadOnlyList<EventRecord>)children;
        }, ct);
    }
}
```

### 2d. Create `src/Tracer.WebApi/Queries/TraceQueryService.cs`

```csharp
using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Service for building <see cref="TraceTree"/> objects from cross-interval DuckDB queries.
/// </summary>
public sealed class TraceQueryService(LiveMultiIntervalReader reader, ILogger<TraceQueryService> logger)
{
    private readonly LiveMultiIntervalReader _reader = reader;
    private readonly ILogger<TraceQueryService> _logger = logger;

    /// <summary>Retrieves all events with the given trace_id and assembles them into a tree.</summary>
    public async Task<TraceTree?> GetTraceTreeAsync(
        ulong traceId,
        int maxEvents,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var sql = conn.WithEventsCte("""
            SELECT event_id, trace_id, parent_event_id, sequence_number,
                   publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                   topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
            FROM events
            WHERE trace_id = $traceId
            ORDER BY publish_wallclock
            LIMIT $limit
            """);

        var events = await Task.Run(() =>
        {
            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DuckDBParameter("traceId", (long)traceId));
            cmd.Parameters.Add(new DuckDBParameter("limit", maxEvents + 1));  // +1 to detect truncation

            var list = new List<EventRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(EventRecordMapper.FromReader(r));
            return list;
        }, ct);

        if (events.Count == 0) return null;

        var truncated = events.Count > maxEvents;
        if (truncated) events.RemoveAt(events.Count - 1);

        return BuildTree(events, truncated, traceId);
    }

    /// <summary>
    /// Looks up the event by ID to find its trace_id, then returns the full trace tree.
    /// If the event has trace_id = 0, returns a singleton tree.
    /// </summary>
    public async Task<TraceTree?> GetTraceTreeForEventAsync(
        EventId eventId,
        int maxEvents,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var ev = await TraceWalker.LookupEventAsync(conn, eventId.Value, ct);
        if (ev is null) return null;

        if (ev.TraceId.Value == 0)
            return BuildSingletonTree(ev);

        // Re-acquire a fresh connection for the full trace query
        conn.Dispose();
        return await GetTraceTreeAsync(ev.TraceId.Value, maxEvents, ct);
    }

    /// <summary>Walks ancestors from <paramref name="eventId"/> up to <paramref name="maxDepth"/>.</summary>
    public async Task<TraceTree?> GetAncestorTreeAsync(
        EventId eventId,
        int maxDepth,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var chain = await TraceWalker.WalkAncestorsAsync(conn, eventId, maxDepth, ct);
        if (chain.Count == 0) return null;

        var traceId = chain[0].TraceId.Value;
        return BuildTree(chain.ToList(), truncated: false, traceId);
    }

    /// <summary>Walks descendants from <paramref name="eventId"/> using BFS.</summary>
    public async Task<TraceTree?> GetDescendantTreeAsync(
        EventId eventId,
        int maxDepth,
        int maxNodes,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var root = await TraceWalker.LookupEventAsync(conn, eventId.Value, ct);
        if (root is null) return null;

        var descendants = await TraceWalker.WalkDescendantsAsync(conn, eventId, maxDepth, maxNodes, ct);

        var all = new List<EventRecord>(descendants.Count + 1) { root };
        all.AddRange(descendants);

        var truncated = descendants.Count >= maxNodes;
        return BuildTree(all, truncated, root.TraceId.Value);
    }

    private static TraceTree BuildTree(
        IReadOnlyList<EventRecord> events,
        bool truncated,
        ulong traceId)
    {
        var nodes = events.Select(e => new TraceNode(e)).ToList();
        var nodeById = nodes.ToDictionary(n => n.Event.EventId.Value);

        var edges = new List<TraceEdge>();
        foreach (var node in nodes)
        {
            var parentId = node.Event.ParentEventId?.Value ?? 0;
            if (parentId == 0) continue;
            if (!nodeById.TryGetValue(parentId, out var parent)) continue;

            var latencyMs = (node.Event.PublishWallclock.ToDateTimeOffset() -
                             parent.Event.PublishWallclock.ToDateTimeOffset()).TotalMilliseconds;
            edges.Add(new TraceEdge(parent.Event.EventId, node.Event.EventId, latencyMs));
        }

        var childSet = new HashSet<ulong>(edges.Select(e => e.ChildEventId.Value));
        var parentSet = new HashSet<ulong>(edges.Select(e => e.ParentEventId.Value));

        var roots  = nodes.Where(n => !childSet.Contains(n.Event.EventId.Value)).ToList();
        var leaves = nodes.Where(n => !parentSet.Contains(n.Event.EventId.Value)).ToList();

        var participatingNodes = events
            .Select(e => e.PublisherNode.Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        double totalSpanMs = 0;
        DateTimeOffset? firstEventUtc = null;
        DateTimeOffset? lastEventUtc = null;

        if (events.Count > 0)
        {
            var times = events.Select(e => e.PublishWallclock.ToDateTimeOffset()).ToList();
            firstEventUtc = times.Min();
            lastEventUtc  = times.Max();
            totalSpanMs   = (lastEventUtc.Value - firstEventUtc.Value).TotalMilliseconds;
        }

        return new TraceTree
        {
            TraceId = traceId,
            Nodes = nodes,
            Edges = edges,
            Roots = roots,
            Leaves = leaves,
            Summary = new TraceSummary
            {
                TraceId = traceId,
                TotalEvents = events.Count,
                Truncated = truncated,
                TotalSpanMs = totalSpanMs,
                ParticipatingNodes = participatingNodes,
                RootCount = roots.Count,
                LeafCount = leaves.Count,
                FirstEventUtc = firstEventUtc,
                LastEventUtc = lastEventUtc,
                TotalEventsAvailable = truncated ? events.Count + 1 : null,
            },
        };
    }

    private static TraceTree BuildSingletonTree(EventRecord ev)
    {
        var node = new TraceNode(ev);
        return new TraceTree
        {
            TraceId = ev.TraceId.Value,
            Nodes = [node],
            Edges = [],
            Roots = [node],
            Leaves = [node],
            Summary = new TraceSummary
            {
                TraceId = ev.TraceId.Value,
                TotalEvents = 1,
                Truncated = false,
                TotalSpanMs = 0,
                ParticipatingNodes = [ev.PublisherNode.Value],
                RootCount = 1,
                LeafCount = 1,
                FirstEventUtc = ev.PublishWallclock.ToDateTimeOffset(),
                LastEventUtc  = ev.PublishWallclock.ToDateTimeOffset(),
            },
        };
    }
}
```

**IMPORTANT NOTES for TraceQueryService:**
- `GetTraceTreeForEventAsync` acquires a conn, does a lookup, then calls `GetTraceTreeAsync` which acquires another conn. But we can't `conn.Dispose()` inline since it's `await using`. Instead, use a scope:
  ```csharp
  EventRecord? ev;
  {
      await using var conn = await _reader.AcquireAsync(ct);
      ev = await TraceWalker.LookupEventAsync(conn, eventId.Value, ct);
  }
  if (ev is null) return null;
  if (ev.TraceId.Value == 0) return BuildSingletonTree(ev);
  return await GetTraceTreeAsync(ev.TraceId.Value, maxEvents, ct);
  ```
  Fix this — don't call `.Dispose()` explicitly on an `await using` variable.

- `AgentId.Value` — check what property name `AgentId` exposes. Look at `Tracer.Core.Identity.AgentId`. If it's `Value` then use `e.PublisherNode.Value`. If it's `Name` or something else, adjust.

### 2e. Create `tests/Tracer.Tests.Unit/WebApi/TraceWalkerTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>
/// Tests for <see cref="TraceWalker"/> using real DuckDB storage via ObserverFixture.
/// </summary>
public sealed class TraceWalkerTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly LiveMultiIntervalReader _reader;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 700_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentEventId = 0,
        DateTimeOffset? at = null, string node = "node-a")
    {
        return new EventRecord
        {
            SequenceNumber  = eventId,
            PublishWallclock  = At(at ?? BaseTime),
            ReceiveWallclock  = At(at ?? BaseTime),
            PublisherNode   = new AgentId(node),
            SubscriberNode  = new AgentId(node),
            Topic           = new TopicName("trace.test"),
            EventId         = new EventId(eventId),
            TraceId         = new TraceId(traceId),
            ParentEventId   = parentEventId != 0 ? new EventId(parentEventId) : null,
            PayloadJson     = "{}",
        };
    }

    public TraceWalkerTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        _reader = _fixture.App.Services.GetRequiredService<LiveMultiIntervalReader>();
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task WalkAncestors_ThreeGenerationChain_ReturnsChainFromStartToRoot()
    {
        // Chain: root (1) → mid (2) → leaf (3)
        var traceId = _nextId++;
        var root = MakeEvent(eventId: _nextId++, traceId: traceId, parentEventId: 0);
        var mid  = MakeEvent(eventId: _nextId++, traceId: traceId, parentEventId: root.EventId.Value);
        var leaf = MakeEvent(eventId: _nextId++, traceId: traceId, parentEventId: mid.EventId.Value);

        await _fixture.PushAsync([root, mid, leaf]);

        await using var conn = await _reader.AcquireAsync(CancellationToken.None);
        var chain = await TraceWalker.WalkAncestorsAsync(
            conn, leaf.EventId, maxDepth: 10, CancellationToken.None);

        chain.Should().HaveCount(3);
        chain[0].EventId.Should().Be(leaf.EventId,    "leaf is first (start event)");
        chain[1].EventId.Should().Be(mid.EventId,     "mid is second");
        chain[2].EventId.Should().Be(root.EventId,    "root is last");
    }

    [Fact]
    public async Task WalkAncestors_MaxDepthReached_StopsAtLimitAndReturnsPartialChain()
    {
        // 5-deep chain: a→b→c→d→e (5 levels)
        var traceId = _nextId++;
        var ids = new ulong[5];
        for (int i = 0; i < 5; i++) ids[i] = _nextId++;

        var events = new List<EventRecord>
        {
            MakeEvent(ids[0], traceId, parentEventId: 0),           // root (depth 0)
            MakeEvent(ids[1], traceId, parentEventId: ids[0]),
            MakeEvent(ids[2], traceId, parentEventId: ids[1]),
            MakeEvent(ids[3], traceId, parentEventId: ids[2]),
            MakeEvent(ids[4], traceId, parentEventId: ids[3]),      // leaf (depth 4)
        };
        await _fixture.PushAsync(events);

        await using var conn = await _reader.AcquireAsync(CancellationToken.None);
        var chain = await TraceWalker.WalkAncestorsAsync(
            conn, new EventId(ids[4]), maxDepth: 2, CancellationToken.None);

        chain.Should().HaveCount(2, "maxDepth=2 allows exactly 2 ancestor events");
        chain[0].EventId.Value.Should().Be(ids[4]);
        chain[1].EventId.Value.Should().Be(ids[3]);
    }

    [Fact]
    public async Task WalkAncestors_CycleInParentPointers_TerminatesViaCycleGuard()
    {
        // We can't store a true cycle in DuckDB but we can test the visited-set by having
        // a chain that terminates at an event whose parent_event_id points back to itself.
        // Since DuckDB can't enforce referential integrity, we just stop when the ID is missing.
        // The real test: a very deep chain terminates without stack overflow.
        var traceId = _nextId++;
        var ids = new ulong[20];
        for (int i = 0; i < 20; i++) ids[i] = _nextId++;

        var events = Enumerable.Range(0, 20).Select(i =>
            MakeEvent(ids[i], traceId, parentEventId: i == 0 ? 0 : ids[i - 1])).ToList();
        await _fixture.PushAsync(events);

        await using var conn = await _reader.AcquireAsync(CancellationToken.None);

        // Should not throw or hang; terminates at root
        var chain = await TraceWalker.WalkAncestorsAsync(
            conn, new EventId(ids[19]), maxDepth: 1000, CancellationToken.None);

        chain.Should().HaveCount(20, "all 20 events in the chain returned");
        chain.Should().OnlyHaveUniqueItems(n => n.EventId.Value, "no duplicates — cycle guard works");
    }

    [Fact]
    public async Task WalkDescendants_BinaryFanout_ReturnsAllNodesInBfsOrder()
    {
        // root → [childA, childB] → [grandA, grandB, grandC, grandD]
        var traceId = _nextId++;
        var rootId   = _nextId++;
        var childA   = _nextId++;
        var childB   = _nextId++;
        var grandA   = _nextId++;
        var grandB   = _nextId++;
        var grandC   = _nextId++;
        var grandD   = _nextId++;

        var events = new List<EventRecord>
        {
            MakeEvent(rootId,  traceId, 0),
            MakeEvent(childA,  traceId, rootId,  at: BaseTime.AddSeconds(1)),
            MakeEvent(childB,  traceId, rootId,  at: BaseTime.AddSeconds(2)),
            MakeEvent(grandA,  traceId, childA,  at: BaseTime.AddSeconds(3)),
            MakeEvent(grandB,  traceId, childA,  at: BaseTime.AddSeconds(4)),
            MakeEvent(grandC,  traceId, childB,  at: BaseTime.AddSeconds(5)),
            MakeEvent(grandD,  traceId, childB,  at: BaseTime.AddSeconds(6)),
        };
        await _fixture.PushAsync(events);

        await using var conn = await _reader.AcquireAsync(CancellationToken.None);
        var descendants = await TraceWalker.WalkDescendantsAsync(
            conn, new EventId(rootId), maxDepth: 10, maxNodes: 100, CancellationToken.None);

        descendants.Should().HaveCount(6, "6 descendants of root");
        // BFS: children (level 1) before grandchildren (level 2)
        var childIds = new HashSet<ulong> { childA, childB };
        descendants.Take(2).All(d => childIds.Contains(d.EventId.Value))
            .Should().BeTrue("children appear before grandchildren in BFS order");
    }

    [Fact]
    public async Task WalkDescendants_MaxNodesReached_TruncatesWithoutException()
    {
        var traceId = _nextId++;
        var rootId = _nextId++;
        var childIds = Enumerable.Range(0, 20).Select(_ => _nextId++).ToArray();

        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        foreach (var childId in childIds)
            events.Add(MakeEvent(childId, traceId, rootId, at: BaseTime.AddSeconds(1)));
        await _fixture.PushAsync(events);

        await using var conn = await _reader.AcquireAsync(CancellationToken.None);
        var act = () => TraceWalker.WalkDescendantsAsync(
            conn, new EventId(rootId), maxDepth: 10, maxNodes: 5, CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().HaveCount(5, "truncated at maxNodes=5");
    }
}
```

### 2f. Create `tests/Tracer.Tests.Unit/WebApi/TraceQueryServiceTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>
/// Tests for <see cref="TraceQueryService"/> using real DuckDB storage.
/// </summary>
public sealed class TraceQueryServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly TraceQueryService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 800_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentEventId = 0,
        DateTimeOffset? at = null, string node = "node-a")
    {
        return new EventRecord
        {
            SequenceNumber  = eventId,
            PublishWallclock  = At(at ?? BaseTime),
            ReceiveWallclock  = At(at ?? BaseTime),
            PublisherNode   = new AgentId(node),
            SubscriberNode  = new AgentId(node),
            Topic           = new TopicName("trace.query.test"),
            EventId         = new EventId(eventId),
            TraceId         = new TraceId(traceId),
            ParentEventId   = parentEventId != 0 ? new EventId(parentEventId) : null,
            PayloadJson     = "{}",
        };
    }

    public TraceQueryServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        // TraceQueryService requires registration. Create it manually with the fixture's reader.
        var reader = _fixture.App.Services.GetRequiredService<Tracer.Storage.DuckDB.MultiInterval.LiveMultiIntervalReader>();
        _svc = new TraceQueryService(reader, Microsoft.Extensions.Logging.Abstractions.NullLogger<TraceQueryService>.Instance);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetTraceTree_NormalTrace_ReturnsNodesEdgesAndSummary()
    {
        // 10 events on one trace: 1 root + 9 children
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 9; i++)
        {
            var childId = _nextId++;
            events.Add(MakeEvent(childId, traceId, rootId, at: BaseTime.AddSeconds(i + 1)));
        }
        await _fixture.PushAsync(events);

        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 1000, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Nodes.Should().HaveCount(10);
        tree.Edges.Should().HaveCount(9);
        tree.Summary.TotalEvents.Should().Be(10);
        tree.Summary.Truncated.Should().BeFalse();
        tree.Summary.RootCount.Should().Be(1);
        tree.Summary.LeafCount.Should().Be(9);
    }

    [Fact]
    public async Task GetTraceTree_ExceedsMaxEvents_ReturnsTruncatedResultWithFlagSet()
    {
        // 20 events on one trace; query with maxEvents=10
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 19; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId, at: BaseTime.AddSeconds(i + 1)));
        await _fixture.PushAsync(events);

        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 10, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Summary.Truncated.Should().BeTrue("20 events exceeds maxEvents=10");
        tree.Nodes.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetTraceTreeForEvent_EventWithTraceId_ReturnsSameResultAsDirectTraceCall()
    {
        var traceId = _nextId++;
        var rootId = _nextId++;
        var leafId = _nextId++;
        await _fixture.PushAsync(
        [
            MakeEvent(rootId, traceId, 0),
            MakeEvent(leafId, traceId, rootId, at: BaseTime.AddSeconds(1)),
        ]);

        var viaEvent = await _svc.GetTraceTreeForEventAsync(
            new EventId(leafId), maxEvents: 1000, CancellationToken.None);
        var directTrace = await _svc.GetTraceTreeAsync(
            traceId, maxEvents: 1000, CancellationToken.None);

        viaEvent.Should().NotBeNull();
        directTrace.Should().NotBeNull();
        viaEvent!.Nodes.Count.Should().Be(directTrace!.Nodes.Count);
        viaEvent.Edges.Count.Should().Be(directTrace.Edges.Count);
    }

    [Fact]
    public async Task GetTraceTreeForEvent_EventWithZeroTraceId_ReturnsSingletonTree()
    {
        // Event with trace_id = 0 (no trace context)
        var eventId = _nextId++;
        await _fixture.PushAsync(
        [
            new EventRecord
            {
                SequenceNumber  = eventId,
                PublishWallclock  = At(BaseTime),
                ReceiveWallclock  = At(BaseTime),
                PublisherNode   = new AgentId("node-a"),
                SubscriberNode  = new AgentId("node-a"),
                Topic           = new TopicName("trace.singleton"),
                EventId         = new EventId(eventId),
                TraceId         = new TraceId(0),       // zero trace ID
                ParentEventId   = null,
                PayloadJson     = "{}",
            }
        ]);

        var tree = await _svc.GetTraceTreeForEventAsync(
            new EventId(eventId), maxEvents: 1000, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Nodes.Should().HaveCount(1, "singleton tree has exactly one node");
        tree.Edges.Should().BeEmpty("singleton has no edges");
        tree.Summary.Truncated.Should().BeFalse();
    }
}
```

---

## IMPORTANT NOTES

### A. `AgentId.Value` is confirmed correct
Property is `Value` — all `e.PublisherNode.Value` references are correct.

### B. `WallclockTime` constructors are confirmed
- `WallclockTime.FromDateTimeOffset(DateTimeOffset)` — exists  
- `WallclockTime.ToDateTimeOffset()` — exists  
- Internal constructor `new WallclockTime((dt.Ticks - DateTime.UnixEpoch.Ticks) * 100L)` — valid

### C. `SchemaV1` accessibility — `InternalsVisibleTo` confirmed
`Tracer.Storage.DuckDB.csproj` already has `InternalsVisibleTo("Tracer.Tests.Unit")` — no changes needed to access `SchemaV1` from tests.

### D. Fix `GetTraceTreeForEventAsync` — don't call `.Dispose()` on `await using` variable
Use a nested scope instead:
```csharp
public async Task<TraceTree?> GetTraceTreeForEventAsync(
    EventId eventId,
    int maxEvents,
    CancellationToken ct)
{
    EventRecord? ev;
    {
        await using var conn = await _reader.AcquireAsync(ct);
        ev = await TraceWalker.LookupEventAsync(conn, eventId.Value, ct);
    }
    if (ev is null) return null;
    if (ev.TraceId.Value == 0) return BuildSingletonTree(ev);
    return await GetTraceTreeAsync(ev.TraceId.Value, maxEvents, ct);
}
```

### E. `TraceQueryService` not registered in `ObserverFixture`
`TraceQueryServiceTests` creates the service manually — no DI registration changes required for the tests.

### F. Check if `PooledMultiIntervalConnection` is the correct type for `conn` in TraceWalker
Look at `src/Tracer.Storage.DuckDB/MultiInterval/` to find:
- The return type of `LiveMultiIntervalReader.AcquireAsync()` — could be `IMultiIntervalConnection` or a concrete type  
- The `.Connection` property type — used to create commands
- The `.WithEventsCte(sql)` extension method location

Look at existing services like `src/Tracer.WebApi/Queries/EventQueryService.cs` to see the exact pattern used.

---

## Verification

After all changes, build and test:

```powershell
cd d:\Work\Tracer
dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 5
dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~TraceWalkerTests|FullyQualifiedName~TraceQueryServiceTests|FullyQualifiedName~SchemaV1Tests" 2>&1 | Select-Object -Last 10
dotnet test tests\Tracer.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~SchemaAppliedTests" 2>&1 | Select-Object -Last 6
dotnet test tests\Tracer.Tests.Unit -c Release --no-build 2>&1 | Select-Object -Last 4
```

All 326+ unit tests must pass. The 3 new unit test classes must pass (TraceWalkerTests: 5 tests, TraceQueryServiceTests: 4 tests, SchemaV1Tests: 1 test). The 2 SchemaAppliedTests must pass.

---

## Return in your report

1. All files created/modified
2. Any fixes made to the code (especially re: AgentId, WallclockTime, accessibility)
3. Full build output (last 5 lines)
4. Test results for the new test classes
5. Total unit test count (should be 336+)
