# Phase 11 — Handoff Notes

**Document:** Phase 11 Real Adapter Integration — External Requirements
**Version:** 1.0
**Last Updated:** 2026-05-23

---

## Overview

Phase 11 of the Tracer system integrates real production adapters:
- **DDS** data source (CycloneDDS.NET)
- **Shared Memory** IPC transport (named ring buffer)
- **Sync System** upload adapter
- **NAS** storage reader

This document specifies what Tracer requires from the **simulation team** and the **sync team** for the integration to function correctly.

---

## Requirements for the Simulation Team

### 1. Trace Context Propagation Discipline

Every DDS publish that originates or propagates a Tracer event MUST:

- Call `dds_write_ts()` on every publish (not `dds_write()`).
- Populate the `traceId`, `eventId`, and `parentEventId` fields in all event IDL types.
- Maintain the parent-child relationship: `parentEventId` of a child event equals the `eventId` of the triggering event.

**Why:** Tracer reconstructs causal trees from these fields. Missing or zero values cause trace chains to appear as isolated events.

### 2. DDS Domain ID Agreement

All simulation processes and Tracer agents must use the **same DDS domain ID** (default: 0, configurable via `appsettings.json` `dds.participant.domainId`). Mismatched domain IDs cause complete topic isolation.

### 3. IDL Type Coverage

All published IDL event types must include the following fields:
- `uint64 traceId` — globally unique trace identifier (same across the trace chain)
- `uint64 eventId` — per-event unique identifier
- `uint64 parentEventId` — zero for root events; `eventId` of the triggering event otherwise

### 4. Simulation Harness Interface

The simulation harness (`TRACER_HARNESS_PATH`) must:
- Accept `--emit-trace <traceId> <depth>` command-line arguments to emit deterministic trace chains.
- Accept `--emit-burst <count> <rate>` to emit event bursts at the specified rate.
- Exit within 5 seconds when sent SIGTERM or CTRL+C.

---

## Requirements for the Sync Team

### 1. Telemetry REST Endpoint Contract

The sync master must expose endpoints matching the contract in `docs/sync_addendum_telemetry.md §A4`:

- `POST /telemetry/submit` — submit a telemetry zip archive for upload.
- `GET /telemetry/status/{correlationId}` — poll upload status.
- Status values: `Pending`, `InProgress`, `Completed`, `Failed`.

**Contract stability:** These endpoints are called by `Tracer.Adapters.Sync.SyncSystemUploadService`. Any schema change requires coordination with the Tracer team.

### 2. `_ready` Sentinel Discipline

The sync agent MUST write the `_ready` entry as the **last entry** in each interval zip archive before declaring the interval complete. Tracer's NAS reader uses the presence of `_ready` to determine whether an interval is safe to read. Zips without `_ready` are skipped and logged as warnings.

### 3. NAS Layout

The NAS share must follow the layout expected by `NasStorageReader`:
```
{NasRoot}\telemetry\{nodeId}\{intervalTimestamp}.zip
```
Where `intervalTimestamp` follows the format `yyyyMMddTHHmmss` (UTC).

---

## Phase 11 Completion Checklist

All 10 success criteria from `tracer_phase11_design.md §1.3` must be verified:

- [ ] **Criterion 1**: DDS adapter reads and decodes samples from subscribed topics
- [ ] **Criterion 2**: SharedMemory ring buffer handles write-ahead and drop-oldest at capacity
- [ ] **Criterion 3**: Sync upload submits and polls correctly; retries on transient errors
- [ ] **Criterion 4**: NAS reader skips non-ready zips; circuit breaker trips after threshold failures
- [ ] **Criterion 5**: AdapterSelection registers correct adapters from configuration
- [ ] **Criterion 6**: `appsettings.json` defaults are coherent; `appsettings.IntegrationReal.json` overrides correctly
- [ ] **Criterion 7**: TransportMonitor logs warnings when SharedMemory drops increase; health endpoint exposes `sharedMemoryDropped` and `ingestChannelDepth`
- [ ] **Criterion 8**: Soak run shows no monotonic RSS or file-handle growth over 48 h
- [ ] **Criterion 9**: All Phase 1–10 unit and integration tests continue to pass
- [ ] **Criterion 10**: Integration-real tests compile and skip cleanly on dev machines; run and pass in the harness environment

---

## Known Limitations

| Issue | Description | Tracking |
|-------|-------------|---------|
| DuckDB process-exit crash | Native DuckDB library crashes during testhost shutdown after all tests complete. Tests pass (394/394); crash is post-run cleanup only. | DT-041 |
| `intervalsAwaitingUpload` health field | Not yet exposed in `/api/health`. Requires tracking state in `UploadIntentDispatcher`. | DT-042 |
