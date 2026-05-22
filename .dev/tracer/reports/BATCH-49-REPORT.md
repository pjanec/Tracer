# BATCH-49 Report — Phase 9 Backend: Latency Analysis, Gap Detection, Network Topology

**Batch:** BATCH-49  
**Tasks:** TRC-P9-001 through TRC-P9-010  
**Status:** COMPLETE  
**Build:** ✅ 0 errors, 0 warnings  
**Unit Tests:** ✅ 220 passed (19 new Phase 9 tests)

---

## Files Created

### Core Domain
- `src/Tracer.Core/Domain/LatencyBudget.cs` — `LatencyBudget` record (`Topic`, `P99BudgetMs?`, `AbsoluteMaxMs?`)

### Mock Adapter
- `src/Tracer.Adapters.Mock/FakeNetworkModel.cs` — Simulates per-subscriber delivery with bad links, jitter, spikes, drops

### WebApi Utilities
- `src/Tracer.WebApi/Util/BundleModeGate.cs` — `IBundleModeMarker` marker interface + `BundleModeGate.CheckBundleOrLive(sp)` guard (returns HTTP 409 if not bundle mode)
- `src/Tracer.WebApi/Util/QuantileSink.cs` — Reservoir sampling quantile estimator
- `src/Tracer.WebApi/Util/HistogramSink.cs` — Log2-based histogram (`HistogramBucket` record)

### Query Services
- `src/Tracer.WebApi/Queries/LatencyDistributionService.cs` — `GetAsync` (aggregate stats) + `ListByPairAsync` (per-pair summary); uses `APPROX_QUANTILE` via DuckDB
- `src/Tracer.WebApi/Queries/LatencyTimeSeriesService.cs` — Bucketed time series of P50/P99/sample count
- `src/Tracer.WebApi/Queries/LatencyOutlierService.cs` — Returns top-N high-latency events above a threshold
- `src/Tracer.WebApi/Queries/GapDetectionService.cs` — LAG-based sequence gap detection; `GapDetectionResult` with `TotalGaps`
- `src/Tracer.WebApi/Queries/NetworkTopologyService.cs` — Publisher/subscriber edge map from events table
- `src/Tracer.WebApi/Queries/BudgetService.cs` — Reads `latencyBudgets` from `metadata.json` in bundle mode; falls back to `InMemoryBudgetRegistry`
- `src/Tracer.WebApi/Queries/InMemoryBudgetRegistry.cs` — Thread-safe in-memory budget store for live mode

### DTOs
- `src/Tracer.WebApi/Contracts/Dto/LatencyDtos.cs` — `LatencyDistributionDto`, `LatencyBucketDto`, `LatencyPairSummaryDto`, `LatencyTimeSeriesPointDto`, `LatencyOutlierDto`
- `src/Tracer.WebApi/Contracts/Dto/LatencyDtoMappers.cs` — `LatencyDtoMapper.Map(...)` for all latency types
- `src/Tracer.WebApi/Contracts/Dto/GapDtos.cs` — `GapDto`, `GapDetectionResultDto`
- `src/Tracer.WebApi/Contracts/Dto/NetworkTopologyDtos.cs` — `NetworkTopologyDto`, `TopologyEdgeDto`, `NetworkTopologyDtoMapper`
- `src/Tracer.WebApi/Contracts/Dto/BudgetDtos.cs` — `LatencyBudgetDto`, `LatencyBudgetListDto`

### Endpoints
- `src/Tracer.WebApi/Endpoints/LatencyEndpoints.cs` — `GET /api/latency/distribution`, `/pairs`, `/timeseries`, `/outliers` — all guarded by `BundleModeGate` (409 in live mode)
- `src/Tracer.WebApi/Endpoints/GapEndpoints.cs` — `GET /api/gaps` — guarded by `BundleModeGate`
- `src/Tracer.WebApi/Endpoints/BudgetEndpoints.cs` — `GET /api/budgets/{sessionId}`, `PUT /api/budgets/{sessionId}/{topic}`, `DELETE /api/budgets/{sessionId}/{topic}` — **NOT** guarded (returns empty list in live mode)
- `src/Tracer.WebApi/Endpoints/TopologyEndpoints.cs` (modified) — Added `GET /api/topology/network` using `NetworkTopologyService`, guarded by `BundleModeGate`

