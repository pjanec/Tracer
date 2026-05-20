# BATCH-19 Review — TRC-P4-009: Web API Bundle Mode

**Verdict: APPROVED**

## Summary
BATCH-19 implements the bundle build/download API on the Observer. All 14 new tests pass. The implementation correctly serializes concurrent builds and handles both directory and zip bundles.

## Code Quality

### IAggregationOrchestrator
- Minimal, correct interface — only the RunAsync method needed for the contract
- Non-breaking: existing callers of `AggregationOrchestrator` still use the concrete class

### BundleCatalog
- Thread-safe with `ConcurrentDictionary`
- Best-effort manifest reads in `ListAsync` — unreadable manifests are logged and skipped (correct behavior)
- `DeleteAsync` removes files best-effort, not failing if already gone
- Clear separation: catalog manages registration; actual file writes happen in BundleBuildService

### BundleBuildService
- Background task spawned without awaiting — correct for async fire-and-forget
- Uses `CancellationToken.None` for the build task (not the request CT, which would cancel on client disconnect) — correct
- `SemaphoreSlim(1,1)` enforces one-at-a-time constraint
- `Enum.TryParse` with `ignoreCase: true` for FastStateScope — safe

### BundleEndpoints
- All 6 routes present and correctly mapped
- `HandleBuildAsync` properly catches exceptions and returns `ProblemHttpResult`
- On-the-fly zip streaming via `Pipe` is correct — avoids full materialization
- `HandleStatusAsync` returns status for unknown IDs without error (returns "Unknown" state) — spec-compliant

### ObserverHostBuilder changes
- Bundle services registered in the correct order (BundleCatalog → ITelemetryStorageReader → IAggregationOrchestrator → BundleBuildService)
- Default NAS root falls back gracefully when NasMockRoot is empty

## Test Quality

### Unit Tests (BundleEndpointTests)
- 8 tests cover all 8 success conditions from the spec
- `FakeAggregationOrchestrator` immediately creates a valid bundle with a real `manifest.json`
- `TwoConcurrentBuilds_OnlyOneRunsAtATime` uses a `delayMs=300` slow orchestrator to test semaphore serialization

### Integration Tests (ObserverBundleBuildTests)
- 6 tests (4 required + 2 additional)
- Use `AggregationFixture.NasRoot` for real interval data
- `configureExtraServices` hook properly overrides/supplements services
- `PollUntilCompletedAsync` with 60s timeout is correct for CI environments

## Issues Found: None

## Approved ✓
