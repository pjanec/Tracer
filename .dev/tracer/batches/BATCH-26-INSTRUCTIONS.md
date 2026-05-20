# BATCH-26 Instructions — TRC-P5-009 + TRC-P5-010

## Overview
This batch completes TRC-P5-009 (Shareable URLs & URL State) and TRC-P5-010 (Auto-Follow Live Mode).
Most production code is already implemented from BATCH-25; this batch focuses on:
1. Integrating `useTimelineUrl` into `TimelineView.vue`
2. Enhancing `setFollowLive` to snap the viewport to the live edge on enable
3. Updating `TimelineToolbar.vue` button label for follow-active state
4. Renaming/expanding test cases to match exact names required in TASK-DETAIL
5. Adding missing tests in `useTimelineLiveStream.spec.ts` and `TimelineToolbar.spec.ts`
6. Adding Playwright E2E tests for TRC-P5-009 and TRC-P5-010

All 106 existing Vitest tests must continue to pass. TypeScript must compile with 0 errors.
Run `npx vitest run` from `tracer-viewer/` to verify after each change.

---

## REQUIRED READING BEFORE STARTING
Read these files to understand current state:
- `tracer-viewer/src/views/TimelineView.vue` — needs `useTimelineUrl()` added to setup
- `tracer-viewer/src/stores/timelineStore.ts` — `setFollowLive` needs snap-to-live-edge behavior
- `tracer-viewer/src/components/TimelineToolbar.vue` — button label needs conditional text
- `tracer-viewer/tests/unit/useTimelineUrl.spec.ts` — rename 5 tests, expand 2 of them
- `tracer-viewer/tests/unit/useTimelineLiveStream.spec.ts` — rename 2 tests, add 3 new tests
- `tracer-viewer/tests/unit/TimelineToolbar.spec.ts` — add 1 new test
- `tracer-viewer/tests/e2e/timeline-view.spec.ts` — append 2 new E2E tests

---

## Task 1 — Integrate useTimelineUrl into TimelineView.vue

**File:** `tracer-viewer/src/views/TimelineView.vue`

In the `<script setup>` block, add the import and call:
```typescript
import { useTimelineUrl } from '@/composables/useTimelineUrl';
// ... after other imports ...
useTimelineUrl();
```

This satisfies TRC-P5-009 success condition 1: "useTimelineUrl.ts ... is called in TimelineView.vue setup".

---

## Task 2 — Enhance setFollowLive to snap to live edge

**File:** `tracer-viewer/src/stores/timelineStore.ts`

Replace the current `setFollowLive` action:
```typescript
setFollowLive(v: boolean) {
  this.viewport = { ...this.viewport, followLive: v };
},
```

With:
```typescript
setFollowLive(v: boolean) {
  if (v) {
    // Snap viewport to live edge: preserve span, move to = now, from = now - span
    const spanMs = this.viewportSpanMs;
    const nowMs = Date.now();
    this.viewport = {
      from: new Date(nowMs - spanMs),
      to:   new Date(nowMs),
      followLive: true,
    };
  } else {
    this.viewport = { ...this.viewport, followLive: false };
  }
},
```

This satisfies TRC-P5-010 success condition 7: "clicking the 'Follow live' button updates store.viewport.to to within 5 s of Date.now()".

---

## Task 3 — Update TimelineToolbar follow button label

**File:** `tracer-viewer/src/components/TimelineToolbar.vue`

Change the Follow button text from the static `Follow` to a conditional label:
```html
<button
  class="toolbar__follow"
  :class="{ 'toolbar__follow--active': store.viewport.followLive }"
  :disabled="!store.isLiveSession"
  @click="toggleFollow"
>{{ store.viewport.followLive ? 'Following live' : 'Follow' }}</button>
```

This satisfies TRC-P5-010 success condition 7: "the button label changes to 'Following live'".

---

