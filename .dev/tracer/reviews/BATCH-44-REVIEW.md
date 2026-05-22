# BATCH-44 Review

**Batch:** BATCH-44  
**Reviewer:** Dev Lead  
**Date:** 2026-05-22  
**Status:** ✅ APPROVED  

---

## Tasks Reviewed

| Task | Description | Verdict |
|------|-------------|---------|
| TRC-P8-007 | TriggerEvalService | ✅ Pass |
| TRC-P8-008 | TriggerEvalEndpoints | ✅ Pass |
| TRC-P8-010 | Lifecycle Topic Configuration | ✅ Pass |

---

## Build Verification

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

✅ Clean build with `TreatWarningsAsErrors=true`.

---

## Test Verification

```
Passed!  - Failed: 0, Passed: 92, Skipped: 0, Total: 92, Duration: 5 s
```

| Category | Passed | Failed |
|----------|--------|--------|
| TriggerEvalServiceTests | 9 | 0 |
| TriggerEvalEndpointsTests | 7 | 0 |
| LifecycleTopicClassifierTests | 10 | 0 |
| BATCH-43 regression (Annotation + SavedView) | 66 | 0 |
| **Total** | **92** | **0** |

✅ All 26 new tests pass. No regressions.

---

## Code Quality Observations

**Strengths:**
- Correct DuckDB query pattern: `pooled.WithEventsCte(innerSql)` used (not `BuildEventsUnionSql`)
- Graceful malformed-payload handling in `ParseEvaluation`: degraded result with original JSON as `Inputs` field — no exceptions thrown to callers
- `TriggerEvaluationDto.NextEventId` correctly serializes as `null` (not `"0000000000000000"`)
- Regex-vs-suffix semantics correctly implemented: when `Regex.Spawn` is set, the spawn suffix list is bypassed entirely (SC-6 verified)
- `using EventId = Tracer.Core.Identity.EventId` alias cleanly resolves the ambiguity with `Microsoft.Extensions.Logging.EventId`
- `ConfigEndpoints.HandleAsync` injects `LifecycleClassificationConfig` directly — works identically in both Observer and OfflineViewer

**Design decisions accepted:**
- `from`/`to` defaults use `session.StartUtc` / `session.EndUtc ?? DateTimeOffset.UtcNow` rather than `DateTimeOffset.MinValue` — more precise given that `session` is always non-null at this point (404 returned earlier)

---

## Debt Items

No new debt items. `ObserverFixture` not registering all Phase 8 services is a pre-existing gap (not introduced in this batch).

---

## Decision

**APPROVED** — 26/26 new tests, 92/92 regression. Build clean. All three tasks implemented correctly.

Update TASK-TRACKER.md: mark TRC-P8-007, TRC-P8-008, TRC-P8-010 ✅.

---

## 📝 Commit Message

```
feat(phase8): TriggerEvalService, TriggerEvalEndpoints, LifecycleTopicClassifier (BATCH-44)

Completes TRC-P8-007, TRC-P8-008, TRC-P8-010

- TriggerEvalService: DuckDB query for scenario.trigger_evaluated events with
  trigger ID + result + time-range filters; graceful malformed-payload handling
- GET /api/scenario/triggers: 404 on unknown session, limit clamped to [1,5000],
  NextEventId as 16-char hex or null
- LifecycleClassificationConfig + ILifecycleTopicClassifier interface +
  ConfigurableLifecycleTopicClassifier with regex-takes-precedence semantics
- GET /api/config/lifecycle-classification endpoint in both Observer and OfflineViewer
- LifecycleClassification property added to ObserverConfig and OfflineViewerConfig
- All services wired in both ObserverHostBuilder and OfflineViewerHostBuilder
- 26 new unit tests, 92/92 pass (targeted regression), 0 regressions
- Build: 0 errors, 0 warnings
```
