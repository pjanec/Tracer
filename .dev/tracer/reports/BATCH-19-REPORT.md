# BATCH-19 Report — TRC-P4-009: Web API Bundle Mode

## Tasks Completed
- **TRC-P4-009** Web API Bundle Mode

## Files Created

| File | Description |
|------|-------------|
| `src/Tracer.Aggregator/IAggregationOrchestrator.cs` | New interface for testability |
| `src/Tracer.WebApi/Bundles/BundleCatalog.cs` | In-memory catalog of built bundles; registers, lists, gets manifest, deletes |
| `src/Tracer.WebApi/Bundles/BundleBuildService.cs` | Queues bundle builds; uses SemaphoreSlim(1) to serialize; runs orchestrator in background task |
| `src/Tracer.WebApi/Endpoints/BundleEndpoints.cs` | 6 routes: POST /build, GET /list, GET /{id}, GET /{id}/status, GET /{id}/download, DELETE /{id} |
| `tests/Tracer.Tests.Unit/WebApi/BundleEndpointTests.cs` | 8 unit tests with in-process TestServer and FakeAggregationOrchestrator |
| `tests/Tracer.Tests.Integration/ObserverBundleBuildTests.cs` | 6 integration tests against real aggregation pipeline |

## Files Modified

| File | Change |
|------|--------|
| `src/Tracer.Aggregator/AggregationOrchestrator.cs` | Implements `IAggregationOrchestrator` |
| `src/Tracer.WebApi/Tracer.WebApi.csproj` | Added refs: Tracer.Aggregator, Tracer.Bundle, Tracer.Adapters.Mock |
| `src/Tracer.Observer/Tracer.Observer.csproj` | Added refs: Tracer.Aggregator, Tracer.Bundle |
| `src/Tracer.Observer/Configuration/ObserverConfig.cs` | Added `BundlesRoot`, `NasMockRoot` |
| `src/Tracer.WebApi/Contracts/Dto/Dtos.cs` | Added bundle DTOs: TimeRangeDto, BundleBuildRequestDto, BundleBuildAcceptedDto, BundleBuildStatusDto, BundleListDto, BundleListEntryDto, BundleManifestDto, etc. |
| `src/Tracer.Observer/ObserverHostBuilder.cs` | Registered BundleCatalog, ITelemetryStorageReader, IAggregationOrchestrator, BundleBuildService; mapped BundleEndpoints |
| `src/Tracer.TestHarness/Observer/ObserverFixture.cs` | Added NasMockRoot/BundlesRoot options; added configureExtraServices/configureExtraApp hooks |

## Test Results
- **Before batch:** 294 (246 unit + 48 integration)
- **After batch:** 308 (254 unit + 54 integration)
- **New tests:** 14 (8 unit BundleEndpointTests + 6 integration ObserverBundleBuildTests)
- **All tests pass:** ✓

## Key Notes
- `IAggregationOrchestrator` makes the orchestrator injectable/mockable for unit tests
- `BundleCatalog` is created with an explicit `bundlesRoot` string (not via ObserverConfig) to avoid circular dependency between Tracer.WebApi and Tracer.Observer
- `BundleBuildService` serializes all builds with a `SemaphoreSlim(1,1)` — one build at a time
- On-the-fly zip streaming uses `System.IO.Pipelines.Pipe` to avoid materializing the full zip in memory
- `ObserverFixture` now accepts `configureExtraServices` and `configureExtraApp` hooks for test-specific DI overrides
