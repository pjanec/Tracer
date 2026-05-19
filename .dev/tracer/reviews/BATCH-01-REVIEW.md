# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-05-20  
**Status:** ⚠️ NEEDS FIXES

---

## Summary

All 32 tests pass and the build is clean. Core implementation is solid — domain types, abstractions, DuckDB writer/reader, and query builder are correctly implemented. However, three test quality issues require fixing before BATCH-02 proceeds.

---

## Issues Found

### Issue 1: `AppendBatch_MixedRecords_RoutesCorrectly` — Missing `slow_state` Verification

**File:** `tests/Tracer.Tests.Unit/Storage/AppenderTests.cs`  
**Problem:** The test writes 1 slow-state record into the batch but NEVER verifies that the `slow_state` table received it. Only the events count (2) is checked. If the slow_state appender were silently broken (all slow-state records dropped), this test would still pass.  
**Spec required:** "batch of 5 events + 3 slow-state records; after flush, `events` table has 5 rows, `slow_state` table has 3 rows" (TRC-P1-005 success condition 11).  
**Fix:** After the flush, open a raw `DuckDBConnection` to the file (same approach used in `SchemaTests`) and `SELECT COUNT(*) FROM slow_state`; assert it equals 1 (or update to use 5 events + 3 slow-state to match the spec exactly).

---

### Issue 2: `Build_MinSeverityWarning_ExpandsToInClause` — Missing Negative Case

**File:** `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`  
**Problem:** The test checks that `sev0` and `sev1` parameters exist (positive case) but never verifies:
- That `Info` is NOT present as a parameter (spec: "no parameter for Info")
- That the parameter VALUES are `"Warning"` and `"Error"`, not arbitrary strings  
**Fix:** Add assertions: `parameters.Should().NotContain(p => p.Value!.ToString() == "Info")`, and verify `sev0`/`sev1` values are `"Warning"` and `"Error"`.

---

### Issue 3: `AppendEvent_1000Records_RoundTrip` — Weak Field Verification

**File:** `tests/Tracer.Tests.Unit/Storage/AppenderTests.cs`  
**Problem:** After writing 1000 records and flushing, the test only checks count == 1000 and that `events[0].PayloadJson` contains `"seq"`. The task requires "fields of a sampled record match the written values" — but `EventId`, `TraceId`, `PublishWallclock`, `PublisherNode`, etc. are never compared against the original written record.  
**Fix:** After the round-trip query, sample one specific record (e.g. the one with `EventId == 42`) and verify its `TraceId`, `PublishWallclock`, `PublisherNode`, `Topic`, and `PayloadJson` exactly match the values from `MakeEvent(42)`.

---

## P2 Issues (Deferred to Debt Tracker)

- **LIMIT/OFFSET inline**: `EventQueryBuilder.Build` embeds `LIMIT {query.Limit} OFFSET {query.Offset}` as inline integers instead of `$limit`/`$offset` parameters per spec (TRC-P1-006 success condition 1). No security risk since integers come from app code, but a spec deviation. Test also checks for `"LIMIT 500"` not `"LIMIT $limit"`.
- **SQL injection test uses wrong field**: `Build_SqlInjectionAttempt_IsParameterized` tests `OwningPlayerId` instead of `PayloadSearch`. The spec explicitly says `PayloadSearch = "'; DROP TABLE events; --"`. `PayloadSearch` has the special `%`/`_` escaping logic — testing it via OwningPlayerId skips that code path entirely.
- **Report missing Developer Insights**: The Q1–Q5 section and commit message were not included in the report. Must be included in all future reports.

---

## Verdict

**Status: NEEDS FIXES**

**Required Actions (P1 — fix in BATCH-02 corrective tasks before new feature work):**
1. Add `slow_state` row count verification to `AppendBatch_MixedRecords_RoutesCorrectly`
2. Add negative case + value assertions to `Build_MinSeverityWarning_ExpandsToInClause`
3. Add specific field-value comparison to `AppendEvent_1000Records_RoundTrip`

---

**Next Batch:** BATCH-02 will start with Corrective Task 0 fixing the three test issues above, then proceed to TRC-P1-007 through TRC-P1-012.