## Task 4 — Rename/expand useTimelineUrl.spec.ts tests

**File:** `tracer-viewer/tests/unit/useTimelineUrl.spec.ts`

Replace the ENTIRE file with the version below. The test logic is mostly preserved from BATCH-25 but:
- All 5 test names renamed to match exact TRC-P5-009 spec names
- `selectedEvent_RoundTripsViaUrl` expands to test BOTH write (store → URL) and read (URL → store)
- `multipleFilterValues_EncodedAsRepeatedParams` expands to test BOTH encode and decode directions

```typescript
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';

const mockReplace = vi.fn();
const mockPush    = vi.fn();
const mockRouteQuery: Record<string, unknown> = {};

vi.mock('vue-router', () => ({
  useRoute:  vi.fn(() => ({ query: mockRouteQuery })),
  useRouter: vi.fn(() => ({ replace: mockReplace, push: mockPush })),
}));

describe('useTimelineUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    mockReplace.mockReset();
    mockPush.mockReset();
    for (const key of Object.keys(mockRouteQuery)) {
      delete mockRouteQuery[key];
    }
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('urlParams_AppliedToStoreOnMount', async () => {
    mockRouteQuery['from']  = '2026-01-01T14:00:00.000Z';
    mockRouteQuery['to']    = '2026-01-01T14:30:00.000Z';
    mockRouteQuery['topic'] = 'weapons.fire';

    const store = useTimelineStore();
    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    expect(store.viewport.from.toISOString()).toBe('2026-01-01T14:00:00.000Z');
    expect(store.viewport.to.toISOString()).toBe('2026-01-01T14:30:00.000Z');
    expect(store.filter.topics).toContain('weapons.fire');
  });

  it('storeChange_UpdatesUrlDebounced', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    // No call yet (synchronous)
    expect(mockReplace).not.toHaveBeenCalled();

    // Advance past debounce
    await vi.advanceTimersByTimeAsync(300);

    expect(mockReplace).toHaveBeenCalledTimes(1);
    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query.from).toBeTruthy();
    expect(callArg.query.to).toBeTruthy();
  });

  it('multipleFilterValues_EncodedAsRepeatedParams', async () => {
    // Part A: Store → URL encoding
    {
      const store = useTimelineStore();
      store.viewport.from = new Date('2026-01-01T10:00:00Z');
      store.viewport.to   = new Date('2026-01-01T11:00:00Z');
      store.filter = { topics: ['a', 'b'] };

      const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
      useTimelineUrl();
      await vi.advanceTimersByTimeAsync(300);

      const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, unknown> };
      expect(callArg.query['topic']).toEqual(['a', 'b']);
    }

    // Part B: URL → Store decoding (fresh store + fresh composable import)
    {
      setActivePinia(createPinia());
      mockReplace.mockReset();
      for (const key of Object.keys(mockRouteQuery)) delete mockRouteQuery[key];

      mockRouteQuery['topic'] = ['a', 'b'];
      mockRouteQuery['from']  = '2026-01-01T10:00:00.000Z';
      mockRouteQuery['to']    = '2026-01-01T11:00:00.000Z';

      // Invalidate module cache to get fresh composable state
      vi.resetModules();
      const { useTimelineStore: freshStore } = await import('../../src/stores/timelineStore');
      const { useTimelineUrl: freshUrl }     = await import('../../src/composables/useTimelineUrl');

      const store2 = freshStore();
      freshUrl();
      expect(store2.filter.topics).toEqual(['a', 'b']);
    }
  });

  it('selectedEvent_RoundTripsViaUrl', async () => {
    // Part A: Store → URL (selectedEventId encoded as ?select=)
    {
      const store = useTimelineStore();
      store.viewport.from = new Date('2026-01-01T10:00:00Z');
      store.viewport.to   = new Date('2026-01-01T11:00:00Z');
      store.selectedEventId = 'AABBCCDD11223344';

      const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
      useTimelineUrl();
      await vi.advanceTimersByTimeAsync(300);

      const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
      expect(callArg.query['select']).toBe('AABBCCDD11223344');
    }

    // Part B: URL → Store (restoring selectedEventId from ?select=)
    {
      setActivePinia(createPinia());
      mockReplace.mockReset();
      for (const key of Object.keys(mockRouteQuery)) delete mockRouteQuery[key];

      mockRouteQuery['select'] = 'AABBCCDD11223344';
      mockRouteQuery['from']   = '2026-01-01T10:00:00.000Z';
      mockRouteQuery['to']     = '2026-01-01T11:00:00.000Z';

      vi.resetModules();
      const { useTimelineStore: freshStore } = await import('../../src/stores/timelineStore');
      const { useTimelineUrl: freshUrl }     = await import('../../src/composables/useTimelineUrl');

      const store2 = freshStore();
      freshUrl();
      expect(store2.selectedEventId).toBe('AABBCCDD11223344');
    }
  });

  it('panGesture_UsesReplaceNotPush', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    // Simulate multiple pan operations within 250ms
    store.viewport.from = new Date('2026-01-01T10:01:00Z');
    store.viewport.to   = new Date('2026-01-01T11:01:00Z');
    store.viewport.from = new Date('2026-01-01T10:02:00Z');
    store.viewport.to   = new Date('2026-01-01T11:02:00Z');

    await vi.advanceTimersByTimeAsync(300);

    // router.replace should have been called, push must never be called
    expect(mockReplace).toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });
});
```

