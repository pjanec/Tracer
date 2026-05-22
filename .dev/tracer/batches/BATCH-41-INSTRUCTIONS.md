# BATCH-41 Instructions

**Batch:** BATCH-41  
**Task:** TRC-P7-020 — Phase 7 Remaining Tests (FastState Parquet Round-Trip + E2E)  
**Assigned to:** Developer  
**Reference skill:** `d:\WORK\Tracer\.github\skills\developer\SKILL.md`

---

## Overview

TRC-P7-020 requires two test files that have not yet been written:

1. `tests/Tracer.Tests.Integration/FastStateParquetRoundTripTests.cs` — Integration tests that write a real Parquet file on disk, read it back via `ParquetReader`, and assert exact equality; also tests multi-interval merge.
2. `tracer-viewer/tests/e2e/entity-history-view.spec.ts` — Playwright E2E smoke tests for the EntityHistoryView.

All prior TRC-P7-020 tests already exist from earlier batches (unit tests in `ParquetReaderTests.cs`, entity service tests, and 244 frontend Vitest unit tests).

---

## Task 1 — `FastStateParquetRoundTripTests.cs`

### Location
`d:\WORK\Tracer\tests\Tracer.Tests.Integration\FastStateParquetRoundTripTests.cs`

### Background

- `ParquetReader` (in `Tracer.Storage.Parquet`) reads Parquet files using DuckDB's `read_parquet()` function.
- To write Parquet in tests, open a DuckDB in-memory connection and execute:
  ```sql
  COPY (SELECT ...) TO 'path/to/file.parquet' (FORMAT PARQUET);
  ```
- The `publish_wallclock` column must be a DuckDB TIMESTAMP that, when read back, can be parsed as nanosecond-epoch. In practice:
  - Store rows using `TIMESTAMP '1970-01-01 00:00:XX'` where XX is the second offset.
  - `WallclockTime.Zero + TimeSpan.FromSeconds(N)` is the expected time.
- `ParquetReader.ReadTimeSeriesAsync` requires `publish_wallclock TIMESTAMP`, `instance_key VARCHAR`, and the numeric column(s).

### Reference files to read first

- `src/Tracer.Storage.Parquet/ParquetReader.cs` — understand `ReadTimeSeriesAsync` signature and return type
- `src/Tracer.Storage.Parquet/ParquetTimeSeriesResult.cs` (or similar) — understand `ParquetSample`, `ParquetTimeSeriesResult`
- `tests/Tracer.Tests.Unit/Parquet/ParquetReaderTests.cs` — copy the `CreateParquetAsync` helper pattern
- `tests/Tracer.Tests.Integration/EntityHistoryRoundTripTests.cs` — IAsyncLifetime pattern

### Tests to implement

```csharp
[Fact]
public async Task ReadTimeSeriesAsync_ExactSampleEquality()
```
- Write a Parquet file with 5 known samples at t=1s,2s,3s,4s,5s; `x` values = 10.0, 20.0, 30.0, 40.0, 50.0
- Read back with `from = Zero`, `to = Zero + 10s`, `maxSamples = 5000`
- Assert: `result.Samples.Count == 5`; `result.Downsampled == false`
- Assert: each sample's `x` value matches exactly (within 0.001 tolerance if needed)
- Assert: samples are ordered ascending by `PublishWallclock`

```csharp
[Fact]
public async Task ReadTimeSeriesAsync_MultiInterval_MergesBothFiles()
```
- Write two Parquet files: fileA with 3 samples at t=1s,2s,3s; fileB with 3 samples at t=4s,5s,6s
- Call `ReadTimeSeriesAsync([fileA, fileB], ...)` with `from = Zero`, `to = Zero + 7s`, `maxSamples = 5000`
- Assert: `result.TotalSamples == 6`; samples are ordered ascending

```csharp
[Fact]
public async Task ReadTimeSeriesAsync_TimeRangeFilter_ExcludesOutOfRange()
```
- Write a Parquet file with 10 samples at t=1s..10s
- Call with `from = Zero + 3s`, `to = Zero + 7s`
- Assert: returned samples have `PublishWallclock >= from && PublishWallclock <= to`
- Assert: count is 5 (samples at t=3,4,5,6,7)

### Implementation notes

