# Sync System Addendum — Telemetry Category
## Addendum to Sync Architecture v2

*Companion to `sync_system_architecture_v2.md`*
*To be applied in the development cycle following the current Revision 2 implementation*
*C# / .NET 8 · ASP.NET Core · SignalR · May 2026*

*This addendum describes additions to the sync system to support the Tracer platform's data upload requirements. It does not modify the existing Recordings flow; it adds a parallel category structurally similar but with per-interval (rather than per-session) semantics.*

---

## A1. Purpose and Background

The Tracer platform (a companion diagnostic/analysis platform — see `tracer_architecture_v1.md`) captures events and state samples from the distributed simulation, persists them locally on each node in fixed-duration capture intervals, and requires reliable transport of those intervals from each node to the NAS for post-scenario aggregation.

Tracer is fully decoupled from the sync system during its own development (it uses mock file-transport adapters). For production deployment, Tracer needs the sync system to handle exactly the kind of work it already does well: chunked uploads with resume, retry, offline-then-reconnect, completion tracking, bandwidth-aware scheduling.

The sync system's existing **Recordings** category (§16 of the main architecture) provides a structurally close fit. This addendum adds a parallel category, **Telemetry**, with two structural differences from Recordings:

1. **Upload trigger is per-interval, not per-session.** Tracer rotates storage at fixed wall-clock-aligned intervals (default 1 hour). Each completed interval is an upload unit.
2. **NAS layout is per-node, per-interval** (no per-session grouping on NAS). Sessions in Tracer are conceptual time ranges discoverable from event content, not structural units.

All other aspects — chunked upload mechanism, intent system, offline-reconnect handling, agent lifecycle, GC — reuse the existing sync infrastructure unchanged.

### A1.1 Scope of This Addendum

- New data category: `Telemetry`
- New NAS directory layout for Telemetry
- New REST API endpoints mirroring the Recordings API
- New data plane HTTP path for chunk uploads
- New SignalR command action for Telemetry uploads
- Additions to site config: category defaults and retention
- Per-interval intent semantics
- GC behavior for Telemetry

### A1.2 Not in Scope

- Changes to the Recordings flow (unchanged)
- Changes to downward distribution categories (unchanged)
- Changes to the session-finalize semantics for Recordings (unchanged — Recordings still uses `_session.json`)
- Changes to fleet topology, identity model, or membership (unchanged)
- Changes to publish gate, bundle distribution, or activation (unchanged)

---

## A2. New Data Category: Telemetry

Add a new row to §5 of the main architecture:

| Category | Direction | Size profile | Container on NAS | Transfer engine | Activation | Scope |
|---|---|---|---|---|---|---|
| **Telemetry** | **Nodes → NAS** | **Few-to-many medium files per node per interval (DuckDB + Parquet, partly pre-compressed)** | **Per-node-per-interval zip (compressed at the node before upload)** | **Chunked HTTP upload** | **In-place on NAS** | **Per-interval, per-node** |

Notes:
- DuckDB native format has some internal compression but zip's deflate adds modest additional gains on structural overhead and JSON manifests.
- Parquet inside the zip is already compressed; use `CompressionLevel.NoCompression` (store mode) for `.parquet` entries; `CompressionLevel.Optimal` for `.duckdb` and `.json` entries.
- Telemetry is an upload/collection flow, architecturally distinct from downward distribution — same architectural class as Recordings.
- A single upload contains a complete capture interval's data for one node: events DuckDB, slow-state DuckDB, fast-state Parquet files, manifest, and a `_ready` sentinel.

### A2.1 Distinction from Recordings

Both Telemetry and Recordings are nodes → NAS upload flows, but they differ:

| Aspect | Recordings | Telemetry |
|---|---|---|
| **Triggered by** | Per-session app finalize call | Per-interval rotation in TracerAgent |
| **Upload unit** | One zip per session per node | One zip per interval per node |
| **NAS grouping** | By sessionId folder | By nodeId folder |
| **Finalize marker** | `_session.json` written by master | Self-contained `manifest.json` and `_ready` inside each interval zip |
| **Frequency** | Once at session end | Every interval (default 1h) throughout session and beyond |
| **Cardinality** | One per node per session | Many per node per session |
| **Audience** | Replay, post-session review | Analysis, diagnosis, scenario review |

The sync system treats both as opaque file transport; the differences are in trigger and layout, not in the underlying upload mechanism.

---

## A3. NAS Layout

Extend §9.1 of the main architecture with a new top-level directory:

```
/NAS/SyncRoot/
  bundles/                                        unchanged
  recordings/                                     unchanged
  telemetry/                                      NEW
    {nodeId}/
      {intervalTimestamp}.zip
      ...
  _draft/                                         unchanged
  _publishing/                                    unchanged
  policies/                                       unchanged
```

### A3.1 Telemetry Directory Structure

```
/NAS/SyncRoot/telemetry/
  blue-cmd-01/
    20260519T140000Z.zip
    20260519T150000Z.zip
    20260519T160000Z.zip
    ...
  blue-veh-01/
    20260519T140000Z.zip
    20260519T150000Z.zip
    ...
  red-cmd-01/
    ...
```

The directory is keyed by `agentId` (Tracer's `nodeId` corresponds to the sync system's `agentId`). Each file is one interval's complete data for one node.

### A3.2 Interval Filename Format

`{intervalTimestamp}.zip` uses ISO 8601 basic format in UTC: `YYYYMMDDTHHMMSSZ`.