## Files Modified

- `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` — Registered `IBundleModeMarker` (sentinel), all Phase 9 services, mapped `LatencyEndpoints`, `GapEndpoints`, `BudgetEndpoints`; `BudgetService` receives `() => BundleOpenManager.Current?.WorkingDirectory`
- `src/Tracer.Observer/ObserverHostBuilder.cs` — Registered all Phase 9 services (without `IBundleModeMarker`); `BudgetService` receives `getBundleWorkingDirectory: null`
- `src/Tracer.TestHarness/Observer/ObserverFixture.cs` — Added `configureExtraServices` parameter for DI overrides in tests
- `src/Tracer.TestHarness/Observer/WebApiFixture.cs` — Added `configureExtraServices` parameter for DI overrides in tests
- `src/Tracer.Aggregator/Consolidation/EventsConsolidator.cs` — Added `CREATE INDEX IF NOT EXISTS idx_events_topic_pub_sub ON events(topic, publisher_node, subscriber_node)` after consolidation

## Unit Tests Created (19 new, 220 total)

- `tests/Tracer.Tests.Unit/Core/LatencyBudgetTests.cs` — 5 tests (record construction, equality, null budgets)
- `tests/Tracer.Tests.Unit/Mock/FakeNetworkModelTests.cs` — 5 tests (determinism, self-subscribe, drops, bad links, spikes)
- `tests/Tracer.Tests.Unit/WebApi/QuantileSinkTests.cs` — 4 tests (empty NaN, p50/p99 accuracy, reservoir sampling)
- `tests/Tracer.Tests.Unit/WebApi/HistogramSinkTests.cs` — 5 tests (empty, single value, logarithmic bounds, negative clamp, count)
- `tests/Tracer.Tests.Unit/WebApi/LatencyDistributionServiceTests.cs` — empty bundle, single sample, exclude self, topic filter (DuckDB-backed)
- `tests/Tracer.Tests.Unit/WebApi/LatencyTimeSeriesServiceTests.cs` — basic time series bucketing
- `tests/Tracer.Tests.Unit/WebApi/LatencyOutlierServiceTests.cs` — threshold filtering, limit
- `tests/Tracer.Tests.Unit/WebApi/GapDetectionServiceTests.cs` — no gaps, single gap, multiple gaps, topic filter
- `tests/Tracer.Tests.Unit/WebApi/NetworkTopologyServiceTests.cs` — empty, edge building, self-subscribe excluded
- `tests/Tracer.Tests.Unit/WebApi/BudgetServiceTests.cs` — live mode empty, registry fallback, bundle metadata.json read
- `tests/Tracer.Tests.Unit/WebApi/LatencyEndpointsTests.cs` — 409 in live mode, 200 in bundle mode (for each endpoint)
- `tests/Tracer.Tests.Unit/WebApi/GapEndpointsTests.cs` — 409 in live mode, 200 in bundle mode
- `tests/Tracer.Tests.Unit/WebApi/NetworkTopologyEndpointsTests.cs` — 409 in live mode, 200 in bundle mode
- `tests/Tracer.Tests.Unit/WebApi/BudgetEndpointsTests.cs` — 200 in live mode (no 409), CRUD operations

---

## Architecture Compliance

| Constraint | Status |
|---|---|
| `IBundleModeMarker` registered only in `OfflineViewerHostBuilder` | ✅ |
| `BundleModeGate` in `Tracer.WebApi.Util` (no circular reference) | ✅ |
| `NetworkTopologyService` (not `TopologyService` — name taken) | ✅ |
| `NetworkTopologyDto` (not `TopologyDto` — name taken by Phase 3) | ✅ |
| `BudgetService` uses `Func<string?>` for working directory | ✅ |
| `BudgetEndpoints` does NOT return 409 in live mode | ✅ |
| All other Phase 9 endpoints return 409 in live mode | ✅ |
| `idx_events_topic_pub_sub` index added in `EventsConsolidator` | ✅ |
| All services use `LiveMultiIntervalReader.AcquireAsync` pattern | ✅ |

---

## Test Results

```
Build succeeded. 0 Warning(s), 0 Error(s)
Passed! Failed: 0, Passed: 220, Skipped: 0, Total: 220
```
(testhost crash after 220 tests — known DT-028 issue, non-fatal)
