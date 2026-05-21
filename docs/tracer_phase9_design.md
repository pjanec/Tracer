# Tracer Phase 9 — Detailed Design
## Replication Latency, Gap Detection, Network Topology

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md` through `tracer_phase8_design.md`*
*Phase 9 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 9 is the first phase that fully exploits the architectural decision from §13.3: in bundle mode, each event appears once per subscribing node that captured it, each row carrying its own `receive_wallclock`. Live mode has only the Observer's receive time — a single number per event. Phase 9 builds the views that turn the per-subscriber receive data into engineering insight: how long does a publish take to land on each subscriber? Where are the outliers? Where are the gaps?*

*This is performance characterization. Phase 9 doesn't tell engineers what's broken; it tells them where the latency lives and where the network behavior diverges from expectations. The customer's distributed simulation engine has explicit latency budgets per topic (architecture §17). Phase 9 makes "are we hitting them?" answerable in seconds.*

*Phase 9 is also the first phase scoped exclusively to bundle mode. Live mode (Phase 3, Phase 5) inherently lacks per-node receive times — the Observer is a single capture point. The Phase 9 endpoints check the mode and return a clear "live-mode unavailable" response when invoked against an Observer.*

---

## 1. Phase 9 Scope and Goals

### 1.1 What Phase 9 Delivers

**Replication Latency View**
- `ReplicationLatencyView.vue` — analysis view at `/v/latency/{sessionId}`
- Per-topic latency distribution: histogram, percentiles (p50, p90, p99, p99.9), max
- Per-publisher-subscriber-pair breakdown: identifies bad legs
- Time-series of p99 latency across the session: shows degradation over time
- Outlier identification: events whose latency exceeds the topic's budget (configurable) or sits in the top 0.1% for the session
- Per-outlier drill-in: pivot to Timeline at the outlier's time, scrolled to the offending event

**Gap Detection View**
- `GapDetectionView.vue` at `/v/gaps/{sessionId}`
- Per (topic, publisher, subscriber) sequence-number gap analysis
- Lists gaps: missing sequence numbers, how many missing, time range of the gap
- Pivot from a gap to "what was happening around then" in Timeline

**Network Topology View**
- `NetworkTopologyView.vue` at `/v/topology/{sessionId}`
- Visual map of publishers → subscribers per topic
- Edge weight: message count over the session
- Click an edge → see the latency distribution for that (publisher, subscriber, topic) tuple
- Identifies "this node should be subscribing to topic X but isn't" — anomalies in the connectivity graph

**Backend Services**
- `/api/latency/...` endpoints: distribution, time-series, outliers, percentiles
- `/api/gaps/...` endpoints: per-tuple gap listing
- `/api/topology/...` endpoint: per-session publisher/subscriber graph

**Latency Budgets**
- Latency budgets per topic declared in scenario metadata (per the integration project's convention)
- Budgets exposed via `/api/scenario/budgets` (read from bundle metadata)
- Views use budgets to define "outlier" threshold per topic
- Default fallback: outliers = top 0.1% per (publisher, subscriber, topic) when no budget declared

**Cross-View Navigation**
- From latency outlier → Timeline view focused on the offending event
- From gap → Timeline view focused on the gap's wallclock range
- From topology edge → drill into latency distribution view, filtered to that edge
- Saved views participate (Phase 8 pattern): bookmark "Engagement topic latency over time"

### 1.2 What Phase 9 Does NOT Deliver

- **No live-mode replication latency** — Observer mode has only one receive time per event. The endpoints return 409 Conflict with a clear "requires bundle mode" message.
- **No real-time alerting** — Phase 9 doesn't notify operators when latency exceeds budget. It surfaces the data; alerting is a Phase 11+ deployment concern.
- **No multi-bundle comparison** — Phase 9 analyzes one bundle at a time. "Compare yesterday's latency to today's" is deferred.
- **No automatic root cause analysis** — Phase 9 flags outliers; the engineer correlates with timeline events to identify the cause.
- **No DDS adapter implementation** — that's a Phase 11 deliverable. Phase 9 assumes a real DDS adapter is in place that captures `receive_wallclock` per subscribing node. For Phase 9 development, the FakeNode (Phase 2) is extended to simulate per-node receive variations.
- **No latency budget editing in Tracer** — budgets are declared in scenario tools; Tracer is read-only for them.
- **No packet-loss or retry tracking** — Phase 9 measures latency and gaps. Network-level diagnostics (retries, packet drops, MTU issues) are deferred.

### 1.3 Success Criteria

1. **Open replication latency view from a bundle**: clicking "Latency analysis" from the bundle's session card opens `ReplicationLatencyView`. Loads in < 1 second for a 30-minute bundle.
2. **Identify the slowest topic**: at-a-glance ranking of topics by p99 latency. The top one is visually distinct.
3. **Identify the slowest publisher-subscriber pair**: drill into a topic, see per-pair breakdown.
4. **See time-series of latency**: select a (topic, pair); see how its p99 evolves through the session. Hover any point to see its sample count.
5. **Find outliers**: a list of "events that exceeded the budget" with timestamps, latencies, and "show in timeline" pivots.
6. **Open gap detection**: per-pair sequence-number-based gap detection runs and surfaces gaps.
7. **Visualize the network topology**: see the topic-routing graph. Click any edge → latency distribution for that edge.
8. **Latency budgets enforced**: outliers are computed against declared budgets when available; otherwise top-0.1% fallback applies.
9. **Performance**:
   - Distribution query for one topic over full bundle: < 500 ms
   - Time-series query: < 500 ms
   - Outlier query (top 100): < 300 ms
   - Gap detection for one (topic, pair): < 1 second
   - Topology query: < 200 ms
10. **All Phase 1-8 tests pass**.

### 1.4 Estimated Duration

Two to three calendar weeks for one developer. Distribution:
- Week 1: backend services — latency distribution, percentile/histogram math, outlier detection, gap analysis, topology
- Week 2: frontend views — replication latency view, time-series chart, outlier list
- Week 3: gap detection view, network topology view, cross-view pivots, FakeNode extensions for testing

---

## 2. Project Layout Additions

Building on Phase 8:

```
tracer/
  src/
    Tracer.Core/                                  (additions for latency types)
      LatencyBudget.cs                            NEW — per-topic budget record
    Tracer.Adapters.Mock/                         (extensions to simulate per-subscriber delays)
      FakeNetworkModel.cs                         NEW — generates synthetic receive times
    Tracer.WebApi/
      Endpoints/
        LatencyEndpoints.cs                       NEW
        GapEndpoints.cs                           NEW
        TopologyEndpoints.cs                      NEW (renamed/extended from Phase 3's stub)
        BudgetEndpoints.cs                        NEW
      Queries/
        LatencyDistributionService.cs             NEW
        LatencyTimeSeriesService.cs               NEW
        LatencyOutlierService.cs                  NEW
        GapDetectionService.cs                    NEW
        TopologyService.cs                        NEW (extended from Phase 3 stub)
        BudgetService.cs                          NEW — reads from bundle metadata
      Contracts/Dto/
        LatencyDistributionDto.cs
        LatencyTimeSeriesDto.cs
        LatencyOutlierDto.cs
        GapDto.cs
        TopologyDto.cs
        BudgetDto.cs
      Util/
        QuantileSink.cs                           NEW — streaming quantile computation
        HistogramSink.cs                          NEW — log-bucket histogram aggregator
  tracer-viewer/
    src/
      views/
        ReplicationLatencyView.vue                NEW
        GapDetectionView.vue                      NEW
        NetworkTopologyView.vue                   NEW
      components/
        LatencyDistributionChart.vue              NEW — histogram + percentile lines
        LatencyTimeSeriesChart.vue                NEW — time-series of p50/p99
        LatencyOutliersTable.vue                  NEW
        TopicBudgetRow.vue                        NEW — single topic with its stats
        PublisherSubscriberMatrix.vue             NEW — matrix view of pair latencies
        GapList.vue                               NEW
        NetworkGraphCanvas.vue                    NEW — directed graph renderer
      composables/
        useLatencyDistribution.ts                 NEW
        useLatencyTimeSeries.ts                   NEW
        useLatencyOutliers.ts                     NEW
        useGapDetection.ts                        NEW
        useTopology.ts                            NEW
      rendering/
        histogramRenderer.ts                      NEW
        latencyTimeSeriesRenderer.ts              NEW
        networkGraphLayout.ts                     NEW — force-directed-ish layout
        networkGraphRenderer.ts                   NEW
      stores/
        latencyStore.ts                           NEW
      types/
        latency.ts                                NEW
  tests/
    Tracer.Tests.Unit/
      WebApi/
        LatencyDistributionServiceTests.cs
        LatencyTimeSeriesServiceTests.cs
        LatencyOutlierServiceTests.cs
        GapDetectionServiceTests.cs
        TopologyServiceTests.cs
        BudgetServiceTests.cs
      Util/
        QuantileSinkTests.cs
        HistogramSinkTests.cs
    Tracer.Tests.Integration/
      LatencyAnalysisRoundTripTests.cs            FakeNode → bundle → Phase 9 endpoints
      GapDetectionIntegrationTests.cs
      TopologyIntegrationTests.cs
  tracer-viewer/tests/
    unit/
      histogramRenderer.spec.ts
      latencyTimeSeriesRenderer.spec.ts
      networkGraphLayout.spec.ts
      useLatencyDistribution.spec.ts
    e2e/
      replication-latency-view.spec.ts
      gap-detection-view.spec.ts
      network-topology-view.spec.ts
```

### 2.1 Dependencies

No new NuGet packages. The percentile/histogram math runs on plain .NET arithmetic with DuckDB SQL doing the heavy lifting where possible.

No new npm packages. The network graph layout is implemented in plain TypeScript (a small Barnes-Hut force-directed layout) since neither vis.js nor cytoscape is justified for Phase 9's scale (typically < 30 nodes).

---

## 3. The Per-Subscriber Data Shape

### 3.1 What Aggregated Mode Gives Us

The architecture's bundle consolidation (§13.3) produces an events table where **each published sample appears once per subscriber that captured it**. For a topic with one publisher and three subscribers, every published event has three rows in the bundle's `events.duckdb`:

```
event_id  publisher_node  subscriber_node  publish_wallclock     receive_wallclock     ...
E1        node-A          node-B           14:23:17.143          14:23:17.146          ...
E1        node-A          node-C           14:23:17.143          14:23:17.149          ...
E1        node-A          node-D           14:23:17.143          14:23:17.151          ...
```

Three rows, same `event_id`, same `publisher_node`, same `publish_wallclock`. Three different `subscriber_node` values, three different `receive_wallclock` values.

Phase 9's central computation: **`replication_latency_ms = (receive_wallclock - publish_wallclock) * 1000`** per row.

### 3.2 The Self-Subscribe Row

An event's own publisher is typically also subscribed (it processes its own publishes for state propagation, for example). The bundle therefore has rows where `publisher_node = subscriber_node`. These are intra-process: the receive is the publish, with a tiny additional cost from local DDS plumbing.

Phase 9 treats self-subscribe rows distinctly:

- **By default**, the distribution view **excludes** them — they distort the histogram with their near-zero latencies and aren't network-meaningful.
- **A toggle** "include self-subscribe rows" lets the engineer include them, useful for measuring local-DDS overhead.
- **Gap detection** treats self-subscribe rows the same as any other (they should never have gaps; if they do, that's a bug in the local DDS or the agent).

### 3.3 Other Mode-Specific Caveats

- **Same-clock assumption**: replication latency math assumes wall-clock synchronization across nodes (architecture §3 — the customer's PLL-based sync targets 1ms precision). Phase 9 doesn't compensate for clock skew; if skew were larger, computed latencies would be misleading. Document.
- **Negative latencies are real**: with 1ms clock-sync precision, sub-millisecond network paths can produce negative computed latencies (subscriber's clock momentarily ahead of publisher's). Phase 9 doesn't filter negatives — it shows them honestly. Histograms include a "≤0 ms" bucket. Engineers learn from real data what their clock-sync error floor looks like.
- **Missing publisher**: in rare cases, a subscriber's row exists but the publisher's own self-subscribe row is missing (publisher didn't capture its own publish). The data still has the publish_wallclock; latency math still works. No special handling needed.

### 3.4 Index Considerations

Phase 1's events table has an index on `(topic, publish_wallclock)`. Phase 9 queries frequently group by `(topic, publisher_node, subscriber_node)`. We add one more index:

```sql
CREATE INDEX IF NOT EXISTS idx_events_topic_pub_sub
ON events (topic, publisher_node, subscriber_node);
```

This index is **bundle-only** — it's created during bundle consolidation (Phase 4) when the events table is written. Live observer events tables don't need it; live mode doesn't run Phase 9 queries.

In `Tracer.Aggregator.Consolidation.EventsConsolidator.Finalize`:

```csharp
// After the consolidated events table is fully written, before close:
await using var cmd = conn.CreateCommand();
cmd.CommandText = """
    CREATE INDEX IF NOT EXISTS idx_events_topic_pub_sub
        ON events (topic, publisher_node, subscriber_node);
    """;
await cmd.ExecuteNonQueryAsync(ct);
```

Index creation on the full consolidated table takes a few seconds; happens once per bundle build.

---

## 4. Backend: Latency Distribution Service

### 4.1 What the Distribution Query Returns

For a given `(topic?, publisher_node?, subscriber_node?, time_range?)` filter:

- **Count**: total events in scope
- **Percentiles**: p50, p90, p99, p99.9 (in milliseconds)
- **Histogram**: counts per logarithmic bucket
- **Mean and stddev** (optional, for parametric reasoning)

The percentiles are the operationally meaningful metric. The histogram is the visualization. Both come from the same underlying data.

### 4.2 DuckDB's Built-in Statistics

DuckDB has `QUANTILE_CONT(col, q)`, `APPROX_QUANTILE(col, q)`, and aggregate functions like `AVG`, `STDDEV`. For Phase 9's data volume (≤ 100M events in a bundle), the `APPROX_QUANTILE` variant gives sub-second answers with negligible error (<0.5% on typical distributions).

```sql
SELECT
    COUNT(*) AS sample_count,
    APPROX_QUANTILE(latency_ms, 0.50) AS p50,
    APPROX_QUANTILE(latency_ms, 0.90) AS p90,
    APPROX_QUANTILE(latency_ms, 0.99) AS p99,
    APPROX_QUANTILE(latency_ms, 0.999) AS p999,
    MAX(latency_ms) AS max,
    AVG(latency_ms) AS mean,
    STDDEV_POP(latency_ms) AS stddev
FROM (
    SELECT
        (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
    FROM events
    WHERE topic = $topic
      AND publisher_node = $publisherNode
      AND subscriber_node = $subscriberNode
      AND publish_wallclock >= $from
      AND publish_wallclock <  $to
      AND publisher_node != subscriber_node   -- if excludeSelf=true
);
```

For the histogram, DuckDB's `histogram` function or a manual GROUP BY with bucket calculation. We use the manual form for control:

```sql
SELECT
    bucket_index,
    COUNT(*) AS count
FROM (
    SELECT
        FLOOR(LOG2(GREATEST(latency_ms, 0.001) + 0.001) * 4) AS bucket_index
        -- 4 buckets per power of 2: ≈18% width per bucket; e.g.,
        --  bucket 0: 1..1.19 ms
        --  bucket 1: 1.19..1.41 ms
        --  bucket 2: 1.41..1.68 ms
        --  bucket 3: 1.68..2.00 ms
    FROM ...
)
GROUP BY bucket_index
ORDER BY bucket_index;
```

Logarithmic bucketing gives natural resolution at all latency scales: tight near zero (where most data lives), coarse at the long tail (where outliers live). 4 buckets per octave produces ~30 buckets covering 0.001 ms to 10 s — a reasonable visualization budget.

### 4.3 LatencyDistributionService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class LatencyDistributionService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<LatencyDistributionService> _logger;

    public LatencyDistributionService(LiveMultiIntervalReader reader, ILogger<LatencyDistributionService> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<LatencyDistribution> GetAsync(LatencyQuery query, CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        var whereClauses = new List<string> {
            "publish_wallclock >= $from",
            "publish_wallclock <  $to"
        };
        if (query.Topic is not null)          whereClauses.Add("topic = $topic");
        if (query.PublisherNode is not null)  whereClauses.Add("publisher_node = $pub");
        if (query.SubscriberNode is not null) whereClauses.Add("subscriber_node = $sub");
        if (query.ExcludeSelfSubscribe)       whereClauses.Add("publisher_node != subscriber_node");
        var whereSql = "WHERE " + string.Join(" AND ", whereClauses);
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereSql);
        
        // Percentiles + summary stats
        var statsSql = $"""
            WITH u AS ({unionSql}),
            latencies AS (
                SELECT (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
                FROM u
            )
            SELECT
                COUNT(*),
                APPROX_QUANTILE(latency_ms, 0.50),
                APPROX_QUANTILE(latency_ms, 0.90),
                APPROX_QUANTILE(latency_ms, 0.99),
                APPROX_QUANTILE(latency_ms, 0.999),
                COALESCE(MAX(latency_ms), 0),
                COALESCE(MIN(latency_ms), 0),
                COALESCE(AVG(latency_ms), 0),
                COALESCE(STDDEV_POP(latency_ms), 0)
            FROM latencies;
            """;
        
        long count = 0;
        double p50 = 0, p90 = 0, p99 = 0, p999 = 0, max = 0, min = 0, mean = 0, stddev = 0;
        await using (var cmd = conn.Connection.CreateCommand())
        {
            cmd.CommandText = statsSql;
            BindCommonParameters(cmd, query);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (await rdr.ReadAsync(ct))
            {
                count = rdr.GetInt64(0);
                if (count > 0)
                {
                    p50  = rdr.IsDBNull(1) ? 0 : rdr.GetDouble(1);
                    p90  = rdr.IsDBNull(2) ? 0 : rdr.GetDouble(2);
                    p99  = rdr.IsDBNull(3) ? 0 : rdr.GetDouble(3);
                    p999 = rdr.IsDBNull(4) ? 0 : rdr.GetDouble(4);
                    max    = rdr.GetDouble(5);
                    min    = rdr.GetDouble(6);
                    mean   = rdr.GetDouble(7);
                    stddev = rdr.GetDouble(8);
                }
            }
        }
        
        if (count == 0)
            return new LatencyDistribution { Query = query, SampleCount = 0, Buckets = Array.Empty<HistogramBucket>() };
        
        // Histogram (manual log-bucketing)
        var histogramSql = $"""
            WITH u AS ({unionSql}),
            latencies AS (
                SELECT (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
                FROM u
            )
            SELECT
                FLOOR(LOG2(GREATEST(latency_ms, 0.001)) * 4) AS bucket_index,
                COUNT(*) AS cnt
            FROM latencies
            GROUP BY bucket_index
            ORDER BY bucket_index;
            """;
        
        var buckets = new List<HistogramBucket>();
        await using (var cmd = conn.Connection.CreateCommand())
        {
            cmd.CommandText = histogramSql;
            BindCommonParameters(cmd, query);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var index = (long)rdr.GetDouble(0);
                var cnt = rdr.GetInt64(1);
                var (low, high) = BucketBounds(index);
                buckets.Add(new HistogramBucket(index, low, high, cnt));
            }
        }
        
        return new LatencyDistribution
        {
            Query = query,
            SampleCount = count,
            P50Ms = p50, P90Ms = p90, P99Ms = p99, P999Ms = p999,
            MaxMs = max, MinMs = min, MeanMs = mean, StddevMs = stddev,
            Buckets = buckets
        };
    }
    
    private static (double low, double high) BucketBounds(long index)
    {
        // bucket i covers latency in [2^(i/4) ms, 2^((i+1)/4) ms)
        return (Math.Pow(2.0, index / 4.0), Math.Pow(2.0, (index + 1) / 4.0));
    }

    private static void BindCommonParameters(DuckDBCommand cmd, LatencyQuery query)
    {
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
        if (query.Topic is not null)          cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
        if (query.PublisherNode is not null)  cmd.Parameters.Add(new DuckDBParameter("pub",   query.PublisherNode));
        if (query.SubscriberNode is not null) cmd.Parameters.Add(new DuckDBParameter("sub",   query.SubscriberNode));
    }
}

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
    public required LatencyQuery Query { get; init; }
    public required long SampleCount { get; init; }
    public double P50Ms { get; init; }
    public double P90Ms { get; init; }
    public double P99Ms { get; init; }
    public double P999Ms { get; init; }
    public double MaxMs { get; init; }
    public double MinMs { get; init; }
    public double MeanMs { get; init; }
    public double StddevMs { get; init; }
    public required IReadOnlyList<HistogramBucket> Buckets { get; init; }
}

public sealed record HistogramBucket(long Index, double LowMs, double HighMs, long Count);
```

### 4.4 The Per-Tuple Aggregate Query

The view also wants "for each (topic, publisher, subscriber) tuple in the session, give me its p50 and p99". This is one query instead of N:

```sql
WITH u AS (<union>),
latencies AS (
    SELECT
        topic, publisher_node, subscriber_node,
        (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
    FROM u
    WHERE publisher_node != subscriber_node      -- excludeSelf
)
SELECT
    topic, publisher_node, subscriber_node,
    COUNT(*) AS cnt,
    APPROX_QUANTILE(latency_ms, 0.50) AS p50,
    APPROX_QUANTILE(latency_ms, 0.99) AS p99,
    MAX(latency_ms) AS maxl
FROM latencies
GROUP BY topic, publisher_node, subscriber_node
HAVING COUNT(*) >= $minSamples   -- exclude noisy small samples; default 50
ORDER BY p99 DESC
LIMIT $limit;
```

Sorted by p99 DESC — the worst legs surface first. `minSamples` defaults to 50 so a one-off two-message tuple doesn't dominate the worst-list.

`LatencyDistributionService.ListByPairAsync` returns these. The DTO is `IReadOnlyList<LatencyPairSummaryDto>`.

---

## 5. Backend: Latency Time-Series Service

### 5.1 What This Returns

For a given filter, the time-series breaks the session into N buckets (e.g., 5-minute buckets across a 60-minute session = 12 buckets) and computes percentiles per bucket. Result: a sequence of (bucket_start, p50, p99, count) — visualized as a line chart with two lines.

This answers questions like "did latency degrade in the second half of the engagement?"

### 5.2 The Query

```sql
WITH u AS (<union>),
latencies AS (
    SELECT
        publish_wallclock,
        (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
    FROM u
    WHERE topic = $topic
      AND publisher_node = $pub
      AND subscriber_node = $sub
      AND publisher_node != subscriber_node
      AND publish_wallclock >= $from
      AND publish_wallclock <  $to
)
SELECT
    time_bucket(INTERVAL '$bucketSpec', publish_wallclock) AS bucket_start,
    APPROX_QUANTILE(latency_ms, 0.50) AS p50,
    APPROX_QUANTILE(latency_ms, 0.99) AS p99,
    COUNT(*) AS cnt
FROM latencies
GROUP BY bucket_start
ORDER BY bucket_start;
```

Bucket size is automatic based on session span (same idiom as Phase 5's event aggregation): hour-long sessions → 5m buckets; minute-long sessions → 1s buckets.

### 5.3 Service

```csharp
namespace Tracer.WebApi.Queries;

public sealed class LatencyTimeSeriesService
{
    private readonly LiveMultiIntervalReader _reader;
    
    public LatencyTimeSeriesService(LiveMultiIntervalReader reader) { _reader = reader; }

    public async Task<LatencyTimeSeries> GetAsync(LatencyTimeSeriesQuery query, CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        var spanMs = (query.To.ToDateTimeOffset() - query.From.ToDateTimeOffset()).TotalMilliseconds;
        var bucketSpec = ChooseBucket(spanMs);
        
        var whereClauses = new List<string> {
            "publish_wallclock >= $from",
            "publish_wallclock <  $to"
        };
        if (query.Topic is not null)          whereClauses.Add("topic = $topic");
        if (query.PublisherNode is not null)  whereClauses.Add("publisher_node = $pub");
        if (query.SubscriberNode is not null) whereClauses.Add("subscriber_node = $sub");
        if (query.ExcludeSelfSubscribe)       whereClauses.Add("publisher_node != subscriber_node");
        var whereSql = "WHERE " + string.Join(" AND ", whereClauses);
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereSql);
        
        var sql = $"""
            WITH u AS ({unionSql}),
            latencies AS (
                SELECT publish_wallclock,
                       (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
                FROM u
            )
            SELECT time_bucket(INTERVAL '{bucketSpec}', publish_wallclock) AS bucket_start,
                   APPROX_QUANTILE(latency_ms, 0.50) AS p50,
                   APPROX_QUANTILE(latency_ms, 0.99) AS p99,
                   COUNT(*) AS cnt
            FROM latencies
            GROUP BY bucket_start
            ORDER BY bucket_start;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
        if (query.Topic is not null)          cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
        if (query.PublisherNode is not null)  cmd.Parameters.Add(new DuckDBParameter("pub",   query.PublisherNode));
        if (query.SubscriberNode is not null) cmd.Parameters.Add(new DuckDBParameter("sub",   query.SubscriberNode));
        
        var points = new List<LatencyTimePoint>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            points.Add(new LatencyTimePoint(
                BucketStartUtc: new DateTimeOffset(rdr.GetDateTime(0), TimeSpan.Zero),
                P50Ms: rdr.IsDBNull(1) ? 0 : rdr.GetDouble(1),
                P99Ms: rdr.IsDBNull(2) ? 0 : rdr.GetDouble(2),
                SampleCount: rdr.GetInt64(3)));
        }
        return new LatencyTimeSeries { Query = query, BucketSize = bucketSpec, Points = points };
    }
    
    private static string ChooseBucket(double spanMs)
    {
        if (spanMs >= 4 * 60 * 60 * 1000) return "5 minutes";
        if (spanMs >= 1 * 60 * 60 * 1000) return "1 minute";
        if (spanMs >= 30 * 60 * 1000)     return "30 seconds";
        if (spanMs >= 5  * 60 * 1000)     return "10 seconds";
        if (spanMs >= 1  * 60 * 1000)     return "1 second";
        return "100 milliseconds";
    }
}

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

public sealed record LatencyTimeSeries
{
    public required LatencyTimeSeriesQuery Query { get; init; }
    public required string BucketSize { get; init; }
    public required IReadOnlyList<LatencyTimePoint> Points { get; init; }
}
```

---

## 6. Backend: Outlier Service and Budgets

### 6.1 Latency Budgets

Each topic may have a declared latency budget. The customer's integration project declares budgets in scenario metadata; Tracer reads them during bundle build (Phase 4) and exposes via a new endpoint:

```
GET /api/scenario/budgets?sessionId={sessionId}
```

Response:

```json
{
  "budgets": [
    { "topic": "weapons.fire",          "p99BudgetMs": 50,  "absoluteMaxMs": 200 },
    { "topic": "physics.transform",     "p99BudgetMs": 30,  "absoluteMaxMs": 100 },
    { "topic": "scenario.phase_change", "p99BudgetMs": 100, "absoluteMaxMs": 500 }
  ]
}
```

- **`p99BudgetMs`**: target value the topic's p99 should not exceed
- **`absoluteMaxMs`**: any single event exceeding this is unambiguously an outlier

If a topic has no declared budget, the outlier service falls back to "top 0.1% of this topic's distribution" as the outlier threshold (effectively `APPROX_QUANTILE(latency, 0.999)`).

### 6.2 Budget Storage in the Bundle

Budgets ride in `bundle/metadata.json` alongside topology and scenario context (Phase 4 §6):

```json
{
  "sessionId": "...",
  "topology": { ... },
  "latencyBudgets": [
    { "topic": "weapons.fire", "p99BudgetMs": 50, "absoluteMaxMs": 200 }
  ]
}
```

`MetadataWriter` (Phase 4) is extended to include latency budgets. The values come from the integration project's scenario metadata topic (consumed at session start by the Observer and propagated into the bundle).

Live Observer: when ingesting a `scenario.metadata.latency_budgets` event (a domain convention), it records the budgets to a small in-memory table that `MetadataWriter` consults at bundle-build time. Pre-Phase-9 bundles lack this section; `BudgetService` handles the absence gracefully (returns empty list).

### 6.3 BudgetService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class BudgetService
{
    private readonly BundleOpenManager? _bundleMgr;
    private readonly InMemoryBudgetRegistry? _liveRegistry;

    public BudgetService(BundleOpenManager? bundleMgr = null, InMemoryBudgetRegistry? liveRegistry = null)
    {
        _bundleMgr = bundleMgr;
        _liveRegistry = liveRegistry;
    }

    public async Task<IReadOnlyList<LatencyBudget>> GetBudgetsAsync(string sessionId, CancellationToken ct)
    {
        // Offline-bundle mode: read from bundle metadata
        if (_bundleMgr?.Current is { } bundle)
        {
            var path = Path.Combine(bundle.WorkingDirectory, "metadata.json");
            if (!File.Exists(path)) return Array.Empty<LatencyBudget>();
            await using var stream = File.OpenRead(path);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("latencyBudgets", out var arr)) return Array.Empty<LatencyBudget>();
            var list = new List<LatencyBudget>();
            foreach (var el in arr.EnumerateArray())
            {
                list.Add(new LatencyBudget
                {
                    Topic = el.GetProperty("topic").GetString() ?? "",
                    P99BudgetMs = el.TryGetProperty("p99BudgetMs", out var p) ? p.GetDouble() : (double?)null,
                    AbsoluteMaxMs = el.TryGetProperty("absoluteMaxMs", out var m) ? m.GetDouble() : (double?)null,
                });
            }
            return list;
        }
        
        // Live observer mode (Phase 9 endpoints return 409, but the registry exists for future use)
        return _liveRegistry?.GetAll() ?? Array.Empty<LatencyBudget>();
    }
}

public sealed record LatencyBudget
{
    public required string Topic { get; init; }
    public double? P99BudgetMs { get; init; }
    public double? AbsoluteMaxMs { get; init; }
}
```

### 6.4 LatencyOutlierService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class LatencyOutlierService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly BudgetService _budgets;

    public LatencyOutlierService(LiveMultiIntervalReader reader, BudgetService budgets)
    {
        _reader = reader;
        _budgets = budgets;
    }

    public async Task<LatencyOutlierResult> FindAsync(LatencyOutlierQuery query, CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        var budgets = await _budgets.GetBudgetsAsync(query.SessionId, ct);
        var budgetByTopic = budgets.ToDictionary(b => b.Topic);
        
        // If the user supplies an explicit thresholdMs, use it.
        // Otherwise: per-topic, use absoluteMaxMs if available; else top-0.1%.
        var outliers = new List<LatencyOutlier>();
        
        var whereClauses = new List<string> {
            "publish_wallclock >= $from",
            "publish_wallclock <  $to",
            "publisher_node != subscriber_node"
        };
        if (query.Topic is not null) whereClauses.Add("topic = $topic");
        var whereSql = "WHERE " + string.Join(" AND ", whereClauses);
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereSql);
        
        if (query.ThresholdMs is { } th)
        {
            // User-specified threshold: simple query
            var sql = $"""
                WITH u AS ({unionSql}),
                latencies AS (
                    SELECT *,
                           (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
                    FROM u
                )
                SELECT * FROM latencies
                WHERE latency_ms > {th}
                ORDER BY latency_ms DESC
                LIMIT $limit;
                """;
            await using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            BindParameters(cmd, query);
            cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                outliers.Add(MapRow(rdr));
        }
        else
        {
            // Per-topic threshold from budgets or top-0.1% fallback
            var sql = $"""
                WITH u AS ({unionSql}),
                latencies AS (
                    SELECT *,
                           (EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000) AS latency_ms
                    FROM u
                ),
                per_topic_thresholds AS (
                    SELECT topic, APPROX_QUANTILE(latency_ms, 0.999) AS top_0_1_pct
                    FROM latencies
                    GROUP BY topic
                )
                SELECT l.*, t.top_0_1_pct
                FROM latencies l
                LEFT JOIN per_topic_thresholds t USING (topic)
                ORDER BY l.latency_ms DESC
                LIMIT $limit;
                """;
            await using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            BindParameters(cmd, query);
            cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit * 4));  // overshoot; filter below
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                var row = MapRow(rdr);
                var topicBudget = budgetByTopic.GetValueOrDefault(row.Topic);
                var threshold = topicBudget?.AbsoluteMaxMs
                    ?? (rdr.IsDBNull(rdr.GetOrdinal("top_0_1_pct"))
                        ? double.PositiveInfinity
                        : rdr.GetDouble(rdr.GetOrdinal("top_0_1_pct")));
                if (row.LatencyMs > threshold)
                {
                    outliers.Add(row with { ThresholdMs = threshold, BudgetSource = topicBudget is null ? "top-0.1%" : "budget" });
                    if (outliers.Count >= query.Limit) break;
                }
            }
        }
        
        return new LatencyOutlierResult
        {
            Query = query,
            Outliers = outliers,
            Budgets = budgets
        };
    }
    
    private static void BindParameters(DuckDBCommand cmd, LatencyOutlierQuery query)
    {
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
        if (query.Topic is not null) cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
    }
    
    private static LatencyOutlier MapRow(DbDataReader rdr)
    {
        // Maps from the unioned + computed latency view; depends on row layout
        // Details elided for clarity
        var ev = EventRecordMapper.FromReader(rdr);
        var latency = rdr.GetDouble(rdr.GetOrdinal("latency_ms"));
        return new LatencyOutlier
        {
            EventId = ev.EventId,
            Topic = ev.Topic,
            PublisherNode = ev.PublisherNode,
            SubscriberNode = ev.SubscriberNode,
            PublishWallclockUtc = ev.PublishWallclock.ToDateTimeOffset(),
            ReceiveWallclockUtc = ev.ReceiveWallclock?.ToDateTimeOffset() ?? default,
            LatencyMs = latency,
            ThresholdMs = 0,         // filled in by caller
            BudgetSource = ""        // filled in by caller
        };
    }
}

public sealed record LatencyOutlierQuery
{
    public required string SessionId { get; init; }
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public double? ThresholdMs { get; init; }    // override; null = use budgets or top-0.1%
    public int Limit { get; init; } = 100;
}

public sealed record LatencyOutlier
{
    public required EventId EventId { get; init; }
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required DateTimeOffset PublishWallclockUtc { get; init; }
    public required DateTimeOffset ReceiveWallclockUtc { get; init; }
    public required double LatencyMs { get; init; }
    public required double ThresholdMs { get; init; }
    public required string BudgetSource { get; init; }    // "budget" | "top-0.1%"
}

public sealed record LatencyOutlierResult
{
    public required LatencyOutlierQuery Query { get; init; }
    public required IReadOnlyList<LatencyOutlier> Outliers { get; init; }
    public required IReadOnlyList<LatencyBudget> Budgets { get; init; }
}
```

---

## 7. Backend: Gap Detection Service

### 7.1 What a Gap Is

A topic in DDS has a per-publisher sequence number. Subscribers receive samples with monotonically increasing sequence numbers (modulo wraparound, which we ignore at Phase 9 scale). A "gap" is a missing sequence number on a (topic, publisher, subscriber) tuple — the subscriber jumped from sequence N to sequence N+2+ without seeing N+1.

Gaps mean dropped messages. Reasons range from harmless (subscriber wasn't joined yet) to alarming (network packet loss, agent buffer overrun).

### 7.2 The Algorithm

For each (topic, publisher_node, subscriber_node) tuple, sort by sequence_number and identify discontinuities. SQL can do this directly with window functions:

```sql
WITH u AS (<union>),
seqs AS (
    SELECT topic, publisher_node, subscriber_node, sequence_number, publish_wallclock,
           sequence_number - LAG(sequence_number) OVER (
               PARTITION BY topic, publisher_node, subscriber_node
               ORDER BY sequence_number
           ) AS gap_size
    FROM u
    WHERE topic = $topic
      AND publisher_node = $pub
      AND subscriber_node = $sub
      AND publisher_node != subscriber_node
)
SELECT
    topic, publisher_node, subscriber_node,
    sequence_number AS resumed_at_seq,
    sequence_number - gap_size AS missed_through_seq,
    gap_size - 1 AS missing_count,
    publish_wallclock AS resumed_at_wallclock
FROM seqs
WHERE gap_size > 1
ORDER BY publish_wallclock;
```

The result lists every gap with its position in time and how many sequence numbers are missing.

### 7.3 Service

```csharp
namespace Tracer.WebApi.Queries;

public sealed class GapDetectionService
{
    private readonly LiveMultiIntervalReader _reader;

    public GapDetectionService(LiveMultiIntervalReader reader) { _reader = reader; }

    public async Task<GapDetectionResult> FindGapsAsync(GapDetectionQuery query, CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        var whereClauses = new List<string> {
            "publish_wallclock >= $from",
            "publish_wallclock <  $to",
            "publisher_node != subscriber_node"
        };
        if (query.Topic is not null)          whereClauses.Add("topic = $topic");
        if (query.PublisherNode is not null)  whereClauses.Add("publisher_node = $pub");
        if (query.SubscriberNode is not null) whereClauses.Add("subscriber_node = $sub");
        var whereSql = "WHERE " + string.Join(" AND ", whereClauses);
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereSql);
        
        var sql = $"""
            WITH u AS ({unionSql}),
            seqs AS (
                SELECT topic, publisher_node, subscriber_node, sequence_number, publish_wallclock,
                       sequence_number - LAG(sequence_number) OVER (
                           PARTITION BY topic, publisher_node, subscriber_node
                           ORDER BY sequence_number
                       ) AS gap_size
                FROM u
            )
            SELECT topic, publisher_node, subscriber_node,
                   sequence_number, sequence_number - gap_size AS prev_seq, gap_size - 1 AS missing,
                   publish_wallclock
            FROM seqs
            WHERE gap_size IS NOT NULL AND gap_size > 1
            ORDER BY missing DESC, publish_wallclock
            LIMIT $limit;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));
        if (query.Topic is not null)          cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
        if (query.PublisherNode is not null)  cmd.Parameters.Add(new DuckDBParameter("pub",   query.PublisherNode));
        if (query.SubscriberNode is not null) cmd.Parameters.Add(new DuckDBParameter("sub",   query.SubscriberNode));
        
        var gaps = new List<Gap>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            gaps.Add(new Gap(
                Topic: rdr.GetString(0),
                PublisherNode: rdr.GetString(1),
                SubscriberNode: rdr.GetString(2),
                ResumedAtSequence: (ulong)rdr.GetInt64(3),
                PreviousSequence: (ulong)rdr.GetInt64(4),
                MissingCount: (ulong)rdr.GetInt64(5),
                ResumedAtWallclockUtc: new DateTimeOffset(rdr.GetDateTime(6), TimeSpan.Zero)));
        }
        return new GapDetectionResult { Query = query, Gaps = gaps };
    }
}

public sealed record GapDetectionQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public int Limit { get; init; } = 500;
}

public sealed record Gap(
    string Topic, string PublisherNode, string SubscriberNode,
    ulong ResumedAtSequence, ulong PreviousSequence, ulong MissingCount,
    DateTimeOffset ResumedAtWallclockUtc);

public sealed record GapDetectionResult
{
    public required GapDetectionQuery Query { get; init; }
    public required IReadOnlyList<Gap> Gaps { get; init; }
}
```

### 7.4 First-Sample Edge Case

The first sample on a (topic, publisher, subscriber) tuple has no predecessor; `LAG` returns NULL; the WHERE clause filters it. Good.

But: if subscriber B joins midway through a session, B's first observed sequence will be N > 1. That's not a real gap in the publisher's stream — B simply wasn't subscribed for messages 1..N-1. Phase 9's gap detection reports this as a gap from "0" to N — misleading.

**Phase 9 chooses to live with this**: the gap is reported with `PreviousSequence: 0`, and the engineer recognizes "this is the subscriber's first sample, not a real gap". A future refinement could cross-reference subscriber-join events to filter these out; deferred.

---

## 8. Backend: Topology Service

### 8.1 What This Returns

For a session, the topology describes the publisher → subscriber graph per topic. Implementation: `SELECT DISTINCT topic, publisher_node, subscriber_node FROM events`.

```sql
SELECT
    topic, publisher_node, subscriber_node,
    COUNT(*) AS message_count,
    MIN(publish_wallclock) AS first_seen_utc,
    MAX(publish_wallclock) AS last_seen_utc
FROM events
WHERE publisher_node != subscriber_node
GROUP BY topic, publisher_node, subscriber_node
ORDER BY message_count DESC;
```

Phase 3 had a stub `TopologyService` for the Session Browser; Phase 9 extends it to produce the per-topic graph data.

### 8.2 Service

```csharp
namespace Tracer.WebApi.Queries;

public sealed class TopologyService
{
    private readonly LiveMultiIntervalReader _reader;

    public TopologyService(LiveMultiIntervalReader reader) { _reader = reader; }

    public async Task<NetworkTopology> GetNetworkTopologyAsync(string sessionId, WallclockTime from, WallclockTime to, CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        var whereSql = "WHERE publisher_node != subscriber_node AND publish_wallclock >= $from AND publish_wallclock < $to";
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereSql);
        
        var sql = $"""
            WITH u AS ({unionSql})
            SELECT topic, publisher_node, subscriber_node,
                   COUNT(*) AS message_count,
                   MIN(publish_wallclock) AS first_seen,
                   MAX(publish_wallclock) AS last_seen
            FROM u
            GROUP BY topic, publisher_node, subscriber_node
            ORDER BY message_count DESC;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   to.ToDateTimeOffset()));
        
        var edges = new List<TopologyEdge>();
        var nodes = new HashSet<string>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var topic = rdr.GetString(0);
            var pub   = rdr.GetString(1);
            var sub   = rdr.GetString(2);
            nodes.Add(pub); nodes.Add(sub);
            edges.Add(new TopologyEdge(
                Topic: topic,
                PublisherNode: pub,
                SubscriberNode: sub,
                MessageCount: rdr.GetInt64(3),
                FirstSeenUtc: new DateTimeOffset(rdr.GetDateTime(4), TimeSpan.Zero),
                LastSeenUtc:  new DateTimeOffset(rdr.GetDateTime(5), TimeSpan.Zero)));
        }
        
        return new NetworkTopology { Nodes = nodes.OrderBy(n => n).ToList(), Edges = edges };
    }
}

public sealed record NetworkTopology
{
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<TopologyEdge> Edges { get; init; }
}

public sealed record TopologyEdge(string Topic, string PublisherNode, string SubscriberNode,
                                  long MessageCount,
                                  DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);
```

---

## 9. Web API Endpoints and Mode Gating

### 9.1 Endpoint Surface

```
GET  /api/latency/distribution                    histogram + percentiles
GET  /api/latency/pairs                           per-tuple summary list (worst legs first)
GET  /api/latency/timeseries                      latency over the session
GET  /api/latency/outliers                        outliers vs. budget/top-0.1%
GET  /api/gaps                                    gap list
GET  /api/topology/network                        network graph
GET  /api/scenario/budgets                        latency budgets per topic
```

### 9.2 Mode Gate

Each Phase 9 endpoint checks the deployment mode. In live (Observer) mode, the endpoint returns 409 Conflict:

```csharp
namespace Tracer.WebApi.Util;

public static class BundleModeGate
{
    public static IResult? CheckBundleOrLive(IServiceProvider sp)
    {
        var mgr = sp.GetService<BundleOpenManager>();
        if (mgr is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Bundle mode required",
                Detail = "This analysis requires per-node receive times, which are only available in bundle mode. Build a bundle from your session, then open it in the offline viewer.",
                Status = StatusCodes.Status409Conflict
            });
        }
        return null;
    }
}
```

Each Phase 9 endpoint's first line:

```csharp
if (BundleModeGate.CheckBundleOrLive(sp) is { } problem) return problem;
```

The frontend displays the 409 with a clear UX message: "Latency analysis requires bundle mode. Open this session's bundle to continue." With a button to navigate to the bundle picker.

### 9.3 LatencyEndpoints

```csharp
namespace Tracer.WebApi.Endpoints;

public static class LatencyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/latency/distribution", HandleDistributionAsync).WithOpenApi();
        app.MapGet("/api/latency/pairs",        HandlePairsAsync).WithOpenApi();
        app.MapGet("/api/latency/timeseries",   HandleTimeSeriesAsync).WithOpenApi();
        app.MapGet("/api/latency/outliers",     HandleOutliersAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<LatencyDistributionDto>, ProblemHttpResult>> HandleDistributionAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string? topic,
        [FromQuery] string? publisherNode,
        [FromQuery] string? subscriberNode,
        [FromQuery] bool excludeSelf = true,
        [FromServices] IServiceProvider sp = default!,
        [FromServices] LatencyDistributionService service = default!,
        CancellationToken ct = default)
    {
        if (BundleModeGate.CheckBundleOrLive(sp) is { } problem)
            return TypedResults.Problem((ProblemDetails)((dynamic)problem).ProblemDetails);
        
        var result = await service.GetAsync(new LatencyQuery
        {
            From = WallclockTime.FromDateTimeOffset(from),
            To   = WallclockTime.FromDateTimeOffset(to),
            Topic = topic,
            PublisherNode = publisherNode,
            SubscriberNode = subscriberNode,
            ExcludeSelfSubscribe = excludeSelf
        }, ct);
        return TypedResults.Ok(LatencyDtoMapper.Map(result));
    }

    public static async Task<Results<Ok<IReadOnlyList<LatencyPairSummaryDto>>, ProblemHttpResult>> HandlePairsAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] int minSamples = 50,
        [FromQuery] int limit = 100,
        [FromServices] IServiceProvider sp = default!,
        [FromServices] LatencyDistributionService service = default!,
        CancellationToken ct = default)
    {
        if (BundleModeGate.CheckBundleOrLive(sp) is { } problem)
            return TypedResults.Problem((ProblemDetails)((dynamic)problem).ProblemDetails);
        
        var result = await service.ListByPairAsync(
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            minSamples, Math.Clamp(limit, 1, 5000), ct);
        return TypedResults.Ok(LatencyDtoMapper.MapPairs(result));
    }

    // HandleTimeSeriesAsync, HandleOutliersAsync follow the same pattern; details elided.
}
```

### 9.4 GapEndpoints and TopologyEndpoints

```csharp
namespace Tracer.WebApi.Endpoints;

public static class GapEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/gaps", HandleAsync).WithOpenApi();
    }
    
    public static async Task<Results<Ok<GapResultDto>, ProblemHttpResult>> HandleAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string? topic,
        [FromQuery] string? publisherNode,
        [FromQuery] string? subscriberNode,
        [FromQuery] int limit = 500,
        [FromServices] IServiceProvider sp = default!,
        [FromServices] GapDetectionService service = default!,
        CancellationToken ct = default)
    {
        if (BundleModeGate.CheckBundleOrLive(sp) is { } problem)
            return TypedResults.Problem((ProblemDetails)((dynamic)problem).ProblemDetails);
        
        var result = await service.FindGapsAsync(new GapDetectionQuery
        {
            From = WallclockTime.FromDateTimeOffset(from),
            To   = WallclockTime.FromDateTimeOffset(to),
            Topic = topic,
            PublisherNode = publisherNode,
            SubscriberNode = subscriberNode,
            Limit = Math.Clamp(limit, 1, 5000)
        }, ct);
        return TypedResults.Ok(GapDtoMapper.Map(result));
    }
}

public static class TopologyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/topology/network", HandleNetworkAsync).WithOpenApi();
    }
    
    public static async Task<Results<Ok<TopologyDto>, ProblemHttpResult>> HandleNetworkAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromServices] IServiceProvider sp = default!,
        [FromServices] TopologyService service = default!,
        CancellationToken ct = default)
    {
        if (BundleModeGate.CheckBundleOrLive(sp) is { } problem)
            return TypedResults.Problem((ProblemDetails)((dynamic)problem).ProblemDetails);
        
        var result = await service.GetNetworkTopologyAsync(sessionId,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to), ct);
        return TypedResults.Ok(TopologyDtoMapper.Map(result));
    }
}

public static class BudgetEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/scenario/budgets", HandleAsync).WithOpenApi();
    }
    
    public static async Task<Ok<BudgetListDto>> HandleAsync(
        [FromQuery] string sessionId,
        [FromServices] BudgetService service,
        CancellationToken ct)
    {
        var budgets = await service.GetBudgetsAsync(sessionId, ct);
        return TypedResults.Ok(new BudgetListDto { Budgets = budgets.Select(BudgetDtoMapper.Map).ToList() });
    }
}
```

### 9.5 DTOs

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record LatencyDistributionDto
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
    public required IReadOnlyList<HistogramBucketDto> Buckets { get; init; }
}

public sealed record HistogramBucketDto(long Index, double LowMs, double HighMs, long Count);

public sealed record LatencyPairSummaryDto
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double MaxMs { get; init; }
}

public sealed record LatencyTimeSeriesDto
{
    public required string BucketSize { get; init; }
    public required IReadOnlyList<LatencyTimePointDto> Points { get; init; }
}

public sealed record LatencyTimePointDto(DateTimeOffset BucketStartUtc, double P50Ms, double P99Ms, long SampleCount);

public sealed record LatencyOutlierDto
{
    public required string EventId { get; init; }
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required DateTimeOffset PublishWallclockUtc { get; init; }
    public required DateTimeOffset ReceiveWallclockUtc { get; init; }
    public required double LatencyMs { get; init; }
    public required double ThresholdMs { get; init; }
    public required string BudgetSource { get; init; }
}

public sealed record LatencyOutlierListDto
{
    public required IReadOnlyList<LatencyOutlierDto> Outliers { get; init; }
    public required IReadOnlyList<BudgetDto> Budgets { get; init; }
}

public sealed record GapDto
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required ulong ResumedAtSequence { get; init; }
    public required ulong PreviousSequence { get; init; }
    public required ulong MissingCount { get; init; }
    public required DateTimeOffset ResumedAtWallclockUtc { get; init; }
}

