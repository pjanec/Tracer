# BATCH-27 Instructions — TRC-P5-011 + TRC-P5-012

## Overview
This batch completes TRC-P5-011 (Backend Unit Tests) and TRC-P5-012 (Backend Integration Tests).

Most test files already exist but use different method names than the spec requires. Work needed:
1. One production code change: add `from`/`to` validation to the aggregate endpoint
2. Rename/add backend unit tests to exactly match the spec-required names
3. Add a missing `SetChanged_FiresAfterEviction` test to IntervalSetTrackerTests.cs
4. Rename/add tests in `LiveMultiIntervalQueryTests.cs`
5. Add missing test `LiveQuery_ResultsOrderedAcrossIntervalBoundaries`
6. Create new `TimelineRoundTripTests.cs` with 4 tests

All 324 existing .NET tests must continue to pass. Run:
```
dotnet test tests\Tracer.Tests.Unit --no-build
dotnet test tests\Tracer.Tests.Integration --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```

---

## REQUIRED READING BEFORE STARTING
Read these files:
- `src/Tracer.WebApi/Endpoints/EventEndpoints.cs` — HandleAggregateAsync needs from/to validation
- `tests/Tracer.Tests.Unit/MultiInterval/IntervalSetTrackerTests.cs` — rename 5 + add 1 test
- `tests/Tracer.Tests.Unit/MultiInterval/LiveMultiIntervalReaderTests.cs` — rename 5 tests
- `tests/Tracer.Tests.Unit/WebApi/EventQueryServiceTests.cs` — rename 9 tests
- `tests/Tracer.Tests.Unit/WebApi/EventAggregationServiceTests.cs` — rename 5 tests + add 2
- `tests/Tracer.Tests.Unit/WebApi/EventEndpointsListTests.cs` — rename 5 tests
- `tests/Tracer.Tests.Unit/WebApi/EventEndpointsAggregateTests.cs` — rename 3 + add 1
- `tests/Tracer.Tests.Integration/LiveMultiIntervalQueryTests.cs` — rename 3 + add 1
- `tests/Tracer.Tests.Integration/ObserverBundleBuildTests.cs` — reference for TimelineRoundTripTests pattern

---

## Task 1 — Add from/to validation to aggregate endpoint

**File:** `src/Tracer.WebApi/Endpoints/EventEndpoints.cs`

In `HandleAggregateAsync`, after the `bucketDuration` validation and before the session lookup, add:
```csharp
if (!from.HasValue || !to.HasValue)
    return TypedResults.Problem("Both 'from' and 'to' query parameters are required for aggregate queries", statusCode: 400);
```

This enables the `GetAggregate_MissingFromOrTo_Returns400ProblemDetails` test.

---

## Task 2 — IntervalSetTrackerTests.cs changes

**File:** `tests/Tracer.Tests.Unit/MultiInterval/IntervalSetTrackerTests.cs`

### 2a. Rename existing tests (5 renames):
- `InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActive` → `InitializeAsync_NoCompletedIntervals_SnapshotContainsOnlyActiveInterval`
- `InitializeAsync_FiveCompleted_CapThree_SnapshotContainsThreeNewestPlusActive` → `InitializeAsync_FiveCompletedIntervals_CappedTo3InSnapshot`
- `OnIntervalRotatedAsync_PreviousActiveBecomesCompleted` → `OnIntervalRotatedAsync_DemotesPreviousActiveToCompleted_AddsNewActive`
- `SetChanged_FiredAfterInitialize` → `SetChanged_FiresAfterInitialize`
- `SetChanged_FiredAfterRotation` → `SetChanged_FiresAfterRotation`
- `SetChanged_NotFiredIfEvictionTargetNotInSet` → `SetChanged_DoesNotFireWhenEvictedIntervalWasNotInCurrentSet`

### 2b. Add new test `SetChanged_FiresAfterEviction` (the positive eviction case — currently missing):

