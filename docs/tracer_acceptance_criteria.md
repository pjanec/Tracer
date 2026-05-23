# Tracer — Acceptance Criteria

*Structured verification checklist for the Tracer diagnostic platform*
*Companion to the architecture document and Phase 1-11 design documents*

This document compiles **what the implementation must demonstrate** for Tracer to be considered successfully built. It is organized by system part — not by build phase — so that someone validating an implementation can walk the system and check off observable behaviors and contracts.

Every criterion references the source-of-truth documents:

- **Arch** → `tracer_architecture_v1.md`
- **Sync** → `sync_addendum_telemetry.md`
- **P1-P11** → `tracer_phase1_design.md` through `tracer_phase11_design.md`

References are written as `Arch §N` or `P5 §M.N` so that you can navigate to the source paragraph for design rationale and additional detail.

**How to read each criterion**:

Each criterion is a checkable statement. The reference column points to where the requirement is defined. The "What to verify" column suggests concrete tests an implementation review would run. Criteria are categorized as:

- **MUST** — required for the implementation to be considered complete
- **SHOULD** — strongly expected but minor deviations may be acceptable
- **DEFERRED** — explicitly out of scope; appears here only to clarify *what is not* in scope

---

## Table of Contents

**Part A — Foundation and Cross-Cutting**
- [A1. Core Types and Identity](#a1-core-types-and-identity)
- [A2. Trace Context Discipline](#a2-trace-context-discipline)
- [A3. Adapter Interfaces and Mock-First Discipline](#a3-adapter-interfaces-and-mock-first-discipline)
- [A4. Storage Schemas](#a4-storage-schemas)
- [A5. Wall Clock and Time Handling](#a5-wall-clock-and-time-handling)
- [A6. Cross-Cutting Implementation Requirements](#a6-cross-cutting-implementation-requirements)

**Part B — Backend Components**
- [B1. TracerAgent](#b1-traceragent)
- [B2. TracerObserver](#b2-tracerobserver)
- [B3. TracerAggregator](#b3-traceraggregator)
- [B4. Web API (Observer and Offline Viewer)](#b4-web-api-observer-and-offline-viewer)

**Part C — Storage Layouts**
- [C1. Per-Interval Storage (Agent and Observer)](#c1-per-interval-storage-agent-and-observer)
- [C2. NAS Layout (Sync System Destination)](#c2-nas-layout-sync-system-destination)
- [C3. Bundle Format](#c3-bundle-format)
- [C4. Annotations, Saved Views, and Saved Queries Store](#c4-annotations-saved-views-and-saved-queries-store)

**Part D — Frontend Application Shell**
- [D1. SPA Structure and Routing](#d1-spa-structure-and-routing)
- [D2. Session Browser and Bundle Library](#d2-session-browser-and-bundle-library)
- [D3. Persona Switcher](#d3-persona-switcher)
- [D4. Cross-View Navigation](#d4-cross-view-navigation)
- [D5. Shareable URLs](#d5-shareable-urls)

**Part E — Analytical Views**
- [E1. Scenario View](#e1-scenario-view)
- [E2. Timeline View](#e2-timeline-view)
- [E3. Causal Tree View](#e3-causal-tree-view)
- [E4. Entity History View](#e4-entity-history-view)
- [E5. Replication Latency View](#e5-replication-latency-view)
- [E6. Gap Detection View](#e6-gap-detection-view)
- [E7. Network Topology View](#e7-network-topology-view)
- [E8. Trigger Evaluation Log](#e8-trigger-evaluation-log)
- [E9. SQL Console](#e9-sql-console)

**Part F — User Content Features**
- [F1. Annotations](#f1-annotations)
- [F2. Saved Views and Bookmarks](#f2-saved-views-and-bookmarks)
- [F3. Saved Queries](#f3-saved-queries)

**Part G — Real Adapter Integration**
- [G1. DDS Adapter](#g1-dds-adapter)
- [G2. Shared Memory Transport](#g2-shared-memory-transport)
- [G3. Sync System Adapter](#g3-sync-system-adapter)
- [G4. NAS Storage Reader](#g4-nas-storage-reader)
- [G5. Adapter Selection and Configuration](#g5-adapter-selection-and-configuration)

**Part H — Operations and Hardening**
- [H1. Resource Limits and Bounded Memory](#h1-resource-limits-and-bounded-memory)
- [H2. Graceful Degradation Under Load](#h2-graceful-degradation-under-load)
- [H3. Crash Recovery and Restart Resilience](#h3-crash-recovery-and-restart-resilience)
- [H4. Monitoring and Health Reporting](#h4-monitoring-and-health-reporting)
- [H5. Performance Targets](#h5-performance-targets)

**Part I — Testing and Quality**
- [I1. Test Suite Structure](#i1-test-suite-structure)
- [I2. Integration with Real Customer Environment](#i2-integration-with-real-customer-environment)

**Part J — Scope Boundaries (Explicitly Deferred)**
- [J1. What Tracer Does Not Do](#j1-what-tracer-does-not-do)

---

# Part A — Foundation and Cross-Cutting

## A1. Core Types and Identity

The system's central abstractions defined in `Tracer.Core` (Arch §6).

### A1.1 Two-Tier Identity Model

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A1.1.1 | MUST | `EventId` is a 64-bit unsigned integer, displayed as 16-char hex throughout the UI and API | Arch §7.1; P1 §3.2 | Inspect a known event's eventId in any view; verify 16-char hex; verify the API echoes the same value |
| A1.1.2 | MUST | `trace_id` and `parent_event_id` are also uint64, hex-displayed | Arch §7.1; P1 §3.2 | Same as A1.1.1 for trace_id and parent_event_id |
| A1.1.3 | MUST | `agentId` is a stable identifier of one agent process | Arch §7.1 | Across agent restarts, the agentId remains the same |
| A1.1.4 | MUST | `nodeId` (the publisher_node and subscriber_node columns) is a string per machine | Arch §6 | Multiple events from one machine share the same nodeId |
| A1.1.5 | MUST | EventId zero is reserved and means "no event" (used for root parent_event_id) | P1 §3.2 | Root events have parent_event_id = 0 (or hex 0000000000000000) |
| A1.1.6 | MUST | trace_id zero means "not on any trace" | Arch §7.2; P6 §9.4 | Events with trace_id = 0 do not show "Show causal tree" pivot |

### A1.2 DiagnosticRecord Type Hierarchy

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A1.2.1 | MUST | `DiagnosticRecord` is the abstract base type, with `EventRecord` and `StateSampleRecord` as the two subtypes | Arch §6; P1 §3 | Inspect `Tracer.Core` source; both subtypes derive from common base |
| A1.2.2 | MUST | `EventRecord` carries: event_id, trace_id, parent_event_id, topic, publish_wallclock, receive_wallclock, publisher_node, subscriber_node, sequence_number, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload_json | P1 §3.3 | Field-by-field schema review |
| A1.2.3 | MUST | `StateSampleRecord` has two kinds: Slow and Fast, distinguished by `Kind` enum | P1 §3.4 | Inspect StateSampleRecord definition |
| A1.2.4 | MUST | Slow state samples store payload_json; fast state samples store typed_values (per-column) | P1 §3.4; P1 §4.4 | Inspect a slow state vs fast state record; slow has JSON payload, fast has structured columns |
| A1.2.5 | MUST | All records carry both publish_wallclock and receive_wallclock | P1 §3.3, §3.4 | Schema verification |

---

## A2. Trace Context Discipline

The architectural premise underpinning Phase 6's causal tree and the broader correlation story (Arch §7).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A2.1 | MUST | The simulation publishes events with trace_id, event_id, and parent_event_id populated for all events | Arch §7.3 | Audit a real bundle: count events with trace_id=0 outside of explicitly-rootless topics. Reasonable threshold: < 5%. |
| A2.2 | MUST | The DDS adapter reads trace_id, event_id, parent_event_id from each sample's payload without rewriting | P11 §3.6 | Round-trip test: emit event with known IDs, inspect bundle, IDs match bitwise |
| A2.3 | MUST | All three fields round-trip through DDS → adapter → SharedMemoryTransport → agent → DuckDB → bundle without corruption | P11 §8.3 (TraceContextPropagationTests) | The `ParentChildRelationshipsPreserved` integration-real test passes |
| A2.4 | MUST | Causal tree queries return only events sharing the same trace_id | P6 §4.2 | Verify via API: `/api/traces/{id}/tree` returns no foreign trace_ids |
| A2.5 | SHOULD | The architecture explicitly documents trace-context propagation as the integration project's responsibility | Arch §7.3; P11 §1.2 | Confirm `docs/dds-integration.md` lists this as integration-project-owned |

---

## A3. Adapter Interfaces and Mock-First Discipline

Tracer.Core defines abstract adapter interfaces; mocks drive development; real adapters plug in at integration time (Arch §6).

### A3.1 Interfaces Are Defined in Tracer.Core

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A3.1.1 | MUST | `IDiagnosticDataSource` interface exists in `Tracer.Core.Adapters` | Arch §6; P1 §6 | Inspect source |
| A3.1.2 | MUST | `IAgentTransport` interface exists in `Tracer.Core.Adapters` | Arch §6; P1 §6 | Inspect source |
| A3.1.3 | MUST | `ITelemetryUploadService` interface exists in `Tracer.Core.Adapters` | Arch §6; P11 §5.2 | Inspect source |
| A3.1.4 | MUST | `ITelemetryStorageReader` interface exists in `Tracer.Core.Adapters` | Arch §6; P11 §6.2 | Inspect source |
| A3.1.5 | MUST | `IClock` interface exists in `Tracer.Core.Adapters` | Arch §6; Arch §22.1 | Inspect source |
| A3.1.6 | MUST | `Tracer.Core` assembly does NOT reference Cyclone DDS, sync system, SMB libraries, or any simulation-specific types | Arch §6; Arch §22.5 | Inspect Tracer.Core.csproj dependencies; should have only BCL references |

### A3.2 Mock Implementations Are Functional

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A3.2.1 | MUST | `MockDataSource` (scenario generator) implements `IDiagnosticDataSource` | Arch §6; P1 §7 | Source inspection; can produce events from a scenario script |
| A3.2.2 | MUST | `InProcessChannelTransport` implements `IAgentTransport` | Arch §6; P1 §8 | Source inspection |
| A3.2.3 | MUST | `LocalFileSystemUploadService` implements `ITelemetryUploadService` | Arch §6; P11 §1.2 | Source inspection |
| A3.2.4 | MUST | `LocalFileSystemStorageReader` implements `ITelemetryStorageReader` | Arch §6 | Source inspection |
| A3.2.5 | MUST | `SimulatedClock` implements `IClock` | Arch §6; Arch §22.1 | Source inspection |
| A3.2.6 | MUST | All mocks live in `Tracer.Adapters.Mock` assembly | Arch §6 | Inspect assembly structure |
| A3.2.7 | MUST | The full system runs end-to-end against mocks alone — no real adapter required for development | Arch §6; P1-P10 | Run all of Phase 1-10 tests; everything passes without DDS/sync/NAS |

### A3.3 Real Implementations Coexist (Phase 11)

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A3.3.1 | MUST | `DdsDiagnosticDataSource` in `Tracer.Adapters.DDS` implements `IDiagnosticDataSource` | P11 §3.3 | Source inspection |
| A3.3.2 | MUST | `SharedMemoryTransport` in `Tracer.Adapters.SharedMemory` implements `IAgentTransport` | P11 §4.5 | Source inspection |
| A3.3.3 | MUST | `SyncSystemUploadService` in `Tracer.Adapters.Sync` implements `ITelemetryUploadService` | P11 §5.3 | Source inspection |
| A3.3.4 | MUST | `NasStorageReader` in `Tracer.Adapters.Nas` implements `ITelemetryStorageReader` | P11 §6.3 | Source inspection |
| A3.3.5 | MUST | Mock and real adapters are interchangeable via configuration without code changes | P11 §7 | Verify a config switch from `"mock"` to `"dds"` results in real adapter activation; no recompile |

---

## A4. Storage Schemas

DuckDB schemas for events and slow state; Parquet layout for fast state (Arch §4; P1 §4).

### A4.1 Events Table Schema

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A4.1.1 | MUST | The `events` table in agent/observer per-interval DuckDB files matches the schema in P1 §4.2 | P1 §4.2 | DESCRIBE events; verify all columns present with correct types |
| A4.1.2 | MUST | The `events` table in bundle DuckDB has one row per (event, subscribing_node) — i.e., duplicated per subscriber | Arch §13.3; P4 §5.4 | In bundle: SELECT COUNT(*) WHERE event_id = X yields N rows for N subscribers |
| A4.1.3 | MUST | Index `idx_events_publish_wallclock` exists on every events table | P1 §4.2 | SELECT * FROM duckdb_indexes |
| A4.1.4 | MUST | Index `idx_events_trace_id` exists (partial, where trace_id != 0) | P1 §4.2 | duckdb_indexes inspection |
| A4.1.5 | MUST | Index `idx_events_entity_id` exists (partial, where entity_id IS NOT NULL) | P1 §4.2 | duckdb_indexes inspection |
| A4.1.6 | MUST | Index `idx_events_topic_time` (composite on topic, publish_wallclock) exists | P1 §4.2 | duckdb_indexes inspection |
| A4.1.7 | MUST | Index `idx_events_player_id` (partial) exists | P1 §4.2 | duckdb_indexes inspection |
| A4.1.8 | MUST | Index `idx_events_parent_event_id` (partial, where != 0) exists on new intervals after Phase 6 | P6 §3.1 | duckdb_indexes inspection; create new interval and verify |
| A4.1.9 | MUST | Index `idx_events_topic_pub_sub` exists on bundle events table | P9 §3.4 | duckdb_indexes against a bundle |

### A4.2 Slow State Table Schema

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A4.2.1 | MUST | The `slow_state` table matches the schema in P1 §4.3 | P1 §4.3 | DESCRIBE slow_state |
| A4.2.2 | MUST | Composite index `idx_slow_state_entity_time` (entity_id, publish_wallclock) exists on new intervals after Phase 7 | P7 §3.1 | duckdb_indexes inspection |

### A4.3 Fast State Parquet Layout

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A4.3.1 | MUST | Fast state stored as `fast_state/{safe_topic}/{safe_entity}/samples.parquet` | P1 §4.4; P2 §6 | Inspect interval directory; verify path layout |
| A4.3.2 | MUST | Parquet files contain `publish_wallclock`, `instance_key`, plus per-topic typed columns | P1 §4.4 | parquet_schema('samples.parquet'); inspect columns |
| A4.3.3 | MUST | Filenames are safe-encoded (no slashes, no special chars in `{safe_topic}` etc.) | P4 §3.1 | Inspect directory listing on disk |
| A4.3.4 | MUST | In bundles, per-entity Parquet files are consolidated across intervals | P4 §5.6 | A bundle has one samples.parquet per (topic, entity), aggregating all live-mode interval data |

### A4.4 Schema Stability

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A4.4.1 | MUST | The schema is identical across attached intervals (no per-interval schema drift) | Arch §13.2; P1 §4.3 | Sample across multiple intervals; columns and types match |
| A4.4.2 | MUST | The schema is identical between agent intervals, observer intervals, and bundle | P1 §4 | DESCRIBE matches across all three |

---

## A5. Wall Clock and Time Handling

The customer's PLL-based clock sync targets 1ms precision (Arch §3). Tracer uses publish_wallclock as the canonical time axis.

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A5.1 | MUST | All timestamps stored as TIMESTAMP (microsecond precision) in DuckDB | P1 §3.3 | DESCRIBE shows TIMESTAMP type |
| A5.2 | MUST | publish_wallclock is the **publisher's** synchronized wall clock at the moment of publish | Arch §3; P11 §3.4 | DDS adapter reads sample.SourceTimestamp; verified in integration-real tests |
| A5.3 | MUST | receive_wallclock is the **subscriber's** synchronized wall clock at the moment of receive | Arch §3; P11 §3.4 | DDS adapter stamps at translation time on each subscriber's machine |
| A5.4 | MUST | Sessions are open-ended (variable duration; not aligned to scenarios) | Arch §13.1 | A 7-hour session and a 4-minute session both supported |
| A5.5 | MUST | Intervals are wall-clock-aligned (default 1 hour) | Arch §13.2; P2 §6.7 | Default rotation occurs at the hour boundary; verifiable from a multi-hour run |
| A5.6 | MUST | Replication latency math: `latency = receive_wallclock - publish_wallclock` per (event, subscriber) row | P9 §3.1 | Verify Phase 9 SQL computes this expression |
| A5.7 | SHOULD | Negative latencies (sub-millisecond clock-sync error) are displayed honestly, not filtered | P9 §3.3 | Histogram has a "≤0 ms" bucket; not silently dropped |

---

## A6. Cross-Cutting Implementation Requirements

Arch §22 specifies process-level conventions that apply across master, agent, relay, observer, aggregator, and CLI tooling.

### A6.1 TimeProvider Injection

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.1.1 | MUST | All time-driven behavior uses `.NET 8 TimeProvider` injected via DI | Arch §22.1 | Code review: no `DateTimeOffset.UtcNow` on behavior-affecting code paths |
| A6.1.2 | MUST | In test builds, a test-controllable `TimeProvider` is registered | Arch §22.1 | Verify tests can advance time without real-time waits |
| A6.1.3 | SHOULD | Log timestamps (purely labelling) may use `DateTimeOffset.UtcNow` directly | Arch §22.1 | Acceptable in log formatters and trace IDs |

### A6.2 Structured JSON Logging

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.2.1 | MUST | Every long-running process writes a per-process log file, one JSON event per line | Arch §22.2 | Inspect log file; each line is valid JSON |
| A6.2.2 | MUST | Logs include structured fields, not just message strings | Arch §22.2 | Sample events contain EventId, BundleId, AgentId, etc. as separate fields |
| A6.2.3 | MUST | State machine transitions log `State`, `PreviousState`, `Trigger` | Arch §22.2 | Look for transitions in agent's interval-rotation log path |
| A6.2.4 | MUST | Transfer/operational events log `DurationMs`, `BytesTransferred` where applicable | Arch §22.2 | Sync upload log includes BytesTransferred |
| A6.2.5 | MUST | Default level is `Information`; Debug is enabled for transfer/intent/safewindow/cache namespaces | Arch §22.2 | Inspect appsettings.json `Logging` section |
| A6.2.6 | MUST | Log paths are configurable via `LogsRoot` | Arch §22.2 | appsettings.json includes a `LogsRoot` value; per-process subdirs |

### A6.3 LOG_FILE= stdout Convention

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.3.1 | MUST | Every long-running process emits `LOG_FILE=<path>` as its very first stdout line | Arch §22.3 | Start process; stdout's first line matches the convention |
| A6.3.2 | MUST | The convention applies in both production and test modes — no test-only variant | Arch §22.3 | Same line in both modes |

### A6.4 Testing.Enabled Compile-Time Gate

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.4.1 | MUST | A compile-time symbol `TESTING_ENABLED` gates test-only endpoints, failure injection, and test-only TimeProvider registration | Arch §22.4 | Inspect csproj; build with and without TESTING_ENABLED |
| A6.4.2 | MUST | Production builds (`dotnet publish -c Release` without TESTING_ENABLED) physically lack test affordances | Arch §22.4 | Decompile or reflect the published assembly; no `/api/test/*` endpoints present |
| A6.4.3 | MUST | Build-time verification confirms no `#if TESTING_ENABLED` block leaks test surface to production | Arch §22.4 | A build step exists that scans for prohibited patterns |

### A6.5 Configuration Path Discipline

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.5.1 | MUST | Configuration path fields accept absolute paths only | Arch §22.7 | Try a relative path in DataRoot; should fail-fast with clear error |
| A6.5.2 | MUST | No `~` expansion or environment-variable interpolation in path config | Arch §22.7 | Try `~/data`; should fail or be treated as literal |

### A6.6 Graceful Shutdown

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.6.1 | MUST | Master, agent, observer participate in `IHostApplicationLifetime` shutdown | Arch §22.8 | SIGINT/SIGTERM causes orderly shutdown |
| A6.6.2 | MUST | Clean shutdown stops accepting connections, cancels in-flight ops, persists state, closes listeners, flushes logs | Arch §22.8 | Observe shutdown sequence in logs |
| A6.6.3 | MUST | Tests can invoke shutdown via test-only `POST /api/test/shutdown` (TESTING_ENABLED only) | Arch §22.8 | Endpoint exists in test builds only |
| A6.6.4 | MUST | Hard shutdown (Process.Kill) verifies restart resilience: snapshot recovery, chunk-state resume, pending-activate re-evaluation | Arch §22.8; P11 §9.3 | Soak test includes hard-kill scenarios |

### A6.7 Scheduler Poll Intervals

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.7.1 | MUST | Fleet sync scheduler and GC scheduler expose `pollIntervalSeconds` in config | Arch §22.6 | appsettings.json contains these values |
| A6.7.2 | MUST | Tests override to 1 second; production default is 60 seconds | Arch §22.6 | Test configs use 1s; prod uses 60s |

### A6.8 NAS Abstraction

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| A6.8.1 | MUST | All master/aggregator code reading from NAS goes through `INasReader` | Arch §22.5; P11 §6 | Code review: NAS UNC paths appear only in SmbNasReader implementations |
| A6.8.2 | MUST | All NAS writes go through `INasWriter` | Arch §22.5 | Similarly: only in SmbNasWriter |
| A6.8.3 | MUST | Tests can register a local-FS implementation against the same interfaces | Arch §22.5; P11 §6 | LocalFileSystemStorageReader available |

---

# Part B — Backend Components

## B1. TracerAgent

Per-machine Windows service that ingests data and writes per-interval files (Arch §5; P2).

### B1.1 Process and Lifecycle

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B1.1.1 | MUST | Agent runs as a Windows service (long-lived, IHostedService) | Arch §5; P2 §3 | `sc.exe query` shows the service; can stop/start cleanly |
| B1.1.2 | MUST | Agent on startup recovers from prior crashes: discovers incomplete intervals and either resumes or discards | P2 §6.10 | Crash mid-interval; restart; verify clean state |
| B1.1.3 | MUST | Agent writes `LOG_FILE=<path>` as first stdout line | Arch §22.3; P2 §3 | Start agent; inspect stdout |
| B1.1.4 | MUST | Agent participates in graceful shutdown | Arch §22.8 | SIGINT triggers orderly shutdown; final interval finalized |

### B1.2 Ingestion Pipeline

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B1.2.1 | MUST | Agent ingests `DiagnosticRecord`s via `IAgentTransport` (mock or shared-memory) | Arch §5; P2 §5 | Code review |
| B1.2.2 | MUST | Ingestion pipeline uses a bounded `Channel<DiagnosticRecord>` (default 50K capacity) | P2 §5 | Inspect agent config; verify default |
| B1.2.3 | MUST | Channel full mode is DropOldest with logged warnings | P2 §5; P11 §3.3 | Stress test the pipeline; verify drops are counted and logged |
| B1.2.4 | MUST | Records dispatched to event-writer or state-writer based on record type | P2 §5 | Inspect dispatch logic |

### B1.3 Interval Writers

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B1.3.1 | MUST | Event writer uses DuckDB Appender for events table | P2 §6 | Inspect EventWriter; uses Appender API |
| B1.3.2 | MUST | Slow state writer uses DuckDB Appender for slow_state table | P2 §6 | Inspect StateWriter |
| B1.3.3 | MUST | Fast state writer writes one Parquet file per (topic, entity), with periodic flush | P2 §6 | Inspect FastStateWriter |
| B1.3.4 | MUST | All writers checkpoint periodically (e.g., every 30s) | P2 §6 | Configurable; default value verified |
| B1.3.5 | MUST | Writers handle entity_id transitions (new entities trigger new fast-state files) | P2 §6 | Push an event with new entity_id; verify new Parquet directory created |

### B1.4 Interval Rotation

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B1.4.1 | MUST | Intervals rotate at the wall-clock boundary (default hourly) | Arch §13.2; P2 §6.7 | Run for > 1 hour; observe rotation at the top of the hour |
| B1.4.2 | MUST | Rotation flushes all pending writes, closes current files, opens new interval directory | P2 §6.7 | Inspect interval directory before and after rotation |
| B1.4.3 | MUST | The `.complete` sentinel file is written as the **last step** of rotation | P11 §6.4 | Inspect a rotated interval; `.complete` exists, is the newest file |
| B1.4.4 | MUST | After rotation, agent submits the completed interval to `ITelemetryUploadService` | P2 §6.7 | Verify upload intent registered after rotation |
| B1.4.5 | MUST | Agent retains interval directory locally until upload Completed | P11 §5.6 | Disable sync; verify intervals accumulate locally; reenable sync; intervals upload then can be cleaned up |

### B1.5 Recovery Behavior

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B1.5.1 | MUST | On startup, agent scans `DataRoot/intervals/` for directories without `.complete` sentinel | P2 §6.10 | Manual test: create incomplete interval; restart agent |
| B1.5.2 | MUST | Incomplete intervals are discarded (logged warning) | P2 §6.10 | The bad interval is deleted; logs explain why |
| B1.5.3 | MUST | New interval started at current wall clock | P2 §6.10 | Agent resumes normal operation after recovery |
| B1.5.4 | MUST | DuckDB Appender state lost on crash is acceptable (records in flight at crash time are gone) | P2 §6.10 | Documented behavior |

---

## B2. TracerObserver

Optional opt-in process for live mode (Arch §5; P3).

### B2.1 Optionality and Independence

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B2.1.1 | MUST | Observer is optional — system works without it (agents alone produce bundles via aggregator) | Arch §5 | Run end-to-end without an observer; bundle still buildable |
| B2.1.2 | MUST | Observer is disposable — its crash or stop loses no data | Arch §5; P3 §3 | Kill observer mid-session; data on agents intact; restart observer; resumes |
| B2.1.3 | MUST | Observer subscribes via the same `IDiagnosticDataSource` interface used by agents | Arch §5; P3 §3 | Code review |

### B2.2 Live Mode Storage

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B2.2.1 | MUST | Observer writes its own per-interval DuckDB files (separate from agents') | Arch §5; P3 §3 | Inspect observer's DataRoot vs agents' |
| B2.2.2 | MUST | Observer's intervals follow the same schema as agents' | P3 §3 | DESCRIBE matches |
| B2.2.3 | MUST | `IntervalRotator` rotates observer's intervals same as agents | P3 §3.10 | Same rotation behavior |
| B2.2.4 | MUST | `ReadOnlyConnectionPool` (P3) replaced by `LiveMultiIntervalReader` (P5) for queries | P5 §3.5 | Code review: observer uses LiveMultiIntervalReader |

### B2.3 Live Multi-Interval Querying

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B2.3.1 | MUST | `IntervalSetTracker` maintains the active interval plus N completed intervals | P5 §3.2 | Inspect tracker; default N is 3 |
| B2.3.2 | MUST | `LiveMultiIntervalReader` attaches all in-set intervals as DuckDB databases | P5 §3.3 | Inspect attachments via DuckDB introspection |
| B2.3.3 | MUST | On rotation, the previously-active interval becomes Completed; the new interval becomes Active | P5 §3.2 | Trigger rotation; observe set transition |
| B2.3.4 | MUST | On retention eviction, the evicted interval is removed from the queryable set | P5 §3.2 | Force eviction; verify queries no longer return the evicted interval's data |
| B2.3.5 | MUST | In-flight queries during rotation continue to succeed against their issued connections | P5 §3.3 | Start a slow query; trigger rotation; query completes |
| B2.3.6 | MUST | Retention waits before deleting interval directories whose data may still be queryable (30s default) | P5 §3.3 | Test: eviction marks interval for delete; 30s delay before actual deletion |

### B2.4 SSE Streaming

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B2.4.1 | MUST | Observer exposes `/api/live/events` for filtered event streaming | P5 §4.7 | Connect with curl/event source; receive events |
| B2.4.2 | MUST | Observer exposes `/api/live/notables` for notables-only streaming (P3) | P3 §5.3 | Same |
| B2.4.3 | MUST | SSE filter is applied per-event server-side | P5 §4.7 | Subscribe with filter; only matching events arrive |
| B2.4.4 | MUST | Per-client bounded buffer; overflow disconnects the slow client | P3 §5.3 | Verify SseConnection has configurable buffer |
| B2.4.5 | MUST | Heartbeats every ~15s to detect dead connections | P3 §5.3 | Observe heartbeats in stream |

### B2.5 Live Query Window Config

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B2.5.1 | MUST | `ObserverConfig.LiveQueryWindow.CompletedIntervalsToInclude` controls how many completed intervals are queryable | P5 §3.4 | Inspect config; verify default = 3 |

---

## B3. TracerAggregator

Builds bundles from per-node interval data (P4).

### B3.1 Build Process

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B3.1.1 | MUST | Aggregator is triggered by `POST /api/bundles/build` | P4 §7 | API endpoint exists; returns bundleId |
| B3.1.2 | MUST | Aggregator reads from `ITelemetryStorageReader` (mock or NAS) | P4 §4; P11 §6 | Code review |
| B3.1.3 | MUST | Aggregator discovers intervals for a session via `ListIntervalsAsync` | P11 §6.3 | Verify enumeration of (session, node, interval) tuples |
| B3.1.4 | MUST | Aggregator skips intervals without `.complete` sentinel | P11 §6.4 | Place incomplete interval; aggregator skips with warning |
| B3.1.5 | MUST | Aggregator's progress is observable via `GET /api/bundles/{id}/status` | P4 §7 | Poll endpoint during build; receive progress updates |

### B3.2 Consolidation Steps

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B3.2.1 | MUST | Events consolidation: per-node events tables UNIONed into one bundle events table | P4 §5.4 | Bundle has events from all participating agents |
| B3.2.2 | MUST | Per-event duplicated per subscribing node (architecture's per-subscriber row shape) | Arch §13.3; P4 §5.4 | Inspect bundle: same event_id appears N times for N subscribers |
| B3.2.3 | MUST | Slow state consolidation: similar union approach | P4 §5.5 | Bundle has slow_state from all agents |
| B3.2.4 | MUST | Fast state consolidation: per-(topic, entity) Parquet files merged chronologically | P4 §5.6 | A bundle has one Parquet per (topic, entity); multi-interval samples in order |
| B3.2.5 | MUST | The aggregator creates the `idx_events_topic_pub_sub` index on the consolidated events table | P9 §3.4 | Inspect bundle DuckDB; index exists |

### B3.3 Metadata Production

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B3.3.1 | MUST | `metadata.json` written with sessionId, time range, topology, scenario context | P4 §6 | Open bundle; metadata.json present and well-formed |
| B3.3.2 | MUST | `metadata.json` is immutable (built by aggregator only) | P10 §7.3 | Code review; aggregator is the only writer |
| B3.3.3 | MUST | Latency budgets recorded into metadata.json when declared by scenario metadata | P9 §6.2 | Bundle from a session with budgets has the budgets section |
| B3.3.4 | MUST | Lifecycle classification config recorded into metadata when Phase 8 customizations exist | P8 §9.3 | Bundle metadata has lifecycleClassification section |

### B3.4 Annotations and Saved Views Export

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B3.4.1 | MUST | Annotations exported to `annotations/annotations.json` in bundle | P8 §3.7 | Build bundle with annotations; file appears; correct content |
| B3.4.2 | MUST | Saved views exported to `annotations/saved_views.json` in bundle | P8 §6.3 | Same for saved views |
| B3.4.3 | MUST | Manifest checksum covers these files | P8 §3.7 | Edit annotations.json by hand; bundle manifest verification fails |

### B3.5 Manifest and Validity

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B3.5.1 | MUST | `manifest.json` lists all files in bundle with SHA-256 hashes | P4 §6 | Inspect manifest; hashes match file content |
| B3.5.2 | MUST | Manifest is written last (publish-gate pattern) | P4 §6 | Until manifest exists, bundle is not considered complete |
| B3.5.3 | MUST | Aggregator is idempotent: rebuilding the same session produces an equivalent bundle | P4 §7 | Build twice; compare structure |

---

## B4. Web API (Observer and Offline Viewer)

ASP.NET Core minimal-API endpoints (P3, P4, P5-P10).

### B4.1 Surface — Sessions and Discovery

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.1.1 | MUST | `GET /api/sessions` lists sessions | P3 §5 | curl returns array of sessions |
| B4.1.2 | MUST | `GET /api/sessions/{id}` returns single session detail | P3 §5 | curl returns one session |
| B4.1.3 | MUST | `GET /api/health` returns health status | P3 §5; P11 §9.4 | curl returns Status: Healthy + agent/observer metrics |
| B4.1.4 | MUST | `GET /api/topology` returns session topology stub (Phase 3) | P3 §5 | Returns nodes/edges |

### B4.2 Surface — Events

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.2.1 | MUST | `GET /api/events/{eventId}` returns single event | P3 §5 | curl |
| B4.2.2 | MUST | `GET /api/events` lists events with filters (Phase 5) | P5 §4.2 | curl with various filters; verify composition |
| B4.2.3 | MUST | `GET /api/events/aggregate` returns time-bucketed counts | P5 §4.3 | curl |
| B4.2.4 | MUST | `GET /api/live/events` streams events via SSE (Phase 5) | P5 §4.7 | SSE stream returns events matching filter |
| B4.2.5 | MUST | `GET /api/live/notables` streams notables (Phase 3) | P3 §5.3 | SSE stream returns only notable events |

### B4.3 Surface — Traces

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.3.1 | MUST | `GET /api/traces/{traceId}/tree` returns full tree | P6 §5.1 | curl with known trace_id |
| B4.3.2 | MUST | `GET /api/traces/{traceId}` returns trace summary | P6 §5.1 | curl |
| B4.3.3 | MUST | `GET /api/events/{eventId}/trace` returns the event's trace tree | P6 §5.1 | curl |
| B4.3.4 | MUST | `GET /api/events/{eventId}/ancestors` returns ancestor chain | P6 §5.1 | curl |
| B4.3.5 | MUST | `GET /api/events/{eventId}/descendants` returns descendant subtree | P6 §5.1 | curl |

### B4.4 Surface — Entities

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.4.1 | MUST | `GET /api/entities` lists entities for a session | P7 §5.1 | curl |
| B4.4.2 | MUST | `GET /api/entities/{id}/summary` returns lifetime stats | P7 §5.1 | curl |
| B4.4.3 | MUST | `GET /api/entities/{id}/events` returns events touching entity | P7 §5.1 | curl |
| B4.4.4 | MUST | `GET /api/entities/{id}/slow-state` returns slow-state samples grouped by topic | P7 §5.1 | curl |
| B4.4.5 | MUST | `GET /api/entities/{id}/fast-state/topics` lists fast-state topics | P7 §5.1 | curl |
| B4.4.6 | MUST | `GET /api/entities/{id}/fast-state/{topic}/schema` returns column schema | P7 §5.1 | curl |
| B4.4.7 | MUST | `GET /api/entities/{id}/fast-state/{topic}` returns time-series data | P7 §5.1 | curl |

### B4.5 Surface — User Content

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.5.1 | MUST | Annotations: GET list, POST create, GET single, PUT update, DELETE | P8 §4.1 | All five verbs work |
| B4.5.2 | MUST | Annotations in offline viewer mode: 405 on write attempts | P8 §3.6 | POST/PUT/DELETE return 405 |
| B4.5.3 | MUST | Saved views: GET list, POST create, GET single, PUT update, DELETE, POST /opened | P8 §6.4 | All verbs |
| B4.5.4 | MUST | Saved queries: GET list, POST create, GET single, PUT update, DELETE, POST /favorite, POST /clone, POST /run | P10 §6.4 | All verbs |
| B4.5.5 | MUST | Built-in saved queries cannot be modified or deleted | P10 §6.3 | PUT/DELETE on built-in returns error |

### B4.6 Surface — Performance Analysis (Phase 9)

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.6.1 | MUST | `GET /api/latency/distribution` returns histogram + percentiles | P9 §9.3 | curl |
| B4.6.2 | MUST | `GET /api/latency/pairs` returns per-tuple summary | P9 §9.3 | curl |
| B4.6.3 | MUST | `GET /api/latency/timeseries` returns latency time series | P9 §9.3 | curl |
| B4.6.4 | MUST | `GET /api/latency/outliers` returns outlier list with budget context | P9 §9.3 | curl |
| B4.6.5 | MUST | `GET /api/gaps` returns gap list | P9 §9.4 | curl |
| B4.6.6 | MUST | `GET /api/topology/network` returns directed graph (Phase 9 extension) | P9 §9.4 | curl |
| B4.6.7 | MUST | `GET /api/scenario/budgets` returns latency budgets | P9 §9.4 | curl |
| B4.6.8 | MUST | `GET /api/scenario/triggers` returns trigger evaluation list | P8 §8.3 | curl |
| B4.6.9 | MUST | All Phase 9 endpoints return 409 Conflict in live (observer) mode | P9 §9.2 | curl against observer; expect 409 with clear message |

### B4.7 Surface — SQL Console

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.7.1 | MUST | `POST /api/sql/execute` runs a constrained SQL query | P10 §4.2 | curl with SELECT; verify result shape |
| B4.7.2 | MUST | `GET /api/sql/schema` returns queryable schema | P10 §4.2 | curl |
| B4.7.3 | MUST | `POST /api/sql/explain` returns EXPLAIN output | P10 §4.2 | curl |
| B4.7.4 | MUST | SQL with forbidden keywords (INSERT/DROP/ATTACH/etc.) returns state=Rejected | P10 §3.2 | curl with forbidden statement |
| B4.7.5 | MUST | Multi-statement queries are rejected | P10 §3.2 | Verify |
| B4.7.6 | MUST | Query timeout enforced (default 30s); state=Timeout on exceeded | P10 §3.3 | Long-running query; verify timeout |

### B4.8 Surface — Bundle Library

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.8.1 | MUST | `GET /api/bundles/library` returns library entries with user metadata | P10 §7.4 | curl |
| B4.8.2 | MUST | `PUT /api/bundles/{id}/metadata` updates label/description/tags/archived | P10 §7.4 | curl PUT; verify persistence |
| B4.8.3 | MUST | `POST /api/bundles/{id}/opened` records last-opened timestamp | P10 §7.4 | curl POST; verify last_opened updates |
| B4.8.4 | MUST | `DELETE /api/bundles/{id}` deletes the bundle directory | P10 §7.4 | curl DELETE; verify directory removed |
| B4.8.5 | MUST | `POST /api/bundles/import` accepts uploaded bundle zip with zip-slip defense | P10 §7.4 | Upload normal zip → success; upload malicious zip (path traversal) → reject |
| B4.8.6 | MUST | `GET /api/bundles/{id}/download` streams bundle as zip | P10 §11 | curl GET; receive valid zip |

### B4.9 Surface — Configuration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.9.1 | MUST | `GET /api/config/lifecycle-classification` returns the active config | P8 §9.1 | curl |

### B4.10 Response Conventions

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| B4.10.1 | MUST | All endpoints documented in OpenAPI spec; TypeScript client regenerated cleanly | P3 §5 | Inspect openapi.json; client builds |
| B4.10.2 | MUST | All endpoints return `application/json` (except SSE) | P3 §5 | Inspect Content-Type headers |
| B4.10.3 | MUST | Validation errors return 400 ProblemDetails (RFC 7807) | P3 §5 | Bad input; receive ProblemDetails |
| B4.10.4 | MUST | Bundle-mode-only endpoints return 409 ProblemDetails when in live mode | P9 §9.2 | Verify; check Detail field is human-meaningful |

---

# Part C — Storage Layouts

## C1. Per-Interval Storage (Agent and Observer)

Both agents and the observer use the same per-interval directory layout (Arch §13.2; P1 §4).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| C1.1 | MUST | Interval directory at `{DataRoot}/intervals/{IntervalTimestamp}/` | P1 §4 | Inspect filesystem after a rotation |
| C1.2 | MUST | Interval timestamp format: `YYYYMMDDTHHMMSSZ` (UTC, no separators) | P2 §6.7 | Verify format on disk |
| C1.3 | MUST | Each interval contains `events.duckdb`, `slow_state.duckdb`, and `fast_state/` directory | P1 §4; P2 §6 | `ls` the interval directory |
| C1.4 | MUST | `fast_state/{safe_topic}/{safe_entity}/samples.parquet` layout | P1 §4.4; A4.3.1 | Inspect fast_state subdirectory |
| C1.5 | MUST | `.complete` sentinel file written last, on clean rotation only | P11 §6.4 | After rotation: ls shows the sentinel; mid-rotation: no sentinel |
| C1.6 | MUST | DuckDB files are checkpointed periodically and on rotation | P2 §6 | DuckDB statistics show recent checkpoints |
| C1.7 | MUST | Retention manager removes old intervals according to policy | P2 §6.10 | Configure retention; observe deletion of aged intervals |

## C2. NAS Layout (Sync System Destination)

Where uploaded intervals end up on NAS (Sync addendum; P11 §6).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| C2.1 | MUST | NAS layout: `<NasRoot>/telemetry/<sessionId>/<nodeId>/<intervalId>/...` | Sync; P11 §5.3 | Inspect a real NAS after upload |
| C2.2 | MUST | Each interval directory on NAS mirrors the agent-side structure exactly (including `.complete`) | P11 §6.4 | File-by-file compare |
| C2.3 | MUST | Sync system uploads atomically: directory appears complete or not at all | Sync | Force a mid-upload state; aggregator sees only the prior state |
| C2.4 | MUST | Aggregator's `NasStorageReader.ListIntervalsAsync` discovers intervals with `.complete` and skips others | P11 §6.3 | Place an incomplete interval on NAS; aggregator skips it with logged warning |

## C3. Bundle Format

Bundles are self-contained zip-format artifacts (P4 §3).

### C3.1 Directory Structure

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| C3.1.1 | MUST | Bundle root contains `events.duckdb`, `slow_state.duckdb`, `metadata.json`, `manifest.json` | P4 §3 | Open a bundle; verify files |
| C3.1.2 | MUST | `fast_state/{safe_topic}/{safe_entity}/samples.parquet` per-entity files | P4 §3 | Same as A4.3.1 |
| C3.1.3 | MUST | `annotations/annotations.json` when annotations exist | P8 §3.7 | Inspect a bundle from a session with annotations |
| C3.1.4 | MUST | `annotations/saved_views.json` when saved views exist | P8 §6.3 | Same |
| C3.1.5 | MUST | `bundle-metadata.json` for user-editable library metadata (Phase 10) | P10 §7.3 | Open a bundle; inspect |

### C3.2 Metadata Content

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| C3.2.1 | MUST | `metadata.json` includes sessionId, sessionStartUtc, sessionEndUtc, builtAtUtc | P4 §6 | JSON inspection |
| C3.2.2 | MUST | `metadata.json` includes topology (nodes, edges per topic) | P4 §6 | JSON inspection |
| C3.2.3 | MUST | `metadata.json` includes scenario context if available | P4 §6 | JSON inspection |
| C3.2.4 | MUST | `metadata.json` includes `latencyBudgets` array when scenario defines them | P9 §6.2 | JSON inspection on a bundle from a session with budgets |
| C3.2.5 | MUST | `metadata.json` includes `lifecycleClassification` when overridden | P8 §9.3 | JSON inspection |
| C3.2.6 | MUST | `metadata.json` is immutable — only the aggregator writes it | P10 §7.3 | Code review: no other writer |

### C3.3 Manifest

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| C3.3.1 | MUST | `manifest.json` lists all files in the bundle | P4 §6 | JSON inspection |
| C3.3.2 | MUST | Each entry has path, size, SHA-256 hash | P4 §6 | JSON inspection |
| C3.3.3 | MUST | Manifest is written last in the build sequence | P4 §6; B3.5.2 | Mid-build: no manifest; finalized: manifest present |
| C3.3.4 | MUST | Manifest verification can validate bundle integrity on import or open | P10 §7.4 | Tamper with a file's content; re-compute hash; mismatch |

## C4. Annotations, Saved Views, and Saved Queries Store

SQLite database holding user-authored content in live observer mode (P8 §3.4; P10 §6.2).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| C4.1 | MUST | Single SQLite file at `{DataRoot}/annotations.db` | P8 §3.4 | Inspect filesystem |
| C4.2 | MUST | Schema includes `annotations`, `saved_views`, `saved_queries` tables | P8 §3.5; P8 §6.2; P10 §6.2 | DESCRIBE each table |
| C4.3 | MUST | Schema is idempotent — `CREATE TABLE IF NOT EXISTS` | P8 §3.5 | Run init twice; no failure |
| C4.4 | MUST | Indexes per design (session+kind, event_id, entity_id, trace_id, favorite) | P8 §3.5 | duckdb_indexes equivalent |
| C4.5 | MUST | Write lock prevents concurrent corruption (SemaphoreSlim around writes) | P8 §3.4 | Concurrent writes don't corrupt |
| C4.6 | MUST | In offline (bundle) mode, the same content lives in JSON files (annotations.json, saved_views.json) | P8 §3.6 | Open bundle; inspect JSON files |
| C4.7 | MUST | Offline writes throw InvalidOperationException → 405 at API | P8 §3.6, §4.2 | API returns 405 with clear message |

---

# Part D — Frontend Application Shell

## D1. SPA Structure and Routing

Single-page application built with Vue 3 + TypeScript (P3 §9).

### D1.1 Application Bootstrap

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D1.1.1 | MUST | SPA loads at `/` and routes via Vue Router | P3 §9 | Browser inspection |
| D1.1.2 | MUST | Same SPA build serves both live observer and offline viewer | P5 §1.3 (criterion 11) | Open against observer URL; open against offline viewer URL; same UI |
| D1.1.3 | MUST | Bundle size remains under reasonable bound (e.g., 3 MB gzipped after Phase 10 additions) | P10 §11 | webpack-bundle-analyzer or equivalent |
| D1.1.4 | MUST | Routes are code-split (lazy loaded) per view | P6 §8.5 | Bundle analysis shows per-view chunks |

### D1.2 Route Map

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D1.2.1 | MUST | `/sessions` — session browser | P3 §9 | Navigate; UI loads |
| D1.2.2 | MUST | `/scenario/{sessionId}` — Scenario View | P3 §9 | Navigate; UI loads |
| D1.2.3 | MUST | `/v/timeline/{sessionId}` — Timeline View | P5 §5 | Navigate; UI loads |
| D1.2.4 | MUST | `/v/causal/{eventId}` and `/v/trace/{traceId}` — Causal Tree View | P6 §8.5 | Both routes load |
| D1.2.5 | MUST | `/v/entity/{entityId}` — Entity History View | P7 §11.4 | Navigate; UI loads |
| D1.2.6 | MUST | `/v/entities/{sessionId}` — Entity Picker | P7 §11.4 | Navigate |
| D1.2.7 | MUST | `/v/latency/{sessionId}` — Replication Latency View | P9 §10.2 | Navigate |
| D1.2.8 | MUST | `/v/gaps/{sessionId}` — Gap Detection View | P9 §12 | Navigate |
| D1.2.9 | MUST | `/v/topology/{sessionId}` — Network Topology View | P9 §11.4 | Navigate |
| D1.2.10 | MUST | `/v/triggers/{sessionId}` — Trigger Evaluation Log | P8 §8.4 | Navigate |
| D1.2.11 | MUST | `/v/sql/{sessionId}` — SQL Console | P10 §5.3 | Navigate |
| D1.2.12 | MUST | `/v/saved-views/{sessionId}` — Saved Views browser | P8 §6.7 | Navigate |
| D1.2.13 | MUST | `/v/saved-queries` — Saved Queries browser | P10 §6 | Navigate |
| D1.2.14 | MUST | `/v/bundles` — Bundle Library | P10 §7.2 | Navigate |

### D1.3 Common UI Primitives

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D1.3.1 | MUST | AppHeader shows session label, mode (live/offline), persona switcher | P3 §9; P8 §7.2 | Visible on every primary page |
| D1.3.2 | MUST | Loading spinner displayed while queries are in flight | P3 §9 | Slow API; spinner visible |
| D1.3.3 | MUST | Error banner / ErrorMessage component for query failures with retry | P3 §9 | Trigger an error; UI surfaces it |
| D1.3.4 | MUST | EventInspector component reused across Timeline, Causal Tree, Entity History | P5 §6.5; P6 §7.2; P7 §6.2 | Select an event in different views; inspector renders consistently |
| D1.3.5 | MUST | Per-node color palette is consistent across all views (`buildNodeColorMap`) | P6 §7.4 | Same node shows same color in Timeline and Causal Tree |
| D1.3.6 | MUST | Date/time formatting uses consistent helper (`formatTime`, `formatRelative`, `formatDuration`) | P6 §7.5 | UI displays times in the same style throughout |

## D2. Session Browser and Bundle Library

### D2.1 Session Browser

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D2.1.1 | MUST | Session Browser at `/sessions` lists all sessions | P3 §9 | UI shows list |
| D2.1.2 | MUST | Filter by date, scenario, label | Arch §16.8 | Filter UI works |
| D2.1.3 | MUST | Click a session opens it (routing depends on persona) | P8 §7.3 | Click; navigation occurs |
| D2.1.4 | MUST | Session cards show summary stats (start, duration, node count, event count) | P3 §9 | Stats visible |
| D2.1.5 | MUST | "Build bundle" action on session card | P5 §9.2 | Click button; bundle build initiated; status visible |
| D2.1.6 | MUST | "Entities" link on session card (Phase 7) | P7 §11.5 | Click; navigate to entity picker |

### D2.2 Bundle Library (Phase 10)

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D2.2.1 | MUST | Bundle Library at `/v/bundles` lists known bundles | P10 §7.2 | UI shows list |
| D2.2.2 | MUST | Each bundle card shows label, description, tags, size, session times | P10 §7.1 | Card content |
| D2.2.3 | MUST | Filter by tag, date range, archived | P10 §7.2 | Filter UI works |
| D2.2.4 | MUST | Sort by built date, session start, size, label (asc/desc) | P10 §7.2 | Sort works |
| D2.2.5 | MUST | "Edit" opens metadata editor modal | P10 §7.2 | Edit label/description/tags; save persists |
| D2.2.6 | MUST | "Archive" hides from default list (doesn't delete) | P10 §7.2 | Archive; bundle hidden; toggle "show archived" reveals |
| D2.2.7 | MUST | "Delete" removes the bundle (with confirmation) | P10 §7.2 | Confirm; bundle gone |
| D2.2.8 | MUST | "Export" downloads bundle as zip | P10 §7.2 | Download; valid zip |
| D2.2.9 | MUST | "Import" uploads previously exported bundle | P10 §7.4 | Upload; appears in library |
| D2.2.10 | MUST | Stale-bundle indicator (not opened in 30+ days) | P10 §7.1 | UI hint visible on aged bundles |

## D3. Persona Switcher

Three personas for UI defaults (P8 §7).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D3.1 | MUST | Three personas available: Engineer, Scenario Author, Operator | P8 §7.2 | Switcher UI shows all three |
| D3.2 | MUST | Persona stored in localStorage; default Engineer | P8 §7.1 | First visit shows Engineer; switch; reload; persists |
| D3.3 | MUST | Session card default click target depends on persona | P8 §7.3 | Engineer → Timeline; Scenario Author → Scenario View; Operator → Scenario View |
| D3.4 | MUST | BookmarkBar filters per current persona | P8 §6.6 | Verify per-persona bookmarks |
| D3.5 | MUST | Persona switcher is UI-only, NOT authorization | P8 §7.4 | Switching has no permission effect; documented |

## D4. Cross-View Navigation

The pivots that connect views together (P5 §6.5; P6 §9; P7 §11.3; P8; P9).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D4.1 | MUST | EventInspector offers "Show causal tree" pivot for events with trace_id != 0 | P5 §6.5; P6 §9.1 | Click; opens Causal Tree at that event |
| D4.2 | MUST | EventInspector offers "Show entity history" pivot for events with entity_id != null | P7 §11.3 | Click; opens Entity History |
| D4.3 | MUST | EventInspector offers "Show in timeline" pivot (from non-Timeline views) | P6 §9.1 | Click; opens Timeline focused on time ±2s |
| D4.4 | MUST | EventInspector offers "Show in scenario" pivot | P3; P6 §9.1 | Click; opens Scenario View |
| D4.5 | MUST | Pivot disabled when not applicable (trace_id=0 hides causal pivot; entity_id=null hides entity pivot) | P6 §9.4; P7 §11.3 | Verify on root events |
| D4.6 | MUST | Pivot from latency outlier → Timeline focused on the offending event | P9 §10.5 | From Replication Latency View, click "Show in timeline" on outlier row |
| D4.7 | MUST | Pivot from gap → Timeline focused on resumed-at wallclock range | P9 §12 | From Gap Detection View |
| D4.8 | MUST | Pivot from topology edge → Replication Latency filtered to that (publisher, subscriber) pair | P9 §11.4 | From Topology View |
| D4.9 | MUST | Pivot from trigger evaluation → Timeline or Causal Tree | P8 §8.4 | From Trigger Eval View |
| D4.10 | MUST | Pivots preserve context (session id, time, selection) via URL parameters | All views | Inspect URL after pivot |
| D4.11 | MUST | Every analytical view has "Show SQL for this view" affordance | P10 §8 | Button visible on Timeline, Causal Tree, Entity History, Replication Latency etc. |
| D4.12 | MUST | Clicking "Show SQL" opens SQL Console with equivalent query pre-loaded | P10 §8.1 | Verify SQL appears in editor |

## D5. Shareable URLs

Every view's state encodes into the URL (P5 §7; P6 §8.3; P7 §11.1).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| D5.1 | MUST | Timeline URL captures: from, to, filter params, select, follow | P5 §7.1 | Inspect URL after interactions |
| D5.2 | MUST | Causal Tree URL captures: traceId or eventId, select, mode (ancestors/descendants), maxDepth | P6 §8.3 | Inspect URL |
| D5.3 | MUST | Entity History URL captures: entityId, session, from, to, select, fastStateTopic, fastStateColumns | P7 §11.1 | Inspect URL |
| D5.4 | MUST | URLs are reproducible: same URL on two machines opens the same view | P5 §1.3 (criterion 9) | Test on two browsers |
| D5.5 | MUST | URL updates are debounced (~250ms) to avoid history pollution | P5 §7.2; P6 §8.4 | Rapid pan/zoom; only a few history entries |
| D5.6 | MUST | URL uses `router.replace` (not push) for state updates within a view | P5 §7.2 | Browser back doesn't traverse every pan |
| D5.7 | MUST | Pivots use `router.push` (new history entry) | P6 §9 | Browser back from pivoted view returns to source |

---

# Part E — Analytical Views

## E1. Scenario View

Scenario-author-facing top-down view (Arch §16.1; P3).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E1.1 | MUST | Scenario View at `/scenario/{sessionId}` | P3 §9 | Navigate |
| E1.2 | MUST | Timeline of phases and major scenario events | Arch §16.1 | UI shows phase progression |
| E1.3 | MUST | Notables list (events with notable_label set) | Arch §16.1; P3 §9 | List visible in sidebar |
| E1.4 | MUST | Click a notable → opens its causal tree (Phase 6 pivot, deferred until P6) | Arch §16.1; P6 §9 | Pivot button works |
| E1.5 | MUST | Live indicator when session is active | P3 §9 | Visible during live session |
| E1.6 | MUST | Auto-refresh of notables via SSE (Phase 3) | P3 §5.3 | New notables appear without manual reload |

## E2. Timeline View

Engineer's primary view: multi-node swimlane timeline (Arch §16.2; P5).

### E2.1 Layout and Rendering

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E2.1.1 | MUST | Multi-node swimlane: one row per node | Arch §16.2; P5 §5 | Visual inspection |
| E2.1.2 | MUST | Wall-clock x-axis with formatted tick labels | Arch §16.2; P5 §5.6 | Verify ticks adjust to zoom level (h/m/s/ms) |
| E2.1.3 | MUST | Events rendered as Canvas markers (3px dot or 5px square for notables) | P5 §5.6 | Visual; markers visible |
| E2.1.4 | MUST | DPI-correct rendering (multiply by devicePixelRatio) | P5 §5.8 | Markers crisp on high-DPI displays |
| E2.1.5 | MUST | Markers colored by publisher_node (consistent palette) | P5 §5.6 | Verify color consistency |
| E2.1.6 | MUST | Severity shown via color/shape (warning, error) | P5 §5.6 | Errored events visually distinct |

### E2.2 Density Modes

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E2.2.1 | MUST | At low density: raw event markers | P5 §5.5 | Zoom in; raw events shown |
| E2.2.2 | MUST | At high density: aggregate bucket bars | P5 §5.6 | Zoom out; bars shown |
| E2.2.3 | MUST | Mode-switching threshold automatic per visible span | P5 §5.5 | Verify thresholds: <1min → raw; >4h → 5m buckets |
| E2.2.4 | MUST | Density indicator shows "showing N of M events" or "buckets of 5s" | P5 §5.1 | Badge visible |

### E2.3 Interaction

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E2.3.1 | MUST | Horizontal drag pans the viewport | P5 §6.1 | Drag; URL updates after debounce |
| E2.3.2 | MUST | Mouse wheel zooms in/out, centered on cursor | P5 §6.3 | Wheel; viewport zooms with cursor as pivot point |
| E2.3.3 | MUST | Click an event marker → inspector populates | P5 §6.5 | Click; inspector visible with payload |
| E2.3.4 | MUST | Click on aggregate bucket bar → zoom into that bucket's time window | P5 §6.5 | Click bar; viewport narrows to that bucket |
| E2.3.5 | MUST | Hover shows tooltip with event details (raw mode) or count/timerange (aggregate mode) | P5 §6.4 | Verify tooltips |
| E2.3.6 | MUST | Data fetch debounced (~100ms) during rapid pan/zoom | P5 §6.2 | No flood of HTTP requests on drag |
| E2.3.7 | MUST | Previous request cancelled when new viewport change occurs (AbortController) | P5 §5.4 | Verify via DevTools network inspector |
| E2.3.8 | MUST | Filters compose: topic AND severity reduces to intersection | P5 §4.4; P5 §1.3 (criterion 8) | Apply multiple; verify |
| E2.3.9 | MUST | Filter chips removable individually | P5 §5.1 | Click X on chip; chip removed |

### E2.4 Live Mode

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E2.4.1 | MUST | When session is active, SSE subscription delivers new events to the timeline | P5 §8.1 | Live session; events arrive as they're published |
| E2.4.2 | MUST | Auto-follow keeps live edge centered | P5 §8.3 | Toggle Follow; viewport stays at live edge |
| E2.4.3 | MUST | User pan disables auto-follow | P5 §8.3 | Pan; follow toggle disengages |
| E2.4.4 | MUST | Follow toggle re-enables and snaps to live edge | P5 §8.3 | Click Follow; viewport snaps |
| E2.4.5 | MUST | Live SSE applies the current filter | P5 §4.7 | Set filter; only matching events stream |

### E2.5 Performance Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E2.5.1 | MUST | Initial render on 1M-event session: < 500ms | P5 §1.3 (criterion 1) | Profile; measure |
| E2.5.2 | MUST | Pan/zoom response: < 100ms (renderer doesn't block on data) | P5 §1.3 (criterion 2) | Profile interaction |
| E2.5.3 | MUST | Apply filter: < 300ms p95 | P5 §1.3 (criterion 3) | Profile |
| E2.5.4 | MUST | Click → inspector populated: < 100ms | P5 §1.3 (criterion 4) | Profile |
| E2.5.5 | MUST | SSE event → marker on screen: < 100ms | P5 §1.3 (criterion 5) | Profile |
| E2.5.6 | MUST | Session-overview zoom on 100M-event session: < 1s | P5 §1.3 (criterion 6) | Profile |

## E3. Causal Tree View

Trace-centered DAG view (Arch §16.3; P6).

### E3.1 Layout and Rendering

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E3.1.1 | MUST | Layered topological layout (Sugiyama-style) | P6 §6.1 | Visual inspection |
| E3.1.2 | MUST | Longest-path layer assignment (convergent branches align) | P6 §6.2 | Verify DAG renders correctly |
| E3.1.3 | MUST | Within-layer ordering minimizes edge crossings (median-of-parents) | P6 §6.2 | Visual inspection of complex traces |
| E3.1.4 | MUST | Each node shows publisher color + severity inner dot + notable corner marker | P6 §7.4 | Visual: encoding is layered |
| E3.1.5 | MUST | Topic label below each node | P6 §7.4 | Visible |
| E3.1.6 | MUST | Edges drawn as Bezier curves | P6 §7.4 | Visual: not straight lines |
| E3.1.7 | MUST | Latency label on every edge, formatted as μs/ms/s | P6 §7.4 | Visible on every edge |

### E3.2 Trace Loading

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E3.2.1 | MUST | Open by trace_id: shows full trace | P6 §4.2 | curl + visual |
| E3.2.2 | MUST | Open by event_id: resolves to trace and shows it | P6 §4.4 | Click pivot; correct trace renders |
| E3.2.3 | MUST | Ancestors-only mode: ancestor chain from selected event | P6 §4.3 | Use ?mode=ancestors |
| E3.2.4 | MUST | Descendants-only mode: BFS-walked descendant subtree | P6 §4.3 | Use ?mode=descendants |
| E3.2.5 | MUST | Convergent DAGs (two parents → one child) rendered as DAG, not duplicated | P6 §1.1; P6 §4.2 | Test trace with convergence |
| E3.2.6 | MUST | Trace truncated at maxEvents (default 1000, hard cap 5000) | P6 §4.5 | Large trace; truncated flag set |
| E3.2.7 | MUST | Truncation surfaces a UI notice in the Trace Summary Panel | P6 §7.5 | Visual: notice appears |
| E3.2.8 | MUST | Cycle defense: walker doesn't infinite-loop on data with cycles | P6 §4.3 | Synthetic cycle data; query terminates |

### E3.3 Interaction

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E3.3.1 | MUST | Pan with drag, zoom with wheel (same patterns as Timeline) | P6 §7.3 | Drag/wheel |
| E3.3.2 | MUST | Click a node → inspector opens for that event | P6 §7.3 | Click; inspector visible |
| E3.3.3 | MUST | Selected node visually distinct (outer ring) | P6 §7.4 | Visual: selection highlight |
| E3.3.4 | MUST | TraceSearchInput accepts pasted event_id or trace_id | P6 §10 | Paste hex; toggle event/trace; navigate |
| E3.3.5 | MUST | Invalid input (non-hex, wrong length) shows inline error | P6 §10 | Type "abc"; error visible |
| E3.3.6 | MUST | Trace Summary Panel shows trace_id, event count, span, root/leaf counts, participating nodes | P6 §7.5 | Verify panel content |

### E3.4 Performance Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E3.4.1 | MUST | Trace query for 1000-event trace: < 200ms p95 | P6 §1.3 (criterion 10) | Profile |
| E3.4.2 | MUST | Descendants walk depth 30, 1000 nodes: < 500ms p95 | P6 §11.5 | Profile |
| E3.4.3 | MUST | Frontend layout of 500-node tree: < 50ms | P6 §11.5 | Profile |
| E3.4.4 | MUST | Frontend render of 500-node tree: < 50ms | P6 §11.5 | Profile |

## E4. Entity History View

Per-entity time-series view (Arch §16.4; P7).

### E4.1 Layout and Content

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E4.1.1 | MUST | Entity summary strip at top: ID, lifespan, player, topics | P7 §6.1 | Visual |
| E4.1.2 | MUST | Lifecycle ribbon: spawn/ownership/destruction markers + ownership-period bands | P7 §7 | Visual: spawn green, ownership blue, destruction red |
| E4.1.3 | MUST | Slow state charts: one row per topic that touched the entity | P7 §8 | Stacked rows |
| E4.1.4 | MUST | Event strip: all events with this entity's entity_id | P7 §9 | Markers along time axis |
| E4.1.5 | MUST | Fast state drill-down panel: collapsed by default | P7 §10.1 | Visual: starts collapsed |
| E4.1.6 | MUST | All panels share the same time axis | P7 §6.1 | Visual alignment |

### E4.2 Slow State Rendering

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E4.2.1 | MUST | Numeric fields rendered as stepped line (last-value-held) | P7 §8.1 | Plot a counter; line steps between samples |
| E4.2.2 | MUST | Categorical fields rendered as color bands (Gantt-style) | P7 §8.1 | Plot a state enum; bands visible |
| E4.2.3 | MUST | Field auto-detection: prefer `value`/`state`/`level`/`health` for numeric; `state`/`status`/`phase` for categorical | P7 §8.3 | Verify auto-pick on sample data |
| E4.2.4 | MUST | Field dropdown per chart lets user override | P7 §8.3 | Switch fields; chart updates |
| E4.2.5 | MUST | Click a slow-state sample → emits selectEvent | P7 §8.3 | Click in chart; corresponding event highlighted in event strip |

### E4.3 Fast State Drill-Down

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E4.3.1 | MUST | Topic picker lists topics with fast-state data for this entity | P7 §10.1 | Dropdown populated |
| E4.3.2 | MUST | After topic selected: column picker shows numeric columns | P7 §10.2 | Checkboxes for numeric columns |
| E4.3.3 | MUST | Selected columns plotted as multi-line chart | P7 §10.3 | Verify multi-line |
| E4.3.4 | MUST | Downsampling kicks in above 5000 samples (default maxSamples) | P7 §4.4 | Read large entity; downsampling notice appears |
| E4.3.5 | MUST | "Downsampled X of Y samples" notice visible when active | P7 §10.1 | Verify notice |
| E4.3.6 | MUST | Single Y axis (multi-axis explicitly deferred) | P7 §10.3 | Documented limitation |

### E4.4 Entity Discovery

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E4.4.1 | MUST | EntityPickerView lists entities for a session, sorted by event count DESC | P7 §11.5 | Navigate to picker; verify sort |
| E4.4.2 | MUST | Filter input narrows by ID, player, or topic substring | P7 §11.5 | Filter; list narrows |

### E4.5 Performance Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E4.5.1 | MUST | Entity discovery on 200-entity session: < 500ms | P7 §1.3 (criterion 9) | Profile |
| E4.5.2 | MUST | Entity history view full load: < 1.5s cold cache | P7 §1.3 (criterion 9) | Profile |
| E4.5.3 | MUST | Fast-state read 30-min entity, downsampled to 5000: < 1s | P7 §1.3 (criterion 9) | Profile |

## E5. Replication Latency View

Per-topic/pair latency analysis (Arch §16.5; P9).

### E5.1 Mode Gate

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E5.1.1 | MUST | View only meaningful in bundle mode (per-subscriber receive times) | Arch §16.5; P9 §1.1 | Documented |
| E5.1.2 | MUST | Live observer mode shows clear "Bundle mode required" banner | P9 §10.2 | Open against observer; banner visible with explanation |

### E5.2 Layout

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E5.2.1 | MUST | Three-panel layout: pair matrix (left), distribution+timeseries (center), outliers (right) | P9 §10.1 | Visual |
| E5.2.2 | MUST | Pair matrix sorted by p99 DESC (worst legs first) | P9 §4.4 | Verify sort |
| E5.2.3 | MUST | Over-budget pairs visually distinguished (e.g., red border) | P9 §10.4 | Identify on test data |
| E5.2.4 | MUST | Selecting a pair narrows distribution + time series to that tuple | P9 §10.2 | Click; charts update |

### E5.3 Distribution Chart

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E5.3.1 | MUST | Logarithmic-bucket histogram (4 buckets per octave) | P9 §4.2 | Verify bucket spacing |
| E5.3.2 | MUST | Percentile lines (p50, p99, p99.9) overlaid as dashed verticals | P9 §10.3 | Visual |
| E5.3.3 | MUST | Budget lines overlaid as solid colored verticals | P9 §10.3 | When budget defined; verify |
| E5.3.4 | MUST | Summary stats (count, p50, p99, max) in upper right | P9 §10.3 | Visual |
| E5.3.5 | MUST | Excludes self-subscribe rows by default; toggle to include | P9 §3.2 | Toggle available |
| E5.3.6 | MUST | Histogram includes a "≤0 ms" bucket (clock-sync error visible) | P9 §3.3 | Verify when test data has negative latencies |

### E5.4 Time Series Chart

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E5.4.1 | MUST | Two lines (p50, p99) over time | P9 §5.1 | Visual |
| E5.4.2 | MUST | Bucket size auto-selected per session span | P9 §5.3 | Inspect: 1h session uses 1-minute buckets etc. |
| E5.4.3 | MUST | Hover bucket: shows sample count | P9 §10.2 | Tooltip on hover |

### E5.5 Outliers

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E5.5.1 | MUST | Top 100 outliers shown with timestamp, topic, pub→sub, latency, threshold | P9 §10.5 | Verify table |
| E5.5.2 | MUST | Threshold source labeled: "budget" (from scenario metadata) or "top-0.1%" (fallback) | P9 §6.4 | Verify column |
| E5.5.3 | MUST | "Show in timeline" button per row → pivots to Timeline at outlier wallclock | P9 §10.5; D4.6 | Click; navigation works |

### E5.6 Performance Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E5.6.1 | MUST | Distribution query for 30-min bundle: < 500ms p95 | P9 §1.3 (criterion 9) | Profile |
| E5.6.2 | MUST | Time-series query: < 500ms p95 | P9 §1.3 | Profile |
| E5.6.3 | MUST | Outlier query (top 100): < 300ms | P9 §1.3 | Profile |

## E6. Gap Detection View

Sequence-number gap analysis (Arch §16.5; P9 §12).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E6.1 | MUST | View at `/v/gaps/{sessionId}` (bundle mode) | P9 §12 | Navigate |
| E6.2 | MUST | Two-panel layout: tuple summary (sorted by total missing count) + gap list | P9 §12 | Visual |
| E6.3 | MUST | Gap row shows: resumed-at time, topic, pub→sub, missing seq range, missing count | P9 §12 | Verify table content |
| E6.4 | MUST | Pivot to Timeline focused on resumed-at wallclock | P9 §12; D4.7 | Click pivot; navigate |
| E6.5 | MUST | First-sample edge case: shown with previousSequence=0, identifiable in UI | P9 §7.4 | Verify; documented as known edge case |
| E6.6 | MUST | Gap detection query for one (topic, pub, sub) with 10K samples: < 1s | P9 §1.3 | Profile |

## E7. Network Topology View

Publisher/subscriber graph (Arch §13.2; P9 §11).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E7.1 | MUST | View at `/v/topology/{sessionId}` (bundle mode) | P9 §11.4 | Navigate |
| E7.2 | MUST | Force-directed layout: ~200 iterations, settles in < 50ms | P9 §11.2 | Profile |
| E7.3 | MUST | Nodes drawn as circles labeled by nodeId | P9 §11.3 | Visual |
| E7.4 | MUST | Edges drawn as Bezier curves with arrowheads showing direction | P9 §11.3 | Visual |
| E7.5 | MUST | Edge weight proportional to log(messageCount) | P9 §11.3 | Heavy edges visually thicker |
| E7.6 | MUST | Per-(publisher, subscriber) edges bundled by default | P9 §11.1 | Multiple topics on one edge: collapsed |
| E7.7 | MUST | Click edge → side panel shows per-topic breakdown | P9 §11.4 | Click; panel visible with topics |
| E7.8 | MUST | Per-topic row has "Latency →" button → pivots to Replication Latency view filtered to that tuple | P9 §11.4; D4.8 | Verify pivot |
| E7.9 | MUST | Topology query: < 200ms p95 | P9 §1.3 | Profile |

## E8. Trigger Evaluation Log

Scenario-author tool (Arch §16.6; P8 §8).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E8.1 | MUST | View at `/v/triggers/{sessionId}` | P8 §8.4 | Navigate |
| E8.2 | MUST | Lists `scenario.trigger_evaluated` events (architecture convention) | P8 §8.1 | Verify; data comes from that topic |
| E8.3 | MUST | Per row: time, triggerId, label, publisher node, result (fired/not-fired) | P8 §8.4 | Verify columns |
| E8.4 | MUST | Filter by triggerId | P8 §8.4 | Dropdown; list narrows |
| E8.5 | MUST | Filter by result (fired/not-fired/all) | P8 §8.4 | Filter works |
| E8.6 | MUST | "Timeline" button per row → pivots to Timeline at evaluation time | P8 §8.4; D4.9 | Pivot works |
| E8.7 | MUST | "Tree" button per row → pivots to Causal Tree at the evaluation event | P8 §8.4 | Pivot works |
| E8.8 | MUST | Malformed payload tolerated: degraded row marked "(malformed payload)" | P8 §8.2 | Inject malformed; row appears defensively |
| E8.9 | MUST | Trigger eval list (5000 evaluations): < 300ms | P8 §1.3 (criterion 7) | Profile |

## E9. SQL Console

Engineer's escape hatch (Arch §16.7; P10).

### E9.1 Editor

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E9.1.1 | MUST | CodeMirror 6 editor with SQL syntax highlighting | P10 §5.2 | Visual |
| E9.1.2 | MUST | Cmd+Enter runs the query | P10 §5.2 | Test |
| E9.1.3 | MUST | Schema autocomplete: tables and columns from the queryable schema | P10 §5.2 | Type partial; suggestions appear |
| E9.1.4 | MUST | Custom completions for DuckDB functions (time_bucket, approx_quantile, json_extract_string) | P10 §5.2 | Type partial; functions suggested |
| E9.1.5 | MUST | History (last 50 queries) in right rail; click to reload | P10 §5.3 | Verify localStorage-backed |
| E9.1.6 | MUST | Schema Panel left rail: click table → inserts into editor | P10 §5.3 | Verify |

### E9.2 Execution Constraints

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E9.2.1 | MUST | Read-only enforcement: INSERT/UPDATE/DELETE/CREATE/DROP/ATTACH/COPY/PRAGMA/etc. all rejected | P10 §3.2 | Try forbidden; state=Rejected |
| E9.2.2 | MUST | Multi-statement queries rejected | P10 §3.2 | `SELECT 1; SELECT 2`; rejected |
| E9.2.3 | MUST | `read_csv_auto`/`read_parquet` with arbitrary paths rejected | P10 §3.2 | Try; rejected |
| E9.2.4 | MUST | Query timeout enforced (default 30s) | P10 §3.3 | Long query; Timeout state after 30s |
| E9.2.5 | MUST | Row limit auto-injected when absent (default 100K) | P10 §3.3 | SELECT without LIMIT; result capped |
| E9.2.6 | MUST | Memory limit applied per query via DuckDB PRAGMA (default 1 GB) | P10 §3.3 | Inspect query execution; limit applied |
| E9.2.7 | MUST | Bundle DuckDB opened in read-only file mode (Phase 4) | P4; P10 §3.2 | Verify file mode; defense in depth |
| E9.2.8 | MUST | Parameter binding via `$paramName` placeholders | P10 §3.3 | Run parameterized query |

### E9.3 Results

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E9.3.1 | MUST | Tabular result with sortable columns | P10 §5.4 | Click column header; sort changes |
| E9.3.2 | MUST | Chart view available when result has numeric column | P10 §5.3 | Toggle to chart tab when applicable |
| E9.3.3 | MUST | Pivot buttons per row for known columns: event_id, entity_id, trace_id, publish_wallclock | P10 §5.4 | Verify |
| E9.3.4 | MUST | Pivot navigates to appropriate view (Timeline / Entity History / Causal Tree) | P10 §5.4; D4.11 | Click pivot; navigate |
| E9.3.5 | MUST | Export to CSV | P10 §5.4 | Click Export CSV; download |
| E9.3.6 | MUST | Cancel button visible while running; cancels in-flight query | P10 §5.3 | Slow query; click cancel; query aborted |
| E9.3.7 | MUST | Error states render the DuckDB error message | P10 §5.3 | Invalid SQL; error visible |
| E9.3.8 | MUST | Elapsed time visible | P10 §5.3 | After run; visible |

### E9.4 Schema Introspection

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E9.4.1 | MUST | Tables exposed: events, slow_state (as DuckDB VIEWs over multi-interval union) | P10 §3.5 | DESCRIBE works against these names |
| E9.4.2 | MUST | fast_state queryable via `read_parquet('fast_state/...')` (documented in dialect notes) | P10 §3.5 | Verify; dialect notes mention this |
| E9.4.3 | MUST | Schema query cached after first call | P10 §3.4 | Second call < 5ms |

### E9.5 Performance Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| E9.5.1 | MUST | Simple SELECT, 1000 rows: < 500ms p95 | P10 §9.6 | Profile |
| E9.5.2 | MUST | Aggregate, 100k rows: < 2s p95 | P10 §9.6 | Profile |
| E9.5.3 | MUST | First paint of SQL Console: < 1s | P10 §9.6 | Profile |

---

# Part F — User Content Features

## F1. Annotations

Engineer/scenario-author markers attached to specific events, entities, traces, or time points (Arch §16.6; P8 §3-§5).

### F1.1 Data Model

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F1.1.1 | MUST | Annotation has: id (ULID), kind (event/entity/trace/time-point), session_id, target identifier, text, author, created_at | P8 §3.3 | Inspect schema |
| F1.1.2 | MUST | Four kinds supported: event, entity, trace, time-point | P8 §3.2 | Create one of each kind; all succeed |
| F1.1.3 | MUST | Kind=event: target_event_id required | P8 §3.3 | Validation rejects missing field |
| F1.1.4 | MUST | Kind=entity: target_entity_id required | P8 §3.3 | Same |
| F1.1.5 | MUST | Kind=trace: target_trace_id required | P8 §3.3 | Same |
| F1.1.6 | MUST | Kind=time-point: target_wallclock_utc required | P8 §3.3 | Same |
| F1.1.7 | MUST | Optional: color tag, urgency level | P8 §3.3 | Supported in schema |

### F1.2 CRUD via API

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F1.2.1 | MUST | `GET /api/annotations?sessionId=...` lists annotations for a session | P8 §4.1 | curl |
| F1.2.2 | MUST | List supports filter by kind | P8 §4.1 | curl with ?kind=event |
| F1.2.3 | MUST | `POST /api/annotations` creates | P8 §4.1 | curl |
| F1.2.4 | MUST | `GET /api/annotations/{id}` reads single | P8 §4.1 | curl |
| F1.2.5 | MUST | `PUT /api/annotations/{id}` updates | P8 §4.1 | curl |
| F1.2.6 | MUST | `DELETE /api/annotations/{id}` deletes | P8 §4.1 | curl |
| F1.2.7 | MUST | Validation: target field matches kind | P8 §4.2 | Mismatch → 400 |
| F1.2.8 | MUST | Validation: text length capped (e.g., 4 KB) | P8 §4.2 | Overlong → 400 |

### F1.3 UI Integration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F1.3.1 | MUST | Annotations indicator on Timeline at the event/time-point's position | P8 §5.1 | Create event annotation; marker visible |
| F1.3.2 | MUST | Click annotation marker → AnnotationDetail popover | P8 §5.1 | Click; popover opens |
| F1.3.3 | MUST | Entity annotations visible in Entity History summary strip | P8 §5.2 | Create entity annotation; appears |
| F1.3.4 | MUST | Trace annotations visible in Causal Tree summary panel | P8 §5.3 | Create trace annotation; appears |
| F1.3.5 | MUST | "Add annotation" action available from EventInspector | P8 §5.1 | Click; modal opens for event annotation |
| F1.3.6 | MUST | Edit/delete from annotation popover | P8 §5.1 | Click edit/delete; UI updates |

### F1.4 Bundle Mode

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F1.4.1 | MUST | When bundle is built, annotations exported to `annotations/annotations.json` | P8 §3.7 | Build bundle; inspect file |
| F1.4.2 | MUST | Offline viewer reads annotations from JSON | P8 §3.6 | Open bundle; annotations visible |
| F1.4.3 | MUST | Offline viewer: write attempts return 405 (read-only) | P8 §3.6 | curl POST against offline viewer; 405 |
| F1.4.4 | MUST | Bundle manifest covers annotations file (hash protected) | C3.3.4 | Verify hash |

## F2. Saved Views and Bookmarks

Named pointers to specific analytical view configurations (Arch §16.6; P8 §6).

### F2.1 Data Model

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F2.1.1 | MUST | Saved view has: id (ULID), label, description, view_type, session_id, route_path, query_params (JSON), tags, author, persona, created_at, last_opened_at | P8 §6.1 | Inspect schema |
| F2.1.2 | MUST | view_type enum: timeline, causal, entity, latency, gaps, topology, scenario, triggers, sql | P8 §6.1 | All view types creatable |
| F2.1.3 | MUST | Bookmark = saved view with `is_bookmark=true` flag for quick-access bar | P8 §6.5 | Toggle; UI updates |
| F2.1.4 | MUST | Optional: persona scope (engineer/scenario-author/operator) | P8 §6.1 | Filter by persona in UI |

### F2.2 CRUD via API

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F2.2.1 | MUST | `GET /api/saved-views` lists; filters: sessionId, viewType, tag, persona, isBookmark | P8 §6.4 | curl with filters |
| F2.2.2 | MUST | `POST /api/saved-views` creates | P8 §6.4 | curl |
| F2.2.3 | MUST | `GET /api/saved-views/{id}` reads single | P8 §6.4 | curl |
| F2.2.4 | MUST | `PUT /api/saved-views/{id}` updates label, description, tags, bookmark flag | P8 §6.4 | curl |
| F2.2.5 | MUST | `DELETE /api/saved-views/{id}` deletes | P8 §6.4 | curl |
| F2.2.6 | MUST | `POST /api/saved-views/{id}/opened` records last-opened timestamp | P8 §6.4 | curl |

### F2.3 UI Integration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F2.3.1 | MUST | Every analytical view has "Save current view" affordance in toolbar | P8 §6.6 | Visible across views |
| F2.3.2 | MUST | Save modal captures: label, description, tags, bookmark toggle, persona scope | P8 §6.6 | Create one; all fields persist |
| F2.3.3 | MUST | SavedViewsView at `/v/saved-views/{sessionId}` lists views grouped by view_type | P8 §6.7 | Navigate |
| F2.3.4 | MUST | BookmarkBar component shows persona-filtered bookmarks for quick access | P8 §6.6 | Visible in app shell |
| F2.3.5 | MUST | Click a saved view → restores the exact view state (URL params) | P8 §6.2 | Click; view restores to saved state |
| F2.3.6 | MUST | Edit a saved view's metadata in-place | P8 §6.7 | UI for editing |
| F2.3.7 | MUST | Tag-based filtering in the saved views browser | P8 §6.7 | Click a tag; list narrows |

### F2.4 Bundle Mode

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F2.4.1 | MUST | When bundle is built, saved views exported to `annotations/saved_views.json` | P8 §6.3 | Build; inspect file |
| F2.4.2 | MUST | Offline viewer reads saved views from JSON | P8 §6.3 | Open bundle; views visible |
| F2.4.3 | MUST | Offline viewer: write attempts return 405 | P8 §6.3 | curl POST; 405 |

## F3. Saved Queries

SQL queries authored by users plus a built-in template library (Arch §16.7; P10 §6).

### F3.1 Data Model

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F3.1.1 | MUST | Saved query has: id (ULID), label, description, sql_text, parameters (array), tags, is_built_in, is_favorite, author, created_at, last_run_at, run_count | P10 §6.1 | Inspect schema |
| F3.1.2 | MUST | Parameter has: name, duckType, defaultValueText, optional description | P10 §6.1 | Inspect |
| F3.1.3 | MUST | Default value text supports tokens: `session_start`, `session_end`, `now`, `N hour ago` etc. | P10 §6.5 | Verify resolution |

### F3.2 CRUD via API

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F3.2.1 | MUST | `GET /api/saved-queries` lists with filters (tag, favorite, built-in) | P10 §6.4 | curl |
| F3.2.2 | MUST | `POST /api/saved-queries` creates | P10 §6.4 | curl |
| F3.2.3 | MUST | `GET /api/saved-queries/{id}` reads single | P10 §6.4 | curl |
| F3.2.4 | MUST | `PUT /api/saved-queries/{id}` updates (rejected for built-ins) | P10 §6.4 | Try on built-in; rejected |
| F3.2.5 | MUST | `DELETE /api/saved-queries/{id}` deletes (rejected for built-ins) | P10 §6.4 | Try on built-in; rejected |
| F3.2.6 | MUST | `POST /api/saved-queries/{id}/favorite` toggles favorite | P10 §6.4 | curl |
| F3.2.7 | MUST | `POST /api/saved-queries/{id}/clone` creates editable copy of any query (including built-ins) | P10 §6.4 | Clone built-in; new editable copy exists |
| F3.2.8 | MUST | `POST /api/saved-queries/{id}/run` increments run_count and updates last_run_at | P10 §6.4 | curl; verify counters |

### F3.3 Built-In Query Set

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F3.3.1 | MUST | Ships with at least these built-in queries: "Top topics by event count", "Events on a trace", "Event counts per node", "Latency distribution by topic", "Events touching an entity" | P10 §6.3 | Inspect built-in list; all present |
| F3.3.2 | MUST | Loaded on first observer/viewer startup from `builtin-queries.json` | P10 §6.3 | First run; appear |
| F3.3.3 | MUST | Not duplicated on subsequent startups | P10 §6.3 | Second run; still single copy |
| F3.3.4 | MUST | Built-ins marked `is_built_in = true` | P10 §6.3 | Inspect record |
| F3.3.5 | MUST | Built-ins visible in SavedQueriesView with visual distinction | P10 §6 | UI distinguishes built-in vs user |
| F3.3.6 | MUST | Built-in latency query labeled as bundle-mode-only | P10 §6.3 | Description includes warning |

### F3.4 UI Integration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| F3.4.1 | MUST | SavedQueriesView at `/v/saved-queries` lists all queries | P10 §6 | Navigate |
| F3.4.2 | MUST | Filter by author, tag, favorite | P10 §6 | UI controls work |
| F3.4.3 | MUST | "Save" button in SQL Console captures current SQL with parameter inference | P10 §5.3 | Save query; reload to verify |
| F3.4.4 | MUST | Parameter editor panel for parameterized queries | P10 §6.5 | Open parameterized query; panel visible; defaults pre-filled |
| F3.4.5 | MUST | User can override parameter values before running | P10 §6.5 | Edit values; run |
| F3.4.6 | MUST | Parameter defaults resolved client-side (session_start, now, etc.) | P10 §6.5 | Open; default values resolved |
| F3.4.7 | MUST | Run-count visible per query (sorting by popularity possible) | P10 §6.1 | Sort by run_count |

---

# Part G — Real Adapter Integration

## G1. DDS Adapter

`Tracer.Adapters.DDS` — loopback subscriber translating Cyclone DDS samples to DiagnosticRecord (P11 §3).

### G1.1 Process Model

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G1.1.1 | MUST | Adapter runs as a loopback subscriber in the simulation's process (not a separate process) | P11 §3.1 | Process inspection |
| G1.1.2 | MUST | No reference to DDS types from `Tracer.Core` | A3.1.6; P11 §3.7 | Inspect Tracer.Core.csproj |
| G1.1.3 | MUST | `IDdsSample` abstraction wraps the customer's binding-specific sample type | P11 §3.7 | Inspect adapter assembly |
| G1.1.4 | MUST | Adapter forwards records to SharedMemoryTransport, not to disk directly | P11 §3.1 | Architecture; no DuckDB writes in adapter |

### G1.2 Subscription Behavior

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G1.2.1 | MUST | One subscriber per declared topic; all funnel into single bounded channel | P11 §3.3 | Code review |
| G1.2.2 | MUST | Bounded channel default 50K capacity | P11 §3.8 | Inspect config |
| G1.2.3 | MUST | DropOldest under load; warning logged | P11 §3.3 | Stress; verify drop count |
| G1.2.4 | MUST | DDS callback thread never blocks on agent-side I/O | P11 §3.3 | Verify via thread analysis |
| G1.2.5 | MUST | DdsTopicRegistry populated from configuration at startup | P11 §3.5 | Inspect appsettings.json |
| G1.2.6 | MUST | Unrecognized topics logged and skipped (not crash) | P11 §3.4 | Unregistered topic sample; logged, no crash |

### G1.3 Sample Translation

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G1.3.1 | MUST | `dds_write_ts()` timestamp → publish_wallclock | P11 §3.4 | Round-trip test |
| G1.3.2 | MUST | Translation-time clock → receive_wallclock (loopback: ~zero latency) | P11 §3.4 | Verify |
| G1.3.3 | MUST | trace_id, event_id, parent_event_id extracted via reflective accessors | P11 §3.6 | Verify exact bit-for-bit round trip |
| G1.3.4 | MUST | Accessors compiled (System.Linq.Expressions) and cached per sample type | P11 §3.6 | Benchmark: no per-call reflection |
| G1.3.5 | MUST | Missing trace context property throws clear error at first sample of that type | P11 §3.6 | Misconfigured topic; clear error |
| G1.3.6 | MUST | Topic kind enum drives output: Event → EventRecord, SlowState/FastState → StateSampleRecord | P11 §3.4 | Verify per kind |
| G1.3.7 | MUST | Fast state typed values extracted via reflection per topic metadata | P11 §3.4 | Inspect a fast-state Parquet row from DDS source |
| G1.3.8 | MUST | Publisher and subscriber nodeId both set to this node (loopback) | P11 §3.4 | Verify |

### G1.4 Configuration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G1.4.1 | MUST | `DdsAdapterConfig` includes PublisherNodeId, Topics array, IngestBufferSize, Cyclone DDS participant settings (DomainId, QosProfile) | P11 §3.8 | Inspect config schema |
| G1.4.2 | MUST | Per-topic configuration includes: TopicName, SampleTypeName, Kind, EntityIdField, optional OwningPlayerIdField, SeverityField, NotableLabelField, InstanceKeyField | P11 §3.5 | Inspect schema |

## G2. Shared Memory Transport

`Tracer.Adapters.SharedMemory` — cross-process IPC (P11 §4).

### G2.1 Primitives

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G2.1.1 | MUST | Shared memory region backed by `MemoryMappedFile` with `CreateOrOpen` | P11 §4.3 | Inspect source |
| G2.1.2 | MUST | Named semaphore for producer→consumer signaling | P11 §4.5 | Inspect source |
| G2.1.3 | MUST | Header is 4096 bytes with magic, version, capacity, atomic offsets, PIDs, heartbeats, dropped_count | P11 §4.2 | Inspect layout |
| G2.1.4 | MUST | Magic check on open; bad magic → fail-fast | P11 §4.3 | Open with wrong layout; fail |
| G2.1.5 | MUST | Atomic operations use Volatile.Read / Volatile.Write for cross-process visibility | P11 §4.3 | Source review |

### G2.2 Ring Buffer Behavior

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G2.2.1 | MUST | Default capacity 64 MB (configurable) | P11 §4.7 | Inspect config |
| G2.2.2 | MUST | Each record: 4-byte length + payload | P11 §4.2 | Verify layout |
| G2.2.3 | MUST | Wraparound discipline: padding marker (length=0) means "wrap" | P11 §4.3 | Test wraparound |
| G2.2.4 | MUST | Producer never blocks; DropOldest activates when buffer full | P11 §4.3 | Stall consumer; verify producer proceeds |
| G2.2.5 | MUST | dropped_count atomically incremented on drop | P11 §4.6 | Verify counter advances |
| G2.2.6 | MUST | SPSC: one producer, one consumer; no locks beyond atomic offsets | P11 §4.2 | Verify in source |

### G2.3 Codec

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G2.3.1 | MUST | Records encoded with System.Text.Json source-generated serializers | P11 §4.4 | Inspect codec |
| G2.3.2 | MUST | Bit-for-bit round-trip preserved for EventRecord and StateSampleRecord | P11 §4.4 | Round-trip test |
| G2.3.3 | MUST | Unicode payload preserved | P11 §4.4 | Test |
| G2.3.4 | SHOULD | If JSON proves too slow, codec swap path to MessagePack is contained in single file | P11 §4.4 | Architectural note |

### G2.4 Transport Wrapper

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G2.4.1 | MUST | `SharedMemoryTransport.CreateProducer` and `CreateConsumer` static factories | P11 §4.5 | Inspect source |
| G2.4.2 | MUST | Consumer-side opens with retry (60 attempts × 1s) — agent can start before simulation | P11 §4.5 | Verify; tested |
| G2.4.3 | MUST | EnqueueAsync: encode + write + signal semaphore | P11 §4.5 | Source review |
| G2.4.4 | MUST | ConsumeAsync: wait semaphore + drain ring + decode | P11 §4.5 | Source review |
| G2.4.5 | MUST | Consumer respects CancellationToken with <100ms latency | P11 §4.5 | Test |
| G2.4.6 | MUST | dropped_count visible to consumer; agent logs deficit | P11 §4.6 | Verify monitoring loop |

### G2.5 Performance

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G2.5.1 | MUST | Sustained 1000+ events/sec for 60s: < 0.1% drop rate | P11 §1.3 (criterion 2); P11 §10 | Integration-real benchmark |
| G2.5.2 | MUST | Burst 5000+ events/sec: no producer blockage | P11 §10 | Same |
| G2.5.3 | MUST | Per-enqueue cost < 1ms | P11 §4.1 | Microbenchmark |

## G3. Sync System Adapter

`Tracer.Adapters.Sync` — per-interval upload via customer's sync system (Sync addendum; P11 §5).

### G3.1 REST Contract

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G3.1.1 | MUST | Uses Telemetry sync category | Sync; P11 §5.3 | Inspect requests; Category="Telemetry" |
| G3.1.2 | MUST | `POST /api/sync/intents` registers upload intent | Sync; P11 §5.4 | curl test |
| G3.1.3 | MUST | `GET /api/sync/intents/{id}` returns intent status | Sync; P11 §5.4 | curl |
| G3.1.4 | MUST | Intent destination path: `telemetry/{sessionId}/{nodeId}/{intervalId}` | P11 §5.3 | Inspect request body |

### G3.2 Behavior

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G3.2.1 | MUST | SubmitAsync registers intent and returns IntentId | P11 §5.3 | Verify |
| G3.2.2 | MUST | WaitForCompletionAsync polls with exponential backoff (2s → 60s max) | P11 §5.3 | Trace polling intervals |
| G3.2.3 | MUST | State transitions: Pending → Uploading → Completed (or Failed) | P11 §5.3 | Verify state mapping |
| G3.2.4 | MUST | Retry transient HTTP failures up to RetryAttempts (default 3) | P11 §5.5 | Inject 503; verify retry |
| G3.2.5 | MUST | Agent does not block simulation on sync failures | P11 §5.6 | Disable sync master; verify simulation unaffected |
| G3.2.6 | MUST | Local interval retained until upload Completed | P11 §5.6; B1.4.5 | Verify |
| G3.2.7 | MUST | Failed uploads surface to operator message queue | P11 §5.6 | Force failure; verify operator notice |

### G3.3 Configuration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G3.3.1 | MUST | `SyncAdapterConfig` includes SyncMasterBaseUrl, RequestTimeout, RetryAttempts | P11 §5.5 | Inspect schema |

## G4. NAS Storage Reader

`Tracer.Adapters.Nas` — aggregator reads uploaded data from NAS (P11 §6).

### G4.1 Discovery

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G4.1.1 | MUST | `ListIntervalsAsync` scans `<NasRoot>/telemetry/<sessionId>/` for per-node, per-interval directories | P11 §6.3 | Inspect implementation |
| G4.1.2 | MUST | Skips intervals lacking `.complete` sentinel; logs warning | P11 §6.3, §6.4 | Place incomplete; verify skip |
| G4.1.3 | MUST | Returns NodeIntervalDescriptor with SessionId, NodeId, IntervalId, SourcePath, EstimatedBytes | P11 §6.3 | Verify shape |

### G4.2 Staging

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G4.2.1 | MUST | `StageAsync` returns StagedInterval with LocalPath | P11 §6.3 | Test |
| G4.2.2 | MUST | Default mode (`PreferLocalStaging=false`): returns UNC path directly | P11 §6.3 | Verify; DuckDB reads from UNC |
| G4.2.3 | MUST | Optional local-staging mode: copies to local temp, cleans up on dispose | P11 §6.3 | Toggle config; verify behavior |

### G4.3 Configuration

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G4.3.1 | MUST | `NasAdapterConfig` includes NasRoot (UNC path), PreferLocalStaging | P11 §6.5 | Inspect schema |
| G4.3.2 | MUST | UNC path resolution works on Windows | P11 §6 | Test with actual NAS share |

## G5. Adapter Selection and Configuration

Configuration-driven DI to choose mock vs real (P11 §7).

### G5.1 Configuration Schema

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G5.1.1 | MUST | Top-level `adapters` section with: dataSource, transport, upload, storageReader, clock | P11 §7.1 | Inspect appsettings.json |
| G5.1.2 | MUST | Valid dataSource values: `"mock"`, `"dds"` | P11 §7.1 | Both selectable |
| G5.1.3 | MUST | Valid transport values: `"in-process"`, `"shared-memory"` | P11 §7.1 | Both selectable |
| G5.1.4 | MUST | Valid upload values: `"local-file-system"`, `"sync"` | P11 §7.1 | Both selectable |
| G5.1.5 | MUST | Valid storageReader values: `"local-file-system"`, `"nas"` | P11 §7.1 | Both selectable |
| G5.1.6 | MUST | Valid clock values: `"system"`, `"simulated"` | P11 §7.1 | Both selectable |

### G5.2 Registry Behavior

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G5.2.1 | MUST | `AdapterRegistry.RegisterAdapters` registers DI bindings based on config | P11 §7.2 | Code review |
| G5.2.2 | MUST | Invalid adapter value throws clear startup error | P11 §7.2 | Try "foo"; clear failure |
| G5.2.3 | MUST | Mock and real adapters interchangeable without code recompile | P11 §7.3; A3.3.5 | Switch in config; restart; behavior changes |

### G5.3 Environment Presets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| G5.3.1 | MUST | `appsettings.json` default: all mocks (safe for fresh checkout) | P11 §7.4 | Verify |
| G5.3.2 | MUST | `appsettings.Development.json`: mocks (IDE debugging) | P11 §7.4 | Verify |
| G5.3.3 | MUST | `appsettings.IntegrationReal.json`: all real adapters | P11 §7.4 | Verify |
| G5.3.4 | MUST | `appsettings.Production.json`: real adapters with prod parameters | P11 §7.4 | Verify |
| G5.3.5 | MUST | Environment selected via `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT` | P11 §7.4 | Standard .NET convention |

---

# Part H — Operations and Hardening

## H1. Resource Limits and Bounded Memory

Every queue, buffer, and cache is bounded (P11 §9.1).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H1.1 | MUST | Agent process RSS capped at 2 GB default via OS Job Object | P11 §9.1 | Inspect job object; force exceed → crash |
| H1.2 | MUST | Observer process RSS capped at 4 GB default | P11 §9.1 | Same |
| H1.3 | MUST | Agent ingest channel bounded at 50K records | P11 §9.1; B1.2.2 | Inspect |
| H1.4 | MUST | SharedMemoryTransport buffer bounded (default 64 MB) | P11 §9.1; G2.2.1 | Inspect |
| H1.5 | MUST | DuckDB memory_limit applied per query (1 GB default) | P11 §9.1; E9.2.6 | Verify |
| H1.6 | MUST | Open file handles per agent < 200 sustained | P11 §9.1 | Soak test; verify |
| H1.7 | MUST | All bounded queues use DropOldest under back-pressure (never block producer) | P11 §3.3, §4.3 | Architectural pattern |

## H2. Graceful Degradation Under Load

Behavior under producer/transport/disk/network overload (P11 §9.2).

### H2.1 Producer-Side Overload

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H2.1.1 | MUST | When DDS adapter channel fills: DropOldest, logs warning | P11 §9.2 | Stress; verify behavior |
| H2.1.2 | MUST | Drops accumulate in counter and surface via operator queue | P11 §9.2 | Force drops; verify reporting |
| H2.1.3 | MUST | Simulation never blocked by Tracer | P11 §9.2 | Stress producer; verify simulation unaffected |

### H2.2 Transport-Side Overload

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H2.2.1 | MUST | When SharedMemoryTransport ring fills: DropOldest, increments dropped_count | P11 §9.2; G2.2 | Stall agent; force drop |
| H2.2.2 | MUST | Agent monitor reports deficit on resume | P11 §4.6 | Verify log line |
| H2.2.3 | MUST | Producer never blocks | P11 §4.3 | Verify |

### H2.3 Disk-Side Overload

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H2.3.1 | MUST | DuckDB Appender backpressure handled via channel boundedness | P11 §9.2 | Bounded channels prevent OOM cascade |
| H2.3.2 | SHOULD | Disk-full failure: writes fail, agent surfaces critical error, simulation continues | P11 §9.2 | Manual: fill disk; verify |

### H2.4 NAS-Side Overload

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H2.4.1 | MUST | NAS unreachable: agent buffers intervals locally; retries on recovery | P11 §9.2; G3.2.5 | Disable NAS; verify accumulation |
| H2.4.2 | MUST | Backlog visible via operator notice when exceeds N intervals or T hours | P11 §9.2 | Configure threshold; verify alert |

## H3. Crash Recovery and Restart Resilience

Each process recovers from transient crashes (P11 §9.3).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H3.1 | MUST | Agent crash mid-interval: incomplete interval discarded on restart (`.complete` sentinel test) | P11 §9.3; B1.5 | Force crash; restart; verify |
| H3.2 | MUST | Agent crash mid-rotation: same (`.complete` written last) | P11 §9.3 | Force crash mid-rotation |
| H3.3 | MUST | Observer crash: observer is disposable; restart preserves no in-memory data but resumes cleanly | P11 §9.3; B2.1.2 | Kill observer; restart |
| H3.4 | MUST | Aggregator crash mid-bundle: bundle build is idempotent — restart resumes/restarts | P11 §9.3; B3.3.2 | Force crash; restart |
| H3.5 | MUST | Sync master unreachable: agent buffers; resumes uploads on master return | P11 §9.3; G3.2.5 | Verify |
| H3.6 | MUST | NAS unreachable for aggregator: bundle build fails cleanly with clear error | P11 §9.3 | Disable NAS; build; clear failure |
| H3.7 | MUST | OS-killed process (memory limit exceeded) logs cause and restarts cleanly | P11 §9.1, §11 | Force memory exceed; observe restart |

## H4. Monitoring and Health Reporting

Operators consume Tracer's signals via existing log aggregation (P11 §9.4).

### H4.1 Structured Logging

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H4.1.1 | MUST | Every adapter operation emits structured log event | P11 §9.4; A6.2 | Sample logs |
| H4.1.2 | MUST | Log schema: timestamp, level, category, message, properties | P11 §9.4 | Inspect any log line |
| H4.1.3 | MUST | Properties carry domain-specific fields (topic, eventId, etc.) | P11 §9.4 | Inspect |
| H4.1.4 | MUST | Default level Information; key namespaces Debug per Arch §22.2 | A6.2.5 | Verify |

### H4.2 Health Endpoint

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H4.2.1 | MUST | `GET /api/health` returns status + agent/observer metrics | P11 §9.4 | curl |
| H4.2.2 | MUST | Agent metrics include: sharedMemoryDropped, ingestChannelDepth, intervalsAwaitingUpload, lastIntervalCompletedAtUtc | P11 §9.4 | Inspect response |
| H4.2.3 | MUST | Observer metrics include: ingestChannelDepth, sseConnectionsActive | P11 §9.4 | Inspect response |
| H4.2.4 | MUST | Status one of: Healthy / Degraded / Unhealthy | P11 §9.4 | Force degraded state; verify status reports it |

### H4.3 Operator Message Queue

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H4.3.1 | MUST | Significant operational events appear in an operator notice queue (Phase 8) | P8; P11 §5.6 | Verify queue exists |
| H4.3.2 | MUST | Examples: sync upload failures, NAS unreachable, drop deficits | P11 §5.6, §9.2 | Force conditions; verify entries |

## H5. Performance Targets

Hard targets at the assumed scale (Arch §17). Operations violating targets are architectural bugs or scope errors.

### H5.1 Query Performance Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H5.1.1 | MUST | Session-overview zoom on 100M-event session: < 1s | Arch §17; P5 §1.3 | Benchmark |
| H5.1.2 | MUST | Timeline initial render on 1M events: < 500ms | E2.5.1 | Benchmark |
| H5.1.3 | MUST | Pan/zoom response: < 100ms | E2.5.2 | Benchmark |
| H5.1.4 | MUST | Apply filter: < 300ms p95 | E2.5.3 | Benchmark |
| H5.1.5 | MUST | Click → inspector: < 100ms | E2.5.4 | Benchmark |
| H5.1.6 | MUST | SSE event → marker: < 100ms | E2.5.5 | Benchmark |
| H5.1.7 | MUST | Trace tree query, 1000-event trace: < 200ms p95 | E3.4.1 | Benchmark |
| H5.1.8 | MUST | Entity history full load: < 1.5s cold cache | E4.5.2 | Benchmark |
| H5.1.9 | MUST | Latency distribution query: < 500ms p95 | E5.6.1 | Benchmark |
| H5.1.10 | MUST | Gap detection on 10K samples: < 1s | E6.6 | Benchmark |
| H5.1.11 | MUST | Topology query: < 200ms p95 | E7.9 | Benchmark |
| H5.1.12 | MUST | SQL Console simple SELECT: < 500ms p95 | E9.5.1 | Benchmark |
| H5.1.13 | MUST | Bundle library list (100 bundles): < 200ms | P10 §11 | Benchmark |

### H5.2 Throughput Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H5.2.1 | MUST | Agent sustains 1000+ events/sec ingestion with < 0.1% drops | P11 §1.3 (criterion 2) | Soak benchmark |
| H5.2.2 | MUST | Agent CPU < 50% (single core) at 5000 events/sec | P11 §12 | Profile under stress |
| H5.2.3 | MUST | Agent memory stays under configured limit (2 GB) at sustained load | P11 §12; H1.1 | Soak |

### H5.3 Resource Targets

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| H5.3.1 | MUST | Agent process RSS at idle: < 200 MB | P11 §9.1 | Baseline measurement |
| H5.3.2 | MUST | Observer process RSS with 100 SSE clients: < 4 GB | P11 §9.1; H1.2 | Measurement |
| H5.3.3 | MUST | 48-hour soak: no monotonic resource growth | P11 §10; P11 §12 | Soak |

---

# Part I — Testing and Quality

## I1. Test Suite Structure

Three test categories with distinct cadences (Arch §19; P1-P11).

### I1.1 Unit Tests

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I1.1.1 | MUST | Per-component unit tests in `Tracer.Tests.Unit` | P1 §11 | Test project exists; runs |
| I1.1.2 | MUST | Each phase's unit-test count meets that phase's DoD target | P1-P11 DoD | Run; count |
| I1.1.3 | MUST | Total unit test count: > 400 | Sum of phase targets | `dotnet test` reports |
| I1.1.4 | MUST | Unit tests run on every PR; suite completes in < 30s | Arch §19; P1 §11 | CI logs |

### I1.2 Integration Tests (Mock-Based)

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I1.2.1 | MUST | `Tracer.Tests.Integration` exercises full stack with mock adapters | P1 §11 | Test project exists |
| I1.2.2 | MUST | Each integration test takes < 1 second | Arch §19 | Profile per test |
| I1.2.3 | MUST | Integration tests run on every PR | Arch §19 | CI logs |
| I1.2.4 | MUST | Coverage includes: full session lifecycle, bundle build round-trip, every analytical view's data path | P1-P10 | Inspect test list |

### I1.3 Integration-Real Tests

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I1.3.1 | MUST | `Tracer.Tests.Integration.Real` exists | P11 §8 | Test project exists |
| I1.3.2 | MUST | Runs against real adapters (DDS, sync, NAS) | P11 §8.1 | Inspect; uses real adapters |
| I1.3.3 | MUST | Runs on dedicated CI lane, not on every PR | P11 §8.4 | CI configuration |
| I1.3.4 | MUST | Failures block release tags but not main merges | P11 §8.4 | CI policy |
| I1.3.5 | MUST | Test categories implemented: TraceContextPropagation, SharedMemoryThroughput, SharedMemoryLoss, SyncUpload, NasReader, EndToEnd, Soak | P11 §8.2 | All present |

### I1.4 Soak Tests

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I1.4.1 | MUST | 48-hour continuous run with real simulation harness | P11 §1.3 (criterion 8); P11 §10.3 | Run completes |
| I1.4.2 | MUST | Monitors memory, CPU, disk, file handles for trend lines | P11 §10.3 | Resource metrics captured |
| I1.4.3 | MUST | No leak slopes detected | P11 §10.3 | Plots flat |
| I1.4.4 | MUST | Bundle build at any time succeeds | P11 §10.3 | Random-time check |
| I1.4.5 | MUST | Viewer queries succeed throughout | P11 §10.3 | Same |
| I1.4.6 | MUST | Agent restart mid-run recovers correctly | P11 §10.3 | Restart triggered; verify |

### I1.5 Frontend Tests

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I1.5.1 | MUST | Vitest unit tests for composables, stores, renderers | P5 §10; P6 §11; P7 §12; P8 §10; P9 §14; P10 §9 | Run vitest |
| I1.5.2 | MUST | Playwright E2E tests cover key user flows per phase | Same | Run playwright |
| I1.5.3 | MUST | E2E test for every analytical view's primary interaction | P5-P10 | Inspect spec files |
| I1.5.4 | MUST | E2E tests run against built artifact (not dev server) | Standard practice | CI configuration |

### I1.6 Security Tests

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I1.6.1 | MUST | Dedicated `SqlGuardrails` security test suite | P10 §9.5 | Test class exists; growable |
| I1.6.2 | MUST | Tests cover all enumerated forbidden constructs | P10 §3.2 | One test per construct |
| I1.6.3 | MUST | Zip-slip defense tests for bundle import | P10 §9.4 | Malicious zip rejected |
| I1.6.4 | MUST | Annotation/saved-view text length cap enforcement | P8 | Overlong → 400 |

## I2. Integration with Real Customer Environment

The system meets real data without redesign (P11 §1.5).

| # | MUST/SHOULD | Criterion | Reference | What to verify |
|---|---|---|---|---|
| I2.1 | MUST | Customer's actual simulation engine produces samples that flow through to bundles | P11 §1.3 (criterion 1, 10) | End-to-end test against real sim |
| I2.2 | MUST | trace_id, event_id, parent_event_id round-trip bitwise correctly | P11 §1.3 (criterion 3); A2.3 | Integration-real test |
| I2.3 | MUST | Per-interval upload succeeds via sync system to NAS | P11 §1.3 (criterion 4) | Integration-real |
| I2.4 | MUST | Aggregator reads multi-node intervals from NAS, produces valid bundle | P11 §1.3 (criterion 5) | Integration-real |
| I2.5 | MUST | Cross-node receive_wallclock values present in bundle | P11 §1.3 (criterion 6) | Inspect bundle |
| I2.6 | MUST | Replication Latency view renders meaningful output on real-data bundle | P11 §1.3 (criterion 6) | Visual verification |
| I2.7 | MUST | All Phase 5-10 views render meaningfully against real-data bundle | P11 §12 | Walk through each view |
| I2.8 | MUST | All Phase 1-10 mock-based tests continue to pass after Phase 11 changes | P11 §1.3 (criterion 7) | Run full suite |
| I2.9 | MUST | At least one full real-data session produces a usable bundle for analysis | P11 §1.3 (criterion 10) | Engineer review |

---

# Part J — Scope Boundaries (Explicitly Deferred)

## J1. What Tracer Does Not Do

The following are explicitly **out of scope** for the 1.0 implementation (Arch §1.2; P11 §13.2). Their absence is **not** a defect — they appear here to clarify the boundary.

### J1.1 Operational Scope

| # | Out-of-scope item | Reference | Why deferred |
|---|---|---|---|
| J1.1.1 | Real-time alerting (PagerDuty/Slack integration) | P9 §1.2; P11 §1.2 | Structured logs are the contract; downstream pipelines are operator-owned |
| J1.1.2 | Automated root cause analysis | P9 §1.2 | Phase 9 flags outliers; engineer correlates |
| J1.1.3 | Multi-bundle comparison (yesterday vs today) | P9 §1.2; P10 §1.2 | Single-bundle is the scope; future ask if needed |
| J1.1.4 | Cross-session analysis | P10 §1.2; P11 §13.2 | SQL Console queries one bundle at a time |
| J1.1.5 | AI/LLM-assisted analysis | P10 §1.2 | Out of architectural scope |
| J1.1.6 | Bundle versioning and migration | P10 §1.2 | Bundle is immutable; rebuild on schema changes |
| J1.1.7 | External authorization (LDAP/AD integration) | P8 §7.4; P11 §13.2 | Personas are UI defaults, not authorization |
| J1.1.8 | Authoritative audit trails | P11 §13.2 | Beyond 1.0 scope |
| J1.1.9 | Adversarial security model | P10 §3.2; P11 §13.2 | Read-only SQL filtering is best-effort; trusts operators |

### J1.2 Scale Scope

| # | Out-of-scope item | Reference | Why deferred |
|---|---|---|---|
| J1.2.1 | Production-grade scaling > 200 nodes | Arch §1.1; P11 §13.2 | Architecture targets ~200 nodes; sharded aggregator etc. needed for larger |
| J1.2.2 | Multi-region topologies | Arch §1.2; P11 §13.2 | Single sync-master, single NAS |
| J1.2.3 | Multi-master topologies | Arch §1.2 | Same |

### J1.3 Adapter Scope

| # | Out-of-scope item | Reference | Why deferred |
|---|---|---|---|
| J1.3.1 | Alternative DDS implementations beyond Cyclone | Arch §1.2 | Customer environment uses Cyclone |
| J1.3.2 | Alternative IPC transports beyond shared memory | P11 §4.1 | Single transport adequate |
| J1.3.3 | Cloud object storage as bundle library backend | P10 | Local filesystem is the scope |

### J1.4 Simulation-Side Scope

| # | Out-of-scope item | Reference | Why deferred |
|---|---|---|---|
| J1.4.1 | Simulation-side trace context propagation | P11 §1.2 | Integration project's responsibility; documented |
| J1.4.2 | Simulation-side DDS source timestamp discipline | P11 §3.4 | Same |
| J1.4.3 | Sync system Telemetry-category implementation | Sync; P11 §1.2 | Sync team owns this |
| J1.4.4 | NAS provisioning and retention policy | P11 §1.2 | Operations team |
| J1.4.5 | Production deployment automation (CI/CD pipelines) | P11 §1.2 | Operations team |

### J1.5 UX Scope

| # | Out-of-scope item | Reference | Why deferred |
|---|---|---|---|
| J1.5.1 | Mobile or tablet UI | All view designs | Desktop-only target |
| J1.5.2 | Accessibility certification (WCAG AAA) | All view designs | Best-effort but not certified |
| J1.5.3 | Internationalization (i18n) | All view designs | English-only |
| J1.5.4 | Custom dashboards arranged from saved-query results | P11 §13.3 | Listed as likely post-1.0 ask |
| J1.5.5 | Collaborative real-time editing of annotations | P10 §1.2 | Single-author semantics |

### J1.6 Storage Scope

| # | Out-of-scope item | Reference | Why deferred |
|---|---|---|---|
| J1.6.1 | Long-term archival to cold storage | P11 §13.3 | Bundles accumulate; cold storage is a future capability |
| J1.6.2 | Compressed bundle storage | P4 | Standard zip is acceptable |
| J1.6.3 | Bundle encryption at rest | P4 | Operations concern |
| J1.6.4 | Time-based retention of annotations | P8 | Annotations live with their bundle |

---

# Appendix A — Verification Procedure

To validate an implementation against this acceptance criteria document:

1. **Walk the document part-by-part** in order (A → J).
2. **For each MUST criterion**, perform the "What to verify" step. Mark Pass / Fail / Not Applicable.
3. **For each SHOULD criterion**, verify and note any deviation in a comments column.
4. **Compile a summary** of:
   - MUSTs passing / total
   - SHOULDs passing / total
   - List of failed MUSTs (these are blockers)
   - List of failed SHOULDs (these are concerns)
5. **A complete implementation has 100% MUSTs passing.** Implementations with failed MUSTs are not yet feature-complete and the gaps should be tracked.

A practical verification spreadsheet template might look like:

```
| Criterion ID | Status (Pass/Fail/N-A) | Date Verified | Verifier | Evidence/Notes |
| ------------ | ---------------------- | ------------- | -------- | -------------- |
| A1.1.1       | Pass                   | 2026-MM-DD    | Name     | "Inspected event in Timeline; hex 16-char format confirmed" |
| A1.1.2       | Pass                   | ...           | ...      | ... |
| ...          | ...                    | ...           | ...      | ... |
```

# Appendix B — Document Cross-Reference Quick Index

| Reference | Document | Coverage |
|---|---|---|
| Arch | `tracer_architecture_v1.md` | System architecture, top-level decisions, scope, scale targets |
| Sync | `sync_addendum_telemetry.md` | Telemetry sync-category contract |
| P1 | `tracer_phase1_design.md` | Core types, storage schemas, mock adapters, test harness |
| P2 | `tracer_phase2_design.md` | TracerAgent, interval rotation, FakeNode |
| P3 | `tracer_phase3_design.md` | TracerObserver, Web API, Vue SPA, Scenario View |
| P4 | `tracer_phase4_design.md` | TracerAggregator, bundle format, offline viewer |
| P5 | `tracer_phase5_design.md` | Timeline View, Canvas rendering, multi-interval reader |
| P6 | `tracer_phase6_design.md` | Causal Tree View, trace walking |
| P7 | `tracer_phase7_design.md` | Entity History View, slow/fast state |
| P8 | `tracer_phase8_design.md` | Annotations, saved views, trigger evaluation log, personas |
| P9 | `tracer_phase9_design.md` | Replication latency, gap detection, network topology |
| P10 | `tracer_phase10_design.md` | SQL Console, saved queries, bundle library |
| P11 | `tracer_phase11_design.md` | Real adapters (DDS, shared memory, sync, NAS) |

---

**End of acceptance criteria document.**