- Use `IDisposable` (not `IAsyncLifetime`) — temp dir cleanup is synchronous.
- Create temp dir in constructor: `_tempDir = Path.Combine(Path.GetTempPath(), $"fast-state-rt-{Guid.NewGuid():N}")`.
- Delete in `Dispose()`: `Directory.Delete(_tempDir, recursive: true)`.
- Write Parquet using a fresh in-memory DuckDB connection:
  ```csharp
  private async Task<string> WriteParquetAsync(string name, string insertSql)
  {
      var path = Path.Combine(_tempDir, $"{name}.parquet");
      await using var conn = new DuckDBConnection("Data Source=:memory:");
      await conn.OpenAsync();
      await using var cmd = conn.CreateCommand();
      cmd.CommandText = $"""
          CREATE TABLE t (publish_wallclock TIMESTAMP, instance_key VARCHAR, x DOUBLE);
          {insertSql}
          COPY t TO '{path.Replace("\\", "/")}' (FORMAT PARQUET);
          """;
      await cmd.ExecuteNonQueryAsync();
      return path;
  }
  ```
- The entity filter in `ReadTimeSeriesAsync` uses `instance_key` column matching; set `instance_key = 'ent-A'` in all rows and pass `"ent-A"` as the `entityId` argument.
- `WallclockTime.Zero` is the nanosecond epoch (1970-01-01 00:00:00 UTC).

### IMPORTANT: No TestHarness needed

This is a pure Parquet read/write test — no DI, no WebApi, no `ObserverFixture`. Just `ParquetReader` + a DuckDB in-memory connection to write the file.

---

## Task 2 — `entity-history-view.spec.ts`

### Location
`d:\WORK\Tracer\tracer-viewer\tests\e2e\entity-history-view.spec.ts`

### Pattern

Follow the same pattern as `tracer-viewer/tests/e2e/causal-tree-view.spec.ts` and `tracer-viewer/tests/e2e/timeline-view.spec.ts`:
- `import { test, expect } from '@playwright/test'`
- `const BASE_URL = 'http://localhost:5300'`
- Use `const TEST_ENTITY_ID = 'test-entity-001'` and `const TEST_SESSION_ID = 'test-session-001'`
- Routes:
  - EntityPickerView: `/v/entities/${TEST_SESSION_ID}`
  - EntityHistoryView: `/v/entity/${TEST_ENTITY_ID}?session=${TEST_SESSION_ID}`

### Tests to implement

```typescript
test.describe('EntityPickerView E2E', () => {
  test('entityPickerView_renders_searchAndList', ...)
  // goto /v/entities/test-session-001
  // Expect heading text to contain 'test-session-001'
  // OR expect locator('.entity-picker') OR locator('h2') to be visible
  // No crash, page doesn't redirect to error
  // Expected element: data-testid or class '.entity-picker__filter' or 'input[placeholder*="Filter"]'
});

test.describe('EntityHistoryView E2E', () => {
  test('entityHistoryView_renders_loadingOrSummary', ...)
  // goto /v/entity/test-entity-001?session=test-session-001
  // Expect: .entity-history-view or a heading with the entityId OR at minimum no JS crash
  // Accept that data may not load (404 from API is ok, error state renders)

  test('entityHistoryView_directUrl_showsEntityId', ...)
  // goto URL with session+from+to params
  // Check URL contains 'test-entity-001'
  // Check page doesn't 404 or crash entirely

  test('entityHistoryView_entityPickerLink_navigatesToPicker', ...)
  // May need to look for a link in the session browser or just go directly
  // goto /v/entities/test-session-001
  // verify page loads without JS crash
});
```

### IMPORTANT — E2E test design philosophy

These are **smoke tests** — they don't require real data. The key assertions are:
- The page renders (no JS crash, no blank white screen)
- Key UI elements are visible (headings, filter inputs, loading/error states)
- Navigation between views works (URL changes)
- `toBeVisible({ timeout: 5000 })` is typical
- Accept that API calls may return 404/empty since there's no seeded test data for entity endpoints

DO NOT write assertions that assume specific entity data to be present. The test entity/session IDs are stubs.

---

## Build Commands

### Backend build + tests
```powershell
dotnet build d:\Work\Tracer\Tracer.sln -c Release --no-incremental
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Integration -c Release --no-build --filter "FastStateParquetRoundTrip"
```

### Frontend unit tests (no change expected)
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm test:unit --run
```

### TypeScript check
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm tsc --noEmit
```

**Note:** E2E Playwright tests (`entity-history-view.spec.ts`) are NOT run as part of this batch verification — they require a running server with seeded data. Just verify the file compiles correctly via TypeScript.

---

## Success Criteria

- `FastStateParquetRoundTripTests.cs` created with 3 tests, all passing
- `entity-history-view.spec.ts` created with 4+ E2E smoke tests (TypeScript valid)
- `dotnet build` succeeds with 0 warnings
- Backend integration tests pass: all 3 new tests green
- Frontend: 244/244 Vitest unit tests still pass, 0 TS errors

---

## Report

Write report to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-41-REPORT.md`

Do NOT commit.