public sealed record GapResultDto
{
    public required IReadOnlyList<GapDto> Gaps { get; init; }
    public required long TotalGaps { get; init; }
}

public sealed record TopologyDto
{
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<TopologyEdgeDto> Edges { get; init; }
}

public sealed record TopologyEdgeDto
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long MessageCount { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
}

public sealed record BudgetDto
{
    public required string Topic { get; init; }
    public double? P99BudgetMs { get; init; }
    public double? AbsoluteMaxMs { get; init; }
}

public sealed record BudgetListDto
{
    public required IReadOnlyList<BudgetDto> Budgets { get; init; }
}
```

---

## 10. Frontend: Replication Latency View

### 10.1 Layout

```
+----------------------------------------------------------------+
| AppHeader                                                       |
+----------------------------------------------------------------+
| Toolbar: topic filter · pub filter · sub filter · time range  |
+----------+--------------------------------+-------------------+
|          |                                |                   |
|  Pair    |   Latency Distribution         |  Outliers         |
|  Matrix  |   Histogram + percentiles      |  (top 100)        |
|          |                                |                   |
|  Worst   +--------------------------------+                   |
|  legs    |   Latency Time-Series          |  Outlier rows     |
|  first   |   p50/p99 over session         |  with             |
|          |                                |  "Show in TL"     |
|          |                                |                   |
+----------+--------------------------------+-------------------+
```

The three panels share filter state. Clicking a row in the Pair Matrix narrows the distribution and time-series to that tuple.

### 10.2 ReplicationLatencyView.vue

```vue
<!-- src/views/ReplicationLatencyView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute } from 'vue-router';
import { useLatencyDistribution } from '@/composables/useLatencyDistribution';
import { useLatencyTimeSeries } from '@/composables/useLatencyTimeSeries';
import { useLatencyOutliers } from '@/composables/useLatencyOutliers';
import { useApi } from '@/api/useApi';
import PublisherSubscriberMatrix from '@/components/PublisherSubscriberMatrix.vue';
import LatencyDistributionChart from '@/components/LatencyDistributionChart.vue';
import LatencyTimeSeriesChart from '@/components/LatencyTimeSeriesChart.vue';
import LatencyOutliersTable from '@/components/LatencyOutliersTable.vue';
import BundleModeRequiredBanner from '@/components/BundleModeRequiredBanner.vue';
import type { LatencyPairSummaryDto, BudgetDto } from '@/types/latency';