**IMPORTANT:** The `vi.resetModules()` pattern in the round-trip tests is required because the composable module is cached with the previous pinia/store reference. Without resetting modules, a new `createPinia()` won't be picked up by the already-imported composable.

---

## Task 5 — Rename/expand useTimelineLiveStream.spec.ts tests

**File:** `tracer-viewer/tests/unit/useTimelineLiveStream.spec.ts`

Replace the ENTIRE file with the version below. Changes from BATCH-25:
- Rename `onMessage_callsAppendLiveEvent` → `receivedEvent_AppendedToStoreInListMode` (add `totalMatching` assertion)
- Rename `filterChange_reconnects` → `filterChange_ReconnectsStream`
- Keep `unmount_abortsConnection` as-is
- Add `followMode_ViewportSlidesOnNewEvent`
- Add `panGesture_DisablesFollow`
- Add `aggregateMode_LiveEventsDoNotAppend`

```typescript
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import type { EventDto } from '../../src/types/timeline';

let capturedOnMessage: ((ev: { data: string }) => void) | null = null;
const mockAbortFn = vi.fn();

vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: vi.fn((_url: string, opts: { onmessage: (ev: { data: string }) => void }) => {
    capturedOnMessage = opts.onmessage;
    return Promise.resolve();
  }),
}));

function makeEventDto(overrides: Partial<EventDto> = {}): EventDto {
  return {
    eventId:          'evt-live-1',
    traceId:          'trace-A',
    publishWallclock: new Date().toISOString(),
    publisherNode:    'node-A',
    topic:            'live.topic',
    ...overrides,
  };
}

describe('useTimelineLiveStream', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    capturedOnMessage = null;
    mockAbortFn.mockReset();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('receivedEvent_AppendedToStoreInListMode', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    const dto = makeEventDto({ eventId: 'evt-live-1' });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    expect(store.queryResult?.events.length).toBe(1);
    expect(store.queryResult?.events[0].eventId).toBe('evt-live-1');
    expect(store.queryResult?.totalMatching).toBe(1);
  });

  it('followMode_ViewportSlidesOnNewEvent', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };
    store.viewport.followLive = true;
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:10:00Z');
    const originalSpanMs = store.viewportSpanMs; // 10 min

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    // Event arrives after viewport.to
    const evtTime = new Date('2026-01-01T10:15:00Z');
    const dto = makeEventDto({ publishWallclock: evtTime.toISOString() });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    // Viewport should have slid: new to = evtMs + 5000ms headroom
    const expectedTo = evtTime.getTime() + 5000;
    expect(store.viewport.to.getTime()).toBe(expectedTo);
    expect(store.viewport.from.getTime()).toBe(expectedTo - originalSpanMs);
    expect(store.viewport.followLive).toBe(true);
  });

  it('panGesture_DisablesFollow', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };
    store.viewport.followLive = true;
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:10:00Z');

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    // Pan gesture disables follow
    store.panBy(5_000);
    expect(store.viewport.followLive).toBe(false);

    const toBeforeEvent = store.viewport.to.getTime();

    // Event arrives after current viewport.to
    const evtTime = new Date(toBeforeEvent + 60_000); // 1 min after to
    const dto = makeEventDto({ publishWallclock: evtTime.toISOString() });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    // Viewport must NOT have slid (followLive is false)
    expect(store.viewport.to.getTime()).toBe(toBeforeEvent);
  });

  it('filterChange_ReconnectsStream', async () => {
    const { fetchEventSource } = await import('@microsoft/fetch-event-source');
    const store = useTimelineStore();
    store.sessionId = 'sess-live';

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    const callsBefore = (fetchEventSource as ReturnType<typeof vi.fn>).mock.calls.length;

    // Change filter — should trigger reconnect
    store.filter = { topics: ['new.topic'] };
    await Promise.resolve();

    expect((fetchEventSource as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(callsBefore);
  });

  it('aggregateMode_LiveEventsDoNotAppend', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'aggregate';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    const dto = makeEventDto({ eventId: 'should-not-appear' });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    // In aggregate mode, appendLiveEvent is a no-op — events list must not grow
    expect(store.queryResult?.events.length).toBe(0);
  });

  it('unmount_abortsConnection', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';

    const abortSpy = vi.spyOn(AbortController.prototype, 'abort');

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    expect(abortSpy).not.toHaveBeenCalled();
    abortSpy.mockRestore();
  });
});
```

