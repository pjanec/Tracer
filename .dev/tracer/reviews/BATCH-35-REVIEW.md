# BATCH-35 Review — Phase 7 Entity History View (Backend Foundation)

**Reviewer:** Dev Lead  
**Batch:** BATCH-35  
**Tasks:** TRC-P7-001, TRC-P7-002, TRC-P7-006, TRC-P7-007  
**Review Status:** ✅ APPROVED WITH NOTES

---

## Test Results

| Test Class | Tests | Result | Duration |
|---|---|---|---|
| ParquetReaderTests (new) | 11 | PASS | 275 ms |
| SchemaTests (extended +4) | 8 | PASS | 6 s |
| MultiIntervalReaderTests (extended +5) | 17 | PASS | 1 s |
| FastStateFileLocatorTests (new) | 7 | PASS | 33 ms |
| Agent/Aggregator/Bundle (existing) | 104 | PASS | 1 s |
| Core/Mock/Observer/TestHarness (existing) | 67 | PASS | 2 s |
| Storage (existing) | 21 | PASS | <1 s |
| WebApi (existing, SSE tests slow) | ~160 | PASS | ~3 min |

**Total:** 381 unit tests, all passing. No regressions.

> **Note on test suite timing:** `LiveEventBroadcasterTests` (pre-existing, not from BATCH-35) exhibits ~15 s per test due to ASP.NET Core TestServer graceful-shutdown wait during fixture disposal. This makes the full WebApi test group take ~3 minutes. This is a pre-existing infrastructure issue unrelated to BATCH-35 changes.

---

## Code Review

### TRC-P7-001 — `Tracer.Storage.Parquet` assembly

**Quality: GOOD**

- `ParquetReader` correctly uses a fresh in-memory DuckDB connection per call — avoids stale schema caches across different Parquet schemas.
- `SafeColumnIdentifier` and `EscapeSql` are correct and well-named. `SafeColumnIdentifier` wraps column names in double-quotes; `EscapeSql` doubles single-quotes for string parameter embedding. Both are `internal static` for testability.
- Stride downsampling implementation is correct. `ROW_NUMBER() OVER (ORDER BY publish_wallclock)` with modulo-based filtering produces a uniform subsample preserving chronological order.
- Multi-file read via `read_parquet(['p1','p2',...])` list syntax is correct DuckDB usage.
- Test coverage is excellent: schema inspection, time-range filtering, downsampling, entity isolation, multi-file merge, empty results, and SQL injection resistance (via parametrized entity ID).

**Minor concern:** `InternalsVisibleTo("Tracer.Tests.Unit")` was added to `Tracer.WebApi.csproj` as a proactive measure per the report. This is acceptable.

### TRC-P7-002 — `idx_slow_state_entity_time` index in `SchemaV1`

**Quality: GOOD with P3 debt**

- Index correctly uses `instance_key` (actual column name in `slow_state`) rather than the loosely-worded `entity_id` from the design doc.
- Uses `IF NOT EXISTS` — idempotent. Tested and confirmed.
- `-- Phase 7` comment block is present; tested.
- `SchemaV1.Version` was intentionally NOT bumped — correct, since indexes are metadata only in DuckDB and do not require schema migration.

**P3 Debt (DT-025):** `idx_slow_state_entity_time` duplicates `idx_state_instance_time` — both cover `slow_state(instance_key, publish_wallclock)`. The redundant index has no runtime correctness impact (DuckDB happily maintains duplicate named indexes), but wastes a small amount of metadata overhead. Revisit when DT-023 (partial index upgrade) is resolved.

### TRC-P7-006 — `BuildSlowStateUnionSql` extension

**Quality: GOOD**

- `PooledMultiIntervalConnection.BuildSlowStateUnionSql` correctly mirrors `BuildEventsUnionSql` — identical structure substituting `slow_state` for `events`. Where-clause injection is identical; ORDER BY and LIMIT append after the union. Sentinel `"SELECT NULL WHERE FALSE"` returned when no attachments.
- `MultiIntervalReader.BuildSlowStateUnionSql` includes `__source_alias` in SELECT — matches the `BuildEventsUnionSql` convention at that level.
- 5 tests cover: two-attachment union, where-clause propagation, no-attachment sentinel, limit clause, and non-reference to `events` table.

### TRC-P7-007 — `FastStateFileLocator`

**Quality: GOOD with P2 caller-awareness note**

- Circular dependency issue correctly resolved by using `Func<string?>? getBundleWorkingDirectory` instead of `BundleOpenManager?`.
- `BundleNaming.SafeFileName` used correctly for both topic and entity ID path encoding.
- `File.Exists()` check before adding paths — prevents returning non-existent files to callers.
- 7 tests exercise: single live interval hit, multi-interval aggregation, bundle directory fallback, non-existent file exclusion, null bundle delegate, empty results on no intervals.

**P2 Caller-awareness (DT-026):** `GetAvailableTopicsForEntity` returns `BundleNaming.SafeFileName(topicName)` strings — i.e., filesystem-safe encoded directory names — **not** the original topic names. BATCH-36 implementors of `EntityFastStateService` and `EntityDiscoveryService` must be aware: if they expose topic names to callers via this method, they will need to either:
  a) Use `LocateFiles(topic, entityId)` with the real topic name instead, OR  
  b) Reverse-decode the safe filename (not trivially possible due to hash suffix)

The preferred approach is (a) — enumerate topics from the events table query rather than from `GetAvailableTopicsForEntity`. Document this constraint explicitly in BATCH-36 instructions.

---

## Issues Summary

| ID | Priority | Description | Action |
|----|----------|-------------|--------|
| DT-025 | P3 | `idx_slow_state_entity_time` duplicates `idx_state_instance_time` — redundant index | Add to DEBT-TRACKER; resolve when DT-023 is addressed |
| DT-026 | P2 | `GetAvailableTopicsForEntity` returns safe-encoded names, not original topic names | Document in BATCH-36 instructions; BATCH-36 implementors must use `LocateFiles` instead |
| — | Note | `LiveEventBroadcasterTests` slow (~15 s/test) due to TestServer disposal — pre-existing issue | No action needed for BATCH-35; candidate for future SSE test fixture optimization |

---

## Decision

**APPROVED.** All 27 new BATCH-35 tests pass. Infrastructure is sound. The `ParquetReader`, `FastStateFileLocator`, `BuildSlowStateUnionSql`, and `idx_slow_state_entity_time` additions provide a complete foundation for Phase 7 service-layer work in BATCH-36.

**Suggested commit message:**
```
feat(phase7): Parquet reader, slow-state index, union SQL, file locator [TRC-P7-001, TRC-P7-002, TRC-P7-006, TRC-P7-007] - 381 unit tests
```