const route = useRoute();
const api = useApi();
const sessionId = computed(() => route.params.sessionId as string);

const sessionRange = ref<{ from: Date; to: Date } | null>(null);
const budgets = ref<BudgetDto[]>([]);
const pairs = ref<LatencyPairSummaryDto[]>([]);
const selectedPair = ref<LatencyPairSummaryDto | null>(null);
const error = ref<{ status: number; message: string } | null>(null);

const filter = computed(() => ({
  sessionId: sessionId.value,
  from: sessionRange.value?.from,
  to: sessionRange.value?.to,
  topic: selectedPair.value?.topic,
  publisherNode: selectedPair.value?.publisherNode,
  subscriberNode: selectedPair.value?.subscriberNode,
}));

const { distribution, loading: distLoading } = useLatencyDistribution(filter);
const { timeseries, loading: tsLoading } = useLatencyTimeSeries(filter);
const { outliers, loading: outLoading } = useLatencyOutliers(filter);

async function init() {
  try {
    const session = await api.getSession(sessionId.value);
    sessionRange.value = {
      from: new Date(session.startUtc),
      to:   new Date(session.endUtc ?? new Date()),
    };
    budgets.value = (await api.getLatencyBudgets(sessionId.value)).budgets;
    pairs.value = await api.listLatencyPairs({
      sessionId: sessionId.value,
      from: sessionRange.value.from,
      to:   sessionRange.value.to,
      minSamples: 50, limit: 200
    });
  } catch (err: any) {
    if (err.response?.status === 409) {
      error.value = { status: 409, message: err.response.data?.detail ?? 'Bundle mode required' };
    } else {
      throw err;
    }
  }
}

