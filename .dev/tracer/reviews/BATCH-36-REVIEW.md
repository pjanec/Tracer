# BATCH-36 Review

**Batch:** BATCH-36 — Phase 7 Entity History Service Layer and API Wiring  
**Tasks:** TRC-P7-003, TRC-P7-004, TRC-P7-005, TRC-P7-008, TRC-P7-009  
**Review Status:** ✅ APPROVED WITH CORRECTIONS  
**Dev Lead Correction Applied:** IAsyncLifetime pattern fix (see §5)

---

## 1. Test Results

| Suite | Result |
|-------|--------|
| Entity unit tests (39) — filter `FullyQualifiedName~Entity` | ✅ 39/39 pass, 6 seconds |
| SSE + Health + EventEndpoint (20 pre-existing) | ✅ 20/20 pass, ~63 seconds |
| Integration (`EntityHistoryRoundTripTests`) | Not run (requires FakeNode infra; reviewed by code inspection) |
| Full `Tracer.Tests.Unit` suite | ⚠️ Pre-existing hang — see DT-028 |

Build: **0 errors, 0 warnings** (Release, `TreatWarningsAsErrors=true`)

---

## 2. Scope Check — All Tasks Implemented

| Task | Service / Artifact | Status |
|------|--------------------|--------|
| TRC-P7-003 | `EntityDiscoveryService` — GROUP BY entity_id, parameterized, ordered by event_count DESC | ✅ |
| TRC-P7-004 | `EntityEventsService` — paged with truncation detection, uses `EventRecordMapper.FromReader` | ✅ |
| TRC-P7-005 | `EntitySlowStateService` — slow_state grouped by topic, SortedDictionary output | ✅ |
| TRC-P7-008 | `EntityFastStateService` — Parquet reader + `FastStateFileLocator`, infrastructure columns filtered | ✅ |
| TRC-P7-009 | `EntityEndpoints` (7 routes) + `EntityDtos.cs` (9 DTOs) + wiring in Observer/OfflineViewer/TestHarness | ✅ |

---

## 3. Design Alignment

### ✅ Correct

- **SQL parameterization** throughout — no string-interpolated user input. DuckDB `DuckDBParameter` used in all queries.
- **`instance_key` for slow_state** — correctly queries `WHERE instance_key = $entityId` (not entity_id).
- **Truncation** — `EntityEventsService` fetches `limit+1`, removes the last if truncated, returns `Truncated=true`.
- **OfflineViewer wiring** — `FastStateFileLocator` registered with `() => BundleOpenManager.Current?.WorkingDirectory` factory.
- **DTO style** — `required ... { get; init; }` for non-nullable properties, `{ get; init; }` for optional. `SlowStateSampleDto.TraceId` correctly nullable with `[JsonIgnore(WhenWritingNull)]`.
- **Endpoints** — 7 routes, all `.WithOpenApi()`, typed `Results<T1, T2>` returns, 400 validation in fast-state endpoints.
- **WallclockTime conversion** — `(dt.Ticks - DateTime.UnixEpoch.Ticks) * 100L` pattern used correctly.
- **`SortedDictionary<string, List<SlowStateSample>>`** — alphabetical key order guaranteed in JSON output.

### ⚠️ Deviation: `GetAvailableTopics` returns safe-encoded names (DT-026, DT-027)

`EntityFastStateService.GetAvailableTopics` delegates directly to `FastStateFileLocator.GetAvailableTopicsForEntity`, which returns `BundleNaming.SafeFileName`-encoded directory names (e.g. `"game.tick_ab12"` instead of `"game.tick"`).

The BATCH-36 instructions pre-approved this as "acceptable for now" (§ DT-026 note). However, the reasoning in the instructions is flawed: when the frontend passes these encoded names back to `GET /fast-state/{topic}`, `LocateFiles(topic, entityId)` will call `SafeFileName` again on an already-encoded name, producing a doubly-encoded path that does not exist on disk (see DT-027).

