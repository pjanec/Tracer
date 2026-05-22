# BATCH-49 Instructions — Phase 9 Backend: Latency Analysis, Gap Detection, Network Topology

**Batch:** BATCH-49  
**Tasks:** TRC-P9-001 through TRC-P9-010  
**Phase:** 9 — Replication Latency, Gap Detection, Network Topology  
**Report to:** `.dev/tracer/reports/BATCH-49-REPORT.md`

---

## Onboarding

Read before starting:
- `docs/tracer_phase9_design.md` — full Phase 9 design (core reference)
- `docs/TASK-DETAIL.md` — sections TRC-P9-001 through TRC-P9-010 (precise success conditions)
- `.dev/tracer/reviews/BATCH-44-REVIEW.md` — for DI registration patterns
- `.dev/tracer/reviews/BATCH-48-REVIEW.md` — for integration test harness patterns

Phase 9 is the first backend phase that is **bundle-only**. The core insight: when a bundle is built from multiple nodes, the `events` table has one row per **subscriber** per published event. This means each published sample appears N times (once per subscribing node), each row with its own `receive_wallclock`. This per-subscriber data enables replication latency and gap analysis. In live mode, only one receive time exists (the Observer's own); therefore all Phase 9 latency/gap/topology analysis endpoints return HTTP 409.

---

## Architecture Notes — READ BEFORE IMPLEMENTING

### 1. `BundleModeGate` — Critical Dependency Constraint

`Tracer.WebApi` does **not** reference `Tracer.OfflineViewer`. Therefore the Phase 9 design's reference to `BundleOpenManager` in `BundleModeGate` cannot be used directly (would create a circular reference: `Observer` → `WebApi` → `OfflineViewer` → `WebApi`).

**Use the marker interface pattern instead:**

1. In `Tracer.WebApi/Util/BundleModeGate.cs`, define a companion marker interface:
```csharp
namespace Tracer.WebApi.Util;

/// <summary>Marker service registered only in bundle (OfflineViewer) mode.</summary>
public interface IBundleModeMarker { }

public static class BundleModeGate
{
    public static IResult? CheckBundleOrLive(IServiceProvider sp)
    {
        if (sp.GetService<IBundleModeMarker>() is null)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Bundle mode required",
                Detail = "This analysis requires per-node receive times, which are only available in bundle mode. Build a bundle from your session, then open it in the offline viewer.",
                Status = StatusCodes.Status409Conflict
            });
        return null;
    }
}
```

2. In `OfflineViewerHostBuilder.cs`, register a singleton of `IBundleModeMarker` (alongside `BundleOpenManager`):
```csharp
builder.Services.AddSingleton<IBundleModeMarker>(_ => new BundleModeMarkerSingleton());
// private sealed class BundleModeMarkerSingleton : IBundleModeMarker { }
// (inner to OfflineViewerHostBuilder)
```

3. `ObserverHostBuilder.cs` does **NOT** register `IBundleModeMarker` — this is the live-mode signal.

### 2. `BudgetService` — Working Directory Access

`BudgetService` reads `metadata.json` from the bundle's working directory. It cannot reference `BundleOpenManager` directly. Use the same `Func<string?>` pattern as `FastStateFileLocator`:

```csharp
public BudgetService(
    Func<string?>? getBundleWorkingDirectory,
    InMemoryBudgetRegistry? registry = null,
    ILogger<BudgetService>? logger = null)
```

In `OfflineViewerHostBuilder.cs`:
```csharp
builder.Services.AddSingleton<BudgetService>(sp =>
    new BudgetService(
        getBundleWorkingDirectory: () => sp.GetRequiredService<BundleOpenManager>().Current?.WorkingDirectory,
        registry: null,
        logger: sp.GetService<ILogger<BudgetService>>()));
```

In `ObserverHostBuilder.cs`:
```csharp
builder.Services.AddSingleton<BudgetService>(sp =>
    new BudgetService(
        getBundleWorkingDirectory: null,
        registry: null,
        logger: sp.GetService<ILogger<BudgetService>>()));
```

### 3. TopologyDto Naming Conflict

`Tracer.WebApi.Contracts.Dto.TopologyDto` already exists in `Dtos.cs` (Phase 3 node-list shape: `Nodes: IReadOnlyList<NodeInfoDto>` + `AsOfUtc`). The Phase 9 network topology DTO has a different shape.

**Use the name `NetworkTopologyDto` for Phase 9** (matching the endpoint `/api/topology/network`):

```csharp
public sealed record NetworkTopologyDto
{
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<TopologyEdgeDto> Edges { get; init; }
}
```

Similarly, the Phase 9 `TopologyService` class must NOT conflict with the existing `TopologyQueryService`. Name it `NetworkTopologyService`.

The existing `TopologyEndpoints.cs` registers `GET /api/topology` (Phase 3). In Phase 9, **add** `GET /api/topology/network` to the same file using `NetworkTopologyService`.

### 4. LiveMultiIntervalReader SQL Pattern

All Phase 9 services use the same pattern as Phase 3/4/5/8 services:
```csharp
await using var pooled = await _reader.AcquireAsync(ct);
using var cmd = pooled.Connection.CreateCommand();
cmd.CommandText = pooled.WithEventsCte(innerSql);
// add parameters
using var reader = cmd.ExecuteReader();
```

For Phase 9's aggregate queries, the `innerSql` is an aggregate SQL (no row-by-row reading):
```csharp
cmd.CommandText = pooled.WithEventsCte(innerSql);
using var reader = cmd.ExecuteReader();
if (reader.Read()) { /* read aggregate columns */ }
```

### 5. Events Table Columns for Phase 9

Phase 9 uses columns that are already in the events schema:
- `publisher_node` (string): the node that published the event
- `subscriber_node` (string): the node that received it (per-row unique in bundle mode)
- `publish_wallclock` (DateTime): when the publisher sent it
- `receive_wallclock` (DateTime): when the subscriber received it
- `topic` (string): DDS topic
- `sequence_number` (ulong/long): per-publisher-topic sequence number

**Replication latency**: `(receive_wallclock - publish_wallclock) * 1000.0` = latency in milliseconds. Can be negative (clock skew). Do NOT filter negatives.

---

## Tasks

### TRC-P9-001 — `LatencyBudget` and Core Latency Types

**Location:** `src/Tracer.Core/Domain/LatencyBudget.cs`

```csharp
namespace Tracer.Core.Domain;

public sealed record LatencyBudget
{
    public required string Topic { get; init; }
    public double? P99BudgetMs { get; init; }
    public double? AbsoluteMaxMs { get; init; }
}
```

**Unit tests** (`tests/Tracer.Tests.Unit/Domain/LatencyBudgetTests.cs`):
1. `LatencyBudget_RequiredTopic_ConstructsCorrectly`
2. `LatencyBudget_NullableBudgets_AreNull` — omit P99/AbsoluteMax; assert both null
3. `LatencyBudget_Equality_SameValues` — `==` returns true
4. `LatencyBudget_Equality_DifferentTopic` — `==` returns false
5. `LatencyBudget_NoBudget_NullIsDistinctFromZero`

---

### TRC-P9-002 — `FakeNetworkModel`

**Location:** `src/Tracer.Adapters.Mock/FakeNetworkModel.cs`

```csharp
namespace Tracer.Adapters.Mock;

public sealed class FakeNetworkModel
{
    public FakeNetworkModel(IReadOnlyList<string> allNodes, int seed) { ... }
    
    public IEnumerable<(string subscriberNode, DateTimeOffset receiveWallclock)> SimulateDelivery(
        string publisherNode, DateTimeOffset publishWallclock, IReadOnlyList<string> subscriberNodes)
    { ... }
}
```

**Design details (from `tracer_phase9_design.md §13.1`):**
- 15% of links are "bad" (BaseLatencyMs elevated, e.g. 15ms baseline with JitterStdMs=3ms)
- Normal links: BaseLatencyMs ~1.5ms, JitterStdMs ~0.4ms
- Self-subscribe: add < 200 µs (0.2ms)
- Drop: yield omitted entries (subscriber receives nothing)
- Spike: rare large additional delay (`SpikeProbability = 0.001`, `SpikeAdditionalMs = 150`)
- Use Box-Muller transform for Gaussian jitter: `Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2) * jitterStdMs`
- Use `System.Random(seed)` for determinism — generate a `Random` per-link-pair from the seed

**`LinkProfile` (private sealed record):**
```csharp
private sealed record LinkProfile(double BaseLatencyMs, double JitterStdMs, double DropProbability, double SpikeProbability, double SpikeAdditionalMs);
```

**Unit tests** (`tests/Tracer.Tests.Unit/FakeNetworkModelTests.cs`):
1. `FakeNetworkModel_SameSeed_DeterministicOutput`
2. `FakeNetworkModel_SelfSubscribe_LowLatency` — assert < 1ms
3. `FakeNetworkModel_Drop_NotReturned` — 100,000 calls on a link with `DropProbability=0.01`; assert omitted count between 0.5% and 2%
4. `FakeNetworkModel_BadLink_ElevatedP99` — 1000 deliveries on a bad link; p99 > 10ms
5. `FakeNetworkModel_Spike_ElevatedTail` — 100,000 deliveries; assert ≥ 1 delivery > `SpikeAdditionalMs * 0.5`

---

### TRC-P9-003 — `QuantileSink` and `HistogramSink`

**Locations:**
- `src/Tracer.WebApi/Util/QuantileSink.cs`
- `src/Tracer.WebApi/Util/HistogramSink.cs`

**`QuantileSink`:**
- Reservoir sampling (Algorithm R): keep first `reservoirSize` items; for item N > reservoirSize, replace a random item with probability `reservoirSize / N`
- `void Add(double value)`, `double GetQuantile(double q)` (sorts reservoir on-demand), `long Count`
- Empty sink: `GetQuantile` returns `double.NaN`

**`HistogramSink`:**
- Bucket index: `(long)Math.Floor(Math.Log2(Math.Max(valueMs, 0.001)) * 4)`
- `LowMs = Math.Pow(2.0, index / 4.0)`, `HighMs = Math.Pow(2.0, (index + 1.0) / 4.0)`
- `void Add(double valueMs)`, `IReadOnlyList<HistogramBucket> GetBuckets()` (returns only non-empty buckets)

```csharp
public sealed record HistogramBucket(long Index, double LowMs, double HighMs, long Count);
```

**Unit tests** (`tests/Tracer.Tests.Unit/Util/QuantileSinkTests.cs` and `HistogramSinkTests.cs`):
1. `QuantileSink_Empty_ReturnsNaN`
2. `QuantileSink_KnownDistribution_P50Accurate` — add 1..1000; p50 in [490, 510]
3. `QuantileSink_KnownDistribution_P99Accurate` — same; p99 in [980, 1000]
4. `QuantileSink_ReservoirFull_OlderValuesReplaced` — add 20,000 items; Count == 20,000; reservoir ≤ 10,000
5. `HistogramSink_Empty_ReturnsNoBuckets`
6. `HistogramSink_SingleValue_OneBucket` — `Add(2.0)`; one bucket, count=1, `LowMs <= 2.0 <= HighMs`
7. `HistogramSink_BucketBounds_Logarithmic` — [1.0, 2.0, 4.0, 8.0] each in distinct bucket
8. `HistogramSink_NegativeAndNearZero_ClampsToMin` — `Add(-0.5)`, `Add(0.0)`, `Add(0.0001)`; no exception
9. `HistogramSink_TotalCount_MatchesAdds` — 500 adds; sum of bucket counts == 500

---

### TRC-P9-004 — `LatencyDistributionService`

**Location:** `src/Tracer.WebApi/Queries/LatencyDistributionService.cs`

**Domain records** (define in the same file or a companion file):
```csharp
public sealed record LatencyQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public bool ExcludeSelfSubscribe { get; init; } = true;
}

public sealed record LatencyDistribution
{
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P90Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double P999Ms { get; init; }
    public required double MaxMs { get; init; }
    public required double MinMs { get; init; }
    public required double MeanMs { get; init; }
    public required double StddevMs { get; init; }
    public required IReadOnlyList<HistogramBucket> Buckets { get; init; }
}

public sealed record LatencyPairSummary
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double MaxMs { get; init; }
}
```

**Service methods:**
1. `GetAsync(LatencyQuery query, CancellationToken ct)` → `LatencyDistribution`
2. `ListByPairAsync(WallclockTime from, WallclockTime to, int minSamples, int limit, CancellationToken ct)` → `IReadOnlyList<LatencyPairSummary>`

**Key SQL for `GetAsync`** (see `tracer_phase9_design.md §4.2`):
```sql
WITH u AS ({union_all_events_cte}),
latencies AS (
    SELECT
        (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) AS latency_ms
    FROM u
    WHERE publish_wallclock >= $from
      AND publish_wallclock < $to
      [AND topic = $topic]
      [AND publisher_node = $pub]
      [AND subscriber_node = $sub]
      [AND publisher_node != subscriber_node]   -- if ExcludeSelfSubscribe
)
SELECT
    COUNT(*) AS sample_count,
    APPROX_QUANTILE(latency_ms, 0.50) AS p50,
    APPROX_QUANTILE(latency_ms, 0.90) AS p90,
    APPROX_QUANTILE(latency_ms, 0.99) AS p99,
    APPROX_QUANTILE(latency_ms, 0.999) AS p999,
    MAX(latency_ms) AS max_ms,
    MIN(latency_ms) AS min_ms,
    AVG(latency_ms) AS mean_ms,
    STDDEV_POP(latency_ms) AS stddev_ms
FROM latencies
```

**Histogram SQL** (separate query):
```sql
SELECT
    CAST(FLOOR(LOG2(GREATEST(latency_ms, 0.001)) * 4) AS BIGINT) AS bucket_index,
    COUNT(*) AS cnt
FROM latencies
GROUP BY bucket_index
ORDER BY bucket_index
```

**Key SQL for `ListByPairAsync`**:
```sql
SELECT
    topic, publisher_node, subscriber_node,
    COUNT(*) AS sample_count,
    APPROX_QUANTILE(latency_ms, 0.50) AS p50,
    APPROX_QUANTILE(latency_ms, 0.99) AS p99,
    MAX(latency_ms) AS max_ms
FROM (
    SELECT topic, publisher_node, subscriber_node,
        (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) AS latency_ms
    FROM events
    WHERE publish_wallclock >= $from AND publish_wallclock < $to
      AND publisher_node != subscriber_node
) sub
GROUP BY topic, publisher_node, subscriber_node
HAVING COUNT(*) >= $minSamples
ORDER BY p99 DESC
LIMIT $limit
```

When `SampleCount == 0`: return empty `LatencyDistribution` with all values = 0.0 and empty `Buckets`. Do NOT use the histogram utility classes in the hot path — use DuckDB SQL. The `HistogramSink` is for fallback/tests only.

**Unit tests** (`tests/Tracer.Tests.Unit/WebApi/LatencyDistributionServiceTests.cs`):
1. `EmptyBundle_ZeroCount`
2. `SingleSample_AllPercentilesEqual` — 5ms latency
3. `ExcludeSelf_Filters` — 2 same-node rows + 2 diff-node rows; ExcludeSelf=true → count=2
4. `TopicFilter_Isolates` — two topics; query one
5. `TimeRange_Respected` — events across 60min; query 10min window
6. `NegativeLatency_Included` — receive < publish; SampleCount==1, MinMs < 0
7. `BucketBounds_AreLogarithmic` — uniform bundle; HighMs/LowMs ≈ 2^(1/4) per bucket
8. `ListByPair_SortedByP99Desc`
9. `ListByPair_MinSamplesFilter`

---

### TRC-P9-005 — `LatencyTimeSeriesService`

**Location:** `src/Tracer.WebApi/Queries/LatencyTimeSeriesService.cs`

**Domain records:**
```csharp
public sealed record LatencyTimeSeriesQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public bool ExcludeSelfSubscribe { get; init; } = true;
}

public sealed record LatencyTimePoint(DateTimeOffset BucketStartUtc, double P50Ms, double P99Ms, long SampleCount);
public sealed record LatencyTimeSeries(string BucketSize, IReadOnlyList<LatencyTimePoint> Points);
```

**Bucket size selection** (`ChooseBucketSql(TimeSpan span) → (string label, string sql)`) per §5.3:
- `span >= 4h` → `5 minutes` → `TIME_BUCKET(INTERVAL '5 minutes', publish_wallclock)`
- `span >= 1h` → `1 minute` → `TIME_BUCKET(INTERVAL '1 minute', ...)`
- `span >= 30m` → `30 seconds` → `TIME_BUCKET(INTERVAL '30 seconds', ...)`
- `span >= 5m` → `10 seconds` → `TIME_BUCKET(INTERVAL '10 seconds', ...)`
- `span >= 1m` → `1 second` → `TIME_BUCKET(INTERVAL '1 second', ...)`
- default → `100 milliseconds` → `TIME_BUCKET(INTERVAL '100 milliseconds', ...)`

**Unit tests** (`tests/Tracer.Tests.Unit/WebApi/LatencyTimeSeriesServiceTests.cs`):
1. `EmptyBundle_EmptyPoints`
2. `OneHourSession_OneMinuteBuckets`
3. `FourHourSession_FiveMinuteBuckets`
4. `SubMinuteSession_CorrectBucket` — 30s → 1 second; 45s → 10 seconds
5. `BucketCounts_SumToTotal` — 120 events across 2h; sum of SampleCounts == 120
6. `EmptyBuckets_NotEmitted` — events in first + last 5-min window only; only 2 buckets returned
7. `P99_PlausibleAgainstInput` — 100 events in single bucket with known distribution; bucket P99 within 5% of true P99

---

### TRC-P9-006 — `LatencyOutlierService`

**Location:** `src/Tracer.WebApi/Queries/LatencyOutlierService.cs`

**Domain records:**
```csharp
public sealed record LatencyOutlierQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public double? ThresholdMs { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record LatencyOutlier
{
    public required string EventId { get; init; }
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required DateTimeOffset PublishWallclockUtc { get; init; }
    public required DateTimeOffset ReceiveWallclockUtc { get; init; }
    public required double LatencyMs { get; init; }
    public required double ThresholdMs { get; init; }
    public required string BudgetSource { get; init; } // "budget" | "top-0.1%"
}

public sealed record LatencyOutlierResult
{
    public required IReadOnlyList<LatencyOutlier> Outliers { get; init; }
    public required IReadOnlyList<LatencyBudget> BudgetsUsed { get; init; }
}
```

**Algorithm:**
- Always exclude `publisher_node == subscriber_node`
- If `query.ThresholdMs` is set: `WHERE latency_ms > $threshold` for all topics
- If null: per-topic threshold from `BudgetService.GetBudgetsAsync` → `AbsoluteMaxMs`
  - Topics without budget: use `APPROX_QUANTILE(latency_ms, 0.999)` as threshold
  - Set `BudgetSource = "budget"` or `"top-0.1%"` accordingly
- Results sorted by `latency_ms DESC`, limited to `query.Limit`

**Unit tests** (`tests/Tracer.Tests.Unit/WebApi/LatencyOutlierServiceTests.cs`):
1. `ExplicitThreshold_ReturnsAboveOnly`
2. `ExplicitThreshold_SortedDesc`
3. `NoBudget_Top0_1Pct`
4. `WithBudget_UsesAbsoluteMax` — `BudgetSource == "budget"`
5. `PerTopicBudgets_Applied` — two topics with different budgets
6. `SelfSubscribe_Excluded`
7. `NoOutliers_EmptyResult`
8. `Limit_Respected`

---

### TRC-P9-007 — `GapDetectionService`

**Location:** `src/Tracer.WebApi/Queries/GapDetectionService.cs`

**Domain records:**
```csharp
public sealed record GapDetectionQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public int Limit { get; init; } = 500;
}

public sealed record Gap
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required ulong ResumedAtSequence { get; init; }
    public required ulong PreviousSequence { get; init; }
    public required ulong MissingCount { get; init; }
    public required DateTimeOffset ResumedAtWallclockUtc { get; init; }
}

public sealed record GapDetectionResult
{
    public required IReadOnlyList<Gap> Gaps { get; init; }
    public required long TotalGaps { get; init; }
}
```

**SQL Algorithm** (see §7.2):
```sql
WITH u AS ({events_cte}),
ordered AS (
    SELECT
        topic, publisher_node, subscriber_node, sequence_number, publish_wallclock,
        LAG(sequence_number) OVER (
            PARTITION BY topic, publisher_node, subscriber_node
            ORDER BY sequence_number
        ) AS prev_seq
    FROM u
    WHERE publish_wallclock >= $from
      AND publish_wallclock < $to
      AND publisher_node != subscriber_node
      [AND topic = $topic]
      [AND publisher_node = $pub]
      [AND subscriber_node = $sub]
)
SELECT
    topic, publisher_node, subscriber_node,
    CAST(sequence_number AS UBIGINT) AS resumed_at_seq,
    CAST(COALESCE(prev_seq, 0) AS UBIGINT) AS prev_seq,
    CAST(sequence_number - COALESCE(prev_seq, 0) - 1 AS UBIGINT) AS missing_count,
    publish_wallclock
FROM ordered
WHERE sequence_number - COALESCE(prev_seq, 0) > 1   -- gap detected
  AND prev_seq IS NOT NULL OR sequence_number > 1   -- skip pure first-row (prev_seq NULL AND seq==1)
ORDER BY missing_count DESC, publish_wallclock
LIMIT $limit
```

**Note on first-sample edge case**: rows where `prev_seq IS NULL` (first appearance of a tuple) and `sequence_number > 1` — report as gap with `PreviousSequence = 0`. This is intentional per §7.4.

**Unit tests** (`tests/Tracer.Tests.Unit/WebApi/GapDetectionServiceTests.cs`):
1. `ContinuousSequence_NoGaps` — seq 1,2,3,4,5
2. `SingleGap_Detected` — seq 1,2,5 → MissingCount=2
3. `MultipleGaps_AllReported`
4. `FirstSample_ReportedWithZeroPrevious` — first event seq=10; PreviousSequence=0
5. `TupleFilter_Isolates`
6. `SelfSubscribe_Excluded`
7. `TimeRange_Respected`
8. `SortedByMissingDesc`

---

### TRC-P9-008 — `NetworkTopologyService` (named `NetworkTopologyService`, NOT `TopologyService`)

**Location:** `src/Tracer.WebApi/Queries/NetworkTopologyService.cs`

**Domain records:**
```csharp
public sealed record NetworkTopology
{
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<TopologyEdge> Edges { get; init; }
}

public sealed record TopologyEdge
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long MessageCount { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
}
```

**SQL:**
```sql
SELECT
    topic, publisher_node, subscriber_node,
    COUNT(*) AS message_count,
    MIN(publish_wallclock) AS first_seen,
    MAX(publish_wallclock) AS last_seen
FROM events
WHERE publish_wallclock >= $from AND publish_wallclock < $to
  AND publisher_node != subscriber_node
GROUP BY topic, publisher_node, subscriber_node
ORDER BY message_count DESC
```

`Nodes` = sorted distinct union of all `publisher_node` and `subscriber_node` values.

**Unit tests** (`tests/Tracer.Tests.Unit/WebApi/NetworkTopologyServiceTests.cs`):
1. `ThreeNode_CorrectEdges` — A→B, A→C; 2 edges, 3 nodes
2. `SelfSubscribe_Excluded`
3. `MessageCount_Aggregated` — 5 events A→B, 3 events A→C; sorted DESC
4. `Nodes_AreUnionOfPubAndSub`
5. `FirstLastSeen_Accurate`
6. `MultiTopic_EachTopicHasOwnEdge`
7. `EmptyBundle_EmptyResult`

---

### TRC-P9-009 — `BudgetService`

**Location:** `src/Tracer.WebApi/Queries/BudgetService.cs`

Companion stub: `src/Tracer.WebApi/Queries/InMemoryBudgetRegistry.cs`

**`metadata.json` format** (bundle working directory root):
```json
{
  "latencyBudgets": [
    { "topic": "weapons.fire", "p99BudgetMs": 50.0, "absoluteMaxMs": 200.0 },
    { "topic": "physics.update" }
  ]
}
```

**Implementation:**
```csharp
public async Task<IReadOnlyList<LatencyBudget>> GetBudgetsAsync(string sessionId, CancellationToken ct)
{
    // 1. Check in-memory registry first (for live mode / test override)
    if (_registry is not null && _getBundleWorkingDirectory?.Invoke() is null)
        return _registry.GetAll();
    
    // 2. Bundle mode: read metadata.json
    var workDir = _getBundleWorkingDirectory?.Invoke();
    if (workDir is null) return [];
    
    var metaPath = Path.Combine(workDir, "metadata.json");
    if (!File.Exists(metaPath)) return [];
    
    try
    {
        var json = await File.ReadAllTextAsync(metaPath, ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("latencyBudgets", out var arr)) return [];
        return arr.Deserialize<List<LatencyBudget>>(JsonSerializerOptions.Web) ?? [];
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to read latency budgets from {Path}", metaPath);
        return [];
    }
}
```

**`InMemoryBudgetRegistry`:**
```csharp
public sealed class InMemoryBudgetRegistry
{
    private readonly List<LatencyBudget> _budgets = new();
    public void Register(LatencyBudget budget) { _budgets.Add(budget); }
    public IReadOnlyList<LatencyBudget> GetAll() => _budgets.AsReadOnly();
}
```

**Unit tests** (`tests/Tracer.Tests.Unit/WebApi/BudgetServiceTests.cs`):
1. `BundleWithBudgets_ReturnsParsedList`
2. `NoBudgetsSection_ReturnsEmpty`
3. `MetadataFileMissing_ReturnsEmpty`
4. `MalformedJson_ReturnsEmpty`
5. `NullableFields_PreservedAsNull`
6. `LiveMode_ReturnsEmpty` — `getBundleWorkingDirectory = null`
7. `InMemoryRegistry_ReturnsRegistered`
8. `MultipleBudgets_AllReturned`

---

### TRC-P9-010 — Endpoints, DTOs, `BundleModeGate`, DI Wiring, EventsConsolidator Index

#### DTOs (`src/Tracer.WebApi/Contracts/Dto/LatencyDtos.cs` etc.)

Use the DTO names exactly as specified in `tracer_phase9_design.md §9.5`, EXCEPT:
- Use **`NetworkTopologyDto`** instead of `TopologyDto` (to avoid conflicting with existing Phase 3 `TopologyDto`)
- Use **`NetworkTopologyEdgeDto`** instead of `TopologyEdgeDto`

All DTOs are `sealed record` with `required` init-only properties.

**Files to create:**
- `src/Tracer.WebApi/Contracts/Dto/LatencyDtos.cs` — `LatencyDistributionDto`, `HistogramBucketDto`, `LatencyPairSummaryDto`, `LatencyTimeSeriesDto`, `LatencyTimePointDto`, `LatencyOutlierDto`, `LatencyOutlierListDto`
- `src/Tracer.WebApi/Contracts/Dto/GapDtos.cs` — `GapDto`, `GapResultDto`
- `src/Tracer.WebApi/Contracts/Dto/NetworkTopologyDtos.cs` — `NetworkTopologyDto`, `NetworkTopologyEdgeDto`
- `src/Tracer.WebApi/Contracts/Dto/BudgetDtos.cs` — `BudgetDto`, `BudgetListDto`

**DTO mapper helpers** (static methods, co-located with DTOs or in separate `*DtoMapper.cs` files):
- `LatencyDtoMapper.Map(LatencyDistribution) → LatencyDistributionDto`
- `LatencyDtoMapper.MapPairs(IReadOnlyList<LatencyPairSummary>) → IReadOnlyList<LatencyPairSummaryDto>`
- `LatencyDtoMapper.MapTimeSeries(LatencyTimeSeries) → LatencyTimeSeriesDto`
- `LatencyDtoMapper.MapOutliers(LatencyOutlierResult) → LatencyOutlierListDto`
- `GapDtoMapper.Map(GapDetectionResult) → GapResultDto`
- `NetworkTopologyDtoMapper.Map(NetworkTopology) → NetworkTopologyDto`
- `BudgetDtoMapper.Map(LatencyBudget) → BudgetDto`

#### `BundleModeGate.cs`

```csharp
// src/Tracer.WebApi/Util/BundleModeGate.cs
namespace Tracer.WebApi.Util;

/// <summary>Marker service registered only in bundle (OfflineViewer) mode.</summary>
public interface IBundleModeMarker { }

public static class BundleModeGate
{
    public static IResult? CheckBundleOrLive(IServiceProvider sp)
    {
        if (sp.GetService<IBundleModeMarker>() is null)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Bundle mode required",
                Detail = "This analysis requires per-node receive times, which are only available in bundle mode. Build a bundle from your session, then open it in the offline viewer.",
                Status = StatusCodes.Status409Conflict
            });
        return null;
    }
}
```

#### Endpoint Files

**`src/Tracer.WebApi/Endpoints/LatencyEndpoints.cs`** (NEW):
- `GET /api/latency/distribution` → `HandleDistributionAsync`
- `GET /api/latency/pairs` → `HandlePairsAsync`
- `GET /api/latency/timeseries` → `HandleTimeSeriesAsync`
- `GET /api/latency/outliers` → `HandleOutliersAsync`
- All check `BundleModeGate.CheckBundleOrLive` first
- All have `.WithOpenApi()`
- Validate `from > to` → HTTP 400 ProblemDetails

**`src/Tracer.WebApi/Endpoints/GapEndpoints.cs`** (NEW):
- `GET /api/gaps` → `HandleAsync`
- Limit clamped to [1, 5000]

**`src/Tracer.WebApi/Endpoints/TopologyEndpoints.cs`** (MODIFY existing):
- Keep existing `GET /api/topology` using `TopologyQueryService`
- ADD `GET /api/topology/network` → `HandleNetworkAsync` using `NetworkTopologyService`
- Network endpoint checks `BundleModeGate.CheckBundleOrLive`

**`src/Tracer.WebApi/Endpoints/BudgetEndpoints.cs`** (NEW):
- `GET /api/scenario/budgets` → `HandleAsync` using `BudgetService`
- **NO 409 gate** — returns empty list in live mode
- Returns `BudgetListDto`

#### DI Registration

**`src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs`** (MODIFY):
```csharp
// Bundle mode marker
builder.Services.AddSingleton<IBundleModeMarker>(_ => new BundleModeSentinel());
// where: private sealed class BundleModeSentinel : IBundleModeMarker { }

// Phase 9 query services
builder.Services.AddSingleton<LatencyDistributionService>();
builder.Services.AddSingleton<LatencyTimeSeriesService>();
builder.Services.AddSingleton<LatencyOutlierService>();
builder.Services.AddSingleton<GapDetectionService>();
builder.Services.AddSingleton<NetworkTopologyService>();
builder.Services.AddSingleton<InMemoryBudgetRegistry>();
builder.Services.AddSingleton<BudgetService>(sp =>
    new BudgetService(
        getBundleWorkingDirectory: () => sp.GetRequiredService<BundleOpenManager>().Current?.WorkingDirectory,
        registry: sp.GetRequiredService<InMemoryBudgetRegistry>(),
        logger: sp.GetService<ILogger<BudgetService>>()));

// Map Phase 9 endpoints
LatencyEndpoints.Map(app);
GapEndpoints.Map(app);
BudgetEndpoints.Map(app);
// TopologyEndpoints.Map(app) already called — adds /api/topology/network to it
```

**`src/Tracer.Observer/ObserverHostBuilder.cs`** (MODIFY):
```csharp
// Phase 9 services (live mode — no bundle marker registered)
builder.Services.AddSingleton<LatencyDistributionService>();
builder.Services.AddSingleton<LatencyTimeSeriesService>();
builder.Services.AddSingleton<LatencyOutlierService>();
builder.Services.AddSingleton<GapDetectionService>();
builder.Services.AddSingleton<NetworkTopologyService>();
builder.Services.AddSingleton<InMemoryBudgetRegistry>();
builder.Services.AddSingleton<BudgetService>(sp =>
    new BudgetService(
        getBundleWorkingDirectory: null,
        registry: sp.GetRequiredService<InMemoryBudgetRegistry>(),
        logger: sp.GetService<ILogger<BudgetService>>()));

// Map endpoints
LatencyEndpoints.Map(app);
GapEndpoints.Map(app);
BudgetEndpoints.Map(app);
```

**TestHarness** (`src/Tracer.TestHarness/Observer/ObserverFixture.cs` and `WebApiFixture.cs`):
Register the same Phase 9 services as the Observer does; do NOT register `IBundleModeMarker` (live-mode fixture).

#### EventsConsolidator Index

**`src/Tracer.Aggregator/Consolidation/EventsConsolidator.cs`** (MODIFY):

After the existing `idx_events_topic_time` index creation, add:
```csharp
await ExecAsync(output,
    "CREATE INDEX IF NOT EXISTS idx_events_topic_pub_sub ON events (topic, publisher_node, subscriber_node);", ct);
```

This runs after all events are inserted but before `CHECKPOINT`. It takes a few seconds on large bundles; acceptable.

#### Unit Tests for Endpoints

**`tests/Tracer.Tests.Unit/WebApi/LatencyEndpointsTests.cs`**:
1. `Distribution_LiveMode_Returns409`
2. `Distribution_BundleMode_Returns200`
3. `Pairs_LiveMode_Returns409`
4. `TimeSeries_LiveMode_Returns409`
5. `Outliers_LiveMode_Returns409`
6. `Distribution_FromAfterTo_Returns400`

**`tests/Tracer.Tests.Unit/WebApi/GapEndpointsTests.cs`**:
1. `LiveMode_Returns409`
2. `BundleMode_Returns200`

**`tests/Tracer.Tests.Unit/WebApi/NetworkTopologyEndpointsTests.cs`** (or add to existing topology tests):
1. `Network_LiveMode_Returns409`
2. `Network_BundleMode_Returns200`

**`tests/Tracer.Tests.Unit/WebApi/BudgetEndpointsTests.cs`**:
1. `LiveMode_Returns200Empty` — no 409 for budget endpoint
2. `BundleWithBudgets_Returns200List`

---

## Integration Tests

Three integration test files need the `FakeNetworkModel` to push events with per-subscriber receive times into the bundle via the existing `AggregationFixture` + bundle build path. 

Since `AggregationFixture` + bundle building is complex, keep integration tests minimal and focused. Use `ObserverFixture` with `configureExtraServices` to register `IBundleModeMarker` and a pre-built `BudgetService`, and push synthetic events directly with appropriate `publisher_node`, `subscriber_node`, `publish_wallclock`, `receive_wallclock` values.

**`tests/Tracer.Tests.Integration/LatencyAnalysisRoundTripTests.cs`**:
- SC-1: Push 100 events with latencies ~2ms; GET `/api/latency/distribution`; assert p99 < 20ms, sampleCount == 100
- SC-2: Push events where one (topic, pub, sub) pair has latencies ~50ms; GET `/api/latency/pairs`; assert that pair appears in the list

**`tests/Tracer.Tests.Integration/GapDetectionIntegrationTests.cs`**:
- SC-1: Push events with sequence gap (seq 1,2,3,10); GET `/api/gaps`; assert 1 gap, MissingCount == 6

**`tests/Tracer.Tests.Integration/NetworkTopologyIntegrationTests.cs`**:
- SC-1: Push events with 3 distinct (topic, publisher, subscriber) combos; GET `/api/topology/network`; assert edges.length == 3

For integration tests, use `ObserverFixture` with `IBundleModeMarker` registered in DI via `configureExtraServices`. Push `EventRecord` objects with explicit `PublisherNode`, `SubscriberNode`, `ReceiveWallclock` fields matching test expectations.

**Note**: Check the `EventRecord` structure and `PushAsync` helper to understand how to set `publisher_node` and `subscriber_node` on pushed records.

Add collection definitions to `TestCollections.cs`:
```csharp
[CollectionDefinition("LatencyAnalysisRoundTrip", DisableParallelization = true)]
public sealed class LatencyAnalysisRoundTripCollection { }
[CollectionDefinition("GapDetectionIntegration")]
public sealed class GapDetectionIntegrationCollection { }
[CollectionDefinition("NetworkTopologyIntegration")]
public sealed class NetworkTopologyIntegrationCollection { }
```

---

## Test-Driven Task Progression (MANDATORY)

**For each task:**
1. Write the test(s) listed in the Success Conditions
2. Run the test(s) — they must fail (compilation or assertion)
3. Implement the minimum code to make them pass
4. Run the test(s) — all must pass
5. Run the full integration test suite before moving to the next task

**Do not skip tests.** Do not mark a task complete until its tests pass.

**Build verification after each task group:**
```
dotnet build Tracer.sln -c Release
```
Zero warnings, zero errors required.

**Final verification:**
```
dotnet test tests/Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~LatencyDistribution|FullyQualifiedName~GapDetection|FullyQualifiedName~NetworkTopology|FullyQualifiedName~LatencyTimeSeries|FullyQualifiedName~LatencyOutlier|FullyQualifiedName~BudgetService|FullyQualifiedName~LatencyBudget|FullyQualifiedName~QuantileSink|FullyQualifiedName~HistogramSink|FullyQualifiedName~FakeNetworkModel|FullyQualifiedName~LatencyEndpoints|FullyQualifiedName~GapEndpoints|FullyQualifiedName~BudgetEndpoints"
dotnet test tests/Tracer.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~LatencyAnalysis|FullyQualifiedName~GapDetection|FullyQualifiedName~NetworkTopology"
```

---

## Report Format

Write your completion report to `.dev/tracer/reports/BATCH-49-REPORT.md` with the following sections:

### 1. Files Created / Modified
List every file with a one-line description of the change.

### 2. Test Results
- Unit tests: total count, pass count, filter used
- Integration tests: total count, pass count

### 3. Issues Encountered
**Answer explicitly:**
- What compilation errors or runtime failures occurred and how they were resolved?
- Were there any DuckDB SQL syntax issues (e.g., `APPROX_QUANTILE`, `TIME_BUCKET`, window functions)?
- Were there type casting issues with `sequence_number` (e.g., ulong/long in DuckDB.NET)?
- Were there any DTO naming conflicts beyond `TopologyDto`?

### 4. Design Decisions Beyond Spec
- Any deviations from the spec (e.g., different method names, SQL variations)? Justify each.
- How did you structure the `BundleModeGate` pattern? Did the marker interface work cleanly?
- How did integration tests push per-subscriber events? What `EventRecord` fields were used?

### 5. Weak Points Spotted
- Any code smells, potential performance issues, or fragile patterns observed
- Any existing code that Phase 9 might interact badly with

### 6. Suggested Git Commit Message
Provide a multi-line commit message.
