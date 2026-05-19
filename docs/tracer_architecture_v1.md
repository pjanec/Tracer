# Tracer — Distributed Event Capture & Analysis Platform
## Architecture Design Document — Revision 1

*Companion platform to the distributed game/simulation engine*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · DuckDB storage · Windows · May 2026*

*First revision. Designs the platform end-to-end from initial scope discussion through detailed component decisions. To be refined as implementation begins.*

---

## 1. Purpose and Scope

This document defines the architecture of **Tracer**, a platform for capturing, storing, and analyzing the event flows of a distributed game/simulation engine. Tracer answers the diagnostic questions that DDS monitoring tools and log grepping currently answer painfully:

- *What happened in this session?* — full timeline of events across all nodes
- *Why did this event happen?* — causal trees following parent_event_id chains
- *What happened to this entity?* — entity-centric history across the cluster
- *Did the scenario play out as intended?* — scenario-level narrative views

Tracer is **independent of and decoupled from the simulation it analyzes**. It defines abstract interfaces for data sources, file transport, and storage; production deployments use real DDS, sync system, and NAS adapters; development and testing use in-process mock adapters that exercise the same code paths.

### 1.1 In Scope

- Capture of events and state samples from a running distributed simulation
- Per-node local persistence in time-bucketed storage intervals
- Live central observation during sessions (optional, opt-in)
- Post-scenario aggregation of per-node data into consolidated analysis bundles
- Web-based viewer for engineers, instructors, QA, and field support
- Offline analysis from self-contained bundle files
- Multiple view types (timeline, scenario flow, causal tree, entity history, replication latency)
- Full development decoupling via mock adapters

### 1.2 Out of Scope (explicitly deferred)

- **Authentication / Authorization** — the entire system runs open. To be addressed when the sync system addresses it; same approach.
- **Cross-session analysis** — every analysis is single-session-scoped. A separate tool can be built later if cross-session querying becomes necessary; the per-interval data on NAS supports retroactive indexing.
- **Deterministic replay of simulation** — the simulation is real-time and not deterministic; replay is not a goal. Tracer captures and inspects; it does not reproduce.
- **Live federated query across running nodes** — live mode uses the central observer; post-scenario mode uses NAS files via the sync system. The complicated middle case is not built.
- **Production-grade redaction / privacy controls on bundles** — bundles include all captured data; redaction tooling is a later concern when external sharing becomes routine.

---

## 2. Core Design Principles

- **Decoupled from the simulation.** Tracer never depends on DDS types, simulation types, or any simulation-internal concept. It deals in abstract `DiagnosticRecord` instances delivered through adapter interfaces. Real adapters (DDS, sync, NAS) plug in at integration time; mock adapters drive development and tests.
- **The data is the system's normal operational signal, not error-only data.** "Telemetry" is the right framing — Tracer captures what the system does, not just what goes wrong. This is the sync system's name for the data category as well.
- **Wall-clock is the primary time axis.** The simulation's PLL-synchronized cluster wall-clock (1ms target precision) anchors all views. Tick-based ordering does not apply (simulation is variable-rate; lockstep is debug-only).
- **Trace context is propagated by application code.** `trace_id`, `event_id`, `parent_event_id` are fields in event IDLs, set and propagated by the simulation. Tracer consumes them; it does not generate or infer them. Propagation discipline is the integration project's responsibility.
- **Storage intervals, not session boundaries.** Sessions are open-ended; storage rotation is interval-based (fixed duration, default 1 hour). Sessions are conceptual time ranges discoverable from session-start/session-end events, not structural storage units.
- **The sync system handles file transport.** Per-node data uploads via the sync system's existing Telemetry category. Tracer does not implement upload, retry, resume, completion tracking, or fleet topology — the sync system already does all of this.
- **Two operational modes, not three.** Live observer (live data) and post-scenario aggregation (NAS data). No federated live query across running nodes.
- **Single-session bundles.** Every analysis is bounded by a chosen time range and works against a self-contained bundle. No unified analysis database.
- **Performance is a first-class requirement.** Specific numerical targets per operation (§17). Architecture choices that violate targets are rejected.
- **Fast state is cold storage.** Per-entity transform data is stored separately from events, in columnar Parquet, fetched only on demand for specific entity analysis. Hot query paths never touch fast state.
- **Mock-first development.** The full system is developed and validated against mock adapters before any real adapter exists. Integration with the real simulation, sync, and NAS is a focused effort at the adapter layer, not a system-wide refactor.

---

## 3. Operational Modes

Tracer has two operational modes that share the same data model, query layer, and viewer.

### 3.1 Live Observer Mode

**When:** during scenario sessions, when engineers or instructors want real-time insight.

**How:** the `TracerObserver` process subscribes to live data sources (real DDS topics in production, MockDataSource in development) and writes records to a central DuckDB. The web API serves queries and pushes new events via SSE to the viewer.

**Properties:**
- Opt-in — the observer is not required for a session to run
- Stateless across sessions — if not running, nothing centrally recorded
- Convenience-oriented — the per-node agents are the durable record
- Observer-side receive times only — per-node receive times are not available in this mode

If the observer crashes or restarts, only its local DuckDB is affected. Per-node capture continues independently. The data is recoverable from per-node uploads via aggregation mode.

### 3.2 Post-Scenario Aggregation Mode

**When:** after a session (or any selected time range) for deep analysis.

