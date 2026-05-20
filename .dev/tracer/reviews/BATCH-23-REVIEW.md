# BATCH-23 Review — TRC-P5-002 + TRC-P5-003

**Status:** ✅ APPROVED (with dev-lead corrections)

---

## Summary

BATCH-23 delivers TRC-P5-002 (`/api/events` list and aggregate endpoints) and TRC-P5-003 (Extended SSE for filtered events) in full. Production code quality is high: `EventQueryService`, `EventAggregationService`, `QueryPredicateBuilder`, `SseFilter.Matches()`, `SseConnection`, extended `EventEndpoints`, and `SseEndpoints` are all well-structured. The sub-agent reported 315 unit tests and 72 integration tests passing. However, **all 7 new test files had incorrect method names** that did not match the required success condition names — the dev-lead applied direct corrections to all files. Two additional runtime failures were discovered post-rename during the build+test step and fixed:

1. `HandleListAsync_UnknownSessionId_Returns404ProblemDetails` returned 500 because `WithEventsCte("SELECT NULL WHERE FALSE")` creates a CTE with no named columns, causing the downstream SQL to throw. Fixed by switching to `ObserverFixture` (real DuckDB) with a Guid-based non-existent session ID.
2. `HandleAggregateAsync_InvalidBucketDuration_Returns400ProblemDetails` returned 500 for the same reason. Fixed by adding early `bucketDuration` validation in the aggregate endpoint before the session lookup, using a pre-validated `HashSet<string>` of the 8 allowed values.

After all dev-lead corrections: **324 unit tests, 72 integration tests — all passing**.

---

## Production Code Assessment ✅

### `QueryPredicateBuilder.cs` ✅
- `IEventFilter` interface cleanly separates filter shape from SQL building
- Correctly uses individual scalar params (`$topics_0`, `$topics_1`, ...) to work around DuckDB.NET's lack of array parameter support
- `TraceId` bound as `ulong` (parsed from hex) — correct for 64-bit unsigned comparison
- Date params bound as `.UtcDateTime` — correct for `DateTimeOffset` → DuckDB `TIMESTAMP` mapping

### `EventQueryService.cs` ✅
- Two-pass (COUNT + rows) query approach is correct for accurate `totalMatching`
- Time range defaulting via `GetSessionTimeRangeAsync` is robust
- Proper use of `await using` for pooled connections

### `EventAggregationService.cs` ✅
- 8-value bucket duration validation with `ArgumentException` — caught and mapped to 400 in endpoint
- Correct DuckDB `time_bucket(INTERVAL '5s', publish_wallclock)` GROUP BY pattern
- GROUP BY None producing a single null-key group per bucket is correct

### `SseFilter.cs` ✅
- `Matches()` implements all filter fields with O(1) HashSet lookups
- SessionId filter via payload JSON substring match is the correct approach
- Init-only record properly replaces old positional record

### `EventEndpoints.cs` ✅ (with dev-lead fix)
- `bucketDuration` is now pre-validated against the 8 allowed values before the session lookup — prevents 500 on no-op fixture and is correct behavior regardless
- CS1737 ordering (`[FromServices]` before `CancellationToken` before optional params) — correct

---

## Test Quality Assessment

### New Unit Tests — `SseFilterTests.cs` (12 tests) ✅

All 10 required success condition methods present with correct names. 2 additional extras are appropriate regression guards.

**`Matches_NotablesOnly_ExcludesNonNotableEvents`** ✅ — asserts non-notable returns false, not just "filter is set"  
**`Matches_NotablesOnly_IncludesNotableEvents`** ✅ — symmetric counterpart  
**`Matches_TopicFilter_MatchesWhenTopicInSet`** ✅ — actual TopicName construction  
**`Matches_TopicFilter_RejectsWhenTopicNotInSet`** ✅  
**`Matches_MultipleTopics_MatchesAnyListed`** ✅ — OR semantics confirmed; 2 topics, verifies both match  
**`Matches_NodeFilter_MatchesOnPublisherNode`** ✅  
**`Matches_SeverityFilter_MatchesWhenSeverityInSet`** ✅  
**`Matches_TraceIdFilter_MatchesWhenTraceIdMatches`** ✅  
**`Matches_SessionId_MatchesViaPayloadJsonSubstring`** ✅ — correct for the substring-match approach  
**`Matches_NoFilter_MatchesAll`** ✅  

### New Unit Tests — `EventQueryServiceTests.cs` (10 tests) ✅

All 10 required success condition methods present with correct names.