onMounted(init);
</script>

<template>
  <div class="latency-view">
    <BundleModeRequiredBanner v-if="error?.status === 409" :message="error.message" :session-id="sessionId" />
    <template v-else-if="sessionRange">
      <header class="latency-view__header">
        <h1>Replication latency</h1>
        <span v-if="selectedPair" class="latency-view__pair">
          {{ selectedPair.topic }}: {{ selectedPair.publisherNode }} → {{ selectedPair.subscriberNode }}
          <button @click="selectedPair = null">×</button>
        </span>
      </header>
      
      <div class="latency-view__grid">
        <PublisherSubscriberMatrix
          class="latency-view__matrix"
          :pairs="pairs"
          :budgets="budgets"
          :selected-pair="selectedPair"
          @select="p => selectedPair = p"
        />
        <LatencyDistributionChart
          class="latency-view__distribution"
          :distribution="distribution"
          :loading="distLoading"
          :budget="selectedPair && budgets.find(b => b.topic === selectedPair.topic) || null"
        />
        <LatencyTimeSeriesChart
          class="latency-view__timeseries"
          :timeseries="timeseries"
          :loading="tsLoading"
        />
        <LatencyOutliersTable
          class="latency-view__outliers"
          :outliers="outliers"
          :session-id="sessionId"
          :loading="outLoading"
        />
      </div>
    </template>
  </div>