**How:** the `TracerAggregator` reads per-node per-interval zip files from NAS for the chosen time range, builds a consolidated bundle (a single DuckDB with all nodes' data plus referenced Parquet files for fast state), stores the bundle as a file. The viewer opens the bundle and queries it.

**Properties:**
- Always available (depends only on NAS and sync system)
- Richer data than live mode — per-node receive times preserved, enables replication latency analysis
- Survives observer outages, network partitions, mid-session restarts
- The bundle is portable — can be opened on a separate machine for offline analysis

### 3.3 Offline Bundle Mode

**When:** field support, post-event review, sharing analysis between teams.

**How:** the viewer opens a bundle file directly. No backend connection needed except a local process serving the viewer's queries against the bundle's DuckDB.

**Properties:**
- Fully self-contained — bundle includes schema, scenario metadata, all data
- Works on any machine with the Tracer viewer installed
- No connection to live cluster, observer, NAS, or sync system

The viewer's behavior in Live and Bundle modes is nearly identical. Live mode adds streaming updates; otherwise the same views, the same queries, the same UX.

---

## 4. Terminology

| Term | Definition |
|---|---|
| **Event** | A discrete occurrence in the simulation, published as a DDS sample on an event topic. Has `trace_id`, `event_id`, optional `parent_event_id`, payload, timestamps. |
| **Slow state sample** | A keyed entity state sample that changes infrequently (damage state, scenario phase, equipment loadout). Captured fully. May carry `trace_id` when the change was event-triggered. |
| **Fast state sample** | A keyed entity state sample that changes frequently (positions, orientations, velocities). Captured to cold storage only; queried on demand for specific entity analysis. |
| **trace_id** | uint64. Identifies a causal chain of events. Generated at the originating event of a chain; copied to derived events. Grouping label. |
| **event_id** | uint64. Unique to each event. Used as `parent_event_id` by derived events. |
| **parent_event_id** | uint64 (or 0 for root events). The `event_id` of the event that directly caused this one. Allows reconstructing causal trees within a trace. |
| **publish_wallclock** | Synchronized cluster wall-clock time at which the publisher created the sample. Set by application code; transmitted as the DDS sample's source timestamp via `dds_write_ts()`. |
| **receive_wallclock** | Synchronized cluster wall-clock time at which a subscriber processed the sample. Stamped at receive in application code. |
| **TracerAgent** | The per-node component that captures live data, persists to local DuckDB and Parquet, and hands completed intervals to the sync agent for upload. |
| **TracerObserver** | The optional central process that subscribes to live data sources and serves a real-time view via the web API. Runs only when live observation is wanted. |
| **TracerAggregator** | The post-scenario tool that reads per-node interval zips from NAS and produces a consolidated bundle. CLI and library form. |
| **TracerViewer** | The Vue 3 / TypeScript web application that renders timelines, scenario flow, causal trees, and other analysis views. |
| **Capture interval** | A fixed-duration window (default 1 hour, configurable) for storage rotation on each node. Independent of simulation session boundaries. The TracerAgent rotates DuckDB and Parquet files at interval boundaries. |
| **Session** | A conceptual time range in the data, bounded by session-start and session-end events from the simulation. Sessions are tags, not structural units. A session may span multiple capture intervals. |
| **Telemetry** | The sync system's data category for Tracer's per-interval upload files. New category, to be added to the sync system in a follow-up development cycle. |
| **Bundle** | A self-contained file (or directory, depending on packaging choice) containing all data needed to analyze a chosen time range offline. Includes events, slow state, optionally fast state, manifests, and scenario metadata. |
| **DiagnosticRecord** | Tracer's internal abstract record type, the unit of data flowing through all interfaces. Has subtypes `EventRecord` and `StateSampleRecord`. Independent of DDS or any source type. |

---

## 5. Data Categories

Three data categories Tracer captures. Each has distinct rate, storage, query, and access semantics.

| Category | Source | Rate (typical) | Storage | Query frequency | Inclusion in bundles |
|---|---|---|---|---|---|
| Events | Event topics (per-publisher DDS) | 100-10,000/sec cluster-wide | DuckDB events table | Constantly, every view | Always |
| Slow state samples | Slow-changing keyed state topics | <100/sec cluster-wide | DuckDB slow_state table | Frequently, on entity drill-down | Always |
| Fast state samples | Fast-changing keyed state topics (transforms, etc.) | Up to 100,000/sec cluster-wide | Parquet files per topic per interval | Rarely, only for specific entity analysis | Optional, per request |

### 5.1 Events

Discrete occurrences with trace context. The hot path. Every view in the application queries events.

Schema (per row):

```
event_id           UBIGINT       -- unique
trace_id           UBIGINT       -- causal chain identifier
parent_event_id    UBIGINT       -- 0 if root
publish_wallclock  TIMESTAMP_NS  -- publisher's wall-clock
receive_wallclock  TIMESTAMP_NS  -- subscriber's wall-clock (observer or node)
publisher_node     VARCHAR       -- node identity of publisher
subscriber_node    VARCHAR       -- node identity of capturer (relevant for per-node mode)
topic              VARCHAR       -- DDS topic name
sequence_number    UBIGINT       -- DDS sample sequence per publisher per topic
entity_id          VARCHAR       -- nullable; extracted from payload at ingest
owning_player_id   VARCHAR       -- nullable; extracted from payload at ingest
scenario_phase     VARCHAR       -- nullable; extracted from payload at ingest
severity           VARCHAR       -- nullable; 'error' / 'warning' / 'info' / null
notable_label      VARCHAR       -- nullable; human-readable scenario annotation if set
payload            JSON          -- full event payload
```

Indexes: `trace_id`, `parent_event_id`, `entity_id`, `topic + publish_wallclock`, `owning_player_id`. DuckDB's columnar zone maps handle time-range queries without explicit indexes.

### 5.2 Slow State Samples

Keyed entity state that changes infrequently. Captured fully; queried as a time series per entity.

Schema:

```
publish_wallclock  TIMESTAMP_NS
receive_wallclock  TIMESTAMP_NS
publisher_node     VARCHAR
subscriber_node    VARCHAR
topic              VARCHAR
instance_key       VARCHAR       -- the keyed entity identifier
sequence_number    UBIGINT
trace_id           UBIGINT       -- nullable; set when change was event-triggered
payload            JSON
```

Indexes: `instance_key + publish_wallclock`, `topic`.

### 5.3 Fast State Samples

High-rate continuous state (positions, orientations). Storage strategy diverges from events and slow state:

- **Per topic per interval Parquet file** in the agent's interval directory
- Column-oriented: each field of the payload becomes a column (no JSON blob)
- Excellent compression for numeric time series (5-10x vs row-store)
- Native DuckDB query support via `read_parquet()`

The TracerAgent extracts fast-state samples to typed columns at ingest. Example: a transform topic produces a Parquet with columns `publish_wallclock`, `instance_key`, `pos_x`, `pos_y`, `pos_z`, `quat_w`, `quat_x`, `quat_y`, `quat_z`.

Capture rate is configurable per topic. Options:
- Full capture (every sample)
- Time-based sampling (every Nth millisecond)
- Significant-change sampling (only when delta exceeds threshold)

Default for high-rate transforms: time-based sampling at 10 Hz (every 100ms). Configurable globally and per-topic.

---

## 6. System Architecture

### 6.1 Layered Component View

```
Simulation node
   ↓ DDS publish (events, slow state, fast state)
   ↓ + DDS loopback subscriber for diagnostic capture (eventual production)
   ↓ → translates DDS samples to DiagnosticRecord
   ↓
Shared memory ring buffer (or in-process channel in dev)
   ↓
TracerAgent (separate process from simulation)
   ↓ writes to local DuckDB (events, slow state) + Parquet (fast state)
   ↓ rotates at interval boundary
   ↓ hands completed interval to sync agent
   ↓
Sync system (Telemetry category)
   ↓ chunked HTTP upload to sync master
   ↓ NAS write
   ↓
NAS  /Telemetry/{nodeId}/{intervalTimestamp}.zip
```

Live observation path (parallel, optional, opt-in):

```
Simulation nodes (via DDS)
   ↓
TracerObserver (single central process)
   ↓ subscribes to data sources directly
   ↓ writes to local DuckDB
   ↓
Web API (ASP.NET Core)
   ↓ queries DuckDB
   ↓ SSE/WebSocket for live updates
   ↓
TracerViewer (Vue 3 SPA, served by Web API)
```

Post-scenario analysis path:

```
NAS  /Telemetry/{nodeId}/{intervalTimestamp}.zip files
   ↓
TracerAggregator (CLI or library invocation)
   ↓ pulls relevant intervals via sync system or directly from NAS
   ↓ consolidates per-node data
   ↓ writes bundle (DuckDB + Parquet + metadata)
   ↓
Bundle file (.tracerbundle)
   ↓ opened by
   ↓
TracerViewer (offline mode, served by local backend process)
```

### 6.2 Component Responsibilities

**Simulation (eventual integration)**: publishes events with trace context fields populated; writes captured records to the local shared-memory transport. The DDS adapter layer handles translation. Beyond the trace context discipline and the local diagnostic write, no other code changes in the simulation.

**TracerAgent**: reads records from local transport. Writes events and slow state to per-interval DuckDB files. Writes fast state to per-interval Parquet files. Rotates files at interval boundaries. Calls the sync system's `POST /api/telemetry` to upload completed intervals. Maintains local manifest and retention policy.

**TracerObserver** (optional, opt-in for live mode): subscribes to live data sources via the same `IDiagnosticDataSource` interface used by the agent. Writes to its own DuckDB (separate from agents' files). Serves the web API. Disposable — if it crashes or stops, no data loss because agents are the durable record.

**TracerAggregator** (CLI tool and embedded library): fetches per-node per-interval zips from NAS for a requested time range. Consolidates data into a single DuckDB plus referenced Parquet files. Writes a bundle. Can be invoked from the command line for field support workflows or from the Web API for "build a bundle from this session" actions.

**TracerViewer** (Vue 3 SPA): renders all analysis views. Queries the Web API for data. Implements timeline rendering, causal trees, entity history, scenario flow, and other views described in §16. Identical in live and bundle modes except for streaming updates.

**Web API** (ASP.NET Core, .NET 8): serves the viewer's queries against either a live observer DuckDB or a bundle DuckDB. Provides streaming endpoints for live mode. Hosts the SPA assets.

### 6.3 The Adapter Boundary

Every external dependency Tracer has is mediated by an interface. The same interface has multiple implementations:

| Interface | Mock implementation | Production implementation |
|---|---|---|
| `IDiagnosticDataSource` | `MockDataSource` (scenario generator) | `DdsDiagnosticDataSource` (DDS loopback subscriber) |
| `ITelemetryUploadService` | `LocalFileSystemUploadService` | `SyncSystemUploadService` (REST calls to sync master) |
| `ITelemetryStorageReader` (used by aggregator) | `LocalFileSystemStorageReader` | `NasStorageReader` (SMB or sync-system pull) |
| `IAgentTransport` | `InProcessChannelTransport` | `SharedMemoryTransport` |
| `IClock` | `SimulatedClock` | `SystemClock` |

Mock implementations live in `Tracer.Adapters.Mock`. Production implementations live in their own assemblies (`Tracer.Adapters.DDS`, `Tracer.Adapters.Sync`, etc.). The Tracer.Core assembly references none of them.

---

## 7. Identity and Trace Context

### 7.1 Trace Context Fields

Three uint64 fields are added to every event IDL by the simulation:

```
uint64 trace_id           // shared across one causal chain; 0 means not part of a trace
uint64 event_id           // unique to this event
uint64 parent_event_id    // 0 if root; otherwise event_id of the causing event
```

Slow state samples carry trace_id (when change was event-triggered) but not event_id/parent_event_id.

Fast state samples carry no trace context.

### 7.2 ID Generation

uint64 random generation, seeded per process from `std::random_device` or .NET `RandomNumberGenerator`. Collision probability is negligible at any practical scale (birthday-bound 50% probability at 2^32 ≈ 4 billion generated IDs; for 8.6M IDs/day, expected time between collisions is ~1000 years).

Generator selection: `std::mt19937_64` in C++, `Random.Shared.NextInt64()` in C#, both seeded from OS entropy.

Reserve 0 as the sentinel for "no value" — both for trace_id (event is not part of a trace) and parent_event_id (event is a root). Reject 0 in normal generation.

### 7.3 Propagation Rules

These rules are integration-project responsibility, not Tracer's. Tracer consumes whatever the simulation produces. The rules are documented here as the contract:

1. **Originating events** — when a node emits an event triggered by an external cause (instructor command, scenario trigger, physical interaction, scheduled timer, internal threshold), generate a fresh `trace_id` and `event_id`. Set `parent_event_id` to 0.

2. **Derived events from a single incoming event** — when a node receives an event and emits a derived event in direct response, copy the incoming event's `trace_id`, generate a fresh `event_id`, set `parent_event_id` to the incoming event's `event_id`.

3. **Derived events from multiple incoming events (aggregation)** — pick the most causally significant incoming event as parent. Document the choice. Optionally use a "related_event_ids" extension field for the others (deferred).

4. **State-mediated causation** — when an incoming event causes a state change that later causes an event to be emitted, the state component must store the causing event's `trace_id` and `event_id`. The later event uses those stored values.

5. **Continuous-process events** — events emerging from physics integration or other continuous processes with no discrete cause are root events. Fresh `trace_id`, fresh `event_id`, `parent_event_id = 0`.

6. **Cross-thread / cross-queue propagation** — work items, message queue items, and other async carriers must propagate `trace_id` and the triggering `event_id` to whichever code eventually emits the derived event.

7. **Trace context on slow state** — when a state change is event-triggered, copy the triggering event's `trace_id` into the state sample. Otherwise leave it 0.

8. **Domain attribution is separate** — fields like `player_id`, `weapon_id`, `entity_id` are domain attribution, not trace context. They coexist with trace fields on the same events. The viewer filters on both.

A wrapper helper for event publication (passing trace context as an explicit parameter rather than relying on ambient state) makes propagation discipline easier to enforce in code review.

---

## 8. Storage Architecture

### 8.1 Per-Node Local Storage (TracerAgent)

```
C:/ProgramData/Tracer/agent/
  intervals/
    20260519T140000Z/                    -- one folder per capture interval, ISO 8601 basic format
      events.duckdb                      -- DuckDB, written via Appender during interval
      slow_state.duckdb                  -- DuckDB
      fast_state/
        topic_transforms.parquet
        topic_velocities.parquet
        ...
      manifest.json                      -- interval metadata
      _ready                             -- sentinel: written last, signals interval is complete
    20260519T150000Z/
      ...
    20260519T160000Z/                    -- currently active interval
      events.duckdb                      -- open for Appender writes
      ...
  config.json                            -- agent configuration
  state.db                               -- agent's own SQLite for upload tracking
```

**Interval rotation protocol** at boundary T:

1. Stop Appender on current interval's `events.duckdb` and `slow_state.duckdb` — final flush
2. Finalize Parquet writers on current interval's fast-state files
3. Write `manifest.json` with interval metadata
4. Write `_ready` sentinel file (last action)
5. Open new Appender on next interval's files (in a new folder)
6. Resume capture into new interval

In-memory buffering covers the few milliseconds of rotation. No samples are lost.

**Manifest schema**:

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

`captureGaps` records any time ranges within the interval where capture was paused or dropped (e.g., backpressure events). Empty in healthy operation.

`sessionMarkers` records the session-start and session-end events observed during this interval, allowing the aggregator to identify session boundaries when assembling a bundle.

**Retention**: configurable via `keepLastNIntervals` (default 24 for hourly intervals = 1 day local). LRU eviction when disk drops below watermark. The sync system's existing GC pattern applies.

**Recovery from agent crash**: on restart, the agent scans `intervals/` for folders lacking `_ready`. Each such folder is a potentially-corrupt interval. Recovery:
- DuckDB recovers via its WAL — most data is preserved
- Parquet files written before the crash are intact (per-row-group atomicity)
- The agent finalizes the recovered interval (writes manifest, writes `_ready`), then resumes with a new interval
- Any data lost between last DuckDB checkpoint and crash is reported in the manifest's `captureGaps`

### 8.2 NAS Storage

Per-node, per-interval zips uploaded via the sync system's Telemetry category:

```
/NAS/Telemetry/
  {nodeId}/
    20260519T140000Z.zip
    20260519T150000Z.zip
    ...
```

Each zip contains the interval folder's contents: events.duckdb, slow_state.duckdb, fast_state/*.parquet, manifest.json, _ready.

Zip compression: deflate Optimal. DuckDB files compress modestly (~1.5x); Parquet is already compressed but the JSON manifest and any sparse data benefits.

Upload is triggered by the agent at interval rotation (after `_ready` is written). The sync system handles chunked upload, resume, retry — all existing recordings infrastructure applies.

### 8.3 Bundle Storage

A bundle is the unit of post-scenario analysis. Built by the aggregator; opened by the viewer.

```
session_20260519_combat_engagement.tracerbundle/   -- directory (optionally zipped)
  manifest.json                                    -- bundle metadata
  events.duckdb                                    -- consolidated events from all nodes
  slow_state.duckdb                                -- consolidated slow state
  fast_state/                                      -- only entities the bundle scope includes
    entity_vehicle_blue_17/
      topic_transforms.parquet
    entity_vehicle_red_03/
      topic_transforms.parquet
  scenario.json                                    -- scenario metadata: phases, objectives, notables
  topology.json                                    -- participating nodes and their roles
  content_versions.json                            -- loaded content packages
  annotations/                                     -- user notes, bookmarks
  source_intervals.json                            -- which per-node intervals this bundle was built from
```

**Bundle manifest schema**:

```json
{
  "bundleId":         "01H8XYZ...",
  "schemaVersion":    1,
  "createdAtUtc":     "2026-05-20T09:30:00Z",
  "tracerVersion":    "1.0.0",
  "timeRange": {
    "startUtc": "2026-05-19T14:03:22Z",
    "endUtc":   "2026-05-19T14:38:51Z"
  },
  "participatingNodes": ["blue-cmd-01", "blue-veh-01", "red-cmd-01", "..."],
  "sessionContext": {
    "sessionId":  "5b2f...",
    "scenarioId": "combat_engagement_v3",
    "label":      "Tuesday morning training run"
  },
  "fastStateScope": "selected-entities",         -- or "all" or "none"
  "annotations": [],
  "sourceIntervals": [
    { "nodeId": "blue-cmd-01", "interval": "20260519T140000Z" },
    ...
  ]
}
```

The `.tracerbundle` extension associates with the Tracer viewer. The viewer can open a bundle by directory or by zipped form (auto-extracted on open if needed).

### 8.4 Live Observer Storage

A single DuckDB on the observer machine, written during live mode:

```
C:/ProgramData/Tracer/observer/
  current/
    events.duckdb
    slow_state.duckdb
    ...
  archive/                                       -- previous live sessions, retained per policy
```

Same schema as agent storage. The observer's data is disposable — the durable record is on per-node agents.

### 8.5 Storage Sizing

At assumed worst case of 100M events for an 8-hour session:
- Events DuckDB: 20-50 GB consolidated
- Slow state DuckDB: 1-5 GB
- Fast state Parquet (full capture, all entities): 100-200 GB
- Fast state Parquet (10 Hz sampled, all entities): 10-20 GB

A bundle's size depends heavily on fast-state inclusion scope:
- Events + slow state only: ~50 GB
- + fast state for ~10 entities of interest: ~55 GB
- + fast state for all entities: ~150 GB

These are operational ranges. Tunable via capture sampling rates and bundle inclusion policy.

---

## 9. Capture Intervals and Sessions

### 9.1 Capture Intervals

A capture interval is a fixed-duration storage window managed by the TracerAgent independently of simulation sessions.

**Default duration: 1 hour.** Configurable globally.

**Why fixed duration:**
- Sessions are open-ended; no reliable end-marker
- Predictable upload cadence
- Predictable storage rotation
- Bounded recovery work on agent crash
- Smaller, manageable upload units (one interval, not a full session)

**Boundary timing:** intervals are aligned to wall-clock hours (e.g., 14:00:00, 15:00:00) rather than agent-start time. This makes interval boundaries consistent across nodes and predictable for operators.

**Rotation cost:** intervals rotate without disrupting capture. The brief rotation work (close Appender, write manifest, open new Appender) is on a background thread; in-memory ring buffering covers the rotation latency.

### 9.2 Sessions

A session in Tracer is a tagged time range, not a structural unit.

**Session-start and session-end are events**, published by the simulation on a dedicated session topic. They carry:

```
sessionId         (GUID)
scenarioId        (string)
sessionLabel      (string, free-form, scenario-author-defined)
participatingNodes (list of agentIds — at session-start only)
```

**Discovery from data**: the aggregator and viewer can list sessions by querying for session-start/end events across a time range. Sessions are reconstructed from the data, not from a separate session database.

**Per-interval session markers**: each capture interval's `manifest.json` lists session-start and session-end events that occurred within it. This makes it efficient to find sessions without opening the full DuckDB — the aggregator scans manifests to find candidate intervals.

**Sessions can be inferred** even without explicit start/end events: the viewer offers "show me activity from this time range" as the primary navigation, with "browse by session" as a convenience when session events are present.

**Open-ended sessions** are sessions whose session-end event is missing (because the simulation didn't emit one, or the session is still running). The viewer handles this by showing them as "in progress" or "ended at last activity."

---

## 10. Sync System Integration

Tracer integrates with the sync system as the file transport for per-node interval uploads and as the canonical NAS storage.

### 10.1 The Telemetry Category (Sync System Addendum)

The sync system gains a new data category `Telemetry`, structurally similar to the existing Recordings category but per-interval rather than per-session.

**To be added to sync architecture v2 (deferred to next sync development cycle):**

- New data category `Telemetry` in §5 of the sync architecture document
- New NAS layout `/NAS/Telemetry/{nodeId}/{intervalTimestamp}.zip`
- New API endpoints `/api/telemetry/*` mirroring `/api/recordings/*`
- New `categoryDefaults.Telemetry` and `agentRetention.telemetry` in site config
- Per-interval upload trigger (not per-session-finalize)
- No `_session.json` equivalent — each interval zip is self-marking via embedded `manifest.json`

The structural difference between Recordings (per-session) and Telemetry (per-interval) is real but does not affect the sync system's chunked-upload mechanism — it only affects what triggers an upload and how files are named on NAS.

### 10.2 Tracer's Use of the Sync System

The TracerAgent calls the sync agent (which is also running on the node) via:

```
POST /api/telemetry
Body: { nodeId, intervalTimestamp, files: [{ path, size }, ...] }
```

The sync system creates a Pending upload intent, zips and uploads to NAS in its own time. Tracer doesn't wait — it continues capturing. The local interval files remain on the node until upload completes and retention policy evicts them.

The sync system handles:
- Chunked upload with resume
- Retry on transient failures
- Offline-then-reconnect queueing
- Bandwidth-aware scheduling (CAL pressure, segment cascading if applicable)
- Completion tracking visible to operators
- Idempotent intents across master/agent restarts

Tracer does not reimplement any of this.

### 10.3 Aggregator Access to NAS

The TracerAggregator reads per-node per-interval zips from NAS for a chosen time range. Access can be:

- **Via SMB to the NAS directly** (when the aggregator runs on a machine with SMB access)
- **Via the sync system's master HTTP endpoints** (when the aggregator runs on a machine with only HTTP access to the sync master)

The `ITelemetryStorageReader` interface abstracts this — both options are valid production implementations.

### 10.4 Mock Implementation for Development

Until the sync system's Telemetry category exists, and during ongoing development, the Tracer platform uses mock implementations:

- `LocalFileSystemUploadService` — writes interval zips to a configurable local directory simulating NAS
- `LocalFileSystemStorageReader` — reads from the same directory

The Tracer code paths are identical between mock and real adapters. Integration with the real sync system is a small focused effort at the adapter layer, not a system-wide refactor.

---

## 11. TracerAgent Lifecycle

### 11.1 Startup

1. Read configuration (paths, capture interval duration, retention, fast-state sampling rates).
2. Scan `intervals/` for any folders lacking `_ready` sentinel (potentially incomplete from prior crash).
3. For each such folder: open DuckDB (triggers WAL recovery), finalize Parquet files, write `manifest.json` with `captureGaps` reflecting any unrecovered data, write `_ready` sentinel.
4. Hand any newly-completed-on-recovery intervals to the sync agent for upload (idempotent — if already uploaded, sync handles it).
5. Open new current interval, start Appender writes.
6. Start the local data source subscriber (DDS loopback in production, mock in dev).
7. Start the interval rotation timer.
8. Begin capturing.

### 11.2 Steady-State Operation

- Data source delivers DiagnosticRecord instances via the transport
- Records dispatched by type:
  - Events → buffered → DuckDB Appender → events.duckdb
  - Slow state → buffered → DuckDB Appender → slow_state.duckdb
  - Fast state → routed by topic → Parquet writer → fast_state/{topic}.parquet
- Appender batch flush: every 100ms or 10,000 records, whichever first
- DuckDB checkpoint: every 60 seconds (configurable)
- Parquet row group close: every 64MB or 60 seconds, whichever first

Backpressure handling: if the transport delivers records faster than the agent can write, the in-memory queue grows. When queue exceeds threshold, the agent:
1. Logs warning to operator message queue
2. Drops fast-state samples first (the lowest-value-per-byte category)
3. If still over threshold, drops slow-state samples
4. Events are dropped only as a last resort — they are the most diagnostically valuable

All drops are recorded in the interval's `manifest.json` `captureGaps`.

### 11.3 Interval Rotation

Triggered by wall-clock alignment (e.g., 15:00:00 UTC):

1. Brief in-memory buffering begins (records arriving during rotation are queued)
2. Close current Appender on events.duckdb — final flush
3. Close current Appender on slow_state.duckdb
4. Close all Parquet writers in fast_state/
5. Write manifest.json with full interval metadata
6. Write `_ready` sentinel
7. Open new interval folder with fresh Appender and Parquet writers
8. Flush buffered records into new interval
9. Hand completed interval to sync agent: `POST /api/telemetry`
10. Trigger retention check: evict old intervals if disk watermark approached

Total rotation work: typically <100ms. Buffered records arrive at the new interval with publish_wallclock unchanged, so no time ordering is disturbed.

### 11.4 Shutdown

- Stop the data source subscriber
- Drain in-flight records into the current interval's Appender
- Close Appender, finalize Parquet, write manifest, write `_ready`
- Trigger final sync upload
- Exit

A hard kill (Process.Kill or power loss) leaves the current interval without `_ready`. Recovery on next startup handles this case as described in §11.1.

---

## 12. TracerObserver (Live Mode)

The observer is an optional central process for real-time observation during sessions.

### 12.1 Architecture

```
TracerObserver process (ASP.NET Core, .NET 8, Windows service or console)
  ├─ Data source subscribers (DDS in production, MockDataSource in dev)
  ├─ Ingestion pipeline (Channel<DiagnosticRecord> → DuckDB Appender)
  ├─ Storage: own DuckDB files (separate from agent storage)
  ├─ Web API (REST + SSE/WebSocket)
  └─ Hosts the Vue SPA assets
```

### 12.2 Live Update Streaming

The viewer subscribes to a live event stream via SSE (Server-Sent Events). The observer pushes new events as they arrive. The viewer's timeline auto-follows the live edge unless the user has pinned a time range.

Streaming filtered events only (per the viewer's current filter state) keeps bandwidth bounded even at high event rates.

### 12.3 Live Mode Limitations

- Observer sees only its own receive times, not per-node receive times. Replication latency analysis is approximate.
- Observer subscriptions can miss events if it starts mid-session (late-joiner semantics depend on DDS QoS).
- Observer crash means loss of central data until restart. Per-node agents continue capturing.

For deep analysis, post-scenario aggregation produces a richer dataset including per-node receive times.

### 12.4 Observer Storage Rotation

The observer applies the same interval-based storage rotation as agents. If the observer runs for many hours, old intervals are archived or evicted per retention policy. Each interval's data remains queryable through the API.

---

## 13. TracerAggregator (Post-Scenario)

The aggregator builds a bundle for a chosen time range by reading per-node per-interval zips from NAS.

### 13.1 Inputs

- Time range (start, end UTC)
- Optional: explicit list of participating nodes (default: all nodes that have data overlapping the range)
- Optional: session ID (auto-resolves to time range from session-start/end events)
- Fast-state inclusion policy:
  - `none` — events and slow state only
  - `selected-entities` — fast state only for explicitly-listed entities
  - `all` — fast state for all entities in the range
- Output path for the bundle

### 13.2 Process

1. Query NAS (via `ITelemetryStorageReader`) for interval zips overlapping the time range, per node
2. Download or stream each zip; extract to temporary local staging
3. For each interval, verify `_ready` and read `manifest.json`
4. Consolidate events: union all per-node events.duckdb files into a single bundle events.duckdb
5. Consolidate slow state: same approach
6. Process fast state per inclusion policy:
   - `none`: skip
   - `selected-entities`: copy only Parquet files matching the entity list, organized into bundle's fast_state/ folder
   - `all`: copy all Parquet files
7. Gather scenario metadata, topology, content versions (from a per-session metadata topic if available, or from scenario authoring tools)
8. Write bundle manifest
9. Optionally zip the bundle directory into a single `.tracerbundle` file
10. Clean up staging directory

### 13.3 Consolidation Details

When merging per-node DuckDB files into a bundle DuckDB:

- Each row gains a `subscriber_node` column identifying which node observed the sample
- Per-node receive times are preserved per node
- The publisher's published-once sample appears multiple times (once per subscribing node that captured it) — this is the data shape that enables replication latency analysis
- Bundle queries that don't care about per-node observations can de-duplicate at query time using `DISTINCT ON (event_id, publisher_node)` or similar

This design tradeoff: bundle size grows linearly with subscriber count, but per-node receive time information is preserved without further transformation needed.

For very large bundles where this duplication is wasteful, a future refinement could store the canonical publish data once and per-node receive times in a separate table joined at query time. Not built initially; the simpler consolidation is fine at the assumed scale.

### 13.4 CLI Invocation

```
tracer-aggregate \
    --time-range "2026-05-19T14:00:00..2026-05-19T16:00:00" \
    --nodes blue-cmd-01,blue-veh-01,red-cmd-01 \
    --fast-state selected-entities \
    --entity-list vehicle:blue:17,vehicle:red:03 \
    --output session_20260519_combat.tracerbundle
```

```
tracer-aggregate \
    --session-id 5b2f0c40-... \
    --fast-state all \
    --output session_full.tracerbundle
```

The CLI is the field-support workflow: customer's IT person runs this against their NAS, produces a bundle, sends it to support.

### 13.5 Bundle Building from Web API

The Web API exposes `POST /api/bundles/build` for triggering aggregation. Useful when a user is browsing live sessions in the viewer and wants to snapshot one for offline analysis. Calls the same aggregator logic.

---

## 14. Web API Surface

ASP.NET Core 8 minimal APIs. The same API serves both live observer mode and bundle mode — the underlying data source differs but the surface is identical.

### 14.1 Discovery and Session Listing

```
GET  /api/sessions?from={isoDate}&to={isoDate}                 list sessions in range
GET  /api/sessions/{sessionId}                                  session detail (time range, participating nodes, scenario)
GET  /api/topology                                              fleet topology, node identities, capabilities
GET  /api/topics                                                topics observed, their schemas
GET  /api/entities?type={type}&from={isoDate}&to={isoDate}      entities seen in time range
```

### 14.2 Event Queries

```
GET  /api/events
  ?from={isoDate}&to={isoDate}                                 time range (required)
  &topic={topic}                                               filter by topic
  &publisherNode={nodeId}                                      filter by publishing node
  &subscriberNode={nodeId}                                     filter by subscribing node (per-node mode)
  &traceId={uint64}                                            filter by trace
  &entityId={entityId}                                         filter by entity
  &playerId={playerId}                                         filter by player attribution
  &severity={severity}                                         filter by severity
  &search={text}                                               free-text search in payload (slow)
  &limit={n}&offset={n}                                        pagination
  &aggregateBucket={duration}                                  if set, returns aggregated buckets

GET  /api/events/{eventId}                                      single event detail
```

### 14.3 Trace Queries

```
GET  /api/traces/{traceId}                                      summary: count, span, nodes, depth
GET  /api/traces/{traceId}/events                               all events in the trace, ordered
GET  /api/traces/{traceId}/tree                                 causal tree structure
GET  /api/events/{eventId}/ancestors                            walk up parent_event_id chain
GET  /api/events/{eventId}/descendants                          walk down children chain
```

### 14.4 Entity Queries

```
GET  /api/entities/{entityId}/history?from={isoDate}&to={isoDate}     lifecycle events, slow state changes
GET  /api/entities/{entityId}/events?from={isoDate}&to={isoDate}      events touching this entity
GET  /api/entities/{entityId}/state?from={isoDate}&to={isoDate}&topic={topic}     slow state time series
GET  /api/entities/{entityId}/fast-state?from={isoDate}&to={isoDate}&topic={topic}     fast state Parquet query
```

### 14.5 Scenario Queries

```
GET  /api/scenario/phases?sessionId={sessionId}                 phase timeline
GET  /api/scenario/notables?sessionId={sessionId}               notable events stream
GET  /api/scenario/triggers?sessionId={sessionId}               trigger evaluation log
GET  /api/scenario/objectives?sessionId={sessionId}             objective tracker
```

### 14.6 Statistics and Aggregates

```
GET  /api/stats/event-rate                                      rate over time, per topic/node
GET  /api/stats/replication-latency                             latency distributions
GET  /api/stats/gaps                                            sequence gaps per topic per instance
GET  /api/stats/topology-traffic                                inter-node traffic over time
```

### 14.7 SQL Escape Hatch

```
POST /api/sql                                                    body: { query: "SELECT ..." }
                                                                returns: tabular results
```

Read-only. Limited to a configured time/row budget. Power-user feature for engineers.

### 14.8 Live Mode Streaming

```
GET  /api/live/events?filter=...                                 SSE stream of new events matching filter
GET  /api/live/status                                            observer health, lag, dropped counts
```

### 14.9 Bundle Operations

```
POST /api/bundles/build                                          start aggregation; returns bundleId
GET  /api/bundles/{bundleId}/status                              aggregation progress
GET  /api/bundles/{bundleId}/download                            download completed bundle file
GET  /api/bundles                                                list known bundles
POST /api/bundles/open                                           open an existing bundle file
```

### 14.10 Bookmarks and Annotations

```
GET    /api/annotations?sessionId={sessionId}                    list annotations
POST   /api/annotations                                          create annotation
PUT    /api/annotations/{annotationId}                           update
DELETE /api/annotations/{annotationId}                           delete
```

---

## 15. Viewer Architecture

Vue 3 SPA, TypeScript, Pinia for state management. Served by the Tracer Web API. Canvas-based rendering for performance-sensitive views.

### 15.1 Project Structure

```
tracer-viewer/
  src/
    views/
      ScenarioView.vue              -- scenario flow, instructor-facing
      TimelineView.vue              -- multi-node timeline, engineer-facing
      CausalTreeView.vue            -- trace exploration
      EntityHistoryView.vue         -- per-entity drill-down
      ReplicationLatencyView.vue    -- network/replication analysis
      SqlConsoleView.vue            -- power-user SQL
      SessionBrowserView.vue        -- list and select sessions/bundles
    components/
      TimelineCanvas.vue            -- Canvas-based timeline renderer
      EventInspector.vue            -- detail panel for selected event
      FilterPanel.vue               -- filter UI
      Swimlane.vue                  -- single-node lane
      ...
    stores/
      sessionStore.ts               -- current session/bundle state
      filterStore.ts                -- active filters
      selectionStore.ts             -- selected event(s), entity
      ...
    api/
      tracerClient.ts               -- generated from .NET DTOs via NSwag
    rendering/
      canvasRenderer.ts             -- timeline drawing
      colorScheme.ts                -- consistent color assignment
      aggregator.ts                 -- client-side small aggregations only
```

### 15.2 Rendering Strategy

**Timeline rendering uses HTML5 Canvas2D.** Events are dots or short bars positioned by wall-clock x and node-swimlane y. At any zoom level, rendering touches no more than ~5,000 visible markers — the backend aggregates beyond that.

**No SVG for high-density views.** SVG is fine for static diagrams and chrome but unusable at scale.

**Color scheme**: consistent per-node colors assigned at session load. Trace highlight uses a temporary overlay color. Severity uses red/orange/yellow consistently.

**Hover and click**: hit-testing on Canvas uses a spatial index built per-render. Hover shows a tooltip; click opens the EventInspector panel.

### 15.3 Query Strategy

The viewer never fetches raw event lists at session-overview zoom. Instead:

```
On time-range change:
  computeBucketSize(range) -> seconds | minutes | "raw"
  if bucket == "raw":
    fetch /api/events?from=...&to=...&limit=5000
  else:
    fetch /api/events?from=...&to=...&aggregateBucket=...
  render
```

Bucket sizing thresholds (tuneable):
- Range > 4 hours → aggregateBucket = "5min"
- Range 30min-4hours → aggregateBucket = "30s"
- Range 5-30min → aggregateBucket = "5s"
- Range < 5min → raw events

### 15.4 Live Updates

In live mode, the viewer subscribes to `/api/live/events` SSE with the current filter. New events arrive incrementally and are inserted into the rendered timeline without re-fetching.

Auto-follow mode keeps the latest events visible. Pinning to a time range pauses follow.

### 15.5 Cross-View Navigation

Clicking an event in any view offers contextual navigation:
- "Show in timeline" → opens TimelineView focused at this event
- "Show causal tree" → opens CausalTreeView centered on this event
- "Show entity history" → opens EntityHistoryView for this event's entity
- "Show full trace" → filters TimelineView to this trace_id

State is preserved in the URL — every navigation is shareable.

### 15.6 Shareable URLs

URL schema:

```
/v/timeline?session={sessionId}&from={isoDate}&to={isoDate}&filter={base64}&select={eventId}
/v/causal/{eventId}
/v/entity/{entityId}?from={isoDate}&to={isoDate}
/v/scenario?session={sessionId}
/v/sql?query={base64}
```

Copy URL → paste in bug report, chat, documentation → recipient opens directly to the same view.

---

## 16. View Catalog

Implemented views, organized by audience and capability. See §18 for build sequence — not all views are built immediately.

### 16.1 Scenario View (Instructor-facing — first to build)

Dashboard-style layout showing the scenario as a narrative:
- Top: current session state (phase, time elapsed, objectives, score)
- Middle: phase timeline horizontal band with phase transitions and notable events
- Bottom: notable events list, latest first or chronological — scenario-meaningful text descriptions
- Click any notable event → opens its causal tree at engineer level (engineer drill-down available but not required)

### 16.2 Multi-Node Timeline (Engineer-facing — core view)

- Wall-clock x-axis with pan/zoom
- One swimlane per node
- Events as markers; slow state changes as small markers; fast state not shown here
- Filter panel: topic, entity, trace, player, severity
- Click event → EventInspector side panel with full payload
- "Filter to this trace" / "Show entity history" pivots from any event

### 16.3 Causal Tree View

- Centered on an event or trace
- Walk up parent_event_id chain → ancestors
- Walk down children → descendants
- Render as tree (or DAG when convergence detected, deferred)
- Latency annotations on edges (time between parent and child)
- Per-event node coloring (which node it occurred on)

### 16.4 Entity History View

- Pick entity
- Lifecycle timeline: spawn, ownership changes, destruction
- Events touching the entity
- Slow state changes as time series (per topic)
- Optional fast state drill-down: time-position chart, etc.
- "Show events from this trace" / "Compare with entity X" pivots

### 16.5 Replication Latency View

- Per topic, per publisher, per subscriber
- Latency distribution (histogram, percentiles)
- Outlier highlighting
- Time-series of latency over the session
- Only meaningful in aggregated mode (per-node receive times)

### 16.6 Trigger Evaluation Log (Scenario-author-facing)

- All scenario triggers evaluated during the session
- Trigger ID, evaluated time, inputs, result, what fired next
- Sortable, filterable
- Key for scenario debugging — "why didn't this trigger fire?"

### 16.7 SQL Console (Engineer power-user)

- Plain SQL editor
- Read-only access to DuckDB
- Tabular result with optional chart
- Saved-query library

### 16.8 Session Browser

- List all sessions in the current data source (live observer or bundle library)
- Filter by date, scenario, label
- Open a session → routes to ScenarioView

### 16.9 Bundle Library

- List of known bundles on this machine
- Open / build new / delete
- Metadata: time range, scenario, size, source

---

## 17. Performance Targets

These are hard targets at the assumed scale (100M events, 8-hour session, single workstation hardware). Operations that violate targets are either architectural bugs or scope errors.

| Operation | Target |
|---|---|
| Open a session (live or bundle) | < 2 seconds to first usable view |
| Initial timeline render | < 500ms |
| Pan/zoom timeline | < 100ms response |
| Apply a filter | < 300ms |
| Click event → show details | < 100ms |
| Causal tree expansion | < 500ms |
| Entity history load | < 500ms |
| Free-text search across session | < 1s for first results, streaming after |
| SQL query (engineer escape hatch) | No hard target; show progress |
| Ingestion during live mode | Sustain peak event rate × 2 with zero drops |
| Bundle build (aggregation) for 8-hour session, all data | < 5 minutes |

### 17.1 How Targets Are Met

**Backend aggregation at query time.** Views never fetch raw rows when aggregated buckets suffice. The frontend declares the visible range and resolution; the backend computes appropriate buckets.

**Columnar storage and zone maps.** DuckDB's columnar layout and per-block min/max stats handle time-range queries efficiently without explicit indexes for most cases.

**Targeted indexes.** Only on high-frequency point lookups: `trace_id`, `event_id`, `entity_id`. Other columns rely on zone maps.

**Insertion in time-order.** The Appender writes records in publish-time order. Time-range queries become column-pruned block scans.

**No client-side filtering of bulk data.** All filtering happens in DuckDB. The viewer receives small, already-filtered, already-aggregated result sets.

**Canvas rendering.** Up to thousands of visible markers without DOM overhead.

**Aggressive bundle caching.** Immutable bundles are cacheable indefinitely. Repeat queries cost nothing.

**SSE incremental updates in live mode.** New events flow as they arrive; no re-query polling.

### 17.2 Performance Test Harness

A dedicated performance test suite runs nightly in CI, exercising:

- Ingestion at sustained 100K events/sec for 60 seconds → zero drops
- Query timeline at session-overview zoom across 100M event session → < 1s
- Causal tree expansion on a 1000-event trace → < 500ms
- Filter application on session-overview view → < 300ms
- Bundle build of 1-hour interval set → < 1 minute

Regressions fail the build. Performance erosion is treated as a bug, not technical debt.

---

## 18. Build Sequence

Each phase is independently useful and testable. Audience-driven priority: scenario view (non-technical bystanders) first, then engineer power, then specialized analytics.

**Phase 1 — Core foundation (weeks 1-2).** Tracer.Core project with interfaces, record types, domain models. Tracer.Storage.DuckDB with schema, Appender-based ingestion, basic queries. Tracer.Adapters.Mock with MockDataSource that generates synthetic events from a scenario script. Test harness scaffolding with first integration tests.

**Phase 2 — TracerAgent and FakeNode (weeks 3-4).** Local agent: in-process transport, interval rotation, DuckDB writes, Parquet for fast state, manifest generation, recovery from missing-`_ready`. FakeNode app combining agent + MockDataSource for end-to-end testing without simulation or DDS. Tests: scenario data flows through node-local storage and is queryable.

**Phase 3 — TracerObserver and first viewer (weeks 5-6).** Observer subscribing to MockDataSource (live mode). Web API with event and session query endpoints. Vue scaffold. **First user-facing view: Scenario View.** Designed for instructors and non-technical demos. Provides immediate value to a broader audience.

**Phase 4 — Aggregator and bundles (weeks 7-8).** TracerAggregator reading from mock NAS (LocalFileSystemStorageReader). Bundle format defined and validated. Bundle export from observer. Bundle import in viewer (offline mode). Self-contained viewer packaging (single-folder distributable). Bundle round-trip tests. **First field-support-style workflow demonstrable end-to-end.**

**Phase 5 — Engineer timeline view (weeks 9-10).** Multi-node TimelineView. Canvas rendering. Filters, navigation, payload inspector. SSE for live updates. Engineers begin using Tracer for real diagnostic work. UX iteration based on actual use.

**Phase 6 — Causal tree view (weeks 11-12).** CausalTreeView. Walking parent_event_id chains, rendering trees. Cross-view navigation from timeline → causal tree → entity history. The trace_id/parent_event_id machinery pays off visibly.

**Phase 7 — Entity history and slow state (weeks 13-14).** EntityHistoryView. Slow state time series rendering. Fast state on-demand drilldown from entity view. Validates the separated-storage decision.

**Phase 8 — Trigger evaluation, saved views, bookmarks (weeks 15-16).** Scenario authoring affordances: trigger evaluation log. Saved queries, bookmarks, shareable annotation. Multi-persona polish.

**Phase 9 — Replication latency and stats (weeks 17-18).** Aggregated-mode-specific views: replication latency analysis, gap detection, network topology. Per-node receive time analysis. Performance characterization.

**Phase 10 — SQL console and bundle library (weeks 19-20).** Power-user SQL access. Bundle browsing and management UI.

**Phase 11 — Integration with real adapters (weeks 21+).** Build Tracer.Adapters.DDS (DDS loopback subscribers translating to DiagnosticRecord). Build Tracer.Adapters.Sync (real sync system integration). Build Tracer.Adapters.SharedMemory (production transport). Begin integration testing with the actual simulation. Sync system addendum implemented by the sync team in parallel.

After Phase 11, Tracer is a production-deployable diagnostic platform. Further phases (performance optimization, additional specialized views, alerts, cross-session analysis if needed) are driven by specific operational needs.

---

## 19. Test Harness and Mock Adapters

The test harness is a first-class component, built from week 1, used for development and CI/CD.

### 19.1 Scenario Generators

Mock data generation is **scenario-driven**, not noise-driven. Each scenario produces realistically-shaped data exercising specific aspects.

```csharp
public interface IScenarioGenerator
{
    IAsyncEnumerable<DiagnosticRecord> GenerateAsync(
        ScenarioConfig config,
        IClock clock,
        CancellationToken ct);
}
```

Initial scenario library:

| Scenario | Exercises |
|---|---|
| Calm | Low event rate, single phase, baseline for performance |
| Combat engagement | Bursts of events, causal trees from player actions |
| Multi-node coordination | Cross-node trace propagation, realistic latencies |
| Trace stress | Deeply nested causal chains, wide fan-out |
| Failure modes | Missing parent events, out-of-order arrival, gaps |
| Performance stress | 100K events/sec sustained |
| Long session | Multi-hour scenario, exercises interval rotation |
| Scenario progression | Realistic mission with phases, objectives, triggers |

Scenarios are **deterministic given a seed**. Tests can assert specific properties of the generated data.

### 19.2 Test Fixture

```csharp
public class TracerStackFixture : IAsyncDisposable
{
    public static async Task<TracerStackFixture> CreateAsync(
        string scenario, int seed, FixtureOptions? options = null);

    public async Task RunScenarioAsync(TimeSpan duration);

    public TracerWebApiClient Api { get; }

    public string BundleExportPath { get; }
    public async Task<string> ExportBundleAsync();
    public static async Task<TracerStackFixture> OpenBundleAsync(string path);
}
```

A fixture spins up the in-process stack (mock data source, agent, observer, Web API) and exposes them for tests. Tests interact via the Web API client as the frontend would.

### 19.3 Test Categories

**Unit tests** — per-component, mocked dependencies. Tracer.Core has no infrastructure deps, so its tests are pure.

**Integration tests** — full stack with mock adapters. Each test takes <1 second. Run on every PR.

**Performance tests** — high-volume scenarios with throughput and latency assertions. Run nightly in CI. Regressions fail the build.

**Bundle round-trip tests** — export → open → validate identical query results. Run on every PR.

**Frontend tests** — Vitest for components, Playwright for end-to-end. Run on frontend changes.

### 19.4 The FakeNode App

A console application combining MockDataSource + TracerAgent + sync mock + observer + viewer, all in one process for development and demos. Lets a developer launch Tracer with a chosen scenario without any external dependencies. Multiple FakeNode instances can run on one machine to simulate a multi-node cluster.

---

## 20. Cross-Cutting Requirements

These requirements apply to all long-running Tracer processes (TracerAgent, TracerObserver, Web API host, FakeNode).

### 20.1 Time Provider Injection

All time-driven behavior uses .NET 8 `TimeProvider` via DI:

- Capture interval rotation
- Retention checks
- Live update tail-following
- Heartbeat/health checks
- Performance test scenarios

Production registers `TimeProvider.System`. Tests register a `SimulatedClock` that supports synchronized cluster-wide time control.

Code paths that read time only for labeling (log timestamps, file naming) may use `DateTimeOffset.UtcNow` directly.

### 20.2 Structured JSON Logging

Each process writes one JSON event per line, configured via Serilog or equivalent. Default log level: `Information`, with `Debug` for `Tracer.Ingest`, `Tracer.Storage`, `Tracer.Query` namespaces.

Standard fields: `IntervalTimestamp`, `NodeId`, `SessionId`, `BundleId`, `TraceId`, `EventCount`, `DurationMs`, etc.

### 20.3 `LOG_FILE=` First-Line Convention

Each process announces its log file path as the first stdout line:

```
LOG_FILE=C:\Tracer\logs\agent-2026-05-19.json
```

Test framework parses this; operators use the same convention.

### 20.4 Testing Compile-Time Gate

`TESTING_ENABLED` compile-time symbol gates test-only HTTP endpoints (`/api/test/*`), failure injection, simulated-clock registration. Production builds physically lack these — runtime feature flags are not used.

### 20.5 Configuration

Absolute paths only (no relative or `~` expansion). All configuration via JSON files at startup (`agent.json`, `observer.json`, `tracer.json`). Reloadable via `POST /api/config/reload` where applicable.

### 20.6 Graceful Shutdown

`IHostApplicationLifetime` protocol:
1. Stop accepting new work
2. Drain in-flight work
3. Persist state — close DuckDB Appenders, finalize Parquet, write manifests, write `_ready`
4. Close listeners
5. Flush logs
6. Exit

Triggered by SIGINT/SIGTERM/Windows service stop in production; by test API endpoint in tests. Hard kill (Process.Kill) is exercised by tests for restart-resilience verification.

---

## 21. Resolved Design Decisions

Summary for traceability.

- **Decoupled from simulation**: Tracer uses abstract `DiagnosticRecord` and adapter interfaces. No reference to DDS or simulation types in Tracer.Core.
- **Wall-clock primary**: PLL-synchronized cluster wall-clock is the time axis. Tick-based ordering does not apply in production.
- **Trace context in event IDLs**: `trace_id`, `event_id`, `parent_event_id` (uint64 each) added to event IDLs by the simulation integration project.
- **Trace propagation is integration project's responsibility**: Tracer consumes; it does not generate or infer trace relationships.
- **DuckDB storage**: events and slow state in DuckDB tables; fast state in Parquet files per topic per interval.
- **Capture intervals, not session boundaries**: storage rotates on fixed-duration intervals (default 1 hour). Sessions are conceptual time ranges, not structural storage units.
- **Sync system Telemetry category**: per-node, per-interval uploads via the sync system's existing chunked-upload infrastructure. To be added to sync architecture in a follow-up cycle.
- **Two operational modes**: live observer (opt-in, observer-only receive times) and post-scenario aggregation (per-node receive times, NAS-sourced).
- **Single-session bundles**: each analysis is scoped to a bundle for one time range. Cross-session querying deferred.
- **Fast state on-demand**: separate storage; never in hot query paths; included in bundles only when needed.
- **Performance targets are hard**: violations are bugs. Backend aggregation, columnar storage, Canvas rendering enforce them.
- **Mock-first development**: full system built and validated against mock adapters before any real adapter exists.
- **Audience-driven build sequence**: Scenario View first (instructors, non-technical), then engineer power, then specialized analytics.
- **Tracer is the platform name**: components are TracerAgent, TracerObserver, TracerAggregator, TracerViewer. The sync system's data category for Tracer files is `Telemetry`.

---

## 22. Open Questions

Items to resolve during implementation, not blocking architectural decisions.

- **Default capture interval duration**: 1 hour is the proposal. Tunable. May refine after initial deployment experience.
- **Default fast-state sampling rate**: 10 Hz for high-rate transforms. May vary by topic.
- **Default bundle fast-state inclusion policy**: "selected-entities" with empty entity list (no fast state by default; user explicitly opts in). May change based on UX experience.
- **Bundle file format**: zipped directory vs single-file binary container. ZIP is the simple default; revisit if size or open-time becomes an issue.
- **Web API authentication**: deferred. Eventually aligned with the sync system's auth approach.
- **Annotation persistence in live mode**: annotations made against a live session must transfer into the bundle on export. Implementation detail.
- **Cross-session queries**: deferred but data is preserved on NAS per-interval, enabling future indexing if needed.
- **Live alerts**: deferred. Architecturally compatible (SSE channel could deliver alert events) but not in initial scope.