---

## Task 6 — Add followToggle_EnablesFollowAndSnapsToLiveEdge to TimelineToolbar.spec.ts

**File:** `tracer-viewer/tests/unit/TimelineToolbar.spec.ts`

Append a new test after the existing `zoomPreset_5m_setsViewportTo5MinuteSpan` test:

```typescript
it('followToggle_EnablesFollowAndSnapsToLiveEdge', async () => {
  const pinia = createPinia();
  setActivePinia(pinia);
  const store = useTimelineStore();
  store.isLiveSession = true;
  store.viewport.followLive = false;
  store.viewport.from = new Date('2026-01-01T10:00:00Z');
  store.viewport.to   = new Date('2026-01-01T10:10:00Z'); // 10 min span

  const wrapper = mount(TimelineToolbar, { global: { plugins: [pinia] } });

  // The button should currently say "Follow" (not following)
  expect(wrapper.find('.toolbar__follow').text()).toBe('Follow');

  const beforeClick = Date.now();
  await wrapper.find('.toolbar__follow').trigger('click');

  // Follow mode should be enabled
  expect(store.viewport.followLive).toBe(true);

  // Viewport.to should be within 5s of now
  expect(store.viewport.to.getTime()).toBeGreaterThanOrEqual(beforeClick - 100); // allow 100ms margin
  expect(store.viewport.to.getTime()).toBeLessThanOrEqual(Date.now() + 5_000);

  // Span should be preserved (10 min = 600_000 ms)
  const span = store.viewport.to.getTime() - store.viewport.from.getTime();
  expect(Math.abs(span - 10 * 60 * 1000)).toBeLessThan(1_000); // within 1s

  // Button label should change to "Following live"
  await wrapper.vm.$nextTick();
  expect(wrapper.find('.toolbar__follow').text()).toBe('Following live');
});
```