</template>

<style lang="scss">
.latency-view {
  max-width: 1800px;
  margin: 0 auto;
  padding: 1.5rem;
  
  &__header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; }
  &__pair { padding: 0.25rem 0.5rem; background: var(--c-bg-subtle); border-radius: 6px; font-family: var(--font-mono); }
  
  &__grid {
    display: grid;
    grid-template-columns: 320px 1fr 380px;
    grid-template-rows: 1fr 1fr;
    grid-template-areas:
      "matrix distribution outliers"
      "matrix timeseries   outliers";
    gap: 1rem;
    min-height: 70vh;
  }
  
  &__matrix       { grid-area: matrix; }
  &__distribution { grid-area: distribution; }
  &__timeseries   { grid-area: timeseries; }
  &__outliers     { grid-area: outliers; }
}
</style>
```

### 10.3 LatencyDistributionChart.vue

A canvas component that renders the histogram with percentile lines overlaid.

```typescript
// src/rendering/histogramRenderer.ts

import type { LatencyDistributionDto, BudgetDto } from '@/types/latency';

export interface HistogramRenderInput {
  distribution: LatencyDistributionDto;
  budget: BudgetDto | null;
  widthPx: number;
  heightPx: number;
}

export function renderHistogram(ctx: CanvasRenderingContext2D, input: HistogramRenderInput) {
  ctx.clearRect(0, 0, input.widthPx, input.heightPx);
  
  const { distribution } = input;
  if (distribution.sampleCount === 0 || distribution.buckets.length === 0) {
    drawEmpty(ctx, input);
    return;
  }
  
  // X-axis: log scale of latency (ms)
  const minMs = Math.min(distribution.buckets[0].lowMs, 0.001);
  const maxMs = Math.max(distribution.buckets[distribution.buckets.length - 1].highMs, distribution.maxMs);
  
  // X scale: log10
  const logMin = Math.log10(Math.max(minMs, 0.001));
  const logMax = Math.log10(maxMs);
  const xRange = logMax - logMin;
  
  const padding = { top: 16, right: 24, bottom: 32, left: 48 };
  const plotW = input.widthPx  - padding.left - padding.right;
  const plotH = input.heightPx - padding.top  - padding.bottom;
  
  function xPx(latencyMs: number): number {
    const lx = Math.log10(Math.max(latencyMs, 0.001));
    return padding.left + ((lx - logMin) / xRange) * plotW;
  }
  
  // Y axis: counts, linear
  const maxCount = Math.max(...distribution.buckets.map(b => b.count));
  function yPx(count: number): number {
    return padding.top + plotH - (count / maxCount) * plotH;
  }
  
  // Draw axes
  ctx.strokeStyle = 'rgba(255,255,255,0.2)';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(padding.left, padding.top);
  ctx.lineTo(padding.left, padding.top + plotH);
  ctx.lineTo(padding.left + plotW, padding.top + plotH);
  ctx.stroke();
  
  // X-axis tick labels (powers of 10)
  ctx.fillStyle = 'rgba(255,255,255,0.6)';
  ctx.font = '10px var(--font-mono)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  for (let i = Math.ceil(logMin); i <= Math.floor(logMax); i++) {
    const x = padding.left + ((i - logMin) / xRange) * plotW;
    ctx.beginPath();
    ctx.moveTo(x, padding.top + plotH);
    ctx.lineTo(x, padding.top + plotH + 4);
    ctx.stroke();
    const ms = Math.pow(10, i);
    ctx.fillText(formatMs(ms), x, padding.top + plotH + 6);
  }
  
  // Draw histogram bars
  for (const b of distribution.buckets) {
    const x0 = xPx(b.lowMs);
    const x1 = xPx(b.highMs);
    const y0 = yPx(b.count);
    const y1 = padding.top + plotH;
    ctx.fillStyle = '#5b9dff';
    ctx.fillRect(x0, y0, Math.max(x1 - x0 - 1, 1), y1 - y0);
  }
  
  // Percentile lines (p50, p99)
  drawPercentileLine(ctx, distribution.p50Ms, 'p50', '#4ec97a', xPx, padding);
  drawPercentileLine(ctx, distribution.p99Ms, 'p99', '#e8b048', xPx, padding);
  drawPercentileLine(ctx, distribution.p999Ms, 'p99.9', '#e85c5c', xPx, padding);
  
  // Budget line (if present)
  if (input.budget?.absoluteMaxMs) {
    drawBudgetLine(ctx, input.budget.absoluteMaxMs, 'max budget', '#ff4444', xPx, padding);
  }
  if (input.budget?.p99BudgetMs) {
    drawBudgetLine(ctx, input.budget.p99BudgetMs, 'p99 budget', '#ffa500', xPx, padding);
  }
  
  // Legend in upper right with the summary stats
  drawSummary(ctx, distribution, input.widthPx - padding.right - 4, padding.top + 4);
}

function drawPercentileLine(
  ctx: CanvasRenderingContext2D, valueMs: number, label: string, color: string,
  xPx: (v: number) => number, padding: { top: number; bottom: number; left: number; right: number }
) {
  const x = xPx(valueMs);
  ctx.strokeStyle = color;
  ctx.setLineDash([4, 3]);
  ctx.beginPath();
  ctx.moveTo(x, padding.top);
  ctx.lineTo(x, padding.top + (ctx.canvas.height - padding.top - padding.bottom));
  ctx.stroke();
  ctx.setLineDash([]);
  
  ctx.fillStyle = color;
  ctx.font = '10px var(--font-mono)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'bottom';
  ctx.fillText(label, x, padding.top - 1);
}

function drawBudgetLine(
  ctx: CanvasRenderingContext2D, budgetMs: number, label: string, color: string,
  xPx: (v: number) => number, padding: { top: number; bottom: number; left: number; right: number }
) {
  // Solid vertical line, thicker
  const x = xPx(budgetMs);
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(x, padding.top);
  ctx.lineTo(x, padding.top + (ctx.canvas.height - padding.top - padding.bottom));
  ctx.stroke();
  ctx.lineWidth = 1;
}

