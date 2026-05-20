# BATCH-29 Review — TRC-P6-001 & TRC-P6-002

**Tasks:** Schema Extension (TRC-P6-001), Trace Walking Backend (TRC-P6-002)  
**Status:** APPROVED — all 351 unit tests pass, 2 integration tests pass

---

## Summary

BATCH-29 confirmed and finalized the Phase 6 foundation work. Most production code was pre-existing from a prior session; this batch validated it, fixed the partial index deviation, and confirmed all tests pass.

---

## TRC-P6-001 — Schema Extension

**Files:**
- `src/Tracer.Storage.DuckDB/Schema/SchemaV1.cs` — index renamed to `idx_events_parent_event_id` (regular index; partial clause dropped due to DuckDB 1.0.2 limitation)
- `tests/Tracer.Tests.Unit/Storage/SchemaV1Tests.cs` — asserts the new index name in DDL
- `tests/Tracer.Tests.Unit/Storage/SchemaTests.cs` — expected array has `"idx_events_parent_event_id"` ✓
- `tests/Tracer.Tests.Integration/SchemaAppliedTests.cs` — 2 tests confirming index exists at runtime

**Schema tests: 1 unit + 2 integration — PASS**

**Deviation:** The `WHERE parent_event_id != 0` partial clause was not applied. DuckDB 1.0.2 throws `Not implemented Error: Creating partial indexes is not supported currently`. This is a valid runtime constraint — tracked as DT-023 (P3).

Minor: `SchemaV1Tests` test name `CreateIndexes_ContainsPartialIndexOnParentEventId` is now slightly misleading since it's a regular index. Non-blocking (P3).

---

## TRC-P6-002 — Trace Walking Backend

**Files (pre-existing, confirmed correct):**
- `src/Tracer.WebApi/Queries/TraceTree.cs` — domain records (TraceTree, TraceNode, TraceEdge, TraceSummary)
- `src/Tracer.WebApi/Queries/EventRecordMapper.cs` — IDataReader → EventRecord mapper
- `src/Tracer.WebApi/Queries/TraceWalker.cs` — WalkAncestorsAsync, WalkDescendantsAsync, LookupEventAsync
- `src/Tracer.WebApi/Queries/TraceQueryService.cs` — GetTraceTreeAsync, GetTraceTreeForEventAsync, GetAncestorTreeAsync, GetDescendantTreeAsync

**Test files:**
- `tests/Tracer.Tests.Unit/WebApi/TraceWalkerTests.cs` — 5 tests
- `tests/Tracer.Tests.Unit/WebApi/TraceQueryServiceTests.cs` — 4 tests

---

## Test Quality Assessment

**TraceWalkerTests (5 tests) — GOOD**
| Test | What it covers |
|------|---------------|
| `WalkAncestors_ThreeGenerationChain_ReturnsChainFromStartToRoot` | Basic 3-level chain, order verification (leaf→root) |
| `WalkAncestors_MaxDepthReached_StopsAtLimitAndReturnsPartialChain` | maxDepth boundary — correct count and IDs at limit |
| `WalkAncestors_CycleInParentPointers_TerminatesViaCycleGuard` | Visited-set cycle guard, 20-deep chain, uniqueness assertion |
| `WalkDescendants_BinaryFanout_ReturnsAllNodesInBfsOrder` | Binary tree with 6 descendants; BFS level ordering verified |
| `WalkDescendants_MaxNodesReached_TruncatesWithoutException` | maxNodes boundary, no exception, exact count |

All tests use real DuckDB via ObserverFixture — no mocking of storage. Assertions are specific.

**TraceQueryServiceTests (4 tests) — GOOD**
| Test | What it covers |
|------|---------------|
| `GetTraceTree_NormalTrace_ReturnsNodesEdgesAndSummary` | 10-event star trace, node/edge counts, summary flags |
| `GetTraceTree_ExceedsMaxEvents_ReturnsTruncatedResultWithFlagSet` | 20 events, maxEvents=10, Truncated=true |
| `GetTraceTreeForEvent_EventWithTraceId_ReturnsSameResultAsDirectTraceCall` | Cross-method consistency (via-event vs via-traceId) |
| `GetTraceTreeForEvent_EventWithZeroTraceId_ReturnsSingletonTree` | Singleton case: TraceId=0, 1 node, 0 edges |

---

## Issues

| ID | Priority | Description |
|----|----------|-------------|
| DT-023 | P3 | When upgrading DuckDB past 1.0.2, apply `WHERE parent_event_id != 0` partial clause to `SchemaV1.CreateIndexes` and update `SchemaV1Tests` |
| DT-024 | P3 | `SchemaV1Tests.CreateIndexes_ContainsPartialIndexOnParentEventId` test name is misleading (no longer a partial index) — rename to `CreateIndexes_ContainsIndexOnParentEventId` when DT-023 is resolved |

---

## Verification

```
Build succeeded. 0 Warning(s), 0 Error(s)
Unit tests: Passed 351 / Total 351 / Failed 0
Integration tests (SchemaAppliedTests): Passed 2 / Total 2 / Failed 0
```
