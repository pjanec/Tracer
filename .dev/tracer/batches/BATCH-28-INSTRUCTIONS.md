# BATCH-28 Instructions — TRC-P5-013: Frontend Tests

## Context

You are a developer implementing task **TRC-P5-013** in `d:\Work\Tracer`.

This batch is entirely frontend test work: rename/add Vitest unit tests and add a Playwright E2E test. No production code changes are required.

**Solution:** `d:\Work\Tracer\Tracer.sln`
**Frontend:** `d:\Work\Tracer\tracer-viewer\` (Vue 3 + TypeScript + Vitest + Playwright)

## Project Conventions

- Test naming: `methodOrSubject_Condition_Expected` (already established by existing tests)
- All Vitest specs live in `tracer-viewer/tests/unit/`
- All Playwright E2E specs live in `tracer-viewer/tests/e2e/`
- No new production files needed — only test file modifications

---

## Task 1: Rename tests in `timelineRenderer.spec.ts`

**File:** `d:\Work\Tracer\tracer-viewer\tests\unit\timelineRenderer.spec.ts`

The describe block is `describe('timelineRenderer', () => {`. Rename the following `it(...)` test names:

| OLD name | NEW name |
|---|---|
| `drawsOneMarkerPerEventInListMode` | `render_ListMode_DrawsOneArcPerNonNotableEvent` |
| `drawsSquareForNotableEvents` | `render_ListMode_DrawsOneRectPerNotableEvent` |
| `drawsBarPerBucketGroupInAggregateMode` | `render_AggregateMode_DrawsFillRectPerBucketGroup` |
| `handlesEmptyEventsListWithoutError` | `render_EmptyEventList_NoArcOrRectCallsMade` |
| `skipsEventsOutsideViewport` | `render_EventOutsideViewportBounds_SkippedDefensively` |
| `hitIndexHasEntryForEachDrawnMarker` | `render_ReturnsHitIndexWithEntryForEachDrawnMarker` |

Only rename the string literals inside `it(...)`. Do not change any test body logic.

---

## Task 2: Rename/add tests in `timelineLayout.spec.ts`

**File:** `d:\Work\Tracer\tracer-viewer\tests\unit\timelineLayout.spec.ts`

### 2a. Rename existing tests in `describe('timelineLayout', () => {`:

| OLD name | NEW name |
|---|---|
| `chooseBucketDuration_SubOneMinute_ReturnsRaw` | `chooseBucketDuration_SpanUnder60s_ReturnsRaw` |
| `chooseBucketDuration_FiveMinutes_Returns100ms` | `chooseBucketDuration_Span1mTo5m_Returns100ms` |
| `chooseBucketDuration_ThirtyMinutes_Returns5s` | `chooseBucketDuration_Span30mTo1h_Returns5s` |
| `chooseBucketDuration_OneHour_Returns30s` | `chooseBucketDuration_Span1hTo4h_Returns30s` |
| `chooseBucketDuration_FourHoursOrMore_Returns5m` | `chooseBucketDuration_SpanOver4h_Returns5m` |

### 2b. Add a new test AFTER the renamed `chooseBucketDuration_Span1mTo5m_Returns100ms` test:

```typescript
it('chooseBucketDuration_Span5mTo30m_Returns1s', () => {
  // Just above 5min → '1s'
  expect(chooseBucketDuration(300_001)).toBe('1s');
  // At 15min
  expect(chooseBucketDuration(15 * 60 * 1000)).toBe('1s');
  // Just below 30min
  expect(chooseBucketDuration(1_799_999)).toBe('1s');
});
```

Only change the test names listed above and add the one new test. Do NOT change any existing test body logic, and do NOT remove any tests.

---

## Task 3: Rename tests in `timelineHitTest.spec.ts`

**File:** `d:\Work\Tracer\tracer-viewer\tests\unit\timelineHitTest.spec.ts`

Rename the following `it(...)` test names inside `describe('timelineHitTest', () => {`:

| OLD name | NEW name |
|---|---|
| `findMarkerAt_ExactCoordinate_ReturnsMarker` | `findMarkerAt_ExactPosition_ReturnsMarker` |
| `findMarkerAt_InsideRadius_ReturnsMarker` | `findMarkerAt_WithinMarkerRadius_ReturnsMarker` |
| `findMarkerAt_OutsideAllMarkers_ReturnsNull` | `findMarkerAt_BeyondMarkerRadius_ReturnsNull` |
| `performanceWith1000Markers_FindTakesUnder1ms` | `findMarkerAt_1000Markers_CompletesUnder1ms` |

The tests `findMarkerAt_TwoMarkersInSameCell_ReturnsCloserOne`, `findBucketAt_PointInsideBucket_ReturnsBucket`, and `findBucketAt_PointOutsideBucket_ReturnsNull` already have correct names — do NOT change them.

---

## Task 4: Rename/split tests in `useTimelineQuery.spec.ts`

**File:** `d:\Work\Tracer\tracer-viewer\tests\unit\useTimelineQuery.spec.ts`

### 4a. Rename these tests (only the string literal in `it(...)`):

| OLD name | NEW name |
|---|---|
| `viewportChange_triggersQuery` | `viewportChange_TriggersNewQuery` |
| `rapidViewportChanges_onlyLastQueryFires` | `rapidViewportChanges_Under100ms_OnlyLastQueryFires` |
| `queryError_setsStoreError` | `queryError_SetsStoreError` |
| `abortError_doesNotSurfaceAsStoreError` | `abortError_NotSurfacedAsStoreError` |

### 4b. Replace the single test `spanThreshold_switchesListToAggregate` with TWO separate tests:

**OLD (remove this):**
```typescript
it('spanThreshold_switchesListToAggregate', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.aggregateEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      bucketDuration: '1s',
      buckets: [],
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    // Set viewport span > 4h (the aggregate threshold)
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T15:00:00Z'); // 5h span

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(api.aggregateEvents).toHaveBeenCalledTimes(1);
    expect(api.listEvents).not.toHaveBeenCalled();
  });
```

**NEW (replace with these two):**
```typescript
  it('spanBelowThreshold_RequestsRawListEndpoint', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      events: [], totalMatching: 0, returned: 0, truncated: false,
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    // Span of 30 minutes — below the aggregate threshold
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:30:00Z');

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(api.listEvents).toHaveBeenCalledTimes(1);
    expect(api.aggregateEvents).not.toHaveBeenCalled();
  });

  it('spanAboveThreshold_RequestsAggregateEndpoint', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.aggregateEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      bucketDuration: '1s',
      buckets: [],
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    // Span of 5 hours — above the aggregate threshold
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T15:00:00Z');

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(api.aggregateEvents).toHaveBeenCalledTimes(1);
    expect(api.listEvents).not.toHaveBeenCalled();
  });
```

**IMPORTANT:** Keep all other tests unchanged (`aggregateLiveMode_repolls_every5Seconds` etc.).

---

## Task 5: Add Playwright E2E test to `timeline-view.spec.ts`

**File:** `d:\Work\Tracer\tracer-viewer\tests\e2e\timeline-view.spec.ts`

Add the following test INSIDE the existing `test.describe('TimelineView E2E', () => {` block, after the last existing test (`autoFollow_KeepsLiveEdgeVisible`):

```typescript
  test('pan_ZoomFilter_CompleteUnder300ms', async ({ page }) => {
    await page.goto(TIMELINE_URL);
    await page.locator('canvas.timeline-canvas').waitFor({ state: 'visible' });

    // (a) Horizontal pan: measure URL update latency
    const canvas = page.locator('canvas.timeline-canvas');
    const box = await canvas.boundingBox();
    if (!box) throw new Error('Canvas bounding box not found');

    const t0pan = await page.evaluate(() => performance.now());
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 - 200, box.y + box.height / 2);
    await page.mouse.up();
    // Wait for URL to include from= param (debounced)
    await page.waitForURL(/from=/, { timeout: 500 }).catch(() => { /* ok if URL not updated in offline mode */ });
    const t1pan = await page.evaluate(() => performance.now());
    const panLatencyMs = t1pan - t0pan;
    console.log(`[perf] pan latency: ${panLatencyMs.toFixed(1)}ms`);
    expect(panLatencyMs).toBeLessThan(300);

    // (b) Filter via FilterPanel: measure network request + repaint latency
    const filterPanel = page.locator('.filter-panel');
    if (await filterPanel.isVisible()) {
      const topicInput = filterPanel.locator('.filter-panel__input');
      const t0filter = await page.evaluate(() => performance.now());
      await topicInput.fill('rt.topic.0');
      await filterPanel.locator('.filter-panel__add-btn').click();

      // Wait for the API request to /api/events to complete
      await page.waitForResponse(resp => resp.url().includes('/api/events'), { timeout: 500 })
        .catch(() => { /* offline bundle may serve from cache */ });
      const t1filter = await page.evaluate(() => performance.now());
      const filterLatencyMs = t1filter - t0filter;
      console.log(`[perf] filter latency: ${filterLatencyMs.toFixed(1)}ms`);
      expect(filterLatencyMs).toBeLessThan(300);
    }

    // (c) Click a marker: measure inspector visibility latency
    const t0click = await page.evaluate(() => performance.now());
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    // Wait for inspector to appear (may not appear if no marker at click position)
    await page.locator('.event-inspector').waitFor({ state: 'visible', timeout: 300 }).catch(() => {
      // No marker at this position — acceptable in unit E2E setup
    });
    const t1click = await page.evaluate(() => performance.now());
    const clickLatencyMs = t1click - t0click;
    console.log(`[perf] click-to-inspector latency: ${clickLatencyMs.toFixed(1)}ms`);
    expect(clickLatencyMs).toBeLessThan(300);

    // Canvas should still be visible after all interactions
    await expect(canvas).toBeVisible();
  });
```

---

## Verification

After making all changes, run:

```powershell
cd d:\Work\Tracer\tracer-viewer
npm run test:unit -- --reporter=verbose 2>&1 | Select-String -Pattern "(PASS|FAIL|✓|×|timelineRenderer|timelineLayout|timelineHitTest|useTimelineQuery|useTimelineUrl)" | Select-Object -First 80
```

Expected: All test files pass, and the new test names appear exactly as specified.

**Do NOT run** the Playwright E2E tests (they require a running dev server with live data).

---

## What to return in your report

1. Confirmation of each file modified
2. Summary of test name changes made
3. Output of the Vitest run showing all tests pass
4. Any issues encountered and how they were resolved