function drawSummary(ctx: CanvasRenderingContext2D, dist: LatencyDistributionDto, x: number, y: number) {
  const lines = [
    `samples: ${dist.sampleCount.toLocaleString()}`,
    `p50: ${dist.p50Ms.toFixed(2)} ms`,
    `p99: ${dist.p99Ms.toFixed(2)} ms`,
    `max: ${dist.maxMs.toFixed(2)} ms`,
  ];
  ctx.font = '11px var(--font-mono)';
  ctx.fillStyle = 'rgba(255,255,255,0.85)';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'top';
  for (let i = 0; i < lines.length; i++) ctx.fillText(lines[i], x, y + i * 14);
}

function drawEmpty(ctx: CanvasRenderingContext2D, input: HistogramRenderInput) {
  ctx.fillStyle = 'rgba(255,255,255,0.5)';
  ctx.font = '14px var(--font-sans)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText('No data in range', input.widthPx / 2, input.heightPx / 2);
}

function formatMs(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}
```

The chart shows the histogram as bars; percentile lines overlay as dashed verticals; budget lines overlay as solid colored verticals; the upper-right corner shows the summary stats.

### 10.4 Pair Matrix

The pair matrix on the left side displays pairs sorted by p99 DESC. Each row shows topic, pub→sub, p50, p99, sample count, and is color-coded by whether p99 exceeds the topic's budget.

```vue
<!-- src/components/PublisherSubscriberMatrix.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { LatencyPairSummaryDto, BudgetDto } from '@/types/latency';

const props = defineProps<{
  pairs: LatencyPairSummaryDto[];
  budgets: BudgetDto[];
  selectedPair: LatencyPairSummaryDto | null;
}>();

const emit = defineEmits<{ select: [pair: LatencyPairSummaryDto] }>();

const budgetByTopic = computed(() => Object.fromEntries(props.budgets.map(b => [b.topic, b])));

function exceedsP99Budget(p: LatencyPairSummaryDto): boolean {
  const b = budgetByTopic.value[p.topic];
  return b?.p99BudgetMs !== undefined && b.p99BudgetMs !== null && p.p99Ms > b.p99BudgetMs;
}
</script>

<template>
  <section class="pair-matrix">
    <h3>Worst legs (by p99)</h3>
    <ul class="pair-matrix__list">
      <li
        v-for="(p, i) in pairs"
        :key="`${p.topic}|${p.publisherNode}|${p.subscriberNode}`"
        class="pair-matrix__row"
        :class="{
          'pair-matrix__row--selected': selectedPair === p,
          'pair-matrix__row--over-budget': exceedsP99Budget(p)
        }"
        @click="$emit('select', p)"
      >
        <div class="pair-matrix__topic">{{ p.topic }}</div>
        <div class="pair-matrix__pair">{{ p.publisherNode }} → {{ p.subscriberNode }}</div>
        <div class="pair-matrix__stats">
          <span class="pair-matrix__p99">{{ p.p99Ms.toFixed(1) }}ms p99</span>
          <span class="pair-matrix__count">{{ p.sampleCount.toLocaleString() }}</span>
        </div>
      </li>
    </ul>
  </section>
</template>

<style lang="scss">
.pair-matrix {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1rem;
  overflow-y: auto;
  max-height: 70vh;
  
  &__list { list-style: none; padding: 0; margin: 0.5rem 0 0 0; display: flex; flex-direction: column; gap: 0.25rem; }
  
  &__row {
    padding: 0.5rem;
    border-radius: 6px;
    cursor: pointer;
    border-left: 3px solid transparent;
    &:hover { background: var(--c-bg-subtle); }
    &--selected { background: var(--c-bg-subtle); border-left-color: var(--c-accent); }
    &--over-budget { border-left-color: var(--c-danger); }
  }
  
  &__topic { font-family: var(--font-mono); font-size: 0.75rem; color: var(--c-text-muted); }
  &__pair { font-size: 0.875rem; }
  &__stats { display: flex; justify-content: space-between; font-size: 0.75rem; color: var(--c-text-muted); margin-top: 0.125rem; }
  &__p99 { font-weight: 500; color: var(--c-text); }
}
</style>
```

### 10.5 LatencyOutliersTable

A scrollable table of the top 100 outliers. Each row has a "Show in timeline" button that pivots to TimelineView focused on the outlier's wallclock ± 1 second.

The table is structurally identical to the Trigger Eval table from Phase 8 §8.4. Implementation pattern repeats.

---

## 11. Frontend: Network Topology View

### 11.1 The Graph Model

Nodes are simulation nodes (e.g., `blue-veh-01`, `red-cmd-01`). Edges are (publisher → subscriber) relationships per topic.

A naive rendering — one edge per (topic, publisher, subscriber) tuple — produces dense visual chaos when many topics share routing. Phase 9 collapses by default: one **bundle edge** per (publisher, subscriber) pair, weighted by total message count, with the underlying per-topic edges available on click.

### 11.2 Layout: Force-Directed

A small force-directed layout (Fruchterman-Reingold-ish) runs for ~200 iterations on view open. Nodes repel each other; edges attract endpoints; the result settles into a readable arrangement.

```typescript
// src/rendering/networkGraphLayout.ts

export interface GraphLayoutInput {
  nodes: string[];
  edges: Array<{ from: string; to: string; weight: number }>;
  widthPx: number;
  heightPx: number;
}

export interface LaidOutGraph {
  nodes: Map<string, { x: number; y: number }>;
  edges: Array<{ from: string; to: string; weight: number }>;
}

export function layoutGraph(input: GraphLayoutInput): LaidOutGraph {
  // Initialize positions in a circle
  const positions = new Map<string, { x: number; y: number; vx: number; vy: number }>();
  const radius = Math.min(input.widthPx, input.heightPx) * 0.35;
  const cx = input.widthPx / 2;
  const cy = input.heightPx / 2;
  input.nodes.forEach((n, i) => {
    const angle = (i / input.nodes.length) * Math.PI * 2;
    positions.set(n, {
      x: cx + Math.cos(angle) * radius,
      y: cy + Math.sin(angle) * radius,
      vx: 0, vy: 0,
    });
  });
  
  const k = Math.sqrt((input.widthPx * input.heightPx) / Math.max(input.nodes.length, 1));  // Fruchterman constant
  const iterations = 200;
  const initialTemp = 0.1 * Math.min(input.widthPx, input.heightPx);
  
  for (let iter = 0; iter < iterations; iter++) {
    const temp = initialTemp * (1 - iter / iterations);
    
    // Repulsive forces: each pair pushes apart
    for (const a of input.nodes) {
      const pa = positions.get(a)!;
      pa.vx = 0; pa.vy = 0;
      for (const b of input.nodes) {
        if (a === b) continue;
        const pb = positions.get(b)!;
        const dx = pa.x - pb.x;
        const dy = pa.y - pb.y;
        const dist = Math.max(Math.sqrt(dx * dx + dy * dy), 0.01);
        const repel = (k * k) / dist;
        pa.vx += (dx / dist) * repel;
        pa.vy += (dy / dist) * repel;
      }
    }
    
    // Attractive forces: edges pull endpoints together (weighted by edge weight)
    for (const e of input.edges) {
      const pa = positions.get(e.from);
      const pb = positions.get(e.to);
      if (!pa || !pb) continue;
      const dx = pa.x - pb.x;
      const dy = pa.y - pb.y;
      const dist = Math.max(Math.sqrt(dx * dx + dy * dy), 0.01);
      const weight = Math.min(Math.log10(e.weight + 1), 3);
      const attract = (dist * dist) / k * weight;
      const fx = (dx / dist) * attract;
      const fy = (dy / dist) * attract;
      pa.vx -= fx; pa.vy -= fy;
      pb.vx += fx; pb.vy += fy;
    }
    
    // Apply with temperature cap, clamp to canvas
    for (const n of input.nodes) {
      const p = positions.get(n)!;
      const v = Math.sqrt(p.vx * p.vx + p.vy * p.vy);
      const cap = Math.min(v, temp);
      if (v > 0) { p.x += (p.vx / v) * cap; p.y += (p.vy / v) * cap; }
      p.x = Math.max(40, Math.min(input.widthPx - 40, p.x));
      p.y = Math.max(40, Math.min(input.heightPx - 40, p.y));
    }
  }
  
  const result = new Map<string, { x: number; y: number }>();
  for (const [n, p] of positions) result.set(n, { x: p.x, y: p.y });
  return { nodes: result, edges: input.edges };
}
```

At Phase 9's scale (typically ≤ 30 nodes), this completes in < 50 ms in the browser. Larger fleets would warrant Barnes-Hut spatial subdivision; not built for Phase 9.

### 11.3 NetworkGraphCanvas.vue

Renders the laid-out graph to a canvas. Edges drawn as Bezier curves with width proportional to log(messageCount); nodes drawn as circles with labels.

```typescript
// src/rendering/networkGraphRenderer.ts

import type { LaidOutGraph } from './networkGraphLayout';

export interface GraphRenderInput {
  laidOut: LaidOutGraph;
  selectedEdge: { from: string; to: string } | null;
  hoveredNode: string | null;
  nodeColors: Map<string, string>;
}

export function renderGraph(ctx: CanvasRenderingContext2D, input: GraphRenderInput) {
  ctx.clearRect(0, 0, ctx.canvas.width, ctx.canvas.height);
  
  // Edges first (so nodes overlay)
  for (const e of input.laidOut.edges) {
    drawEdge(ctx, input.laidOut, e, input.selectedEdge);
  }
  
  // Nodes
  for (const [name, pos] of input.laidOut.nodes) {
    drawNode(ctx, name, pos, input.hoveredNode === name, input.nodeColors.get(name) ?? '#5b9dff');
  }
}

function drawEdge(ctx, layout, e, selected) {
  const pa = layout.nodes.get(e.from);
  const pb = layout.nodes.get(e.to);
  if (!pa || !pb) return;
  
  const width = Math.min(Math.max(Math.log10(e.weight + 1) * 1.5, 1), 8);
  const isSelected = selected && selected.from === e.from && selected.to === e.to;
  
  ctx.strokeStyle = isSelected ? '#5b9dff' : 'rgba(255,255,255,0.3)';
  ctx.lineWidth = width;
  
  // Bezier from a to b with offset for direction
  const dx = pb.x - pa.x;
  const dy = pb.y - pa.y;
  const dist = Math.sqrt(dx * dx + dy * dy);
  const ux = -dy / dist;
  const uy = dx / dist;
  const offsetK = 30;
  const cx = (pa.x + pb.x) / 2 + ux * offsetK;
  const cy = (pa.y + pb.y) / 2 + uy * offsetK;
  
  ctx.beginPath();
  ctx.moveTo(pa.x, pa.y);
  ctx.quadraticCurveTo(cx, cy, pb.x, pb.y);
  ctx.stroke();
  
  // Arrowhead at b
  drawArrowhead(ctx, cx, cy, pb.x, pb.y);
}

function drawArrowhead(ctx, cx, cy, tx, ty) {
  const dx = tx - cx, dy = ty - cy;
  const angle = Math.atan2(dy, dx);
  const arrowLen = 8;
  ctx.fillStyle = ctx.strokeStyle;
  ctx.beginPath();
  ctx.moveTo(tx, ty);
  ctx.lineTo(tx - arrowLen * Math.cos(angle - Math.PI / 6), ty - arrowLen * Math.sin(angle - Math.PI / 6));
  ctx.lineTo(tx - arrowLen * Math.cos(angle + Math.PI / 6), ty - arrowLen * Math.sin(angle + Math.PI / 6));
  ctx.closePath();
  ctx.fill();
}

function drawNode(ctx, name, pos, hovered, color) {
  const r = hovered ? 18 : 14;
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.arc(pos.x, pos.y, r, 0, Math.PI * 2);
  ctx.fill();
  
  // Label below
  ctx.font = '12px var(--font-mono)';
  ctx.fillStyle = 'rgba(255,255,255,0.95)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText(name, pos.x, pos.y + r + 4);
}
```

### 11.4 NetworkTopologyView.vue

Manages: layout computation, edge selection, click pivots, and a side panel showing detail when an edge is selected.

```vue
<!-- src/views/NetworkTopologyView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import { layoutGraph } from '@/rendering/networkGraphLayout';
import NetworkGraphCanvas from '@/components/NetworkGraphCanvas.vue';
import BundleModeRequiredBanner from '@/components/BundleModeRequiredBanner.vue';

const route = useRoute();
const router = useRouter();
const api = useApi();
const sessionId = computed(() => route.params.sessionId as string);
const topology = ref<TopologyDto | null>(null);
const selectedEdge = ref<{ from: string; to: string } | null>(null);
const error = ref<{ status: number; message: string } | null>(null);

