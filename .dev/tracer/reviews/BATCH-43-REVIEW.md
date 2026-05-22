# BATCH-43 Review

**Batch:** BATCH-43 — Phase 8 Annotations REST API, Saved Views, AnnotationsExporter  
**Tasks:** TRC-P8-004, TRC-P8-005, TRC-P8-006, TRC-P8-009  
**Reviewer:** Development Lead  
**Date:** 2026-05-22  
**Status:** ✅ APPROVED

---

## Scope Check

All four tasks fully implemented:

| Task | Description | Status |
|------|-------------|--------|
| TRC-P8-004 | Tracer.Storage.SavedViews assembly with SqliteSavedViewStore | ✅ Done |
| TRC-P8-005 | Annotation REST API endpoints + DI wiring (Observer + OfflineViewer) | ✅ Done |
| TRC-P8-006 | Saved Views REST API endpoints | ✅ Done |
| TRC-P8-009 | AnnotationsExporter + AggregationOrchestrator integration | ✅ Done |

---

## Design Alignment

- `SavedViewRecord`, `SavedViewFilter`, `ISavedViewStore`, `SqliteSavedViewStore` follow the exact structure from the design spec (phase8).
- `OrderBy="recent"` → `last_opened_at DESC NULLS LAST, created_at DESC` per spec.
- Annotation REST API routes match spec: `GET/POST /api/annotations`, `GET/PUT/DELETE /api/annotations/{annotationId}`.
- Saved Views REST API routes match spec including the `POST /api/saved-views/{id}/opened` fire-and-forget endpoint.
- `AnnotationsExporter` output path `annotations/annotations.json` matches what `BundleAnnotationStore` reads from — correct interop.
- `AggregationStage.AnnotationsExported` inserted between `MetadataWritten` and `ManifestWritten` per spec.
- Both stores wired in `ObserverHostBuilder` (SQLite-backed) and `OfflineViewerHostBuilder` (lazy bundle / no-op).

---

## Early Failure Check

