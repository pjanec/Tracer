# Tracer — User & Developer Guide

*A practical guide to using Tracer effectively. Companion to the architecture and phase design documents.*

*Audience: developers and engineers who need to understand how the system works in order to use it well — not just where to click.*

---

## How to Read This Document

The guide is in two halves.

**Part I — Tutorial chapters** (§1–§10) walks you through Tracer from first principles: what it is, how its data model is shaped, what each view answers, and how to drive it end-to-end with concrete examples. Read this top to bottom on first contact with the system.

**Part II — Reference appendix** (§A–§E) is the lookup material: the full HTTP API surface, the SQL schemas, configuration knobs, and a recipe book. Skim once, return often.

Throughout, **concrete examples come first**. When the design document says "the events table has a `trace_id` column", we show you the `curl` that queries by it and the JSON you get back.

---

# Part I — Tutorial

## 1. What Tracer Is, in One Page

Tracer captures, stores, and analyzes the event flow of a distributed simulation. It answers four questions that DDS monitoring and log grepping cannot answer cleanly:

1. *What happened in this session?* — timeline across all nodes
2. *Why did this event happen?* — causal trees walking parent-event-id chains
3. *What happened to this entity?* — entity-centric history across the cluster
4. *Did the scenario play out as intended?* — scenario-level narrative views

If you have ever asked "wait, did the red commander even see the blue vehicle spawn?" or "this trigger should have fired — why didn't it?" — Tracer is the tool.

### 1.1 The Mental Model

Three things are worth internalizing before you touch the UI.

**Tracer is decoupled from the simulation it observes.** It does not know about DDS. It does not know about your entity types. Everything flows through abstract `DiagnosticRecord` instances delivered by pluggable adapters. In production a DDS loopback subscriber translates samples into records; in development a mock data source generates records from a scenario script. *The same code paths run in both cases.* If something works in the FakeNode, it will work in production once the adapters are swapped in.

**Wall-clock is the primary axis.** Every record carries `publish_wallclock` (when the publisher created it) and `receive_wallclock` (when the subscriber processed it), both stamped from a PLL-synchronized cluster clock (1 ms target precision). There is no tick-based ordering. If your simulation is variable-rate, this is the only thing that makes sense; if it is lockstep, lockstep is a debug-only concern and Tracer ignores it.

**Tracer never generates trace context.** `trace_id`, `event_id`, and `parent_event_id` are uint64 fields in the *event IDLs of the simulation*. The simulation populates and propagates them — Tracer just consumes whatever it sees. If the simulation's propagation is sloppy, causal trees will have holes; that is an integration-project bug, not a Tracer bug.

### 1.2 The Two Modes

You will operate Tracer in one of two modes. Knowing which one you are in shapes what data is available.

**Live observer mode.** An optional central process (`tracer-observer.exe`) subscribes to data sources in real time, writes to its own DuckDB, and serves the web UI. Use it during sessions when an engineer or instructor wants to watch what is happening *as* it happens. Live mode sees only the observer's own receive times, so per-node replication latency analysis is approximate at best.

**Post-scenario bundle mode.** After a session (or any selected time range), the `tracer-aggregate` CLI builds a *bundle* — a self-contained file with all per-node data for that range, including per-subscriber receive times. The viewer opens the bundle directly; no live cluster needed. This is the richer mode: it preserves per-node observation, which is the data shape that makes replication-latency, gap-detection, and network-topology views meaningful.

A bundle is portable. You can open it on a clean laptop with just the viewer installed — that's the field-support workflow.

```
Live mode:                                 Bundle mode:
                                          
  Simulation nodes ──► DDS                 NAS/Telemetry/{node}/{interval}.zip
        │                                          │
        ▼                                          ▼
  TracerObserver ──► DuckDB ──► Web API     TracerAggregator
                                  │                │
                                  ▼                ▼
                              TracerViewer    .tracerbundle ──► TracerViewer
                                              (events.duckdb,
                                               slow_state.duckdb,
                                               fast_state/*.parquet,
                                               manifests, annotations)
```

The viewer behaves nearly identically in both modes. Live mode adds SSE streaming; bundle mode enables the latency/gap/topology views. Otherwise, same URLs, same queries, same UX.

---

## 2. The Data Model

Everything in Tracer is one of three things, and understanding which is which determines what you can do with it.

### 2.1 Events

Discrete occurrences with trace context. The hot path. Every view in the UI queries events.

An event row looks like this (schema simplified for clarity):

| Column | Type | Notes |
|---|---|---|
| `event_id` | uint64 | unique; serialized as 16-char hex in API/UI |
| `trace_id` | uint64 | causal-chain identifier; 0 means "not on any trace" |
| `parent_event_id` | uint64 | 0 if root, else the `event_id` that caused this |
| `publish_wallclock` | TIMESTAMP_NS | publisher's wall-clock |
| `receive_wallclock` | TIMESTAMP_NS | subscriber's wall-clock |
| `publisher_node` | VARCHAR | which node emitted it |
| `subscriber_node` | VARCHAR | which node captured it (per-node mode only) |
| `topic` | VARCHAR | DDS topic name |
| `sequence_number` | uint64 | per-publisher, per-topic sequence |
| `entity_id` | VARCHAR | nullable; extracted from payload at ingest |
| `owning_player_id` | VARCHAR | nullable; extracted from payload |
| `scenario_phase` | VARCHAR | nullable; extracted from payload |
| `severity` | VARCHAR | nullable; `error` / `warning` / `info` |
| `notable_label` | VARCHAR | nullable; human-readable scenario annotation |
| `payload` | JSON | full event payload |

The first thing you should commit to memory: **`event_id` zero is reserved for "no event"**. A root event has `parent_event_id = 0`. An event with `trace_id = 0` is not on any trace — that's why the "Show causal tree" pivot is disabled for such events.

### 2.2 Slow State Samples

Keyed entity state that changes infrequently — damage state, scenario phase, equipment loadout. Captured fully, queried as a time series per entity.

Carries `trace_id` *when the change was event-triggered*. Does not carry `event_id` or `parent_event_id` (it is not an event). If a damage state change was caused by an incoming hit event, the integration project is responsible for copying that event's `trace_id` into the resulting state sample.

### 2.3 Fast State Samples

High-rate continuous state — positions, orientations, velocities. Lives in **columnar Parquet files, not DuckDB**. This separation is fundamental and worth understanding.

- Events and slow state live in `events.duckdb` / `slow_state.duckdb`. They are on the hot path — every view queries them.
- Fast state lives in `fast_state/{topic}.parquet`. No view queries it by default. The Entity History view fetches it *on demand* when an engineer asks "show me this vehicle's position over time".

The architectural payoff: 100,000 transform samples per second do not slow down the timeline. They sit cold until needed.

Fast state carries **no trace context**. It is too high-rate to make causal queries meaningful, and the propagation cost would be prohibitive.

### 2.4 How These Three Combine

A typical causal story uses all three:

1. A player fires a weapon → emits a `weapons.fire` event (Event, root, fresh `trace_id`).
2. The target vehicle receives the event → emits a `damage.applied` event with same `trace_id`, `parent_event_id = <fire_event_id>` (Event, derived).
3. The vehicle's damage state changes → emits a `vehicle.damage_state` slow state sample with `trace_id = <same>` (Slow State, event-triggered).
4. Meanwhile, the vehicle is continuously emitting transform samples at 60 Hz (Fast State, no trace).

In the Causal Tree view, steps 1–3 are nodes in the tree. In the Entity History view for that vehicle, steps 1–3 appear as markers on the timeline strip; the transform samples are accessible via the fast-state drill-down panel.

---

## 3. Identity and Trace Context — How Causation Works

Tracer's causal-tree story is built entirely on three numeric fields. Spend a minute getting these right.

### 3.1 The Three Fields

```
trace_id          uint64    shared across one causal chain; 0 means not part of a trace
event_id          uint64    unique to this event; the primary key
parent_event_id   uint64    0 if root; otherwise event_id of the directly causing event
```

All three are displayed as 16-character hex throughout the API and UI. `A3F2B4C8D9E0F1A2`, not `11813055717892198562`.

### 3.2 The Propagation Rules — Integration-Project Responsibility

Tracer does not generate trace context. The simulation must follow these rules; if it doesn't, causal trees will be incomplete.

1. **Originating event** (instructor command, scenario trigger, scheduled timer, threshold crossing): fresh `trace_id`, fresh `event_id`, `parent_event_id = 0`.

2. **Derived from one incoming event**: copy the incoming `trace_id`, fresh `event_id`, `parent_event_id = <incoming.event_id>`.