// Bundle edges by (from, to) pair
const bundledEdges = computed(() => {
  if (!topology.value) return [];
  const map = new Map<string, { from: string; to: string; weight: number; topics: string[] }>();
  for (const e of topology.value.edges) {
    const key = `${e.publisherNode}|${e.subscriberNode}`;
    const existing = map.get(key);
    if (existing) {
      existing.weight += e.messageCount;
      existing.topics.push(e.topic);
    } else {
      map.set(key, { from: e.publisherNode, to: e.subscriberNode, weight: e.messageCount, topics: [e.topic] });
    }
  }
  return Array.from(map.values());
});

const selectedEdgeDetails = computed(() => {
  if (!selectedEdge.value || !topology.value) return null;
  const matching = topology.value.edges.filter(e =>
    e.publisherNode === selectedEdge.value!.from && e.subscriberNode === selectedEdge.value!.to);
  return matching;
});

async function load() {
  try {
    const session = await api.getSession(sessionId.value);
    topology.value = await api.getNetworkTopology(
      sessionId.value,
      new Date(session.startUtc),
      new Date(session.endUtc ?? new Date())
    );
  } catch (err: any) {
    if (err.response?.status === 409) {
      error.value = { status: 409, message: err.response.data?.detail ?? 'Bundle mode required' };
    } else { throw err; }
  }
}

function drillIntoEdge(e: { from: string; to: string; topic?: string }) {
  router.push({
    name: 'replication-latency',
    params: { sessionId: sessionId.value },
    query: { publisherNode: e.from, subscriberNode: e.to, ...(e.topic && { topic: e.topic }) }
  });
}

onMounted(load);
</script>

<template>
  <div class="topology-view">
    <BundleModeRequiredBanner v-if="error?.status === 409" :message="error.message" :session-id="sessionId" />
    <template v-else-if="topology">
      <header><h1>Network topology</h1></header>
      <div class="topology-view__grid">
        <NetworkGraphCanvas
          class="topology-view__canvas"
          :nodes="topology.nodes"
          :edges="bundledEdges"
          :selected-edge="selectedEdge"
          @select-edge="e => selectedEdge = e"
        />
        <section v-if="selectedEdgeDetails" class="topology-view__details">
          <h3>{{ selectedEdge.from }} → {{ selectedEdge.to }}</h3>
          <ul>
            <li
              v-for="t in selectedEdgeDetails"
              :key="`${t.topic}`"
              class="topology-view__topic-row"
            >
              <span class="topology-view__topic">{{ t.topic }}</span>
              <span class="topology-view__count">{{ t.messageCount.toLocaleString() }} messages</span>
              <button @click="drillIntoEdge({ from: t.publisherNode, to: t.subscriberNode, topic: t.topic })">
                Latency →
              </button>
            </li>
          </ul>
        </section>
      </div>
    </template>
  </div>
</template>
```

---

## 12. Frontend: Gap Detection View

A scrollable list of gaps, similar in shape to LatencyOutliersTable. Each row has the gap details and pivot buttons. Implementation pattern repeats from the Latency Outliers Table; details elided.

The view's distinguishing feature: the gap count by tuple **summary panel** showing "tuples with > 0 gaps" sorted by total missing-message count. Engineers start there and drill into specific tuples.

```vue
<!-- src/views/GapDetectionView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import type { GapDto } from '@/types/latency';
import BundleModeRequiredBanner from '@/components/BundleModeRequiredBanner.vue';

const route = useRoute();
const router = useRouter();
const api = useApi();
const sessionId = computed(() => route.params.sessionId as string);
const gaps = ref<GapDto[]>([]);
const error = ref<{ status: number; message: string } | null>(null);

// Tuple summary
const tupleSummary = computed(() => {
  const map = new Map<string, { topic: string; pub: string; sub: string; gapCount: number; missing: bigint }>();
  for (const g of gaps.value) {
    const key = `${g.topic}|${g.publisherNode}|${g.subscriberNode}`;
    const existing = map.get(key);
    if (existing) {
      existing.gapCount += 1;
      existing.missing += BigInt(g.missingCount);
    } else {
      map.set(key, {
        topic: g.topic, pub: g.publisherNode, sub: g.subscriberNode,
        gapCount: 1, missing: BigInt(g.missingCount)
      });
    }
  }
  return Array.from(map.values()).sort((a, b) => Number(b.missing - a.missing));
});

async function load() {
  try {
    const session = await api.getSession(sessionId.value);
    const result = await api.getGaps(sessionId.value, new Date(session.startUtc), new Date(session.endUtc ?? new Date()));
    gaps.value = result.gaps;
  } catch (err: any) {
    if (err.response?.status === 409) {
      error.value = { status: 409, message: err.response.data?.detail ?? 'Bundle mode required' };
    } else { throw err; }
  }
}

function showInTimeline(g: GapDto) {
  const t = new Date(g.resumedAtWallclockUtc).getTime();
  router.push({
    name: 'timeline',
    params: { sessionId: sessionId.value },
    query: {
      from: new Date(t - 5000).toISOString(),
      to:   new Date(t + 1000).toISOString(),
      topic: g.topic, node: g.subscriberNode,
    }
  });
}

onMounted(load);
</script>

<template>
  <div class="gap-view">
    <BundleModeRequiredBanner v-if="error?.status === 409" :message="error.message" :session-id="sessionId" />
    <template v-else>
      <header><h1>Gap detection</h1></header>
      <div class="gap-view__grid">
        <section class="gap-view__tuples">
          <h3>Tuples with gaps</h3>
          <ul>
            <li v-for="t in tupleSummary" :key="`${t.topic}|${t.pub}|${t.sub}`">
              <div class="gap-view__tuple">{{ t.topic }}: {{ t.pub }} → {{ t.sub }}</div>
              <div class="gap-view__meta">{{ t.gapCount }} gaps · {{ t.missing }} messages missing</div>
            </li>
          </ul>
        </section>
        <section class="gap-view__gaps">
          <h3>Gaps ({{ gaps.length }})</h3>
          <table>
            <thead>
              <tr>
                <th>Resumed at</th>
                <th>Topic</th>
                <th>Pub → Sub</th>
                <th>Missing (seq)</th>
                <th>Count</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(g, i) in gaps" :key="i">
                <td>{{ formatTime(g.resumedAtWallclockUtc) }}</td>
                <td>{{ g.topic }}</td>
                <td>{{ g.publisherNode }} → {{ g.subscriberNode }}</td>
                <td>{{ g.previousSequence }}..{{ g.resumedAtSequence - 1n }}</td>
                <td>{{ g.missingCount }}</td>
                <td><button @click="showInTimeline(g)">Timeline →</button></td>
              </tr>
            </tbody>
          </table>
        </section>
      </div>
    </template>
  </div>
</template>
```

---

## 13. FakeNode Network Simulation

Phase 9 endpoints need test data with realistic per-subscriber receive variation. The FakeNode (Phase 2 §10) is extended to simulate per-subscriber delivery latencies.

### 13.1 FakeNetworkModel

```csharp
namespace Tracer.Adapters.Mock;

/// <summary>
/// Adds simulated per-subscriber receive timestamps to events generated by the FakeNode.
/// Models realistic per-link latency distributions and occasional message drops.
/// </summary>
public sealed class FakeNetworkModel
{
    private readonly Random _random;
    private readonly IReadOnlyList<string> _allNodes;
    private readonly Dictionary<(string, string), LinkProfile> _linkProfiles;

    public FakeNetworkModel(IReadOnlyList<string> allNodes, int seed)
    {
        _random = new Random(seed);
        _allNodes = allNodes;
        _linkProfiles = new();
        foreach (var p in allNodes)
            foreach (var s in allNodes)
                if (p != s)
                    _linkProfiles[(p, s)] = GenerateLinkProfile();
    }

    private LinkProfile GenerateLinkProfile()
    {
        // Most links: low latency, low jitter
        // Some links: occasionally a "bad" profile representing distant nodes
        var isBad = _random.NextDouble() < 0.15;
        return new LinkProfile
        {
            BaseLatencyMs   = isBad ? 8.0 + _random.NextDouble() * 12 : 1.5 + _random.NextDouble() * 2.5,
            JitterStdMs     = isBad ? 4.0 : 0.5,
            DropProbability = isBad ? 0.001 : 0.0001,
            SpikeProbability = 0.0005,
            SpikeAdditionalMs = 50 + _random.NextDouble() * 150,
        };
    }

    public IEnumerable<(string subscriberNode, DateTimeOffset receiveWallclock)> SimulateDelivery(
        string publisherNode, DateTimeOffset publishWallclock,
        IReadOnlyList<string> subscriberNodes)
    {
        foreach (var sub in subscriberNodes)
        {
            if (sub == publisherNode)
            {
                // Self-subscribe: near-zero latency
                yield return (sub, publishWallclock + TimeSpan.FromMicroseconds(_random.NextDouble() * 200));
                continue;
            }
            
            var profile = _linkProfiles[(publisherNode, sub)];
            if (_random.NextDouble() < profile.DropProbability) continue;     // simulated drop
            
            var jitter = SampleNormal(_random, 0, profile.JitterStdMs);
            var spike  = _random.NextDouble() < profile.SpikeProbability ? profile.SpikeAdditionalMs : 0;
            var latencyMs = Math.Max(0.1, profile.BaseLatencyMs + jitter + spike);
            yield return (sub, publishWallclock + TimeSpan.FromMilliseconds(latencyMs));
        }
    }

    private static double SampleNormal(Random r, double mean, double stddev)
    {
        var u1 = 1.0 - r.NextDouble();
        var u2 = 1.0 - r.NextDouble();
        var z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return mean + z * stddev;
    }
    