Add after `OnIntervalEvictedAsync_RemovesEvictedIntervalFromSnapshot`:
```csharp
[Fact]
public async Task SetChanged_FiresAfterEviction()
{
    await using var rotator = CreateRotator(_tempDir);
    await rotator.OpenCurrentAsync(default);

    // Create a completed interval that WILL be in the snapshot
    var completedTs = "20260102T000000Z";
    var completedDir = new IntervalDirectory(_tempDir, new IntervalTimestamp(completedTs));
    completedDir.EnsureCreated();
    completedDir.WriteReadySentinel();

    var tracker = new IntervalSetTracker(rotator, 3, NullLogger<IntervalSetTracker>.Instance);
    await tracker.InitializeAsync(default);

    int fired = 0;
    tracker.SetChanged += (_, _) => { fired++; return Task.CompletedTask; };

    // Evict the completed interval that IS in the snapshot
    await tracker.OnIntervalEvictedAsync(completedDir, default);

    fired.Should().Be(1, "SetChanged must fire when an interval that was in the snapshot is evicted");
}
```

---

## Task 3 — LiveMultiIntervalReaderTests.cs renames

**File:** `tests/Tracer.Tests.Unit/MultiInterval/LiveMultiIntervalReaderTests.cs`

Rename these test methods:
- `PoolSize_AfterInitialize_AllConnectionsAreAvailable` → `InitializeAsync_BuildsPoolConnectionsEqualToConfiguredPoolSize`
- `AcquireAsync_EmptySnapshot_ConnectionSqlIsEmptySentinel` → `AcquireAsync_ReturnsConnectionWithCurrentIntervalsAttached`
- `SetChanged_TriggersPoolRebuild_NewConnectionsReflectNewSnapshot` → `AfterSetChangedFires_NewAcquiredConnectionsHaveUpdatedIntervalSet`
- `StaleConnection_ReturnedAfterRebuild_IsDiscarded` → `ConnectionIssuedFromOldPool_DisposesRatherThanReturnsToPool`
- `ConcurrentAcquireAndRebuild_DoesNotDeadlock` → `ConcurrentAcquireAndRebuild_CompletesWithoutExceptionOrLeak`

---

## Task 4 — EventQueryServiceTests.cs renames

**File:** `tests/Tracer.Tests.Unit/WebApi/EventQueryServiceTests.cs`

Rename these test methods:
- `ListAsync_NoFilter_ReturnsAllEventsInTimeOrder` → `ListAsync_EmptyFilter_ReturnsEventsInPublishWallclockAscendingOrder`
- `ListAsync_TimeRange_ExcludesEventsOutsideRange` → `ListAsync_TimeRange_ReturnsOnlyEventsWithinRange`
- `ListAsync_TopicFilter_ReturnsOnlyMatchingTopics` → `ListAsync_SingleTopicFilter_ReturnsOnlyMatchingTopic`
- `ListAsync_MultiTopicFilter_OrsWithinFilter` → `ListAsync_MultipleTopics_OredWithinFilter`
- `ListAsync_MultipleFilterTypes_AndsAcrossFilters` → `ListAsync_TopicAndSeverity_AndedAcrossFilterTypes`
- `ListAsync_Limit_TruncatesAndSetsTruncatedFlag` → `ListAsync_LimitHit_TotalMatchingReflectsTrueCount_TruncatedTrue`
- `ListAsync_TraceIdFilter_ReturnsOnlyThatTrace` → `ListAsync_TraceIdFilter_ReturnsOnlyEventsForThatTrace`
- `ListAsync_OrderDescending_ReturnsByNewestFirst` → `ListAsync_OrderDescending_ReturnsNewestFirst`
- `ListAsync_EmptyResult_ReturnsTotalMatchingZero` → `ListAsync_EmptyResult_TotalMatchingIsZero_TruncatedFalse`

Note: `ListAsync_NotablesOnly_ExcludesNonNotables` is an extra test not in the spec — keep it as-is.