3. **Derived from multiple incoming events (aggregation)**: pick the most causally significant as parent, document the choice. Optional `related_event_ids` extension deferred.

4. **State-mediated causation**: when an incoming event causes a state change that *later* triggers an event emission, the state component must store the causing event's `trace_id` and `event_id`. The downstream event uses those stored values.

5. **Continuous-process events** (physics integration, no discrete cause): root events. Fresh trace, fresh id, parent = 0.

6. **Cross-thread / cross-queue propagation**: work items must carry `trace_id` and triggering `event_id` with them.

7. **Slow state with event trigger**: copy the triggering event's `trace_id`. Otherwise leave it 0.

A wrapper helper that takes trace context as an explicit parameter (rather than ambient state) makes this easier to enforce in code review. Don't infer; pass explicitly.

### 3.3 Quick Sanity Check

If you suspect propagation is broken:

```sql
-- How many events have no trace?
SELECT COUNT(*) AS no_trace
FROM events
WHERE trace_id = 0;

-- How many root events vs derived events?
SELECT
  COUNT(*) FILTER (WHERE parent_event_id = 0) AS roots,
  COUNT(*) FILTER (WHERE parent_event_id != 0) AS derived
FROM events;

-- Find events with a parent_event_id that doesn't match any known event_id
-- (these are orphans — propagation said "this had a parent" but the parent isn't captured)
SELECT e.event_id, e.topic, e.parent_event_id
FROM events e
LEFT JOIN events p ON e.parent_event_id = p.event_id
WHERE e.parent_event_id != 0 AND p.event_id IS NULL
LIMIT 50;
```

Run these in the SQL Console (§7). The accept-criteria threshold for "trace-id-zero events" is < 5% outside of explicitly-rootless topics. Higher than that, the integration project has a propagation gap.

---

## 4. Storage, Sessions, and Capture Intervals

You will see three time concepts in Tracer that are easy to confuse. Get them straight now.

### 4.1 Capture Intervals — How the Agent Rotates Files

Each TracerAgent rotates its local storage on a **fixed-duration wall-clock-aligned interval** (default: 1 hour, configurable). At 14:00:00, 15:00:00, 16:00:00 UTC, the agent:

1. Closes the Appender on the current `events.duckdb` and `slow_state.duckdb`.
2. Finalizes Parquet writers on fast-state files.
3. Writes `manifest.json` with interval metadata.
4. Writes the `_ready` sentinel file (last action — this is the "interval is complete" signal).
5. Opens a new interval directory and resumes capture.

The agent's local directory tree:

```
C:/ProgramData/Tracer/agent/
  intervals/
    20260519T140000Z/             one folder per capture interval
      events.duckdb
      slow_state.duckdb
      fast_state/
        topic_transforms.parquet
        topic_velocities.parquet
      manifest.json
      _ready                       ← written last; absence means recovery needed
    20260519T150000Z/
    20260519T160000Z/              ← currently active
```

After `_ready` is written, the agent calls `POST /api/telemetry` on the local sync agent, which uploads the interval to NAS as `/Telemetry/{nodeId}/20260519T140000Z.zip`. Tracer does not implement upload, retry, or completion tracking — the sync system handles all of that.

Intervals are *not* session boundaries. Sessions are open-ended; intervals are predictable storage units. A 35-minute session might span one interval; a 4-hour exercise spans four.

### 4.2 Sessions — How They Are Discovered

A session is a **conceptual time range**, not a structural storage unit. It is bounded by `session-start` and `session-end` events the simulation publishes on a dedicated topic:

```
sessionId          (GUID)
scenarioId         (string)
sessionLabel       (string, free-form)
participatingNodes (list, at session-start only)
```

The aggregator and viewer discover sessions by querying for these events across a time range. Each interval's `manifest.json` lists session markers that occurred within it, so the aggregator can find candidate intervals without opening the DuckDB:

```json
{
  "intervalStartUtc": "2026-05-19T14:00:00Z",
  "intervalEndUtc":   "2026-05-19T15:00:00Z",
  "sessionMarkers": [
    { "sessionId": "5b2f...", "type": "start", "wallclock": "2026-05-19T14:03:22.143Z" }
  ]
}
```

**Open-ended sessions** (session-end never observed — the session is still running, or the simulation just didn't emit it) appear as "Active" with `endUtc: null`. The viewer treats them as "ended at last activity" for retrospective views.

### 4.3 Bundles — The Unit of Post-Scenario Analysis

A bundle is what you build for retrospective analysis. Layout:

```
session_20260519_combat.tracerbundle/      directory (optionally zipped)
  manifest.json                            bundleId, time range, source intervals, etc.
  events.duckdb                            consolidated events from all nodes
  slow_state.duckdb                        consolidated slow state
  fast_state/                              optional, scope-controlled
    entity_vehicle_blue_17/
      topic_transforms.parquet
  scenario.json                            phases, objectives, notables
  topology.json                            participating nodes, roles
  content_versions.json                    loaded content packages
  annotations/                             user notes, bookmarks
  source_intervals.json                    provenance: which intervals built this
```

The same event published to multiple subscribers appears once per subscribing node (each with its own `receive_wallclock`). De-duplicate at query time with `DISTINCT ON (event_id, publisher_node)` when you don't care about per-node receive times — and *keep* the duplication when you do (that's how replication-latency analysis works).

Bundles are immutable once built. Cache aggressively. If the schema changes, rebuild from source intervals — they live on NAS for as long as retention allows.

---

## 5. Running Tracer Locally — The FakeNode Workflow

Before touching the real simulation, you exercise the full system against synthetic data. The FakeNode app is the development workhorse.

### 5.1 What FakeNode Is

`tracer-fakenode.exe` is a single-process app that combines:

- MockDataSource (scenario script generator)
- InProcessChannelTransport (no shared memory needed)
- TracerAgent (interval rotation, DuckDB writes)
- LocalFileSystemUploadService (writes interval zips to a configurable "mock NAS" path)
- *Optionally*: TracerObserver + Web API + SPA assets in the same process

It lets you exercise the full ingestion-to-viewer path without DDS, the sync system, NAS, or any production infrastructure.

### 5.2 A Minimal `fakenode.json`

```json
{
  "FakeNode": {
    "ScenarioName": "CombatEngagement",
    "ScenarioConfig": {
      "Duration": "00:30:00",
      "NodeCount": 4,
      "EntityCount": 20,
      "EventsPerSecond": 200,
      "Seed": 42,
      "StartTime": "2026-05-19T14:00:00Z"
    },
    "AgentConfig": {
      "NodeId": "fakenode-01",
      "DataRoot": "C:/Tracer/fakenode/agent",
      "LogsRoot": "C:/Tracer/fakenode/logs",
      "IntervalDuration": "00:15:00",
      "KeepLastNIntervals": 4,
      "Transport": { "Kind": "InProcessChannel", "CapacityRecords": 100000 },
      "UploadService": {
        "Kind": "LocalFileSystem",
        "LocalFileSystemRoot": "C:/Tracer/fakenode/mock-nas/telemetry"
      },
      "Backpressure": {
        "InflightThresholdRecords":     50000,
        "FastStateDropThresholdRecords": 70000,
        "SlowStateDropThresholdRecords": 90000,
        "EventsDropThresholdRecords":    98000
      }
    },
    "Observer": {
      "Enabled":          true,
      "DataRoot":         "C:/Tracer/observer-data",
      "LogsRoot":         "C:/Tracer/observer-logs",
      "HttpPort":         5300,
      "IntervalDuration": "00:15:00"
    }
  }
}
```

A few notes on the knobs:

- **`Seed: 42`** — scenarios are deterministic given a seed. Reruns produce identical data, which makes test assertions possible.
- **`IntervalDuration: "00:15:00"`** — 15 minutes for FakeNode (vs 1 hour in production) so you see rotations during a development session.
- **Backpressure thresholds** — when the in-memory queue grows past these, the agent drops in order: fast state first, then slow state, then (last resort) events. All drops are logged in the manifest's `captureGaps`.
- **`Observer.Enabled: true`** — runs the observer in the same process; you get a Web API on port 5300.

### 5.3 The Demo

```
> tracer-fakenode.exe --config fakenode.json
LOG_FILE=C:/Tracer/fakenode/logs/tracer-fakenode.json
[info] Starting CombatEngagement scenario, 30 min, seed=42
[info] Observer ingestion starting with 1 source(s)
[info] HTTP listening on http://localhost:5300
```

Then in a browser:

1. `http://localhost:5300/` → SPA loads, redirects to `/sessions`
2. Within a few seconds, a session card appears
3. Click it → `/scenario/{sessionId}` opens
4. Watch notable events stream in via SSE; phase changes update live