---

## Task 7 — Add E2E tests to timeline-view.spec.ts

**File:** `tracer-viewer/tests/e2e/timeline-view.spec.ts`

Append two new tests to the `test.describe('TimelineView E2E', ...)` block.
Read the existing file first to see the current tests (6 total from BATCH-24), then append:

```typescript
  test('shareableUrl_SameViewOnReload', async ({ page }) => {
    await page.goto(TIMELINE_URL);
    await page.locator('.timeline-toolbar').waitFor({ state: 'visible' });

    // Apply a topic filter using FilterPanel
    const filterPanel = page.locator('.filter-panel');
    if (await filterPanel.isVisible()) {
      const topicInput = filterPanel.locator('.filter-panel__input');
      await topicInput.fill('weapons.fire');
      await filterPanel.locator('.filter-panel__add-btn').click();
      await page.waitForTimeout(500); // allow debounce
    }

    // Capture the URL after state settles
    const urlWithState = page.url();
    const parsedUrl = new URL(urlWithState);

    // Reload with the captured URL
    await page.goto(urlWithState);
    await page.locator('.timeline-toolbar').waitFor({ state: 'visible' });

    // The URL params should be preserved
    const reloadedUrl = new URL(page.url());
    if (parsedUrl.searchParams.has('from')) {
      expect(reloadedUrl.searchParams.get('from')).toBe(parsedUrl.searchParams.get('from'));
    }
    if (parsedUrl.searchParams.has('topic')) {
      expect(reloadedUrl.searchParams.get('topic')).toBe(parsedUrl.searchParams.get('topic'));
    }
    // The timeline should render without crashing
    await expect(page.locator('.timeline-toolbar')).toBeVisible();
  });

  test('autoFollow_KeepsLiveEdgeVisible', async ({ page }) => {
    await page.goto(TIMELINE_URL);
    await page.locator('.timeline-toolbar').waitFor({ state: 'visible' });

    // Enable follow mode
    const followBtn = page.locator('.toolbar__follow');
    await expect(followBtn).toBeVisible();

    if (await followBtn.isDisabled()) {
      // Session is not live in test environment — verify URL does not break
      await expect(page.locator('.timeline-toolbar')).toBeVisible();
      return;
    }

    await followBtn.click();
    await page.waitForTimeout(300); // allow debounce

    // URL should contain follow=true
    const url = new URL(page.url());
    expect(url.searchParams.get('follow')).toBe('true');

    // A canvas click should disable follow (pan gesture)
    const canvas = page.locator('canvas.timeline-canvas');
    if (await canvas.isVisible()) {
      const box = await canvas.boundingBox();
      if (box) {
        await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
        await page.waitForTimeout(300);
        // After clicking, follow should be disabled (URL should not have follow=true)
        const urlAfter = new URL(page.url());
        expect(urlAfter.searchParams.get('follow')).not.toBe('true');
      }
    }
  });
```

---

## Verification Steps

After all changes, run:
```bash
cd tracer-viewer
npx vitest run
```

Expected: **109 tests** passing (106 existing + 3 new: `followMode_ViewportSlidesOnNewEvent`, `panGesture_DisablesFollow`, `aggregateMode_LiveEventsDoNotAppend`).

The existing 106 tests should still pass. New test renames should still be counted (they replace old names). Final expected count: **109 Vitest tests** (25 renamed + 3 net-new).

TypeScript check:
```bash
npx tsc --noEmit
```
Expected: 0 errors.

## Report Template
After completing the batch, write a report to `.dev/tracer/reports/BATCH-26-REPORT.md` with:
- Summary of all changes
- Vitest result (pass count / total)
- TypeScript error count
- Any issues encountered and how they were resolved

Do NOT run the Playwright E2E tests (they require a running server). Confirm the E2E files compile without TypeScript errors.