---

## Task 5 — EventAggregationServiceTests.cs renames

**File:** `tests/Tracer.Tests.Unit/WebApi/EventAggregationServiceTests.cs`

Rename these test methods:
- `AggregateAsync_OneHourAt5sBuckets_ReturnsExpectedBucketCount` → `AggregateAsync_OneHourViewportAt5sBuckets_Returns720Buckets`
- `AggregateAsync_EmptyRange_ReturnsEmptyBuckets` → `AggregateAsync_EmptyTimeRange_ReturnsEmptyBucketList`
- `AggregateAsync_GroupByNone_EachBucketHasSingleGroupWithNullKey` → `AggregateAsync_GroupByNone_EachBucketHasOnlyOneGroupWithNullKey`
- `AggregateAsync_GroupByNode_GroupsArePublisherNodes` → `AggregateAsync_GroupByNode_GroupsResultsByPublisherNode`
- `AggregateAsync_FilterAppliedBeforeAggregation_ExcludesNonMatchingEvents` → `AggregateAsync_FilterAppliedBeforeGrouping_OnlyMatchingEventsCounted`

Keep as-is: `AggregateAsync_BucketTotalsEqualSumOfGroupCounts`, `AggregateAsync_InvalidBucketDuration_ThrowsArgumentException`, `AggregateAsync_GroupByTopic_GroupsAreTopics`, `ValidDurations_AllAccepted`

---

## Task 6 — EventEndpointsListTests.cs renames

**File:** `tests/Tracer.Tests.Unit/WebApi/EventEndpointsListTests.cs`

Rename these test methods:
- `HandleListAsync_NoFilter_Returns200WithEventList` → `GetEvents_ValidRequest_Returns200WithEventListDto`
- `HandleListAsync_LimitZero_Returns400ProblemDetails` → `GetEvents_LimitZero_Returns400ProblemDetails`
- `HandleListAsync_LimitOverMax_Returns400ProblemDetails` → `GetEvents_LimitOver5000_Returns400ProblemDetails`
- `HandleListAsync_UnknownSessionId_Returns404ProblemDetails` → `GetEvents_UnknownSessionId_Returns404ProblemDetails`
- `HandleListAsync_MultipleTopicParams_PassedAsListToService` → `GetEvents_MultipleTopicQueryParams_PassedAsListToQueryService`

Keep as-is: `HandleListAsync_NoSessionId_Returns400`, `HandleListAsync_ValidLimitMaximum_NotBadRequest`

---

## Task 7 — EventEndpointsAggregateTests.cs renames + new test

**File:** `tests/Tracer.Tests.Unit/WebApi/EventEndpointsAggregateTests.cs`

Rename these test methods:
- `HandleAggregateAsync_ValidRequest_Returns200WithAggregateDto` → `GetAggregate_ValidRequest_Returns200WithAggregateDto`
- `HandleAggregateAsync_InvalidBucketDuration_Returns400ProblemDetails` → `GetAggregate_InvalidBucketDuration_Returns400ProblemDetails`

Keep as-is: `HandleAggregateAsync_MissingSessionId_Returns400`, `HandleAggregateAsync_NoBucketDuration_Returns400`