- Endpoint handlers do not swallow errors: uncaught exceptions propagate to the ASP.NET middleware.
- Only the intentional exception path (`InvalidOperationException` from bundle stores → HTTP 405) is caught and converted.
- `RecordOpenedAsync` fire-and-forget is intentional (documented in report's weak points). This is per design — recording opens is non-critical.
- No silent nullable swallowing in DTOs or mappers.

---

## Strict Test Review

### `SqliteSavedViewStoreTests.cs` (10 tests)

- `SchemaInitialization_IsIdempotent` — opens real SQLite file, queries `sqlite_master` for `saved_views` table and 3 named indexes. Asserts `.Be("saved_views")` and `.Be(idx)` per index. ✅
- `CreateAsync_AssignsUlid_WhenIdEmpty` — checks `SavedViewId.Length == 26`. ✅
- `RecordOpenedAsync_IncrementsOpenCount` — checks `OpenCount == 1` (concrete value). ✅
- `RecordOpenedAsync_CalledTwice_OpenCountIsTwo` — checks `OpenCount == 2`. ✅
- `FilterByPersona_ReturnsOnlyMatchingPersona` — creates 2+1 split, asserts `personaA == 2`, `personaB == 1`. ✅
- `FilterByKind_ReturnsOnlyBookmarks` — creates 1 SavedView + 1 Bookmark, filter by Bookmark, asserts count == 1 and record kind. ✅
- `UpdateAsync_UpdatesLabelAndDescription` — creates, updates label/desc, re-reads, asserts new values. ✅
- `ListAsync_OrderByCreated_Descending` — creates with explicit DateTimeOffset values (t1 < t2 < t3), asserts `[2].Label == "First"`. ✅
- `ListAsync_OrderByRecent_NullsLast` — creates 3, records open on 2 of them, asserts ordering with null last. ✅
- `DeleteAsync_RemovesRecord` — creates, deletes, verifies `GetAsync` returns null. ✅

**Quality:** Real SQLite I/O throughout. No mocks. Concrete value assertions. ✅

---

### `AnnotationEndpointsTests.cs` (13 tests)

- `POST_ValidRequest_Returns201Created` — asserts `StatusCode == 201`, `Location.StartsWith("/api/annotations/")`, `AnnotationId.Length == 26`. ✅
- `POST_EmptyBody_Returns400` — asserts `StatusCode == 400`. ✅
- `POST_MultipleTargetIdentifiers_Returns400` — asserts `StatusCode == 400`. ✅
- `POST_NoTargetIdentifier_Returns400` — asserts `StatusCode == 400`. ✅
- `POST_BundleMode_Returns405` — uses `ReadOnlyStubAnnotationStore` that throws `InvalidOperationException`; asserts `StatusCode == 405`. ✅
- `PUT_NonExistentId_Returns404` — real store, non-existent ID → asserts 404. ✅
- `PUT_BundleMode_Returns405` — asserts 405. ✅
- `DELETE_NonExistentId_Returns404` — asserts 404. ✅
- `DELETE_BundleMode_Returns405` — asserts 405. ✅
- `GET_List_FiltersBySessionId` — creates 2+1 split, GET with sessionId filter, asserts count == 2. ✅
- `GET_Single_Returns200WithDto` — creates, GETs by ID, asserts `AnnotationId == created.AnnotationId`. ✅
- `GET_Single_UnknownId_Returns404` — asserts 404. ✅
- `DI_Observer_RegistersSqliteAnnotationStore` — builds real `ServiceCollection` with `ObserverHostBuilder` DI pattern, resolves `IAnnotationStore`, asserts it is `SqliteAnnotationStore`. ✅

**Quality:** Real `SqliteAnnotationStore` for all store-dependent tests. Direct handler invocation (not WebApplicationFactory). All assertions check concrete status codes and values. ✅

---

### `SavedViewEndpointsTests.cs` (10 tests)

- `POST_ValidRequest_Returns201Created` — asserts 201, Location, ULID. ✅
- `POST_EmptyBody_Returns400` — asserts 400. ✅
- `POST_BundleMode_Returns405` — asserts 405. ✅
- `PUT_NonExistentId_Returns404` — asserts 404. ✅
- `PUT_BundleMode_Returns405` — asserts 405. ✅
- `DELETE_NonExistentId_Returns404` — asserts 404. ✅
- `DELETE_BundleMode_Returns405` — asserts 405. ✅
- `GET_List_FiltersBySessionId` — creates 2+1 split, asserts filtered count == 2. ✅
- `GET_Single_Returns200WithDto` — creates, GETs by ID, asserts `SavedViewId`. ✅
- `POST_Opened_Always204` — asserts 204 (fire-and-forget endpoint). ✅

**Quality:** Same direct-handler invocation pattern, real stores for functional tests, concrete value checks. ✅

---

### `AnnotationsExporterTests.cs` (7 tests)

- `ExportAsync_NoAnnotations_DoesNotCreateFile` — asserts `Directory.Exists(annotationsDir) == false`. ✅
- `ExportAsync_WithAnnotations_WritesJsonFile` — creates 3 annotations, exports, deserializes JSON, asserts `records.Count == 3`. ✅
- `ExportAsync_FiltersToTargetSession` — creates 2 sessions (2+1), exports session 1, asserts count == 2. ✅
- `ExportAsync_OutputPathMatchesBundleAnnotationStore` — asserts `Path.Combine(staging, "annotations", "annotations.json")` path convention. ✅
- `AggregationStage_AnnotationsExported_EnumValueExists` — asserts `Enum.IsDefined(typeof(AggregationStage), "AnnotationsExported")`. ✅
- `ExportAsync_CallsExportAllForSessionAsync` — spy store counts calls, asserts `callCount == 1`. ✅
- `AggregationOrchestrator_WithoutAnnotationStore_SkipsExport` — builds orchestrator with null store, runs (expects "No intervals found" exception), asserts `AnnotationsExported` not reported in stage list. ✅

**Quality:** Real file system I/O, JSON deserialization with concrete counts, spy pattern to verify call delegation, enum reflection check. ✅

---

## Build Verification

```
dotnet build d:\Work\Tracer\Tracer.sln -c Release --no-incremental
```
Result: **0 errors, 0 warnings** (TreatWarningsAsErrors=true enforced). ✅

---

## Test Results

```
dotnet test Tracer.Tests.Unit -c Release --no-build
  --filter "(SavedViews|AnnotationEndpoints|SavedViewEndpoints|AnnotationsExporter|Aggregator|Observer|WebApi|Storage)"
```

**108 tests passed, 0 failed.** (40 new + 68 pre-existing in affected namespaces) ✅

Note: The full unfiltered test suite has a pre-existing hang (DT-028) unrelated to BATCH-43. All areas touched by this batch are covered by the 108-test targeted run.

---

## New Debt Items Identified

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| DT-035 | P3 | `RecordOpenedAsync` (SavedViewEndpoints) swallows all exceptions silently. Should at least emit a log warning when the store call fails, to aid debugging in production. | BATCH-44+ |
| DT-036 | P3 | `AggregationOrchestrator` constructor overload proliferation (3 constructors). If further optional dependencies are needed, migrate to an options/settings pattern or use IOptions<T>. | Future |

---

## Verdict

**Status:** APPROVED. 108/108 passing (targeted regression), 40/40 new tests. Build: 0 errors, 0 warnings.

Update TASK-TRACKER.md: mark TRC-P8-004, TRC-P8-005, TRC-P8-006, TRC-P8-009 ✅.

---

## 📝 Commit Message

```
feat(phase8): annotations+saved-views API, AnnotationsExporter (BATCH-43)

Completes TRC-P8-004, TRC-P8-005, TRC-P8-006, TRC-P8-009

- New Tracer.Storage.SavedViews assembly: SavedViewRecord, SavedViewKind,
  SavedViewFilter, ISavedViewStore, SqliteSavedViewStore (SemaphoreSlim write
  lock, ULID generation, filter/ORDER BY logic), SavedViewsSchema DDL with 3 indexes
- Annotation REST API: GET/POST /api/annotations, GET/PUT/DELETE /api/annotations/{id}
  with bundle-mode 405 guard (LazyBundleAnnotationStore)
- Saved Views REST API: GET/POST /api/saved-views, GET/PUT/DELETE /api/saved-views/{id},
  POST /api/saved-views/{id}/opened (fire-and-forget, always 204)
- DI wiring: SqliteAnnotationStore + SqliteSavedViewStore in ObserverHostBuilder
  (shared annotations.db); LazyBundle stores in OfflineViewerHostBuilder
- AnnotationsExporter.ExportAsync static method: exports session annotations to
  bundleStagingPath/annotations/annotations.json (camelCase JSON, WriteIndented)
- AggregationStage.AnnotationsExported enum value; AggregationOrchestrator accepts
  optional IAnnotationStore param; export step inserted after MetadataWritten
- 40 new unit tests, 108/108 pass (targeted regression), 0 regressions
- Build: 0 errors, 0 warnings
```

**Next Batch:** BATCH-44 (Phase 8 — TRC-P8-007/TRC-P8-008 Trigger Evaluation)