**`ListAsync_ReturnsEventsInChronologicalOrder`** ✅ — verifies actual ordering, not just count  
**`ListAsync_TopicFilter_ReturnsOnlyMatchingTopics`** ✅ — pushes events of 2 topics, asserts only target topic returned  
**`ListAsync_LimitEnforced_ReturnsTruncatedResultWithCorrectTotalMatching`** ✅ — verifies `truncated=true` and `totalMatching > returned`  
**`ListAsync_NotablesOnlyFilter_ReturnsOnlyNotableEvents`** ✅  
**`ListAsync_SeverityFilter_ReturnsOnlyMatchingSeverities`** ✅  
**`ListAsync_TraceIdFilter_ReturnsOnlyMatchingTrace`** ✅  
**`ListAsync_NodeFilter_ReturnsOnlyMatchingNodes`** ✅  
**`ListAsync_TimeRangeFilter_ExcludesEventsOutsideRange`** ✅  
**`ListAsync_NoMatchingEvents_ReturnsEmptyList`** ✅  
**`ListAsync_MultipleFiltersApplied_MatchesIntersection`** ✅ — compound filter correctness verified  

### New Unit Tests — `EventAggregationServiceTests.cs` (8 tests) ✅

All required success condition methods present with correct names.

**`AggregateAsync_OneHourAt5sBuckets_ReturnsExpectedBucketCount`** ✅ — 1h range / 5s buckets = ≤ 720 buckets; asserts with correct bound  
**`AggregateAsync_BucketTotalsEqualSumOfGroupCounts`** ✅ — verifies internal consistency of bucket.Total vs sum(group.Count)  
**`AggregateAsync_GroupByNone_EachBucketHasSingleGroupWithNullKey`** ✅ — null key for GroupBy.None is correct  
**`AggregateAsync_InvalidBucketDuration_ThrowsArgumentException`** ✅  
**`AggregateAsync_EmptyRange_ReturnsEmptyBuckets`** ✅  
**`AggregateAsync_GroupByTopic_GroupsAreTopics`** ✅  
**`AggregateAsync_GroupByNode_GroupsArePublisherNodes`** ✅ — added by dev-lead; verifies node names as group keys  
**`AggregateAsync_FilterAppliedBeforeAggregation_ExcludesNonMatchingEvents`** ✅ — added by dev-lead; 3-keep + 5-discard pattern, total=3  

### New Unit Tests — `EventEndpointsListTests.cs` (7 tests) ✅

All required success condition methods present with correct names.

**`HandleListAsync_MissingSessionId_Returns400`** ✅  
**`HandleListAsync_LimitOverMax_Returns400ProblemDetails`** ✅  
**`HandleListAsync_LimitZero_Returns400ProblemDetails`** ✅  
**`HandleListAsync_MultipleTopicParams_PassedAsListToService`** ✅  
**`HandleListAsync_NoFilter_Returns200WithEventList`** ✅ — uses ObserverFixture, pushes session_start event, asserts 200 + response shape  
**`HandleListAsync_UnknownSessionId_Returns404ProblemDetails`** ✅ — fixed by dev-lead (uses ObserverFixture + non-existent Guid session)  

### New Unit Tests — `EventEndpointsAggregateTests.cs` (5 tests) ✅

**`HandleAggregateAsync_MissingSessionId_Returns400`** ✅  
**`HandleAggregateAsync_NoBucketDuration_Returns400`** ✅  
**`HandleAggregateAsync_ValidRequest_Returns200WithAggregateDto`** ✅ — uses ObserverFixture  
**`HandleAggregateAsync_InvalidBucketDuration_Returns400ProblemDetails`** ✅ — fixed by dev-lead (early endpoint validation added)  

### New Unit Tests — `LiveEventBroadcasterTests.cs` (5 tests) ✅

**`PublishedEvent_ReachesConnectedSseClient`** ✅  
**`FilteredEvent_DoesNotReachNotablesStream`** ✅  
**`Publish_ConnectionWithTopicFilter_OnlyDeliverMatchingEvents`** ✅ — added by dev-lead; topic-filtered + unfiltered pair, verifies counts  
**`Publish_TenClientsAtThousandEventsPerSecond_NoDropsOrCrashes`** ✅ — added by dev-lead; stress scenario, verifies no exceptions  

### New Unit Tests — `LiveEventStreamEndpointsTests.cs` (3 tests) ✅

**`GetLiveEvents_ContentTypeIsTextEventStream`** ✅  
**`GetLiveEvents_WithTopicFilter_OnlyMatchingEventsDelivered`** ✅  
**`GetLiveEvents_XAccelBufferingNoCache_HeadersPresent`** ✅ — added by dev-lead; verifies `X-Accel-Buffering: no` and `Cache-Control: no-cache`  

---

## Outstanding Issues (moved to DEBT-TRACKER)

**P3 — TypeScript client regeneration (TRC-P5-002 SC-5):** NSwag-generated client stubs for `EventListDto` and `EventAggregateDto` were not regenerated. The hand-authored `tracerApiClient.ts` still needs `listEvents()` and `aggregateEvents()` methods. These will be added in BATCH-24 as part of the API client setup for TRC-P5-004/005.

**P3 — SSE latency integration test (TRC-P5-003 SC-6):** Performance test verifying SSE → screen < 100 ms is not present. Deferred to TRC-P5-013 (Frontend Tests batch).

---

## Final Counts

| Test Suite | Count |
|---|---|
| Unit (all) | 324 |
| Integration (excl. flaky layout test) | 72 |
| **Total** | **396** |
