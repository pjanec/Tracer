# BATCH-42 Review

**Batch:** BATCH-42 — Phase 8 Annotations Storage Assembly  
**Tasks:** TRC-P8-001, TRC-P8-002, TRC-P8-003  
**Reviewer:** Development Lead  
**Date:** 2026-05-22  
**Status:** ✅ APPROVED

---

## Issues Found

No issues found.

---

## Test Quality Assessment

**`SqliteAnnotationStoreTests.cs` (12 tests + 2 schema tests)**
- `InitializeAsync_CreatesSchemaAndIndexes` — opens real SQLite file, checks `sqlite_master` for `annotations` table and all 5 named indexes ✅
- `CreateAsync_GeneratesUlid_WhenIdEmpty` — verifies 26-char non-empty ID ✅
- `CreateAsync_SetsCreatedAtUtc_WhenDefault` — verifies timestamp within 5s window ✅
- `UpdateAsync_SetsModifiedAtUtc` — creates then updates; asserts `ModifiedAtUtc >= CreatedAtUtc` ✅
- `ListAsync_OrdersByCreatedAtDesc` — uses explicit `DateTimeOffset` values (not wall-clock), asserts exact order by verifying `t3 > t2 > t1` ✅
- `Tags_RoundTrip` — creates with `["alpha","beta"]`, `GetAsync`, asserts exact list ✅
- `ListAsync_FilterBySessionId` — 2+1 split, asserts session-A count == 2 with `OnlyContain` ✅
- SQL injection test covers parameterization ✅

**`BundleAnnotationStoreTests.cs` (10 tests)**
- Read-only throws verified for all 3 write methods ✅
- Cache test: populates, overwrites file, re-queries, asserts first-call data returned ✅
- `ListAsync_FilterByKind` — 2 Event + 1 Trace, filter by Event, assert count == 2 ✅
- `ExportAllForSessionAsync` — 2+1 split, filters by session ✅

**Quality:** Tests exercise real SQLite I/O (not mocked) for SqliteAnnotationStore. All assertions verify concrete values/behavior.

---

## Verdict

**Status:** APPROVED. 26/26 passing. Build: 0 errors, 0 warnings.

Update TASK-TRACKER.md: mark TRC-P8-001, TRC-P8-002, TRC-P8-003 ✅.

---

## 📝 Commit Message

```
feat(phase8): Tracer.Storage.Annotations assembly (BATCH-42)

Completes TRC-P8-001, TRC-P8-002, TRC-P8-003

- New assembly Tracer.Storage.Annotations with AnnotationRecord, AnnotationKind,
  IAnnotationStore, AnnotationFilter, AnnotationsSchema
- SqliteAnnotationStore: full CRUD, SemaphoreSlim write lock, ULID generation,
  parameterized SQL throughout (no injection), BuildSelectSql for filter queries
- BundleAnnotationStore: read-only, file-backed with lazy cache, in-memory filtering
- Add Microsoft.Data.Sqlite v8.0.0 to Directory.Packages.props
- Add project to Tracer.sln; add reference in Tracer.Tests.Unit.csproj
- 26 unit tests: 12 SqliteAnnotationStore + 2 AnnotationsSchema + 10 BundleAnnotationStore
  all passing; full suite 169/169; build 0 errors 0 warnings
```

**Next Batch:** BATCH-43 (Phase 8 — Saved Views + API wiring)