If you can do that end-to-end, you have a working stack. Everything else is variations.

### 5.4 Multiple FakeNodes for a Multi-Node Cluster

Run multiple instances with different `NodeId`s pointing at the same mock-NAS root. Each writes its own interval files under its node id; the aggregator (§9) reads them all together when building a bundle.

---

## 6. Driving the Viewer — Views in Order of Audience

The viewer's URL is meaningful state. Every view is shareable: copy the URL, paste it in a bug report, your colleague opens it to the same screen.

URL conventions:
```
/sessions                                       Session Browser
/scenario/{sessionId}                           Scenario View
/v/timeline/{sessionId}?from=...&to=...&filter=...&select={eventId}
/v/causal/{eventId}
/v/causal/trace/{traceId}
/v/entity/{entityId}?session={sessionId}&from=...&to=...
/v/latency/{sessionId}                          Replication Latency (bundle mode only)
/v/topology/{sessionId}                         Network Topology (bundle mode only)
/v/gaps/{sessionId}                             Gap Detection (bundle mode only)
/v/triggers/{sessionId}                         Trigger Evaluation Log
/v/sql/{sessionId}                              SQL Console
/v/bundles                                      Bundle Library
```

### 6.1 Scenario View — The Narrative

`/scenario/{sessionId}`. Instructor-facing dashboard. Top: current state (phase, elapsed time, objectives). Middle: phase timeline band. Bottom: notable events stream.

Use it when someone non-technical asks "what's going on?" The notable events stream is scenario-author-controlled — events with a non-null `notable_label` show here. Click a notable → causal-tree pivot at engineer level.

In live mode the notables stream updates via SSE. Click a session card in the browser; this is where you land by default (engineers can flip the persona switcher to land in the Timeline instead — §6.7).

### 6.2 Timeline View — The Engineer's Primary

`/v/timeline/{sessionId}`. Multi-node swimlane. Wall-clock x-axis. Pan with horizontal drag; zoom with mouse wheel centered on cursor. Click an event marker → EventInspector side panel with the full payload.

**Density modes are automatic**:

| Visible span | Mode |
|---|---|
| < 5 min | raw event markers |
| 5–30 min | 5-second aggregate bars |
| 30 min – 4 hr | 30-second aggregate bars |
| > 4 hr | 5-minute aggregate bars |

If you are looking at a 6-hour session at full zoom, the timeline shows 5-minute bucket bars colored by node. Zoom in and individual events appear. Click an aggregate bar → zoom into that bucket. Behind the scenes the frontend calls `/api/events/aggregate` or `/api/events` depending on the bucket choice; you do not need to think about it.

**Filters compose**. Topic AND severity ANDed across filter chips; within a single chip with multiple values, ORed. So `topic=weapons.fire&topic=damage.applied&severity=warning` means `(topic IN ('weapons.fire','damage.applied')) AND severity='warning'`.

Pivots from EventInspector: "Show causal tree" → §6.3; "Filter to this trace" stays in the timeline narrowed to that `trace_id`; "Show entity history" → §6.4.

Live mode adds SSE auto-follow. Pan the view to pin a time range; auto-follow pauses. Click the live-edge button to resume.

### 6.3 Causal Tree View — Why Did This Happen

`/v/causal/{eventId}` or `/v/causal/trace/{traceId}`. Walks the `parent_event_id` chain from one event upward (ancestors) and downward (descendants), or shows all events in a given `trace_id`. Renders as a tree, or a DAG when convergence is detected (two parents → one child).

Each edge is labeled with the wall-clock duration `(child.publish_wallclock - parent.publish_wallclock)`. Nodes are colored by publisher_node — same palette as the timeline.

When a trace exceeds the configured node threshold (5,000 events default), the view shows a focused sub-tree around the selected event and surfaces a truncation notice. Use the "Open ancestors-only" pivot if you only need lineage; "Open descendants-only" if you only need what this event spawned.

The trace summary panel reports:
- Total span (first → last wallclock)
- Participating nodes
- Root and leaf counts
- Whether the result was truncated

### 6.4 Entity History View — What Happened to This Thing

`/v/entity/{entityId}?session={sessionId}`. Four stacked panels:

1. **Lifecycle ribbon** — spawn, ownership transitions, destruction events on a single horizontal band. Spawn topics (configurable per deployment) are detected by the lifecycle classification config; defaults match common conventions (`*.spawn`, `*.created`, etc.).
2. **Slow-state time series** — one row per slow-state topic the entity emitted. Numeric fields plot as stepped lines; categorical fields as colored bands. Hover for values.
3. **Event strip** — every event with `entity_id` matching, as markers along the timeline.
4. **Fast-state drill-down** (collapsed by default) — pick a topic, pick numeric columns, get a time-series chart.

The fast-state drill-down is the only place fast state appears in the UI. Server-side, it reads the per-entity Parquet files and downsamples to ~5,000 points for the visible range. The view notifies you when downsampling is in effect ("Showing 5000 of 108K samples").

Cross-view: from any event marker, "Show causal tree" or "Show in timeline" works. Slow-state events with `trace_id = 0` have the causal-tree pivot disabled (no trace to walk).

### 6.5 Replication Latency, Gap Detection, Network Topology — Bundle Mode Only

These three views require per-subscriber `receive_wallclock` values — which only the bundle has. Open them in live mode and you'll see a clear "Bundle mode required" banner with a link to the bundle picker.

**Replication Latency** (`/v/latency/{sessionId}`):
- Three panels: pair matrix (left, sorted by p99 worst-first), distribution + time series (center), outliers (right)
- Distribution is a logarithmic-bucket histogram with p50/p99/p99.9 dashed lines and budget lines (when declared)
- Time series shows p50 and p99 over the session
- Outliers list with "Show in timeline" pivots — find the worst-latency events and jump to their context

Budgets come from scenario metadata. Pairs over budget are flagged with a red border. Without a budget, a top-0.1% fallback applies.

**Gap Detection** (`/v/gaps/{sessionId}`): per `(topic, publisher_node, subscriber_node)` tuple, finds discontinuities in `sequence_number`. Each gap row gives the missing range and a "Show in timeline" pivot centered on the resumed-at wallclock.

**Network Topology** (`/v/topology/{sessionId}`): force-directed graph of publisher→subscriber edges, weighted by message count. Click an edge → per-topic breakdown with a "Drill into latency" pivot to the Latency view filtered by that pair.

### 6.6 Trigger Evaluation Log — Scenario Author's View

`/v/triggers/{sessionId}`. Tabular view of all `scenario.trigger_evaluated` events. Columns: trigger id, time, result (fired/not-fired), inputs (expandable JSON).

Use it when a scenario author asks "why didn't this trigger fire?" — find the relevant evaluations, see what state values the trigger actually saw, compare against what was expected. Click "Tree" on a fired evaluation to follow what events fired downstream.

### 6.7 The Persona Switcher

A small switcher in the app header. Three personas: **Engineer**, **Scenario Author**, **Operator**. Stored in localStorage; defaults to Engineer.

Changing persona changes the **default landing view from the session browser**:

| Persona | Session card click → |
|---|---|
| Engineer | Timeline |
| Scenario Author | Scenario View |
| Operator | Scenario View |

It also filters the bookmark bar to that persona's bookmarks. Personas are *not* authorization — anyone can switch. They are UX defaults.

---

## 7. The SQL Console — When the Views Don't Answer Your Question

`/v/sql/{sessionId}`. The escape hatch. Plain SQL editor (CodeMirror), read-only access to the bundle's DuckDB, tabular result with optional chart.

### 7.1 What's Queryable

Two tables and a function:

- **`events`** — the events table. Exposed as a DuckDB VIEW over the multi-interval union, so a single query touches all attached intervals in scope.
- **`slow_state`** — the slow state table, same multi-interval shape.
- **`read_parquet('fast_state/...')`** — for fast-state samples, when needed. Restricted to paths inside the bundle.

DuckDB-specific functions you'll use a lot:

- `time_bucket(INTERVAL '5 seconds', publish_wallclock)` — group into time buckets
- `approx_quantile(latency_ms, 0.99)` — fast streaming quantile
- `json_extract_string(payload, '$.fieldName')` — pull a field out of the JSON payload
- `LAG(...) OVER (PARTITION BY ... ORDER BY ...)` — for sequence-number gap analysis

### 7.2 What's Forbidden

The executor validates with a hand-rolled tokenizer before sending to DuckDB. Rejected:

- INSERT, UPDATE, DELETE, MERGE
- CREATE, DROP, ALTER, TRUNCATE
- ATTACH, DETACH
- COPY (to or from)
- PRAGMA (any)
- Multi-statement (`SELECT 1; SELECT 2`)
- `read_csv_auto` / `read_parquet` with paths outside the bundle

The defenses are layered (validator + multi-statement rejection + DuckDB opened in read-only file mode). It is best-effort filtering, not formal proof — an operator with local access can read the bundle directly anyway. The point is preventing accidental damage.

### 7.3 Limits

- Query timeout: 30 s default
- Row limit: 100,000 default (auto-injected as `LIMIT 100000` if you didn't specify)
- Memory limit: 1 GB via `PRAGMA memory_limit` (set by the executor, not by you)

You can override timeout and rows per-query via the request body.

### 7.4 Show SQL — Reverse Engineering a View's Query

Every analytical view has a "Show SQL" button in its toolbar. It opens the SQL Console pre-loaded with the user-friendly equivalent of what the view runs. The actual view SQL is more complex (multi-interval unions, aggregations); the generated SQL is *shape-equivalent* — what a human would write to get roughly the same result.

This is the on-ramp from "I see what I want, but the view doesn't show it the way I need" to "let me write it myself".

### 7.5 Examples

Find the slowest events by replication latency:

```sql
SELECT
  event_id,
  topic,
  publisher_node,
  subscriber_node,
  EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000 AS latency_ms
FROM events
WHERE publisher_node != subscriber_node
ORDER BY latency_ms DESC
LIMIT 50;
```

Distribution of trace fan-out (how many events per trace):

```sql
SELECT trace_id, COUNT(*) AS event_count
FROM events
WHERE trace_id != 0
GROUP BY trace_id
ORDER BY event_count DESC
LIMIT 20;
```

Find unmatched parent references (orphan children — propagation broke somewhere):

```sql
SELECT e.event_id, e.topic, e.parent_event_id, e.publisher_node
FROM events e
LEFT JOIN events p ON e.parent_event_id = p.event_id
WHERE e.parent_event_id != 0 AND p.event_id IS NULL
LIMIT 100;
```

Event rate per node per minute:

```sql
SELECT
  time_bucket(INTERVAL '1 minute', publish_wallclock) AS bucket,
  publisher_node,
  COUNT(*) AS events
FROM events
GROUP BY bucket, publisher_node
ORDER BY bucket, publisher_node;
```

Sequence gaps for one tuple (manual gap detection):

```sql
WITH seqs AS (
  SELECT
    topic, publisher_node, subscriber_node,
    sequence_number, publish_wallclock,
    sequence_number - LAG(sequence_number) OVER (
      PARTITION BY topic, publisher_node, subscriber_node
      ORDER BY sequence_number
    ) AS step
  FROM events
  WHERE topic = 'weapons.fire'
    AND publisher_node = 'blue-cmd-01'
    AND publisher_node != subscriber_node
)
SELECT subscriber_node, sequence_number AS resumed_at_seq,
       step - 1 AS missing_count, publish_wallclock
FROM seqs
WHERE step > 1
ORDER BY publish_wallclock;
```

Pull a value out of the payload JSON:

```sql
SELECT
  publish_wallclock,
  json_extract_string(payload, '$.damage.amount') AS damage,
  json_extract_string(payload, '$.target.entity_id') AS target
FROM events
WHERE topic = 'damage.applied'
LIMIT 20;
```

### 7.6 Saved Queries

The console ships with a small **built-in query library** (top traces by fan-out, slowest pairs, recent errors, etc.). You can save your own, with parameter placeholders:

```sql
SELECT * FROM events
WHERE topic = $topic
  AND publish_wallclock >= $from
  AND publish_wallclock <  $to
ORDER BY publish_wallclock
LIMIT 100;
```

Parameters are bound via `$paramName` placeholders (DuckDB native parameterization). Saved queries appear with an input panel pre-filled with declared defaults.

---

## 8. Annotations, Saved Views, and Bookmarks

User content. Annotations are the first thing Tracer *writes back* — they survive bundle export, live-to-bundle migration, and the offline-viewer round-trip.

### 8.1 Annotations

Attach a note to one of: an event, an entity, a trace, or a free-floating time point.

From any view: click an event → EventInspector → "Add note" → modal opens → type, save. The annotation appears as a subtle marker on the timeline, on the causal tree node, on the entity history strip, wherever that event/entity/trace shows up.

The data shape (from the API):

```json
{
  "annotationId":        "01H8XYZ...",
  "sessionId":           "5b2f...",
  "kind":                "Event",   // "Event" | "Entity" | "Trace" | "TimePoint"
  "eventId":             "A3F2B4C8D9E0F1A2",
  "body":                "This event has the wrong damage value — see #427",
  "title":               "Damage calc bug",
  "tags":                ["bug", "damage-system"],
  "author":              "alice",
  "createdAtUtc":        "2026-05-20T10:15:00Z"
}
```

Exactly one of `eventId` / `entityId` / `traceId` / `targetWallclockUtc` must be set, matching the `kind`. Body length is capped (4 KB).

Author is a display name from a Settings preference. There is no authentication; "author" is whoever the local browser is configured as.

In **bundle mode the API is read-only**. POST/PUT/DELETE return 405. Annotations baked into the bundle came from the live session and survive into the bundle's `annotations/annotations.json` file. The aggregator includes this file in the manifest's checksums.

### 8.2 Saved Views

Bookmark a viewport + filter combination with a label and description. From any analytical view's toolbar: "Save current view" → enter a label (and optional description, tags, persona scope) → save. The URL with all filter state is stored.

`SavedViewsView` at `/v/saved-views/{sessionId}` lists them, grouped by view type, filterable by persona and tag.

The data shape:

```json
{
  "savedViewId":   "01H8XYZ...",
  "label":         "Network errors during engagement phase",
  "description":   "Filter shows only severity=error events between 14:23 and 14:38",
  "viewType":      "timeline",
  "sessionId":     "5b2f...",
  "routePath":     "/v/timeline/5b2f...",
  "queryParams":   { "from": "...", "to": "...", "severity": "error", "topic": "..." },
  "tags":          ["engagement-phase", "errors"],
  "author":        "alice",
  "persona":       "engineer",
  "isBookmark":    false,
  "createdAt":     "2026-05-20T10:15:00Z",
  "lastOpenedAt":  "2026-05-20T11:30:00Z"
}
```

Click → restores the exact state. Edit in place — label, description, tags, bookmark flag.

**Bookmarks** are saved views with `isBookmark: true`. The BookmarkBar component shows recent bookmarks filtered to your current persona, for one-click access.

Like annotations, saved views are exported to the bundle (`annotations/saved_views.json`) and the offline viewer is read-only for them.

---

## 9. Aggregation — Building a Bundle from NAS Data

When the session ends, you build a bundle.

### 9.1 The CLI

`tracer-aggregate.exe` is the field-support tool. It also runs from CI and can be invoked from the Web API.

**By session ID** (most common):

```
tracer-aggregate build \
  --nas-root C:/Tracer/mock-nas \
  --session-id 5b2f0c40-1234-5678-9abc-def012345678 \
  --output C:/bundles/training_run.tracerbundle
```

**By explicit time range**:

```
tracer-aggregate build \
  --nas-root C:/Tracer/mock-nas \
  --time-range "2026-05-19T14:00:00Z..2026-05-19T15:00:00Z" \
  --nodes blue-cmd-01,blue-veh-01 \
  --fast-state selected \
  --fast-state-entities vehicle:blue:17,vehicle:red:03 \
  --output C:/bundles/engagement.tracerbundle.zip
```

Notes on the options:

- `--output ending in .zip` produces a zipped bundle; otherwise a directory.
- `--nodes` restricts which nodes' intervals are pulled in. Default: all available.
- `--fast-state` is one of `none` (events + slow state only — default), `selected` (only listed entities), or `all` (everything). The default of `none` keeps bundles small; explicitly opt in to fast state.
- For `--fast-state selected`, supply `--fast-state-entities` with a comma-separated list.

### 9.2 What the Aggregator Does

1. Queries NAS (via `ITelemetryStorageReader`) for interval zips overlapping the requested time range, per node.
2. Downloads/streams each zip; extracts to a temporary staging directory.
3. Verifies `_ready` exists in each; reads `manifest.json`.
4. **Consolidates events**: union all per-node `events.duckdb` files into the bundle's single `events.duckdb`. Each row gets a `subscriber_node` column identifying which node observed it. The publisher's once-published sample appears once per subscribing node — this duplication is *intentional* and is what enables replication-latency and gap-detection views.
5. **Consolidates slow state** the same way.
6. **Processes fast state** per the `--fast-state` policy.
7. Writes scenario metadata, topology, content versions, source intervals manifest.
8. Optionally zips the bundle directory.
9. Cleans up staging.

Performance target: bundle build for 8-hour session, all data: < 5 minutes. For 1M-event aggregation: < 60 seconds.

### 9.3 Inspecting and Validating Bundles

```
tracer-aggregate inspect C:/bundles/training_run.tracerbundle
```

Produces:

```
Bundle: training_run.tracerbundle
ID:          01H8XYZ7K3M4P5Q6R7S8T9V0W1
Schema:      v1 (compatible)
Created:     2026-05-20T09:30:00Z by tracer-aggregate 1.0.0 on support-laptop-03
Time range:  2026-05-19T14:03:22Z .. 2026-05-19T14:38:51Z (35m 29s)
Label:       Tuesday morning training run
Session:     5b2f0c40-1234-5678-9abc-def012345678 (combat_engagement_v3)

Statistics:
  Events:               1,247,831
  Slow-state samples:   8,420
  Fast-state rows:      184,200
  Uncompressed bytes:   236.4 MB

Participating nodes (5):
  blue-cmd-01, blue-veh-01, blue-veh-02, red-cmd-01, red-veh-01

Fast-state scope: selected-entities (2 entities: vehicle:blue:17, vehicle:red:03)

Files (8):
  events.duckdb        40.0 MB  a3f2b4c8...
  slow_state.duckdb     0.5 MB  b4c5d6e7...
  scenario.json         4.0 KB  c5d6e7f8...
```

For corruption detection:

```
tracer-aggregate validate C:/bundles/training_run.tracerbundle --strict
```

Without `--strict`, just size checks. With `--strict`, full SHA-256 verification — slower but catches bit rot.

### 9.4 Triggering from the Web API

From the viewer (engineer browsing a live session):

```
POST /api/bundles/build
Content-Type: application/json

{
  "sessionId":        "5b2f0c40-...",
  "fastState":        "selected",
  "fastStateEntities": ["vehicle:blue:17"],
  "label":            "Tuesday morning - engagement phase only"
}
```

Returns `202 Accepted` with a `bundleId`. Poll `GET /api/bundles/{bundleId}/status` for `Queued → InProgress → Completed`. Download with `GET /api/bundles/{bundleId}/download`.

Only one bundle build runs concurrently per Observer; additional requests queue.

### 9.5 Opening a Bundle Offline

`tracer-viewer.exe` is the self-contained packaging — a single folder with the SPA assets and a local backend serving the bundle's DuckDB.

```
tracer-viewer.exe C:/bundles/training_run.tracerbundle
```

Browser opens to the SPA against the bundle. No network needed beyond loopback.

Or run it without args and use drag-and-drop or the BundleOpenView UI.

The Bundle Library view (`/v/bundles`) lists bundles known to this machine, with metadata (label, time range, size, source), tags for organization, import/delete actions, and a one-click "Open" that calls `POST /api/bundles/open`.

---

## 10. Putting It Together — A Worked Diagnostic Session

You will use Tracer in flows, not in single-view sessions. Here are three concrete flows.

### 10.1 "An event looked wrong — what caused it?"

1. You see a strange `damage.applied` event in the Timeline (filtered by `severity=warning`).
2. Click the marker → EventInspector shows the payload. Damage amount looks ten times too high.
3. "Show causal tree" pivot → Causal Tree centered on this event.
4. Walk up: parent is `weapons.fire` from `red-veh-04`; grandparent is `scenario.trigger_evaluated` with `triggerId: red_alpha_engage`.
5. Walk down: a `vehicle.damage_state` slow-state change to "destroyed".
6. Sanity check: in the timeline you would not have seen the slow state change — Timeline doesn't render slow state. But the Causal Tree does.
7. Right-click the trigger evaluation node → "Show in Trigger Log" → see what inputs the trigger saw.

The whole flow is 4–5 clicks, never leaves the bundle, every URL is shareable.

### 10.2 "We're seeing dropped messages — where?"

1. Session ends. Build a bundle for the suspect time range:
   ```
   tracer-aggregate build --session-id ... --output evening_run.tracerbundle
   ```
2. Open it. Navigate to `/v/gaps/{sessionId}`.
3. The summary panel shows tuples with > 0 gaps, sorted by total missing count. `(weapons.fire, red-cmd-01, blue-veh-02)` is at the top with 47 gaps.
4. Click into it. The gap list shows each gap's resumed-at wallclock and missing count.
5. Click "Show in timeline" on the first gap → Timeline at that exact moment.
6. Compare to neighboring activity. You see `subscriber_node = blue-veh-02` had a CPU spike (other topics' gaps too around the same time). Now you have a story to take to ops.

### 10.3 "Why didn't this trigger fire?"

1. Scenario author reports: "the `withdrawal_phase` trigger never fired".
2. Open the bundle. Switch to Scenario Author persona.
3. Navigate to `/v/triggers/{sessionId}`. Filter by `triggerId = withdrawal_phase`.
4. Find the evaluation closest to when it should have fired. Result: "not-fired".
5. Expand inputs. The trigger's condition requires `blue_force_strength < 0.3`; actual input shows `0.31`. Off by 1%.
6. Pivot to causal tree of the evaluation event to confirm the inputs match what the upstream state events said.

The diagnostic does not require log grepping or running the scenario again. The data is in the bundle; the views just turn it into an answer.

---

# Part II — Reference

## A. The Web API Surface

ASP.NET Core 8 minimal APIs. The same surface serves live observer and bundle viewer modes — the underlying data source differs, the API is identical.

All endpoints return `application/json` except SSE streams (`text/event-stream`). Validation errors return 400 ProblemDetails (RFC 7807). Bundle-mode-only endpoints return 409 ProblemDetails in live mode. Bundle annotations/saved-views: writes return 405.

### A.1 Discovery

```
GET  /api/sessions?from={iso}&to={iso}                    list sessions in time range
GET  /api/sessions/{sessionId}                            session detail
GET  /api/topology                                        fleet topology (live: discovered; bundle: from topology.json)
GET  /api/topics                                          topics observed, their schemas
GET  /api/entities?sessionId={...}&topic={...}            entity discovery
GET  /api/health                                          liveness + agent/observer metrics
```

Example — list sessions in a range:

```
GET /api/sessions?from=2026-05-19T00:00:00Z&to=2026-05-20T00:00:00Z

[
  {
    "sessionId":          "5b2f0c40-1234-5678-9abc-def012345678",
    "scenarioId":         "combat_engagement_v3",
    "label":              "Tuesday morning training run",
    "startUtc":           "2026-05-19T14:03:22Z",
    "endUtc":             "2026-05-19T14:38:51Z",
    "status":             "Completed",
    "participatingNodes": ["blue-cmd-01", "blue-veh-01", "red-cmd-01"],
    "eventCount":         1247831
  }
]
```

Session status: `Active` | `Completed` | `Inferred`.

### A.2 Event Queries

```
GET  /api/events?sessionId=...&from=...&to=...&[filters]&limit=5000
GET  /api/events/aggregate?sessionId=...&from=...&to=...&bucketDuration=...&groupBy=...
GET  /api/events/{eventId}
GET  /api/live/events?filter=...                          SSE — new events matching filter
GET  /api/live/notables?sessionId=...                     SSE — only events with notable_label
GET  /api/live/status                                     observer health, lag, drop counters
```

`/api/events` query parameters:

| Parameter | Type | Notes |
|---|---|---|
| `sessionId` | string | **required** — constrains to that session's time range |
| `from`, `to` | ISO 8601 UTC | defaults: session start/end (or "now" for active) |
| `topic` | string | repeatable, OR'd within |
| `node` | string | repeatable; matches publisher_node |
| `publisherNode`, `subscriberNode` | string | explicit filters when needed |
| `traceId` | hex 16-char | filter to one trace |
| `entityId`, `playerId` | string | repeatable |
| `severity` | `info`/`warning`/`error` | repeatable |
| `notablesOnly` | bool | non-null `notable_label` only |
| `search` | string | free-text in payload (slow — last-resort) |
| `limit` | int | default 5000, max 5000 |
| `orderBy` | string | `publish_wallclock` or `publish_wallclock_desc` |

Filter composition: across parameters AND; within a repeated parameter OR.

Example — small slice of recent errors:

```
GET /api/events?sessionId=5b2f...&from=2026-05-19T14:20:00Z&to=2026-05-19T14:30:00Z&severity=error&limit=100

{
  "events": [
    {
      "eventId":          "A3F2B4C8D9E0F1A2",
      "traceId":          "B4C5D6E7F8A9B0C1",
      "parentEventId":    null,
      "publishWallclock": "2026-05-19T14:23:17.143Z",
      "receiveWallclock": "2026-05-19T14:23:17.146Z",
      "publisherNode":    "blue-veh-01",
      "subscriberNode":   "blue-veh-01",
      "topic":            "weapons.fire",
      "sequenceNumber":   1247831,
      "entityId":         "vehicle:blue:17",
      "owningPlayerId":   "player-12",
      "scenarioPhase":    "engagement",
      "severity":         "info",
      "notableLabel":     null,
      "payloadJson":      "{...}"
    }
  ],
  "totalMatching": 4127,
  "returned":      4127,
  "truncated":     false
}
```

`/api/events/aggregate` — time-bucketed counts:

```
GET /api/events/aggregate?sessionId=...&from=...&to=...&bucketDuration=5s&groupBy=node

{
  "bucketDuration": "5s",
  "buckets": [
    {
      "bucketStartUtc": "2026-05-19T14:23:15.000Z",
      "groups": [
        { "groupKey": "blue-cmd-01", "count": 142 },
        { "groupKey": "blue-veh-01", "count": 1187 }
      ],
      "total": 1329
    }
  ]
}
```

`bucketDuration` values: `100ms`, `1s`, `5s`, `30s`, `1m`, `5m`, `30m`, `1h`. `groupBy`: `node` (default), `topic`, `severity`, `none`.

### A.3 Trace Queries

```
GET  /api/traces/{traceId}                                summary: count, span, nodes, depth
GET  /api/traces/{traceId}/events                         all events on the trace, ordered
GET  /api/traces/{traceId}/tree                           causal tree structure
GET  /api/events/{eventId}/trace                          full trace containing this event
GET  /api/events/{eventId}/ancestors                      walk up parent_event_id
GET  /api/events/{eventId}/descendants                    walk down children
```

Example — trace tree:

```
GET /api/traces/B4C5D6E7F8A9B0C1/tree?maxEvents=500

{
  "traceId":  "B4C5D6E7F8A9B0C1",
  "sessionId": "5b2f...",
  "summary": {
    "totalEvents":           247,
    "totalEventsAvailable":  247,
    "truncated":             false,
    "rootCount":             1,
    "leafCount":             34,
    "participatingNodes":    ["blue-veh-01", "red-cmd-01"],
    "firstEventUtc":         "2026-05-19T14:23:17.143Z",
    "lastEventUtc":          "2026-05-19T14:23:19.012Z"
  },
  "nodes": [
    {
      "eventId":          "A3F2B4C8D9E0F1A2",
      "parentEventId":    null,
      "publishWallclock": "2026-05-19T14:23:17.143Z",
      "publisherNode":    "blue-veh-01",
      "topic":            "weapons.fire",
      "severity":         "info"
    }
  ],
  "edges": [
    {
      "fromEventId":  "A3F2B4C8D9E0F1A2",
      "toEventId":    "B5C6D7E8F9A0B1C2",
      "latencyMs":    3.0
    }
  ]
}
```

### A.4 Entity Queries

```
GET  /api/entities?sessionId=...&[topic]&[playerId]&limit=200
GET  /api/entities/{entityId}/summary
GET  /api/entities/{entityId}/events?from=...&to=...
GET  /api/entities/{entityId}/slow-state?from=...&to=...&topic=...
GET  /api/entities/{entityId}/fast-state/topics
GET  /api/entities/{entityId}/fast-state/{topic}/schema
GET  /api/entities/{entityId}/fast-state/{topic}?from=...&to=...&columns=...&maxSamples=5000
```

Example — fast-state drill-down:

```
GET /api/entities/vehicle:blue:17/fast-state/topic_transforms
    ?from=2026-05-19T14:20:00Z&to=2026-05-19T14:30:00Z
    &columns=pos_x,pos_y,pos_z
    &maxSamples=5000

{
  "entityId":     "vehicle:blue:17",
  "topic":        "topic_transforms",
  "columns":      ["publish_wallclock", "pos_x", "pos_y", "pos_z"],
  "rowCount":     5000,
  "totalAvailable": 108000,
  "downsampled": true,
  "samples": [
    ["2026-05-19T14:20:00.000Z", 124.5, 88.2, 0.0],
    ["2026-05-19T14:20:00.100Z", 124.7, 88.2, 0.0],
    ...
  ]
}
```

### A.5 Scenario Queries

```
GET  /api/scenario/phases?sessionId=...                   phase timeline
GET  /api/scenario/notables?sessionId=...&limit=100       notable events (Phase 3)
GET  /api/scenario/triggers?sessionId=...&triggerId=...&result=fired  trigger evaluation log
GET  /api/scenario/objectives?sessionId=...               objective tracker
GET  /api/scenario/state?sessionId=...                    current scenario state
```

### A.6 Stats and Analytics (Bundle Mode)

```
GET  /api/stats/replication-latency/distribution?sessionId=...&topic=...&publisherNode=...&subscriberNode=...
GET  /api/stats/replication-latency/timeseries?sessionId=...&...
GET  /api/stats/replication-latency/outliers?sessionId=...&topic=...&threshold=...
GET  /api/stats/replication-latency/pairs?sessionId=...   per-pair p50/p99 matrix
GET  /api/stats/gaps?sessionId=...&topic=...&publisher=...&subscriber=...
GET  /api/stats/topology?sessionId=...                    nodes + edges with weights
GET  /api/stats/budgets?sessionId=...                     declared latency budgets
GET  /api/stats/event-rate?sessionId=...&groupBy=topic
```

These return 409 ProblemDetails in live mode.

Example — latency distribution:

```
GET /api/stats/replication-latency/distribution
    ?sessionId=...&topic=weapons.fire&publisherNode=blue-cmd-01&subscriberNode=red-veh-02

{
  "sampleCount": 5410,
  "stats":       { "p50": 2.3, "p90": 8.1, "p99": 24.5, "p999": 67.1, "max": 142.0, "mean": 4.7, "stddev": 6.3 },
  "histogram": [
    { "bucketLowMs": 0.5,  "bucketHighMs": 0.59, "count": 12 },
    { "bucketLowMs": 0.59, "bucketHighMs": 0.71, "count": 38 }
  ],
  "budget":      { "thresholdMs": 50.0, "source": "scenario_metadata" }
}
```

### A.7 SQL Console

```
POST /api/sql

{
  "sql":              "SELECT topic, COUNT(*) FROM events GROUP BY topic ORDER BY 2 DESC LIMIT 20",
  "parameters":       {},
  "timeoutSeconds":   30,
  "maxRows":          10000
}

{
  "state":     "Succeeded",
  "columns":   [{ "name": "topic", "duckType": "VARCHAR" }, { "name": "count", "duckType": "BIGINT" }],
  "rows":      [["weapons.fire", 12483], ["damage.applied", 8120]],
  "elapsedMs": 142,
  "truncated": false
}
```

States: `Succeeded`, `Failed`, `Timeout`, `Rejected`. On `Rejected` the result includes `errorMessage` explaining why the guardrail tripped.

### A.8 Bundle Operations

```
POST   /api/bundles/build                                 start a bundle build
GET    /api/bundles                                       list known bundles (Observer + local library)
GET    /api/bundles/library                               library entries with user metadata
GET    /api/bundles/{bundleId}                            manifest
GET    /api/bundles/{bundleId}/status                     build progress
GET    /api/bundles/{bundleId}/download                   stream the bundle zip
DELETE /api/bundles/{bundleId}                            remove from disk
POST   /api/bundles/open                                  open an existing bundle (offline viewer)
PUT    /api/bundles/{bundleId}/metadata                   label/description/tags/archived
POST   /api/bundles/{bundleId}/opened                     record last-opened timestamp
POST   /api/bundles/import                                upload a bundle zip
```

### A.9 Annotations, Saved Views, Saved Queries

```
GET    /api/annotations?sessionId=...&kind=...           list
GET    /api/annotations/{annotationId}                   single
POST   /api/annotations                                  create
PUT    /api/annotations/{annotationId}                   update
DELETE /api/annotations/{annotationId}                   delete

GET    /api/saved-views?sessionId=...&viewType=...&persona=...&isBookmark=true
POST   /api/saved-views
GET    /api/saved-views/{savedViewId}
PUT    /api/saved-views/{savedViewId}
DELETE /api/saved-views/{savedViewId}
POST   /api/saved-views/{savedViewId}/opened             increment open count

GET    /api/saved-queries?favoritesOnly=true
POST   /api/saved-queries
GET    /api/saved-queries/{savedQueryId}
PUT    /api/saved-queries/{savedQueryId}
DELETE /api/saved-queries/{savedQueryId}
```

In bundle mode, all writes (POST/PUT/DELETE) return 405 Method Not Allowed.

### A.10 Configuration

```
GET    /api/config/lifecycle-classification               active lifecycle topic config
POST   /api/config/reload                                 reload from disk (where supported)
```

### A.11 Conventions

- **IDs are hex strings** in API and UI. `event_id`, `trace_id`, `parent_event_id` are uint64 internally, serialized as 16-char uppercase hex.
- **Timestamps are ISO 8601 UTC** with millisecond precision (or finer when the source supports it).
- **Validation errors → 400** with RFC 7807 ProblemDetails.
- **Read-only-mode violations → 405** with a `Detail` field naming the constraint.
- **Bundle-mode-only endpoints in live mode → 409** with a CTA-style `Detail` ("requires bundle mode — build a bundle first").

---

## B. The Schema in Detail

### B.1 The `events` Table

```sql
CREATE TABLE events (
    event_id           UBIGINT,
    trace_id           UBIGINT,
    parent_event_id    UBIGINT,
    publish_wallclock  TIMESTAMP_NS,
    receive_wallclock  TIMESTAMP_NS,
    publisher_node     VARCHAR,
    subscriber_node    VARCHAR,
    topic              VARCHAR,
    sequence_number    UBIGINT,
    entity_id          VARCHAR,
    owning_player_id   VARCHAR,
    scenario_phase     VARCHAR,
    severity           VARCHAR,
    notable_label      VARCHAR,
    payload            JSON
);

CREATE INDEX idx_events_publish_wallclock ON events (publish_wallclock);
CREATE INDEX idx_events_trace_id          ON events (trace_id)         WHERE trace_id != 0;
CREATE INDEX idx_events_entity_id         ON events (entity_id)        WHERE entity_id IS NOT NULL;
CREATE INDEX idx_events_topic_time        ON events (topic, publish_wallclock);
CREATE INDEX idx_events_player_id         ON events (owning_player_id) WHERE owning_player_id IS NOT NULL;
CREATE INDEX idx_events_parent_event_id   ON events (parent_event_id)  WHERE parent_event_id != 0;
-- Bundle-only:
CREATE INDEX idx_events_topic_pub_sub     ON events (topic, publisher_node, subscriber_node);
```

DuckDB's columnar zone maps handle time-range queries without explicit indexes for most cases; the indexes above are for high-frequency point lookups.

### B.2 The `slow_state` Table

```sql
CREATE TABLE slow_state (
    publish_wallclock  TIMESTAMP_NS,
    receive_wallclock  TIMESTAMP_NS,
    publisher_node     VARCHAR,
    subscriber_node    VARCHAR,
    topic              VARCHAR,
    instance_key       VARCHAR,    -- the keyed entity id
    entity_id          VARCHAR,    -- often the same as instance_key
    sequence_number    UBIGINT,
    trace_id           UBIGINT,    -- nullable; set when event-triggered
    payload            JSON
);

CREATE INDEX idx_slow_state_entity_time ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;
```

### B.3 Fast-State Parquet

Per topic per entity per interval:

```
fast_state/{topic}/{entity}/samples.parquet:
  publish_wallclock  TIMESTAMP
  instance_key       VARCHAR
  ... per-topic columns derived from the topic IDL ...
```

A transform topic produces a Parquet with `pos_x`, `pos_y`, `pos_z`, `quat_w`, `quat_x`, `quat_y`, `quat_z`, etc. Numeric columns are excellent compression targets (5–10× vs row-store).

### B.4 Manifest Schemas

Per-interval `manifest.json`:

```json
{
  "intervalStartUtc": "2026-05-19T14:00:00Z",
  "intervalEndUtc":   "2026-05-19T15:00:00Z",
  "nodeId":           "blue-cmd-01",
  "tracerVersion":    "1.0.0",
  "schemaVersion":    1,
  "eventCount":       324817,
  "slowStateCount":   1820,
  "fastStateTopics":  ["topic_transforms", "topic_velocities"],
  "captureGaps":      [],
  "sessionMarkers": [
    { "sessionId": "5b2f...", "type": "start", "wallclock": "2026-05-19T14:03:22.143Z" }
  ]
}
```

Bundle `manifest.json`:

```json
{
  "bundleId":         "01H8XYZ...",
  "schemaVersion":    1,
  "createdAtUtc":     "2026-05-20T09:30:00Z",
  "tracerVersion":    "1.0.0",
  "timeRange":        { "startUtc": "2026-05-19T14:03:22Z", "endUtc": "2026-05-19T14:38:51Z" },
  "participatingNodes": ["blue-cmd-01", "blue-veh-01", "red-cmd-01"],
  "sessionContext": {
    "sessionId":  "5b2f...",
    "scenarioId": "combat_engagement_v3",
    "label":      "Tuesday morning training run"
  },
  "fastStateScope": "selected-entities",
  "annotations":    [],
  "files":          [{ "path": "events.duckdb", "sizeBytes": 41943040, "sha256": "a3f2..." }],
  "sourceIntervals": [
    { "nodeId": "blue-cmd-01", "interval": "20260519T140000Z" }
  ]
}
```

---

## C. Configuration Reference

### C.1 `agent.json` (TracerAgent)

```json
{
  "NodeId":             "blue-cmd-01",
  "DataRoot":           "C:/ProgramData/Tracer/agent",
  "LogsRoot":           "C:/Tracer/logs",
  "IntervalDuration":   "01:00:00",
  "KeepLastNIntervals": 24,

  "Transport":      { "Kind": "SharedMemory", "Name": "Tracer.Agent.SHM" },
  "UploadService":  { "Kind": "SyncSystem",   "SyncMasterUrl": "http://sync-master:5000" },

  "Backpressure": {
    "InflightThresholdRecords":      50000,
    "FastStateDropThresholdRecords": 70000,
    "SlowStateDropThresholdRecords": 90000,
    "EventsDropThresholdRecords":    98000
  },

  "FastStateSampling": {
    "Default":     { "Strategy": "TimeBased", "Hz": 10 },
    "PerTopic":    { "topic_transforms": { "Strategy": "TimeBased", "Hz": 10 } }
  }
}
```

- **Absolute paths only.** No `~`, no relative paths.
- **`IntervalDuration`**: default 1 hour. Anything between 5 min and 4 hr is reasonable; outside that range, reconsider.
- **`KeepLastNIntervals`**: how many completed intervals to keep on local disk. LRU eviction; 24 hourly intervals = 1 day local.
- **`FastStateSampling.Strategy`**: `Full` (every sample), `TimeBased` (every Nth ms — set `Hz`), or `SignificantChange` (delta threshold). Per-topic overrides win over defaults.

### C.2 `observer.json` (TracerObserver)

```json
{
  "DataRoot":         "C:/ProgramData/Tracer/observer",
  "LogsRoot":         "C:/Tracer/observer-logs",
  "HttpPort":         5300,
  "IntervalDuration": "01:00:00",
  "KeepLastNIntervals": 4,

  "DataSources": [
    { "Name": "dds-primary", "Kind": "Dds", "DdsDomain": 42, "TopicFilter": "*" }
  ],

  "LiveStreaming": {
    "PerClientBufferSize":     1000,
    "HeartbeatInterval":       "00:00:15",
    "MaxConcurrentClients":    50
  },

  "LifecycleClassification": {
    "SpawnTopicPatterns":      ["*.spawn", "*.created"],
    "OwnershipTopicPatterns":  ["*.ownership", "*.handover"],
    "DestructionTopicPatterns":["*.destroyed", "*.removed"]
  }
}
```

### C.3 Cross-Cutting Process Requirements

Every long-running Tracer process honors:

- **`LOG_FILE=...` on stdout as the first line** — test harnesses and operators parse this:
  ```
  LOG_FILE=C:\Tracer\logs\agent-2026-05-19.json
  ```
- **Structured JSON logging** — one event per line. Default level `Information`; `Debug` for `Tracer.Ingest`, `Tracer.Storage`, `Tracer.Query` namespaces.
- **`TimeProvider` via DI** — production registers `TimeProvider.System`; tests register a `SimulatedClock`. Used for capture interval rotation, retention checks, heartbeats, performance tests. Pure-labeling code (log timestamps, filenames) is allowed to use `DateTimeOffset.UtcNow` directly.
- **Graceful shutdown via `IHostApplicationLifetime`**: stop accepting work → drain in-flight → persist state (close Appenders, finalize Parquet, write manifest, write `_ready`) → close listeners → flush logs → exit.
- **`TESTING_ENABLED` compile-time gate** — test-only endpoints (`/api/test/*`), failure injection, simulated-clock registration are physically absent from production builds.

---

## D. Performance Targets and Limits

Hard targets at the assumed scale (100M events, 8-hour session, single workstation). Operations violating these are architectural bugs or scope errors.

| Operation | Target |
|---|---|
| Open a session (live or bundle) | < 2 s to first usable view |
| Initial timeline render | < 500 ms |
| Pan/zoom timeline | < 100 ms response |
| Apply a filter | < 300 ms |
| Click event → details | < 100 ms |
| Causal tree expansion | < 500 ms |
| Entity history load | < 500 ms |
| Free-text search across session | < 1 s for first results, streaming after |
| SQL query | no hard target; progress shown |
| Ingestion during live mode | peak event rate × 2 with zero drops |
| Bundle build, 8-hour session, all data | < 5 min |
| Bundle build, 1M events | < 60 s |
| Bundle open, 1 GB | < 3 s |
| Latency distribution query | < 500 ms p95 |
| Gap detection per (topic, pair) | < 1 s |
| Topology query | < 200 ms p95 |
| SQL simple SELECT 1000 rows | < 500 ms p95 |
| SQL aggregate 100k rows | < 2 s p95 |

How the targets are met — the architectural commitments behind the numbers:

- **Backend aggregation at query time.** Views never fetch raw rows when aggregated buckets suffice.
- **Columnar storage + zone maps.** DuckDB handles time-range queries efficiently without explicit indexes for most cases.
- **Targeted indexes** on high-frequency point lookups: `trace_id`, `event_id`, `entity_id`, `parent_event_id`.
- **Time-ordered insertion.** The Appender writes records in publish-time order; range queries become column-pruned block scans.
- **No client-side filtering of bulk data.** All filtering happens in DuckDB.
- **Canvas rendering**, not SVG, for the timeline and causal tree at scale.
- **Immutable bundles → aggressive caching.**
- **SSE incremental updates**, not polling, in live mode.

---

## E. Recipe Book

Common diagnostic flows you will run repeatedly. Each is a curl example or SQL snippet — keep them at hand.

### E.1 List recent sessions

```
curl 'http://localhost:5300/api/sessions?from=2026-05-19T00:00:00Z&to=2026-05-20T00:00:00Z'
```

### E.2 Find all errors in a session

```
curl 'http://localhost:5300/api/events?sessionId=5b2f...&severity=error&limit=500'
```

### E.3 Walk one trace

```
curl 'http://localhost:5300/api/traces/B4C5D6E7F8A9B0C1/tree?maxEvents=500'
```

### E.4 Get one entity's events in a window

```
curl 'http://localhost:5300/api/entities/vehicle:blue:17/events?from=2026-05-19T14:20:00Z&to=2026-05-19T14:30:00Z'
```

### E.5 Get one entity's transform Parquet, downsampled

```
curl 'http://localhost:5300/api/entities/vehicle:blue:17/fast-state/topic_transforms?from=2026-05-19T14:20:00Z&to=2026-05-19T14:30:00Z&columns=pos_x,pos_y,pos_z&maxSamples=5000'
```

### E.6 Get the latency distribution for one pair

```
curl 'http://localhost:5300/api/stats/replication-latency/distribution?sessionId=5b2f...&topic=weapons.fire&publisherNode=blue-cmd-01&subscriberNode=red-veh-02'
```

### E.7 Build a bundle by session id

```
tracer-aggregate build \
  --nas-root C:/Tracer/mock-nas \
  --session-id 5b2f0c40-1234-5678-9abc-def012345678 \
  --output C:/bundles/training_run.tracerbundle
```

### E.8 Validate a bundle's integrity

```
tracer-aggregate validate C:/bundles/training_run.tracerbundle --strict
```

### E.9 Inspect a bundle without opening it in the viewer

```
tracer-aggregate inspect C:/bundles/training_run.tracerbundle
```

### E.10 Stream live notable events

```
curl -N 'http://localhost:5300/api/live/notables?sessionId=5b2f...'

data: {"eventId":"...","label":"Red commander issues advance order","occurredAtUtc":"...","nodeId":"red-cmd-01",...}

data: {"eventId":"...","label":"Blue vehicle destroyed","occurredAtUtc":"...","nodeId":"blue-veh-02",...}
```

### E.11 SQL — top traces by fan-out

```sql
SELECT trace_id, COUNT(*) AS events
FROM events
WHERE trace_id != 0
GROUP BY trace_id
ORDER BY events DESC
LIMIT 20;
```

### E.12 SQL — slowest pairs

```sql
SELECT
  topic, publisher_node, subscriber_node,
  COUNT(*) AS samples,
  APPROX_QUANTILE(EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000, 0.99) AS p99_ms
FROM events
WHERE publisher_node != subscriber_node
GROUP BY topic, publisher_node, subscriber_node
HAVING samples > 100
ORDER BY p99_ms DESC
LIMIT 30;
```

### E.13 SQL — events with broken parent reference

```sql
SELECT e.event_id, e.topic, e.parent_event_id, e.publisher_node
FROM events e
LEFT JOIN events p ON e.parent_event_id = p.event_id
WHERE e.parent_event_id != 0 AND p.event_id IS NULL
LIMIT 100;
```

### E.14 SQL — events per node per minute

```sql
SELECT
  time_bucket(INTERVAL '1 minute', publish_wallclock) AS bucket,
  publisher_node,
  COUNT(*) AS events
FROM events
GROUP BY bucket, publisher_node
ORDER BY bucket, publisher_node;
```

### E.15 SQL — extract a field from the JSON payload

```sql
SELECT
  publish_wallclock,
  publisher_node,
  json_extract_string(payload, '$.damage.amount') AS damage,
  json_extract_string(payload, '$.target.entity_id') AS target
FROM events
WHERE topic = 'damage.applied'
ORDER BY publish_wallclock
LIMIT 100;
```

### E.16 SQL — sequence gaps for a tuple

```sql
WITH seqs AS (
  SELECT
    topic, publisher_node, subscriber_node,
    sequence_number, publish_wallclock,
    sequence_number - LAG(sequence_number) OVER (
      PARTITION BY topic, publisher_node, subscriber_node
      ORDER BY sequence_number
    ) AS step
  FROM events
  WHERE topic = 'weapons.fire'
    AND publisher_node = 'blue-cmd-01'
    AND publisher_node != subscriber_node
)
SELECT subscriber_node,
       sequence_number AS resumed_at_seq,
       step - 1 AS missing_count,
       publish_wallclock
FROM seqs
WHERE step > 1
ORDER BY publish_wallclock;
```

---

## F. Scope Boundaries — What Tracer Does Not Do

Knowing where the system stops keeps you from filing bugs against features that were never built.

**Operational:**
- No real-time alerting (PagerDuty / Slack / etc.). Logs are the contract; downstream pipelines are operator-owned.
- No automated root-cause analysis. Tracer flags; engineers correlate.
- No cross-session analysis. Each analysis is single-session-scoped. (Per-interval data on NAS supports future retroactive indexing.)
- No multi-bundle comparison (yesterday vs today). Future ask if needed.
- No AI/LLM-assisted analysis.
- No bundle versioning or migration — bundles are immutable; rebuild on schema changes.
- No external authorization. The persona switcher is UI default, not access control.
- No authoritative audit trail.

**Scale:**
- Designed for ~200 nodes per fleet. Larger fleets need additional design (sharded aggregator, hierarchical observers).
- Single sync-master, single NAS. No multi-region, no multi-master.

**Adapters:**
- Cyclone DDS only. Other DDS implementations not supported.
- Shared memory IPC only. No alternative transports.
- Local filesystem for bundle library. No cloud object storage.

**Simulation side:**
- Tracer does **not** generate trace context. The simulation's propagation discipline is the integration project's responsibility.
- DDS source-timestamp discipline is the integration project's responsibility.

**UX:**
- Desktop only (latest Edge and Chrome on Windows; Firefox best-effort).
- English only, no i18n.
- Best-effort accessibility, no WCAG certification.
- No mobile or tablet UI.
- No collaborative real-time editing of annotations.

**Replay:**
- No deterministic replay of the simulation. Tracer captures and inspects; it does not reproduce.

If you need one of the above, file it as a feature request against the architectural roadmap — not as a bug.

---

**End of guide.**

*If you find a section unclear or an example that no longer matches behavior, the source-of-truth documents are `tracer_architecture_v1.md` for system-wide decisions and `tracer_phase{N}_design.md` for the per-phase deliverables. `tracer_acceptance_criteria.md` is the structured verification matrix.*
