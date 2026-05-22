# BATCH-48 Review

**Batch:** BATCH-48
**Reviewer:** Dev Lead
**Date:** 2026-05-22
**Status:** ✅ APPROVED

---

## Tasks Reviewed

| Task | Description | Verdict |
|------|-------------|---------|
| Task 1 | `.csproj` — 3 project references added | ✅ Pass |
| Task 2 | `TestCollections.cs` — 3 collection definitions | ✅ Pass |
| Task 3 | `AnnotationsRoundTripTests.cs` — 2 tests | ✅ Pass |
| Task 4 | `SavedViewsRoundTripTests.cs` — 2 tests | ✅ Pass |
| Task 5 | `TriggerEvalIntegrationTests.cs` — 3 tests | ✅ Pass |
| Task 6 | E2E Playwright stubs (3 files) | ✅ Pass |
| Task 7 | DTO verification + production validator fix | ✅ Pass |

---

## Test Verification

### New Integration Tests

```
Test Run Successful.
Total tests: 7 (all new)
     Passed: 7
  AnnotationsRoundTrip_LiveToBundleToOffline        PASS
  AnnotationsRoundTrip_BundleMode_PostReturns405    PASS
  SavedViews_LiveMode_CreateListDelete               PASS
  SavedViews_BundleMode_WriteThrows                  PASS
  TriggerEval_HTTP_ReturnsEvents                     PASS
  TriggerEval_HTTP_FilterByTriggerId                 PASS
  TriggerEval_HTTP_FilterByResult                    PASS
```

### Unit Test Regression (Targeted — affected areas)

```
Filter: Annotation | SavedView | TriggerEval
Passed: 82/82
Duration: 4s
```

Note: Full unit suite hits DT-028 (known hang). Targeted subset confirms no regressions in affected feature areas.

### Frontend Unit Tests

```
Test Files  65 passed (65)
     Tests  319 passed (319)
  Duration  34.15s
```

✅ E2E stubs excluded from Vitest (Playwright files). 0 regressions.

### Annotation Endpoints Unit Tests (direct regression for validator change)

```
Total tests: 13
     Passed: 13
```

---

## Code Quality Observations

**Strengths:**
- `WaitForBundleLoadedAsync` correctly avoids bundleId correlation (two separate ULIDs) — checks for any loaded bundle instead
- Static `_nextId` counter in `TriggerEvalIntegrationTests` ensures unique IDs per test run
- `BuildTriggerUrl` helper centralizes ISO 8601 + URI-encoded DateTimeOffset formatting
- `TriggerEvalIntegrationTests.InitializeAsync` correctly pushes `system.session_start` event before trigger events so `SessionQueryService.GetAsync` returns non-null
- `SavedViewsRoundTripTests` correctly registers `SqliteSavedViewStore` via `configureExtraServices`
- E2E stubs all use the correct `test.skip(process.env['E2E'] !== 'true', ...)` guard pattern

**Production Fix Quality:**
- `AnnotationEndpoints.ValidateCreate`: `IsNullOrWhiteSpace` → `is null` is minimal, correct, and well-reasoned (empty string is valid for time-range annotations). 13/13 unit tests still pass.

**Issues Corrected During Implementation (not in batch spec):**
- Batch instructions had wrong TriggerEval query params (`fromNs`/`toNs` nanoseconds → actual `from`/`to` DateTimeOffset)
- `CreateSavedViewDto.ViewType` is `required string` — not in batch spec
- `SavedViewRecord.OpenCount` is `required` — not in batch spec
- `BundleId` mismatch in `WaitForBundleLoadedAsync` — spec assumed single ULID but there are two

---

## Scope Check

- ✅ All 7 tasks from instructions implemented
- ✅ No scope creep (only `AnnotationEndpoints.ValidateCreate` changed in production — directly required for the round-trip test)
- ✅ Test assertions check behavior/values, not just string existence or compilation
- ✅ Design alignment: tests use the existing `ObserverFixture`/`AggregationFixture`/`OfflineViewerHostBuilder` harness pattern

---

## Decision

**APPROVED** — 7/7 new integration tests pass, 82/82 targeted unit tests pass, 319/319 frontend unit tests pass. Production validator fix is minimal and safe. TRC-P8-018 complete.

---

## Suggested Git Commit Message

```
test: add P8 integration and E2E stub tests (TRC-P8-018)

- AnnotationsRoundTripTests: live→bundle→offline round-trip (SC-13),
  bundle-mode POST rejects with 405 (SC-14)
- SavedViewsRoundTripTests: CRUD round-trip in live mode, write rejection
  in bundle mode
- TriggerEvalIntegrationTests: HTTP query, triggerId filter, result filter
- E2E Playwright stubs: annotations-flow, saved-views-flow, persona-switcher
- AnnotationEndpoints.ValidateCreate: allow SessionId="" for time-range
  annotation export use case (IsNullOrWhiteSpace → is null)
- Tracer.Tests.Integration.csproj: add Storage.Annotations, Storage.SavedViews,
  Observer project references
```
