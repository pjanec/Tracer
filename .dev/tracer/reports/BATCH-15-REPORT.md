# BATCH-15 Report — TRC-P4-005: Aggregation Core

## Summary

All source files for the `Tracer.Aggregator` assembly have been created and the full aggregation
pipeline compiles and passes tests. A new `ITelemetryStorageReader` abstraction was added to
`Tracer.Core.Abstractions` and implemented in `Tracer.Adapters.Mock.Storage`.

**Build status**: ✅ 0 warnings, 0 errors  
**Test status**: ✅ 269/269 passing (228 unit + 41 integration; +12 new unit tests)

---

## Files Created / Modified

### New assembly — `src/Tracer.Aggregator/`

| File | Description |
|------|-------------|
| `Tracer.Aggregator.csproj` | Project file; references Core, Storage.DuckDB, Storage.DuckDB.MultiInterval, Bundle, Adapters.Mock |
| `AggregationOrchestrator.cs` | Public entry point; 9-stage pipeline: time-range resolution → discovery → extraction → consolidation → metadata → manifest → finalize |
| `Configuration/AggregationRequest.cs` | `required string OutputPath`; optional TimeRange / SessionId / NodeFilter / FastStateScope / etc. |
| `Configuration/AggregationResult.cs` | `BundleId`, `OutputPath`, `TimeRange`, `Statistics`, `Duration`, `SourceIntervalsUsed` |
| `Configuration/FastStateScope.cs` | Enum: `None`, `SelectedEntities`, `All` |
| `Progress/AggregationStage.cs` | 12-value enum for granular progress reporting |
| `Progress/IAggregationProgressReporter.cs` | Single-method `void Report(AggregationStage, string?)` |
| `Discovery/DiscoveredIntervals.cs` | Records `DiscoveredInterval` and `DiscoveredIntervals` (with `Count`, `NodeCount`) |
| `Discovery/ExtractedInterval.cs` | `record ExtractedInterval(NodeId, Descriptor, Directory)` |
| `Discovery/IntervalDiscovery.cs` | `FindOverlappingAsync` — case-insensitive node filter, half-open overlap check |
| `Discovery/SessionResolver.cs` | Scans all manifests for session markers; falls back to `UtcNow` when no end marker |
| `Staging/StagingDirectory.cs` | `IAsyncDisposable`; creates temp dir, exposes `BundleStagingPath` and `SourcesPath` |
| `Consolidation/ConsolidationStats.cs` | Stats records for events, slow state, and fast state |
| `Consolidation/EventsConsolidator.cs` | **Stub** — writes empty `.duckdb` |
| `Consolidation/SlowStateConsolidator.cs` | **Stub** — writes empty `.duckdb` |
| `Consolidation/FastStateCopier.cs` | **Stub** — no-op, returns empty stats |
| `Consolidation/ScenarioMetadataCollector.cs` | **Stub** — returns minimal scenario metadata |
| `Consolidation/TopologyExtractor.cs` | **Stub** — groups sources by nodeId |
| `Consolidation/SourceIntervalsBuilder.cs` | **Stub** — maps to entries with 0 contributed events |
| `Consolidation/ManifestBuilder.cs` | Computes SHA-256 per staged file, assembles `BundleManifest` |
| `Consolidation/BundleMetadataWriter.cs` | Writes `scenario.json`, `topology.json`, `source_intervals.json` |

### New — `src/Tracer.Core/`

| File | Description |
|------|-------------|
| `Time/TimeRange.cs` | `record TimeRange(WallclockTime StartUtc, WallclockTime EndUtc)` |
| `Domain/IntervalDescriptor.cs` | `record IntervalDescriptor(IntervalTimestamp, DateTimeOffset StartUtc, DateTimeOffset EndUtc)` |
| `Abstractions/ITelemetryStorageReader.cs` | Interface: `ListNodesAsync`, `ListIntervalsAsync`, `ReadIntervalManifestAsync`, `GetIntervalZipPath` |

### New — `src/Tracer.Adapters.Mock/`

| File | Description |
|------|-------------|
| `Storage/LocalFileSystemStorageReader.cs` | Implements `ITelemetryStorageReader`; reads NAS layout `{root}/{nodeId}/{ts}.zip`; private duplicate JSON converters for `IntervalTimestamp`, `WallclockTime`, `AgentId` |

### Modified

| File | Change |
|------|--------|
| `src/Tracer.Bundle/Tracer.Bundle.csproj` | Added `InternalsVisibleTo("Tracer.Aggregator")` for `BundleDirectoryWriter.ComputeSha256Async` |
| `Tracer.sln` | Added `Tracer.Aggregator` project entry with Debug/Release config and NestedProjects mapping |
| `tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj` | Added `Tracer.Aggregator` project reference |

### New tests — `tests/Tracer.Tests.Unit/Aggregator/`

| Test class | Tests |
|------------|-------|
| `IntervalDiscoveryTests` | 5 — no-filter, node-filter, case-insensitive, no overlap, boundary exclusion |
| `SessionResolverTests` | 4 — start+end, start-only (uses now), non-existent, multi-interval earliest/latest |
| `AggregationOrchestratorTests` | 3 — ArgumentException, InvalidOperationException, happy-path progress reporting |

---

## Design Decisions

- **Stub consolidators**: EventsConsolidator, SlowStateConsolidator, FastStateCopier write empty or no-op — real implementations deferred to BATCH-16 (TRC-P4-006).
- **ManifestBuilder enumerates only known files** (`events.duckdb`, `slow_state.duckdb`, `scenario.json`, `topology.json`, `source_intervals.json`) so `manifest.json` and `checksums.txt` are not listed in their own manifest.
- **`FinalizeAsync`**: uses `Directory.Move` with IOException fallback to cross-device copy+delete.
- **Circular dependency avoidance**: `Tracer.Adapters.Mock` cannot reference `Tracer.Agent`, so the three JSON converters are duplicated in `LocalFileSystemStorageReader` (explicit comment in code).

---

## Test Count

| Suite | Before | After |
|-------|--------|-------|
| Unit | 216 | 228 (+12) |
| Integration | 41 | 41 |
| **Total** | **257** | **269** |