    private sealed record LinkProfile
    {
        public required double BaseLatencyMs { get; init; }
        public required double JitterStdMs { get; init; }
        public required double DropProbability { get; init; }
        public required double SpikeProbability { get; init; }
        public required double SpikeAdditionalMs { get; init; }
    }
}
```

The model is used during bundle-build test fixtures: the FakeNode generates events, the FakeNetworkModel computes per-subscriber receive timestamps, and the resulting bundle has the duplicated rows Phase 9 needs.

### 13.2 Integration into Test Fixtures

`Tracer.Tests.Integration` test fixtures use FakeNetworkModel to populate per-subscriber rows in synthetic bundles. The fixtures are configurable:

- **Healthy network**: all links < 5ms, no drops, no spikes
- **Degraded network**: one link with 20ms+ latency
- **Lossy network**: one link with elevated drop rate
- **Spike scenario**: occasional 100ms+ spikes

Tests assert specific distributional outcomes (e.g., "in degraded scenario, p99 for X→Y exceeds 15ms").

---

## 14. Test Plan for Phase 9

### 14.1 Backend Unit Tests

**WebApi/LatencyDistributionServiceTests.cs**
- Empty event set: returns zero count, empty buckets
- Single sample: returns count=1, p50=p99=p999=sample value
- Uniform distribution of 1000 samples: p50 is near median, p99 near top
- ExcludeSelfSubscribe filter: rows where publisher=subscriber excluded
- Histogram bucket boundaries are correctly logarithmic
- Time range respected
- Topic/pub/sub filters compose with AND

**WebApi/LatencyTimeSeriesServiceTests.cs**
- Bucket size auto-selection across span ranges
- 12-bucket result for 1h session at 5m buckets
- Bucket counts sum to total sample count
- Empty bucket: skipped, not emitted as zero
- Per-bucket p50/p99 plausible against input distribution

**WebApi/LatencyOutlierServiceTests.cs**
- Explicit threshold: returns samples > threshold, sorted DESC by latency
- No explicit threshold + no budget: uses top-0.1%
- Budget with absoluteMaxMs: uses that threshold
- Per-topic budgets applied per-topic (different topics use different thresholds)
- BudgetSource correctly reported ("budget" vs "top-0.1%")

**WebApi/GapDetectionServiceTests.cs**
- Continuous sequence: no gaps
- Single gap (5..7): reports missing=1
- Multiple gaps in same tuple
- First-sample edge case: reports gap from 0 to first (documented behavior)
- Tuple filter: only matching tuple's gaps returned
- Time range respected

**WebApi/TopologyServiceTests.cs**
- Bundle with three nodes A→B, A→C, B→C: returns 3 edges, 3 nodes
- Excludes publisher=subscriber rows
- Message count aggregated correctly per (topic, pub, sub)

**WebApi/BudgetServiceTests.cs**
- Bundle with budgets: returns parsed list
- Bundle without latencyBudgets section: returns empty
- Bundle metadata.json missing: returns empty
- Live mode: returns empty (no registry populated)

**Util/QuantileSinkTests.cs** and **HistogramSinkTests.cs**
- These util tests verify the local-streaming variants used as fallbacks if DuckDB approx_quantile is unavailable for any reason
- (Phase 9 ships with DuckDB-native; these are defensive util implementations)

**WebApi/LatencyEndpointsTests.cs**, **GapEndpointsTests.cs**, **TopologyEndpointsTests.cs**
- Bundle-mode endpoints return 409 in live mode
- Bundle-mode endpoints return 200 with valid data in bundle mode
- Invalid parameters (negative limit, etc.): 400 ProblemDetails
- Time range from > to: 400 or empty result

### 14.2 Backend Integration Tests

**LatencyAnalysisRoundTripTests.cs**
- FakeNode generates events
- FakeNetworkModel populates per-subscriber receive times
- Bundle built; offline viewer opened
- Latency distribution endpoint: returns expected p50/p99 within tolerance
- Time series endpoint: returns expected per-bucket values

**GapDetectionIntegrationTests.cs**
- FakeNetworkModel with elevated drop probability
- Bundle built
- Gap endpoint identifies the dropped sequence numbers
- Gap counts match injected drops

**TopologyIntegrationTests.cs**
- Multi-node FakeNode scenario
- Bundle built
- Topology endpoint: nodes/edges match the configured FakeNode graph

### 14.3 Frontend Unit Tests (Vitest)

**histogramRenderer.spec.ts**
- Empty distribution: shows "No data" message, no crash
- Bars rendered: count matches input bucket count
- Percentile lines drawn at correct x positions (log scale)
- Budget lines drawn when budget supplied
- Summary text in upper right

**latencyTimeSeriesRenderer.spec.ts**
- Two lines (p50, p99) drawn
- Y-axis range covers max p99 value
- Bucket alignment with x-axis

**networkGraphLayout.spec.ts**
- Empty graph: no crash, no nodes
- Single node: positioned at canvas center
- Layout terminates in 200 iterations
- Connected nodes end up nearer than disconnected nodes
- Layout deterministic given same seed (positions stable)

**useLatencyDistribution.spec.ts**
- Reactive: changes to filter → refetch
- Cancellation: previous request aborted on filter change
- Error handling: 409 surfaces as error.status === 409

### 14.4 E2E Tests (Playwright)

```typescript
test('replication latency: drill from matrix to distribution', async ({ page }) => {
  await page.goto('http://localhost:5300/v/latency/test-bundle-session');
  // Pair matrix loads
  await page.waitForSelector('.pair-matrix__row');
  // Click first (worst) pair
  await page.locator('.pair-matrix__row').first().click();
  // Distribution updates
  await page.waitForSelector('.latency-view__pair'); // selected pair indicator
  // Outlier table populated
  await expect(page.locator('.latency-outliers-table tbody tr').first()).toBeVisible();
});

test('outlier pivot to timeline', async ({ page }) => {
  await page.goto('http://localhost:5300/v/latency/test-bundle-session');
  await page.waitForSelector('.latency-outliers-table');
  await page.locator('.latency-outliers-table button:has-text("Timeline")').first().click();
  await expect(page).toHaveURL(/\/v\/timeline\//);
});

test('topology drill to latency', async ({ page }) => {
  await page.goto('http://localhost:5300/v/topology/test-bundle-session');
  await page.waitForSelector('.network-graph-canvas canvas');
  // Click an edge (positions are deterministic; tests use known fixtures)
  await page.locator('.network-graph-canvas canvas').click({ position: { x: 400, y: 300 } });
  // Detail panel opens
  await page.waitForSelector('.topology-view__details');
  await page.locator('.topology-view__details button:has-text("Latency")').first().click();
  await expect(page).toHaveURL(/\/v\/latency\//);
});

test('live mode: bundle required banner', async ({ page }) => {
  // Visiting against the Observer (not offline viewer)
  await page.goto('http://localhost:5200/v/latency/live-session');
  await expect(page.locator('.bundle-mode-required-banner')).toBeVisible();
  await expect(page.locator('text=requires bundle mode')).toBeVisible();
});
```

### 14.5 Performance Tests

- Distribution query, 30-min bundle, single topic: < 500 ms
- Pair listing across full session, ~50 tuples: < 500 ms
- Time series with 12 buckets across full session: < 500 ms
- Outlier query (top 100), per-topic threshold: < 500 ms
- Gap detection on one (topic, pub, sub) with 10K samples: < 1 s
- Topology query: < 200 ms
- Frontend graph layout, 30 nodes / 100 edges: < 50 ms
- Replication Latency View full load: < 2 s cold cache

---

## 15. Phase 9 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `APPROX_QUANTILE` error is unacceptably large on long-tail distributions | Low | Medium | Bench on day 1. If error > 1% on outliers, switch to `QUANTILE_CONT` (exact, slower). Performance budget likely still holds at Phase 9's data sizes. |
| Clock skew between nodes produces misleading latencies | Medium | Medium | Document the assumption. Histogram includes "≤0 ms" bucket so clock-sync error is visible. Customer's PLL targets 1ms; if real skew exceeds that, the histogram reveals it. |
| Real DDS adapter (Phase 11) populates `sequence_number` differently than the FakeNode | Medium | Medium | Document the expected schema (Phase 1 §4.2 already defines it). Phase 11 adapter has explicit tests for sequence-number propagation. |
| Network topology layout is unreadable for very large fleets (> 50 nodes) | Medium | Low | Phase 9 targets typical scale (~30 nodes per architecture §1.1). Beyond that, the layout degrades visually but doesn't break. Future Phase: hierarchical layout, node grouping. |
| Per-topic latency budgets are absent in customer's existing scenario metadata | High | Low | Top-0.1% fallback covers this case. Budget integration is opt-in; Phase 9 works without it. |
| FakeNetworkModel's synthetic distributions don't match real network behavior | Medium | Low | Phase 9 ships with several configurable profiles. Real bundles from Phase 11 onward use real adapter data and are the ground truth. |
| Bundle-mode-only endpoints surprise users who navigate from live observer | Medium | Low | The banner explains it clearly with a CTA to open the bundle. Sessions list shows which sessions have bundles built. |
| `SELECT publisher_node != subscriber_node` filter inadvertently excludes data when subscriber_node is NULL | Low | High | The bundle consolidator (Phase 4) guarantees both publisher_node and subscriber_node are non-null. Schema enforces NOT NULL. Tested. |
| Time-bucketing chooses too-large buckets for sub-minute sessions, leading to a single bucket | Low | Low | `ChooseBucket` handles sub-minute cases with 100ms buckets. Tested. |
| First-sample-edge-case in gap detection floods the gap list | Medium | Medium | The first-sample gap is reported with `previousSequence: 0`, identifiable in the UI. The view filters them by default with a "include subscriber-join 'gaps'" toggle. |

---

## 16. Definition of Done for Phase 9

### Build & Run

- [ ] All new Tracer.WebApi services and endpoints compile clean
- [ ] FakeNetworkModel in Tracer.Adapters.Mock builds clean
- [ ] OpenAPI spec includes all new endpoints
- [ ] TypeScript client regenerates
- [ ] Frontend new views/components build

### Schema

- [ ] `idx_events_topic_pub_sub` created on bundle consolidation
- [ ] No regression on bundle build time

### Backend: Latency

- [ ] `GET /api/latency/distribution`: returns 200 with valid distribution
- [ ] Returns 409 in live observer mode
- [ ] All filter params honored
- [ ] `excludeSelf` defaults to true
- [ ] `GET /api/latency/pairs`: returns sorted-by-p99 list
- [ ] `minSamples` filter respected
- [ ] `GET /api/latency/timeseries`: bucket size auto-selected
- [ ] `GET /api/latency/outliers`: uses budgets when available; top-0.1% otherwise

### Backend: Gaps and Topology

- [ ] `GET /api/gaps`: identifies sequence-number discontinuities
- [ ] First-sample edge case documented behavior
- [ ] `GET /api/topology/network`: returns node/edge graph
- [ ] `GET /api/scenario/budgets`: returns budgets from bundle metadata

### Frontend: Replication Latency View

- [ ] Three-panel layout (pair matrix, distribution+timeseries, outliers)
- [ ] Pair matrix sorted by p99 DESC
- [ ] Over-budget pairs visually distinguished
- [ ] Click pair → distribution and timeseries update
- [ ] Distribution histogram with percentile lines and budget lines
- [ ] Time series shows p50 and p99 over session
- [ ] Outliers table populates; "Show in timeline" pivot works

### Frontend: Network Topology View

- [ ] Force-directed layout settles in < 100 ms
- [ ] Nodes color-keyed consistently with other views
- [ ] Edge weights visible (proportional line widths)
- [ ] Click edge → side panel with per-topic breakdown
- [ ] Drill-into-latency button works

### Frontend: Gap Detection View

- [ ] Two-panel layout: tuple summary + gap list
- [ ] Tuple summary sorted by total missing-count
- [ ] Pivot to timeline works (centered on resumed-at wallclock)

### Bundle-Mode Gate

- [ ] All Phase 9 endpoints return 409 in live mode
- [ ] Frontend banner displays cleanly
- [ ] Banner CTA navigates to bundle picker

### Cross-View Pivots

- [ ] Latency outlier → Timeline focused on event
- [ ] Topology edge → Latency view filtered by pair
- [ ] Gap row → Timeline focused on resumed-at wallclock
- [ ] Saved views work for Phase 9 view types

### Testing

- [ ] All Phase 1-8 tests pass
- [ ] Phase 9 backend unit tests pass (target: 40+)
- [ ] FakeNetworkModel integration tests verify expected distributions
- [ ] Phase 9 frontend unit tests pass
- [ ] At least three Playwright E2E tests pass

### Performance

- [ ] Distribution query: < 500 ms p95
- [ ] Pair listing: < 500 ms p95
- [ ] Time-series query: < 500 ms p95
- [ ] Topology query: < 200 ms p95
- [ ] Replication Latency View full load: < 2 s cold cache

### Documentation

- [ ] `docs/replication-latency.md` explains the view and key concepts (per-subscriber receive times, budgets, outliers)
- [ ] `docs/gap-detection.md` covers the algorithm and first-sample edge case
- [ ] `docs/network-topology.md` documents the graph view
- [ ] `docs/latency-budgets.md` describes how customers declare budgets in scenario metadata
- [ ] `docs/clock-sync-assumptions.md` documents the wall-clock-precision dependency
- [ ] CHANGELOG entry

---

## 17. Handoff to Phase 10

What Phase 10 inherits from Phase 9:

- **The bundle-mode gate pattern**: Phase 10's SQL console may want similar gating depending on which tables it exposes
- **The DuckDB query patterns** with `APPROX_QUANTILE`, `time_bucket`, `LAG OVER`: Phase 10 SQL examples and saved queries can lean on these as reference
- **The performance baseline**: Phase 9 establishes "< 500 ms for analysis queries on 30-min bundles" as the bar
- **Cross-view pivots**: every Phase 9 view pivots back into Timeline; SQL Console queries can offer the same pivots when results contain `event_id` or `publish_wallclock`

What Phase 10 must address that Phase 9 deferred:

- **SQL escape hatch**: arbitrary read-only DuckDB queries against bundle/observer tables
- **Saved queries**: library of common analyses, including Phase 9-style latency queries as reusable templates
- **Bundle library UI improvements**: better browsing, filtering, tagging of accumulated bundles
- **Bundle metadata enrichment**: tagging, descriptions, custom fields

What's now possible after Phase 9:

The performance characterization layer is in place. Engineers can answer:

- "Which topic has the worst p99 latency?" → Pair matrix
- "Is latency degrading over the session?" → Time series
- "Where are the outliers? Show me the worst 100." → Outliers table → Timeline pivot
- "Are we losing messages anywhere?" → Gap detection
- "Which nodes talk to which nodes about what?" → Network topology
- "Are we meeting our latency budgets?" → Pair matrix highlights over-budget pairs

Combined with Phases 5-7 (timeline, causal tree, entity history) and Phase 8 (annotations, saved views), the diagnostic system now covers temporal, causal, entity, performance, and topology dimensions. The remaining phases add an SQL escape hatch (Phase 10) and the real adapter integration (Phase 11) that brings live data into the system end to end.