The timestamp denotes the **start** of the interval (interval start times are wall-clock-aligned in Tracer's design). Example: an interval covering 14:00:00Z to 15:00:00Z UTC on 19 May 2026 produces filename `20260519T140000Z.zip`.

The basic format (no separators) is used because Windows file systems are case-insensitive but the SignalR / HTTP routes are case-sensitive; the format is unambiguous and sortable lexically.

### A3.3 Zip Contents

Each interval zip contains:

```
events.duckdb
slow_state.duckdb
fast_state/
  topic_transforms.parquet
  topic_velocities.parquet
  ...
manifest.json
_ready
```

`manifest.json` is the self-describing interval metadata (schema defined by Tracer; see `tracer_architecture_v1.md` §8.1). The sync system treats this file as opaque payload.

`_ready` is a zero-byte sentinel indicating the interval was completed cleanly (not interrupted by a crash mid-write). Tracer writes `_ready` last during interval rotation. Its presence in the uploaded zip is a correctness signal.

### A3.4 No `_session.json` Equivalent

Unlike Recordings, Telemetry has no NAS-side session marker. Sessions in Tracer are conceptual ranges discoverable from event content; each interval zip is self-contained and self-marking via its `manifest.json`.

The sync master does not write any per-session metadata for Telemetry. The session-finalize flow for Recordings is unchanged and unaffected.

---

## A4. REST API Additions

Extend §8.1 of the main architecture with new endpoints mirroring the Recordings API:

```
REST API — Integration                           ADDITIONS
  POST /api/telemetry                            tracer-agent: declare per-node interval ready for upload

REST API — Operator: Status                      ADDITIONS
  GET  /api/telemetry                            list telemetry uploads (filterable by nodeId, time range)
  GET  /api/telemetry/{nodeId}/{intervalTimestamp}    detail of a specific interval upload

REST API — Operator: Control                     ADDITIONS
  DELETE /api/telemetry/{nodeId}/{intervalTimestamp}  delete a telemetry interval from NAS

Data Plane HTTP                                  ADDITIONS
  PUT  /content/telemetry/{nodeId}/{intervalTimestamp}/chunks/{n}    interval chunk upload
```

### A4.1 `POST /api/telemetry`

Called by the TracerAgent (or any client that wraps it) when an interval has completed and is ready for upload.

**Request body:**

```json
{
  "nodeId": "blue-cmd-01",
  "intervalTimestamp": "20260519T140000Z",
  "intervalStartUtc": "2026-05-19T14:00:00Z",
  "intervalEndUtc":   "2026-05-19T15:00:00Z",
  "files": [
    { "path": "C:/ProgramData/Tracer/agent/intervals/20260519T140000Z/events.duckdb", "size": 41943040 },
    { "path": "C:/ProgramData/Tracer/agent/intervals/20260519T140000Z/slow_state.duckdb", "size": 524288 },
    { "path": "C:/ProgramData/Tracer/agent/intervals/20260519T140000Z/fast_state/topic_transforms.parquet", "size": 16777216 },
    { "path": "C:/ProgramData/Tracer/agent/intervals/20260519T140000Z/manifest.json", "size": 1842 },
    { "path": "C:/ProgramData/Tracer/agent/intervals/20260519T140000Z/_ready", "size": 0 }
  ]
}
```

**Response:** `201 Created` with `intentId`.

```json
{ "intentId": "8a3f2c40-..." }
```

The master creates a Pending upload intent for this interval. The sync agent processes the intent: zips the files, chunk-uploads to master, master writes to NAS. Same mechanism as Recordings upload.

**Idempotency:** if an interval is declared twice (e.g., agent restart, re-declare on reconnect), the master returns the existing intent's `intentId` rather than creating a duplicate. The interval is identified by `(nodeId, intervalTimestamp)`.

**Behavior when offline:** if the agent calls this when offline, normal intent handling applies — the intent is created locally on the agent side (queued for delivery to master on reconnect) or, when called via local sync agent that is itself offline-from-master, the local sync agent persists the request for later delivery. Standard sync system behavior.

### A4.2 `GET /api/telemetry`

Returns a list of telemetry uploads, filterable.

**Query parameters:**

- `nodeId={agentId}` — filter to one node
- `from={isoDate}` — only intervals whose start ≥ this time
- `to={isoDate}` — only intervals whose start < this time
- `status={Pending|InProgress|Complete|Failed}` — filter by upload state
- `limit={n}`, `offset={n}` — pagination

**Response:** list of intervals with status:

```json
{
  "intervals": [
    {
      "nodeId": "blue-cmd-01",
      "intervalTimestamp": "20260519T140000Z",
      "intervalStartUtc": "2026-05-19T14:00:00Z",
      "intervalEndUtc":   "2026-05-19T15:00:00Z",
      "uploadStatus": "Complete",
      "uploadCompletedUtc": "2026-05-19T15:02:14Z",
      "intentId": "8a3f2c40-...",
      "sizeBytes": 67108864
    },
    ...
  ],
  "totalCount": 247
}
```

This endpoint is what the TracerAggregator uses to discover what intervals are available for a chosen time range.

### A4.3 `GET /api/telemetry/{nodeId}/{intervalTimestamp}`

Returns detail of a specific interval, including the path on NAS where its zip can be read:

```json
{
  "nodeId": "blue-cmd-01",
  "intervalTimestamp": "20260519T140000Z",
  "intervalStartUtc": "2026-05-19T14:00:00Z",
  "intervalEndUtc":   "2026-05-19T15:00:00Z",
  "uploadStatus": "Complete",
  "uploadCompletedUtc": "2026-05-19T15:02:14Z",
  "intentId": "8a3f2c40-...",
  "sizeBytes": 67108864,
  "nasPath": "/NAS/SyncRoot/telemetry/blue-cmd-01/20260519T140000Z.zip",
  "downloadUrl": "/content/telemetry/blue-cmd-01/20260519T140000Z.zip"
}
```

The TracerAggregator uses either `nasPath` (for direct SMB access) or `downloadUrl` (for HTTP access via master).

### A4.4 `DELETE /api/telemetry/{nodeId}/{intervalTimestamp}`

Deletes an interval from NAS. Operator action. Telemetry GC §A7 may also issue these.

### A4.5 `GET /content/telemetry/{nodeId}/{intervalTimestamp}.zip`

Read endpoint for downloading a telemetry interval zip. Byte-range capable for resumable downloads by the TracerAggregator. Same pattern as `/content/bundles/...` for downward distribution.

### A4.6 `PUT /content/telemetry/{nodeId}/{intervalTimestamp}/chunks/{n}`

Chunk upload endpoint, parallel to the existing `PUT /content/recordings/{sessionId}/{logicalNodeId}/chunks/{n}`. The sync agent uploads the zipped interval in 64 MB chunks; the master streams chunks to NAS staging; on the final chunk, the master moves staging → final NAS location at `/NAS/SyncRoot/telemetry/{nodeId}/{intervalTimestamp}.zip`.

The chunk upload protocol is identical to Recordings — chunk size, hash sidecars, resume semantics. The only difference is the destination path.

---

## A5. SignalR Hub Additions

Extend §8.2 of the main architecture.

### A5.1 Master → Agent: New ReceiveCommand Action

Add a new value to the `action` parameter of `ReceiveCommand`:

| action | Payload | Purpose |
|---|---|---|
| `UploadTelemetry` | `intentId, nodeId, intervalTimestamp, files[]` | Tell the agent to zip the listed files and chunk-upload to the master |

The agent's behavior on receiving `UploadTelemetry`:

1. Reads the files from the agent's `intervals/{intervalTimestamp}/` directory
2. Creates a temporary local zip with the listed contents
3. Chunk-uploads the zip to the master via `PUT /content/telemetry/...`
4. Reports `AckCommand(intentId, Complete)` when done
5. Deletes the temporary zip
6. Local interval files remain on disk until retention policy evicts them (see §A8)

This parallels exactly the existing `UploadRecording` action.

### A5.2 EvictSession Generalization

The existing `ReceiveCommand(action=EvictSession, sessionId)` evicts local extracted recording copies on agents. Telemetry does not use the session concept for storage, so this command does not apply to Telemetry intervals.

However, for symmetry, add a new command:

| action | Payload | Purpose |
|---|---|---|
| `EvictTelemetryInterval` | `nodeId, intervalTimestamp` | Tell the agent to remove the named interval from local storage if present |

This is issued by the master when an interval is GC'd from NAS (so agents stop wasting disk on intervals that no longer exist centrally) or when an operator explicitly deletes an interval via `DELETE /api/telemetry/...`.

Most agents will receive `EvictTelemetryInterval` only for their own intervals (the agent that originally produced them). The aggregator running on a separate machine, if it has extracted a copy for analysis, may also be notified (out of scope for this addendum; the aggregator's local cache management is Tracer's concern).

---

## A6. Site Configuration Additions

Extend §17.1 of the main architecture.

### A6.1 Category Defaults

Add `Telemetry` to `categoryDefaults`:

```json
{
  "categoryDefaults": {
    "RuntimeAsset":    { "chunkSize": "64MB", "verifyMode": "ChunkHash" },
    "ChunkedHugeFile": { "chunkSize": "64MB", "verifyMode": "ChunkHash" },
    "Config":          { "verifyMode": "FullHash", "staleAfter": "1h" },
    "Dataset":         { "verifyMode": "FullHash" },
    "Recording":       { "chunkSize": "64MB", "compressionLevel": "Optimal" },
    "Telemetry":       { "chunkSize": "64MB", "compressionLevel": "Mixed" }
  }
}
```

`"compressionLevel": "Mixed"` is a new value indicating per-entry compression policy:
- `.parquet` files inside the zip: `NoCompression` (already compressed)
- `.duckdb` files: `Optimal`
- `.json` files and `_ready` sentinel: `Optimal`

This is a small extension to the sync system's compression logic — the existing zip-creation code per category gains a "mixed" branch for telemetry.

### A6.2 Retention

Add `telemetry` retention to `operational.agentRetention`:

```json
{
  "operational": {
    "fleetSyncWindow":      "01:00-05:00",
    "diskWatermarkPercent": 10,
    "sessionQueueDepth":    5,
    "agentRetention": {
      "bundles":    { "keepActiveAndPrevious": true, "keepLastN": 2 },
      "recordings": { "keepLastNSessions": 5 },
      "telemetry":  { "keepLastNIntervals": 24 }
    }
  }
}
```

`keepLastNIntervals: 24` with 1-hour intervals retains 1 day of local data. Operators tune based on disk budget and likelihood of needing very recent intervals locally (e.g., for fast re-aggregation without NAS round-trip).

### A6.3 NAS Retention

Telemetry intervals on NAS are kept indefinitely by default, same policy as Recordings. Operator-triggered deletion only, via `DELETE /api/telemetry/...` or the GC tool (§A7).

---

## A7. Garbage Collection

Extend §18 of the main architecture.

### A7.1 Agent-Local Telemetry GC

For each node, the TracerAgent keeps:
- The currently-writing interval
- The most recent `keepLastNIntervals` completed intervals
- Any interval whose upload intent is still Pending or InProgress

When free disk drops below `diskWatermarkPercent`, the agent evicts the oldest completed-and-uploaded intervals LRU until above the watermark.

This GC is implemented in the **TracerAgent**, not the sync agent. The sync agent does not own local Telemetry storage — it only handles uploads. Local interval storage is Tracer's responsibility, managed via the standard sync system disk watermark and retention configuration plumbed through.

### A7.2 Master Pull Cache

Telemetry uploads flow agent → master → NAS. The master may briefly cache uploaded chunks during the staging phase but does not maintain a long-term pull cache of Telemetry files (unlike bundles, which are downloaded by relays and nodes).

Telemetry on the master is transient — staged on upload, moved to NAS final location, removed from master local disk. No GC needed beyond this.

### A7.3 NAS Telemetry GC

By default Telemetry is retained indefinitely. The same dry-run / run pattern as bundles applies for operator-triggered GC:

```
POST /api/gc/preview                    extended to include telemetry
POST /api/gc/run                        extended to include telemetry
```

GC policy options for Telemetry (operator-configurable):

- **Time-based**: delete intervals older than N days
- **Node-based**: delete intervals from decommissioned nodes
- **Size-based**: when NAS free space drops below threshold, evict oldest intervals LRU
- **Manual**: no automatic GC; operator deletes explicitly

Default is **manual**: no auto-deletion. NAS storage is cheaper than the cost of accidentally deleting data someone might want to analyze.

When GC removes an interval from NAS, the master issues `EvictTelemetryInterval` to any agents that might hold local copies. (Typically only the producing agent, but the broadcast cost is negligible.)

---

## A8. Intent Lifecycle for Telemetry

Telemetry uploads use the existing intent system unchanged. Concretely:

- **Pending**: master has created the intent but the agent hasn't yet started
- **Executing**: the agent is reading files, zipping, and uploading chunks
- **Complete**: all chunks uploaded, NAS write finalized, agent acked
- **Failed**: upload failed (network, disk, hash mismatch); operator can retry via `POST /api/intents/{intentId}/retry`
- **Stale**: intent age exceeded `staleAfter` threshold — warning written to operator message queue; intent still executes when agent reconnects
- **Cancelled**: operator-cancelled via `DELETE /api/intents/{intentId}`

`staleAfter` for Telemetry: defaults to `48h`. Telemetry uploads should normally complete within minutes; a 48-hour staleness indicates a significant problem worth surfacing.

### A8.1 Telemetry-Specific Considerations

- **Per-interval intents accumulate**: a busy node produces 24 intents per day (one per hourly interval). The intent store should handle this volume — sync architecture v2's JSON snapshot approach is fine at this scale (~5000 intents per fleet per day, comfortably within snapshot size).
- **Stale telemetry is more tolerable than stale recordings**: a session recording is unique and irrecoverable if not uploaded; a single missed telemetry interval is one hour of diagnostic data that other intervals likely overlap (next interval also captures session boundaries from manifest markers). Operator severity should reflect this.
- **Bulk re-upload after extended outage**: if a node was offline for a day and rejoins, it has 24 intervals to upload. The intent system handles this naturally — the sync agent processes them in order, respecting any FleetSyncMode gates.

### A8.2 FleetSyncMode Interaction

Telemetry uploads do **not** participate in the FleetSyncMode gate (§3.2 of the main architecture). The gate is for downward distribution operations that compete for inter-segment bandwidth during fleet sync windows.

Telemetry is upward and small-per-interval (typically tens of MB compressed). Blocking it during sessions would create exactly the recovery-after-extended-outage problem and lose the operational value of fresh data on NAS for live analysis.

Telemetry uploads run whenever the agent has data and the master is reachable. Bandwidth contention with other operations is left to the OS / network stack to mediate.

---

## A9. Orchestrator State Additions

Extend §19 of the main architecture.

Add to the master's in-memory model:

```
Master in-memory model:
  ...                              (unchanged)
  TelemetryIntervals:              Dictionary<(agentId, intervalTimestamp), TelemetryIntervalRecord>
```

`TelemetryIntervalRecord` shape:

```csharp
public record TelemetryIntervalRecord
{
    public required string NodeId { get; init; }
    public required string IntervalTimestamp { get; init; }
    public required DateTimeOffset IntervalStartUtc { get; init; }
    public required DateTimeOffset IntervalEndUtc { get; init; }
    public required string IntentId { get; init; }
    public required UploadStatus Status { get; init; }
    public DateTimeOffset? UploadCompletedUtc { get; init; }
    public long? SizeBytes { get; init; }
    public string? ErrorDetail { get; init; }
}
```

This collection is updated on:
- `POST /api/telemetry` — new interval declared, record created with `Pending` status
- Intent state changes — status updated
- `DELETE /api/telemetry/...` — record removed
- NAS GC — record removed

Included in the master's JSON snapshot (§19 of the main architecture).

---

## A10. Implementation Sharing with Recordings

The Telemetry category is structurally close enough to Recordings that the implementation should share underlying mechanisms with a category discriminator, not duplicate the code.

### A10.1 Recommended Code Organization

Refactor the existing Recordings upload code into a shared `UploadCategory` mechanism:

```csharp
public enum UploadCategory { Recording, Telemetry }

public interface IUploadOrchestrator
{
    Task<string> CreateUploadIntentAsync(
        UploadCategory category,
        string uploadKey,        // sessionId+logicalNodeId for Recording; nodeId+intervalTimestamp for Telemetry
        IReadOnlyList<FileToUpload> files,
        CancellationToken ct);
    
    Task<UploadStatus> GetStatusAsync(string intentId, CancellationToken ct);
    Task<IReadOnlyList<UploadRecord>> ListAsync(UploadCategory category, UploadFilter filter, CancellationToken ct);
    Task DeleteAsync(UploadCategory category, string uploadKey, CancellationToken ct);
}
```

The category-specific differences:
- NAS path construction (`/recordings/{sessionId}/{logicalNodeId}.zip` vs `/telemetry/{nodeId}/{intervalTimestamp}.zip`)
- HTTP chunk path construction (`/content/recordings/...` vs `/content/telemetry/...`)
- Compression policy per entry (uniform for Recordings, Mixed for Telemetry)
- Session-finalize participation (Recordings yes, Telemetry no)

are handled by a small category descriptor object passed to the shared upload pipeline. The chunked-upload mechanism, intent state machine, retry, resume, completion tracking, SignalR command dispatch — all shared.

### A10.2 Implementation Effort Estimate

If the Recordings upload code is well-factored after Revision 2 implementation, adding Telemetry should be a small focused effort — order of weeks, not months. Specifically:

- Add `UploadCategory` enum and refactor existing Recordings code to use it: ~1 week
- Add new REST endpoints under `/api/telemetry`: ~3 days
- Add new data plane HTTP path and chunk handler: ~2 days
- Add `UploadTelemetry` and `EvictTelemetryInterval` SignalR commands: ~2 days
- Add Telemetry to site config schema and validation: ~1 day
- Add Telemetry to GC dry-run / run: ~2 days
- Add Telemetry intervals to orchestrator state and JSON snapshot: ~1 day
- Testing (unit, integration with Tracer mock client): ~1 week

Roughly 3-4 weeks of focused work assuming clean Recordings code to extend.

---

## A11. Backward Compatibility

- Sessions and Recordings created before this change are unaffected. Their `_session.json` files do not reference Telemetry.
- Existing Recordings API endpoints, payloads, and behaviors are unchanged.
- Existing SignalR commands and their payloads are unchanged.
- The new `UploadCategory` parameter has a default of `Recording` for any code paths that internally need to call shared upload primitives without explicit category — making the refactor safe for callers that aren't aware of Telemetry.
- Existing site config files without `categoryDefaults.Telemetry` or `agentRetention.telemetry` are accepted; the sync master applies internal defaults (Mixed compression, 24 intervals) when these are missing.

---

## A12. Summary of Changes to Main Architecture

For traceability, here is the full list of changes the main `sync_system_architecture_v2.md` needs when this addendum is applied:

| Section | Change |
|---|---|
| §4 Terminology | Add `Telemetry` term, `intervalTimestamp` term, `TracerAgent` term as a consumer of the sync system |
| §5 Data Categories | Add `Telemetry` row to the categories table; add note about Mixed compression policy |
| §8.1 Master Endpoints | Add `/api/telemetry` endpoints (one Integration, two Status, one Control); add `/content/telemetry/{...}` data plane paths |
| §8.2 SignalR Hub Methods | Add `UploadTelemetry` and `EvictTelemetryInterval` actions to `ReceiveCommand` |
| §9.1 Directory Structure | Add `telemetry/` top-level directory to NAS layout |
| §17.1 Canonical Schema | Add `Telemetry` to `categoryDefaults`; add `telemetry` to `agentRetention` |
| §18 Garbage Collection | Add §18.4 Telemetry GC subsection covering local agent and NAS |
| §19 Orchestrator State | Add `TelemetryIntervals` to the master in-memory model and JSON snapshot |
| §23 Resolved Design Decisions | Add a Telemetry-related note |

No existing rows or values change. All additions are additive and backward-compatible per §A11.

---

## A13. Open Questions for Implementation

Items to resolve during the implementation cycle, not blocking the design.

- **Exact `Mixed` compression policy specification**: should it be a fixed mapping (extension → compression level) or a configurable per-category mapping? Fixed is simpler; configurable is more flexible. Recommend fixed initially.
- **Upload prioritization**: when a node has many telemetry intervals queued (e.g., after extended outage), is FIFO sufficient or should there be a "freshest-first" option? FIFO is simpler and preserves intent ordering. Recommend FIFO.
- **Concurrent uploads per agent**: should the agent upload one Telemetry interval at a time or several in parallel? Sequential is safer and aligns with existing Recordings behavior. Recommend sequential, revisit if upload throughput becomes a bottleneck.
- **Telemetry on relay agents**: relays themselves produce telemetry just like any other agent. No special-casing needed — they upload their own intervals like everyone else.
- **Per-interval intent staleness severity**: a 48-hour stale telemetry intent surfaces a warning, same as Recordings. May want to differentiate (telemetry less critical than recordings) — defer until operational experience suggests it.
