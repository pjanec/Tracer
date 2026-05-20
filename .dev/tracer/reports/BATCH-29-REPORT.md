# BATCH-29 Report — TRC-P6-001 & TRC-P6-002

## Summary

Both tasks were already substantially implemented by a prior session. The work in this batch focused on:
- Verifying the existing implementation matched the spec
- Applying the TRC-P6-001 schema extension (partial index rename with corrective adjustment)
- Updating `SchemaV1Tests.cs` to document the deviation

---

## Files Created / Modified

### TRC-P6-001 — Schema Extension

| File | Action | Notes |
|------|--------|-------|
| `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` | **Verified** (no net change) | Index was already renamed from `idx_events_parent` to `idx_events_parent_event_id` by prior batch. The `WHERE parent_event_id != 0` partial clause was attempted but reverted (see deviation below). |
| `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs` | **Pre-existing, correct** | Already had `"idx_events_parent_event_id"` in expected array. |
| `tests/Tracer.Tests.Unit/Storage/SchemaV1Tests.cs` | **Updated** | Updated assertion string and comment to document the DuckDB 1.0.2 limitation. |
| `tests/Tracer.Tests.Integration/SchemaAppliedTests.cs` | **Pre-existing, correct** | Already implemented with correct `idx_events_parent_event_id` checks (second test verifies via `duckdb_indexes().sql` rather than EXPLAIN plan). |

### TRC-P6-002 — Trace Walking Backend

All files were already created by a prior session and pass tests without modification:

| File | Status |
|------|--------|
| `src/Tracer.WebApi/Queries/TraceTree.cs` | Pre-existing, complete |
| `src/Tracer.WebApi/Queries/EventRecordMapper.cs` | Pre-existing, complete |
| `src/Tracer.WebApi/Queries/TraceWalker.cs` | Pre-existing, complete |
| `src/Tracer.WebApi/Queries/TraceQueryService.cs` | Pre-existing, complete (with session ID resolution beyond spec) |
| `tests/Tracer.Tests.Unit/WebApi/TraceWalkerTests.cs` | Pre-existing, 5 tests |
| `tests/Tracer.Tests.Unit/WebApi/TraceQueryServiceTests.cs` | Pre-existing, 4 tests |

---

## Deviations

### 1. Partial index `WHERE parent_event_id != 0` not applied (DT-NEW)

**Instruction:** Replace the `idx_events_parent_event_id` index with a partial index:
```sql
CREATE INDEX IF NOT EXISTS idx_events_parent_event_id ON events (parent_event_id) WHERE parent_event_id != 0;
```

**Actual:** Kept the regular index:
```sql
CREATE INDEX IF NOT EXISTS idx_events_parent_event_id ON events(parent_event_id);
```

**Reason:** DuckDB version 1.0.2 (the pinned version in `Directory.Packages.props`) does not support partial indexes. Attempting to apply the partial index causes:
```
DuckDB.NET.Data.DuckDBException : Not implemented Error: Creating partial indexes is not supported currently
```
This caused 10 test failures in `TraceWalkerTests` and `TraceQueryServiceTests` (all using `ObserverFixture` which calls `DuckDbStorageWriter.CreateAsync` on construction). The index was reverted to the regular form and `SchemaV1Tests.cs` was updated to reflect the actual constraint and document the DuckDB 1.0.2 limitation.

### 2. `TraceQueryService` has additional `SessionId` resolution

The pre-existing `TraceQueryService` includes session ID resolution via a `ResolveSessionId` helper and `TraceTree.SessionId` property — additions from a later design phase not in the BATCH-29 spec. These are already correct and pass tests; no changes made.

---

## Build Output (last 5 lines)

```
  Tracer.OfflineViewer -> D:\Work\Tracer\src\Tracer.OfflineViewer\bin\Release\net8.0\tracer-viewer.dll
  Tracer.Tests.Integration -> D:\Work\Tracer\tests\Tracer.Tests.Integration\bin\Release\net8.0\Tracer.Tests.Integration.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Results

### New unit test classes

| Class | Tests | Pass | Fail |
|-------|-------|------|------|
| `SchemaV1Tests` | 1 | 1 | 0 |
| `TraceWalkerTests` | 5 | 5 | 0 |
| `TraceQueryServiceTests` | 4 | 4 | 0 |
| **Subtotal** | **10** | **10** | **0** |

### New integration test class

| Class | Tests | Pass | Fail |
|-------|-------|------|------|
| `SchemaAppliedTests` | 2 | 2 | 0 |

### Total unit test count

```
Passed!  - Failed: 0, Passed: 351, Skipped: 0, Total: 351
```

Prior count was 326+. Current count: **351** (increase of 10 new unit tests + prior accumulated tests from other batches).

---

## Developer Insights

### Issues Encountered

1. **DuckDB 1.0.2 partial index limitation**: The primary blocker. The specification called for a `WHERE parent_event_id != 0` partial index, which is a standard SQL feature but not yet implemented in DuckDB 1.0.2. The index name rename (`idx_events_parent` → `idx_events_parent_event_id`) was already applied by a prior batch.

2. **Pre-existing implementation**: All TRC-P6-002 files (`TraceTree.cs`, `EventRecordMapper.cs`, `TraceWalker.cs`, `TraceQueryService.cs`, test files) were already created in a prior session. The implementation was found to be correct and fully passing.

### Weak Points Spotted

- **SchemaV1 partial index**: When upgrading DuckDB beyond 1.0.2 (v0.9+ supports partial indexes), the `WHERE parent_event_id != 0` clause should be applied to `SchemaV1.cs` and `SchemaV1Tests.cs` to match the original design intent. This should be tracked as tech debt.
- **`SchemaAppliedTests.DescendantQuery_ExplainPlanReferencesParentEventIdIndex`**: The test was pragmatically adapted (querying `duckdb_indexes().sql` instead of EXPLAIN plan) because DuckDB EXPLAIN does not include index names for empty tables. This is correct behavior for the current DuckDB version.

### Design Decisions Beyond Spec

- None — all implementation decisions were made by the prior session.