**Impact:** No runtime impact today (frontend not yet implemented). Must be resolved before TRC-P7-014.

---

## 4. Test Quality Review

### EntityDiscoveryServiceTests (8 tests)
- `ThreeEntities_OrderedByEventCount` — verifies descending rank with concrete values ✅
- `TopicFilter` — verifies filtered entity not present in results ✅
- `PlayerFilter` — verifies player-filtered entity not present ✅
- `FirstAndLastSeen` — verifies exact DateTimeOffset values ✅
- `TopicsArray_DeduplicatedAndSorted` — verifies array contents and order ✅
- `EmptySession` — verifies empty list returned ✅
- `LimitRespected` — inserts 10, requests 3, asserts Count==3 ✅
- `SqlInjection` — inserts `"ent' OR '1'='1"` as entity ID, asserts no results for injected string ✅

**Quality:** All tests check concrete values/behavior. No "it compiled" tests.

### EntityEventsServiceTests (7 tests)
- Covers: entity filtering, time window filtering, pagination + truncation, multi-event ordering, missing entity empty result ✅

### EntitySlowStateServiceTests (6 tests)
- Covers: grouping by topic, payload roundtrip, topic filter, TraceId zero → null, time window ✅

### EntityFastStateServiceTests (7 tests)
- Uses `StubTracker` + real Parquet files via in-memory DuckDB `COPY TO`
- Infrastructure columns (`publish_wallclock`, `instance_key`) excluded from schema ✅
- Downsampling flag propagated ✅
- Multiple-file total sample count ✅

### EntityEndpointsTests (5 tests)
- `GetFastState_MissingColumnParam_Returns400WithTitle` — checks `ProblemDetails.Title` ✅
- `GetFastState_MaxSamplesBelowMinimum_Returns400` ✅
- `GetFastState_MaxSamplesAboveMaximum_Returns400` ✅
- `GetEntitySummary_Routes_ToEntityEndpoint` — routing smoke test ✅
- Note: "missing session → 404" tests correctly moved to integration test scope; documented in report ✅

### EntityHistoryRoundTripTests (1 test)
- Full push → query round-trip: session_start + 20 entity events + 5 slow-state rows
- Verifies: entity in list, event count, slow-state grouping, truncation at limit=5 ✅

---

## 5. Debt Tracker Updates

| ID | Action |
|----|--------|
| DT-026 | Remains **Open** — topics endpoint still returns safe-encoded names; target moved to BATCH-37+ |
| DT-027 | **NEW P2** — Double-encoding bug: `GetAvailableTopics` safe names + `LocateFiles` → not found. Must fix before TRC-P7-014 |
| DT-028 | **NEW P2** — Pre-existing: full `Tracer.Tests.Unit` suite hangs; individual subsets pass |
| DT-029 | **NEW P3** — `WebApiFixture` missing reader init causes session queries to return 500 not 404 |

---

## 6. TASK-TRACKER.md Updates

Mark the following as complete in `docs/TASK-TRACKER.md`:
- [x] **TRC-P7-003** EntityDiscoveryService
- [x] **TRC-P7-004** EntityEventsService
- [x] **TRC-P7-005** EntitySlowStateService
- [x] **TRC-P7-008** EntityFastStateService
- [x] **TRC-P7-009** Entity Web API Endpoints, DTOs, and Wiring

---

## 7. Suggested Git Commit Message

```
feat(phase7): entity history service layer and REST API wiring (TRC-P7-003..009)

Add EntityDiscoveryService, EntityEventsService, EntitySlowStateService, and
EntityFastStateService backed by DuckDB multi-interval queries and Parquet reads.
Wire 7 new endpoints at /api/entities/... in Observer, OfflineViewer, and test
harness. Add 34 new tests across 6 test files (39 unit + 1 round-trip integration).

Closes TRC-P7-003, TRC-P7-004, TRC-P7-005, TRC-P7-008, TRC-P7-009
```