Add new test `GetAggregate_MissingFromOrTo_Returns400ProblemDetails`:
```csharp
[Fact]
public async Task GetAggregate_MissingFromOrTo_Returns400ProblemDetails()
{
    // from and to are required for aggregate; omitting them should return 400
    var response = await _fixture.Client.GetAsync(
        "/api/events/aggregate?sessionId=any&bucketDuration=1s");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

**Note:** This test requires the `from`/`to` validation added in Task 1. The existing `HandleAggregateAsync_NoBucketDuration_Returns400` test omits both `from`/`to` AND `bucketDuration` — it returns 400 because bucketDuration is missing (checked first). The new test includes `bucketDuration` but omits `from`/`to`.

---

## Task 8 — LiveMultiIntervalQueryTests.cs renames + new test

**File:** `tests/Tracer.Tests.Integration/LiveMultiIntervalQueryTests.cs`

### 8a. Rename existing tests:
- `QuerySpansThreeIntervals_AllSessionsReturned` → `LiveQuery_EventsSpanThreeIntervals_AllReturnedByListEndpoint`
- `AfterRotation_NewIntervalEventsIncluded` → `LiveQuery_AfterRotation_NewActiveIntervalQueriedImmediately`
- `AfterEviction_EvictedIntervalEventsExcluded` → `LiveQuery_AfterEviction_EvictedIntervalExcludedFromResults`

### 8b. Read the existing tests carefully before renaming to understand the patterns, then add new test:

`LiveQuery_ResultsOrderedAcrossIntervalBoundaries` — events pushed into two intervals in reverse time order should be returned in ascending `publishWallclock` order.

Add this test to the class (pattern: push events to interval 1, rotate, push earlier-timestamped events to interval 2, query, verify ascending order):

```csharp
/// <summary>Results from multiple intervals are ordered by publishWallclock ascending.</summary>
[Fact]
public async Task LiveQuery_ResultsOrderedAcrossIntervalBoundaries()
{
    var sessionId = $"order-test-{Guid.NewGuid():N}";

    // Push event at T+1 minute into interval 1
    var evLate = MakeEvent(sessionId, BaseTime.AddMinutes(1), "order-topic");
    await _fixture.PushAsync([evLate]);
    await _fixture.ForceRotationAsync();

    // Push event at T+0 (earlier) into interval 2 (active)
    var evEarly = MakeEvent(sessionId, BaseTime, "order-topic");
    await _fixture.PushAsync([evEarly]);

    var url = $"/api/events?sessionId={sessionId}&topic=order-topic&limit=100";
    var response = await _fixture.Client.GetAsync(url);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    var dto = System.Text.Json.JsonSerializer.Deserialize<EventListDto>(json, _jsonOptions);

    dto.Should().NotBeNull();
    dto!.Events.Should().HaveCountGreaterOrEqualTo(2,
        "both events must appear regardless of which interval they're in");

    // Verify ascending order by publishWallclock
    var times = dto.Events
        .Select(e => DateTimeOffset.Parse(e.PublishWallclock!))
        .ToList();
    times.Should().BeInAscendingOrder(
        "events from multiple intervals must be sorted by publishWallclock ascending");
}
```

You'll also need to:
1. Add a `MakeEvent` helper that accepts `(string sessionId, DateTimeOffset at, string topic)` parameters — read the existing `MakeSessionStart` helper and adapt it. You need to push actual telemetry events, not just session_start events. Look at how other integration tests push events with custom topics.
2. Add a `_jsonOptions` field (or use inline `JsonSerializerOptions` with camelCase).
3. Add an `EventListDto` record/class compatible with the JSON response if not already present in usings.

**Read the existing tests carefully** — look at how `AfterEviction_EvictedIntervalEventsExcluded` pushes events with different topics, as that shows the correct MakeEvent pattern.

---

## Task 9 — Create TimelineRoundTripTests.cs

**File:** `tests/Tracer.Tests.Integration/TimelineRoundTripTests.cs` (NEW FILE)

This file needs to:
1. Create a live Observer session with known events
2. Build a bundle from that session using the bundle build API
3. Start OfflineViewer pointing at the bundle
4. Compare `GET /api/events` and `GET /api/events/aggregate` responses

**IMPORTANT:** Read `tests/Tracer.Tests.Integration/ObserverBundleBuildTests.cs` fully before writing this — it shows the exact pattern for bundle build setup with `AggregationFixture`.

Also read `tests/Tracer.Tests.Integration/BundleRoundTripTests.cs` for the OfflineViewer pattern.

The tests needed:
1. `RoundTrip_ListQuery_LiveAndBundleReturnIdenticalEvents` — GET /api/events on both live and bundle with same params returns same event IDs in same order
2. `RoundTrip_AggregateQuery_LiveAndBundleReturnIdenticalBuckets` — GET /api/events/aggregate on both returns same bucket start times and counts
3. `RoundTrip_OpenSession_1MEvents_FirstResponseUnder500ms` — performance test, `[Trait("Category", "Performance")]`; uses 10K events (scaled from spec's 1M) with Stopwatch assertion < 500ms
4. `RoundTrip_AggregateQuery_100MEvents_CompletesUnder1s` — performance test, `[Trait("Category", "Performance")]`; uses 10K events (scaled from spec's 100M) with Stopwatch assertion < 1000ms

**Test class structure:**
```csharp
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracer.Adapters.Mock.Storage;
using Tracer.Aggregator;
using Tracer.Bundle.Format;
using Tracer.Core.Abstractions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.OfflineViewer;
using Tracer.TestHarness;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Bundles;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Endpoints;
using Xunit;

namespace Tracer.Tests.Integration;

[Collection("TimelineRoundTrip")]
public sealed class TimelineRoundTripTests : IAsyncLifetime
{
    // Live Observer
    private AggregationFixture _nasFixture = null!;
    private ObserverFixture _observer = null !;
    private string _bundlesRoot = null!;
    
    // Bundle / OfflineViewer
    private string _builtBundlePath = null!;
    private Microsoft.AspNetCore.Builder.WebApplication? _viewerApp;
    private HttpClient? _bundleClient;
    
    // Shared test data
    private string _sessionId = null!;
    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static ulong _nextId = 300_000;
    
    // ... (implement fixture setup similar to ObserverBundleBuildTests.cs)
}
```

For the fixture setup, follow `ObserverBundleBuildTests.InitializeAsync` exactly but:
1. After creating the observer, push events for `_sessionId`
2. Call `POST /api/bundles/build` with a time range covering those events
3. Poll until bundle is done
4. Find the bundle file path (from response)
5. Start OfflineViewer pointing at that bundle

For `InitializeAsync`, push ~10 events into the live session — enough to make the round-trip tests meaningful.

The `RoundTrip_ListQuery_LiveAndBundleReturnIdenticalEvents` test should:
1. Call `GET /api/events?sessionId={_sessionId}&limit=100` on live client (`_observer.Client`)
2. Call the same on `_bundleClient` (using the session ID from the bundle manifest, which matches `_sessionId`)
3. Compare event IDs (sort both by eventId for order-independent comparison, then also compare order)

For the performance tests:
- Push 10K events (using a loop generating events quickly)
- Measure the query time with `Stopwatch`
- Assert < 500ms (for list) or < 1000ms (for aggregate)
- Add `// Note: spec requires 1M / 100M events; using 10K for practical test execution` comment

**Collection definition:** Add `[CollectionDefinition("TimelineRoundTrip")]` in the TestCollections.cs file:
- Read `tests/Tracer.Tests.Integration/TestCollections.cs` to see the pattern, then add `[CollectionDefinition("TimelineRoundTrip")]` if not already present.

---

## Verification

After all changes:
```powershell
cd d:\Work\Tracer
dotnet build Tracer.sln --no-incremental -c Release 2>&1 | Select-Object -Last 5
dotnet test tests\Tracer.Tests.Unit -c Release --no-build 2>&1 | Select-Object -Last 10
dotnet test tests\Tracer.Tests.Integration -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout" 2>&1 | Select-Object -Last 15
```

Expected:
- Build: 0 errors
- Unit tests: all pass (count will increase from 324 with new tests added)
- Integration tests: all pass (count will increase with new tests)

## Report Template
Write your report to `.dev/tracer/reports/BATCH-27-REPORT.md` with:
- List of all renames and additions
- Unit test pass count / total
- Integration test pass count / total
- Any issues and resolutions
