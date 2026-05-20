# BATCH-33 Instructions — TRC-P6-009 + TRC-P6-010

**Tasks:**
- TRC-P6-009 — Cross-view navigation
- TRC-P6-010 — Shareable URL for causal view

**Expected results after this batch:**
- ~14 new tests (5 EventInspector + 2 backend + 6 useCausalTreeUrl + 1 router)
- 1 existing test updated (EventInspector causal-tree button test)
- Frontend: 145 + 13 = ~158 tests passing
- Backend: 349 + 2 = ~351 tests passing

---

## Context

- Workspace: `d:\Work\Tracer`
- Frontend tests: `cd d:\Work\Tracer\tracer-viewer ; npx vitest run`
- Backend tests: `cd d:\Work\Tracer ; dotnet test tests\Tracer.Tests.Unit -c Release --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"`
- Do NOT break existing tests

---

## PART 1 — Backend: Add SessionId to TraceTree (TRC-P6-009)

### 1.1 — `src/Tracer.WebApi/Queries/TraceTree.cs`

Add a non-required `SessionId` property with default `""` to `TraceTree`:

```csharp
public sealed record TraceTree
{
    public required ulong TraceId { get; init; }
    public required IReadOnlyList<TraceNode> Nodes { get; init; }
    public required IReadOnlyList<TraceEdge> Edges { get; init; }
    public required IReadOnlyList<TraceNode> Roots { get; init; }
    public required IReadOnlyList<TraceNode> Leaves { get; init; }
    public required TraceSummary Summary { get; init; }
    /// <summary>Session ID of the session whose time range contains the trace's first event. Empty when not resolvable.</summary>
    public string SessionId { get; init; } = string.Empty;
}
```

### 1.2 — `src/Tracer.WebApi/Queries/TraceQueryService.cs`

Add a private static helper `ResolveSessionId` and call it in all 4 public tree methods.

**Add this private static method** (at the bottom of the class, after `BuildSingletonTree`):

```csharp
/// <summary>
/// Returns the sessionId of the session whose <c>system.session_start</c> event
/// has the most recent timestamp at or before <paramref name="eventTime"/>.
/// Returns empty string when no matching session is found.
/// </summary>
private static string ResolveSessionId(
    PooledMultiIntervalConnection conn,
    DateTimeOffset? eventTime)
{
    if (eventTime is null) return string.Empty;

    var sql = conn.WithEventsCte("""
        SELECT json_extract_string(payload, '$.sessionId') as session_id
        FROM events
        WHERE topic = 'system.session_start'
          AND json_extract_string(payload, '$.sessionId') IS NOT NULL
          AND publish_wallclock <= $eventTime
        ORDER BY publish_wallclock DESC
        LIMIT 1
        """);

    using var cmd = conn.Connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.Add(new DuckDBParameter("eventTime", eventTime.Value.UtcDateTime));

    using var reader = cmd.ExecuteReader();
    if (reader.Read() && !reader.IsDBNull(0))
        return reader.GetString(0);

    return string.Empty;
}
```

**Update `GetTraceTreeAsync`** — after building the tree, resolve SessionId and return with it:

```csharp
var tree = BuildTree(events, truncated, traceId);
var sessionId = await Task.Run(
    () => ResolveSessionId(conn, tree.Summary.FirstEventUtc), ct);
return tree with { SessionId = sessionId };
```

**Update `GetTraceTreeForEventAsync`** — the method currently calls `GetTraceTreeAsync` which now returns session ID. The singleton path needs explicit resolution:

At the end of `GetTraceTreeForEventAsync`, change the singleton return:
```csharp
if (ev.TraceId.Value == 0)
{
    var singleton = BuildSingletonTree(ev);
    // For singleton trees, resolve session from the event's own time
    await using var conn2 = await _reader.AcquireAsync(ct);
    var singletonSessionId = await Task.Run(
        () => ResolveSessionId(conn2, singleton.Summary.FirstEventUtc), ct);
    return singleton with { SessionId = singletonSessionId };
}
return await GetTraceTreeAsync(ev.TraceId.Value, maxEvents, ct);
```

**Update `GetAncestorTreeAsync`** — after `BuildTree`:
```csharp
var tree = BuildTree(chain.ToList(), truncated: false, traceId);
var sessionId = await Task.Run(
    () => ResolveSessionId(conn, tree.Summary.FirstEventUtc), ct);
return tree with { SessionId = sessionId };
```

**Update `GetDescendantTreeAsync`** — after `BuildTree`:
```csharp
var truncated = descendants.Count >= maxNodes;
var tree = BuildTree(all, truncated, root.TraceId.Value);
var sessionId = await Task.Run(
    () => ResolveSessionId(conn, tree.Summary.FirstEventUtc), ct);
return tree with { SessionId = sessionId };
```

Also add the using for `PooledMultiIntervalConnection` at the top:
```csharp
using Tracer.Storage.DuckDB.MultiInterval;
```

But check if it's already there in the existing usings. If not, add it.

### 1.3 — `src/Tracer.WebApi/Contracts/Dto/TraceDtos.cs`

Add `SessionId` to `TraceTreeDto`:

```csharp
public sealed record TraceTreeDto
{
    public required string TraceId { get; init; }
    public required string SessionId { get; init; }
    public required IReadOnlyList<TraceNodeDto> Nodes { get; init; }
    public required IReadOnlyList<TraceEdgeDto> Edges { get; init; }
    public required IReadOnlyList<string> RootEventIds { get; init; }
    public required IReadOnlyList<string> LeafEventIds { get; init; }
    public required TraceSummaryDto Summary { get; init; }
}
```

### 1.4 — `src/Tracer.WebApi/Contracts/Mapping/TraceDtoMapper.cs`

Map `SessionId` in `Map(TraceTree tree)`:

```csharp
return new TraceTreeDto
{
    TraceId      = tree.TraceId.ToString("X16"),
    SessionId    = tree.SessionId,
    Nodes        = nodes,
    Edges        = edges,
    RootEventIds = rootIds,
    LeafEventIds = leafIds,
    Summary      = Map(tree.Summary),
};
```

---

## PART 2 — Backend tests (TRC-P6-009 SC6, SC7)

### 2.1 — Update `tests/Tracer.Tests.Unit/WebApi/TraceQueryServiceTests.cs`

Add this new test after the existing ones:

```csharp
[Fact]
public async Task GetTraceTree_SessionIdResolved_MatchesSessionContainingFirstEvent()
{
    // Arrange: push a session_start event, then trace events after it
    var sessionId = $"session-{_nextId++}";
    var traceId   = _nextId++;
    var rootId    = _nextId++;

    // Session start event at BaseTime - 10 seconds
    var sessionStart = new EventRecord
    {
        SequenceNumber   = _nextId++,
        PublishWallclock = At(BaseTime.AddSeconds(-10)),
        ReceiveWallclock = At(BaseTime.AddSeconds(-10)),
        PublisherNode    = new AgentId("system"),
        SubscriberNode   = new AgentId("system"),
        Topic            = new TopicName("system.session_start"),
        EventId          = new EventId(_nextId++),
        TraceId          = new TraceId(0),
        PayloadJson      = $"{{\"sessionId\":\"{sessionId}\",\"scenarioId\":\"Test\"}}",
    };

    var traceEvent = MakeEvent(rootId, traceId, 0, at: BaseTime);

    await _fixture.PushAsync([sessionStart]);
    await _fixture.PushAsync([traceEvent]);

    // Act
    var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 100, CancellationToken.None);

    // Assert
    tree.Should().NotBeNull();
    tree!.SessionId.Should().Be(sessionId);
    tree.Summary.FirstEventUtc.Should().NotBeNull();
}
```

### 2.2 — Update `tests/Tracer.Tests.Unit/WebApi/TraceDtoMapperTests.cs`

Add this test after the existing ones (add to the existing `MakeTree` helper + existing pattern):

```csharp
[Fact]
public void MapTraceTree_SessionIdPresentInDto()
{
    var ev = MakeEvent(1001, 2002);
    var tree = MakeTree(2002, ev);
    // Create tree with SessionId via 'with' expression
    var treeWithSession = tree with { SessionId = "my-session-xyz" };

    var dto = TraceDtoMapper.Map(treeWithSession);

    dto.SessionId.Should().Be("my-session-xyz");
}
```

Note: `MakeTree` in the existing tests creates a `TraceTree`. Since `SessionId` is non-required with default `""`, `MakeTree` doesn't need to change. The new test uses `tree with { SessionId = ... }`.

---

## PART 3 — Frontend: Update types and EventInspector (TRC-P6-009)

### 3.1 — `tracer-viewer/src/types/causalTree.ts`

Add `sessionId` to `TraceTreeDto`:

```typescript
export interface TraceTreeDto {
  traceId: string;
  sessionId: string;              // NEW — resolves to empty string when unresolvable
  nodes: TraceNodeDto[];
  edges: TraceEdgeDto[];
  rootEventIds: string[];
  leafEventIds: string[];
  summary: TraceSummaryDto;
}
```

### 3.2 — `tracer-viewer/src/components/EventInspector.vue`

Rewrite the component to support dual mode:
- **Store mode** (existing — no `event` prop): reads from `useTimelineStore`, fetches via API
- **Prop mode** (new — `event` prop provided): uses TraceNodeDto data directly

The component must:
1. Show "Show causal tree" button only when `showCausalTreePivot && traceId !== '0000000000000000'` (enabled, not disabled)
2. Show "Show in timeline" button only when `showTimelinePivot && resolvedSessionId`
3. In default mode (no props), match existing behavior: causal tree button ABSENT, timeline button absent
4. Keep all existing functionality: "Filter to this trace", "Show in scenario", "Copy event ID"

**IMPORTANT**: The existing test `eventInspector_showsCausalTree_buttonPresentButDisabled` MUST be updated since the behavior changes. The new behavior (with `showCausalTreePivot=false` default): causal tree button is ABSENT (not present-but-disabled).

Full component:

```vue
<!-- src/components/EventInspector.vue -->
<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';
import { api } from '@/api/tracerApiClient';
import type { EventDto as ApiEventDto } from '@/api/tracerApiClient';
import type { TraceNodeDto } from '@/types/causalTree';

const props = withDefaults(defineProps<{
  event?: TraceNodeDto | null;
  sessionId?: string | null;
  showCausalTreePivot?: boolean;
  showTimelinePivot?: boolean;
}>(), {
  event: null,
  sessionId: null,
  showCausalTreePivot: false,
  showTimelinePivot: false,
});

const store  = useTimelineStore();
const router = useRouter();

// Store mode: fetched event (used when no event prop)
const fetchedEvent = ref<ApiEventDto | null>(null);
const loading = ref(false);

// Detect which mode we're in
const isPropMode = computed(() => props.event !== null && props.event !== undefined);

// Resolved values: prefer prop, fall back to store
const resolvedTraceId = computed<string | null>(() => {
  if (isPropMode.value) return props.event!.traceId;
  return fetchedEvent.value?.traceId ?? null;
});

const resolvedSessionId = computed<string | null>(() => {
  if (props.sessionId) return props.sessionId;
  return store.sessionId ?? null;
});

// In store mode: watch selectedEventId and fetch event
watch(
  () => store.selectedEventId,
  async (id) => {
    if (isPropMode.value) return;
    if (!id) { fetchedEvent.value = null; return; }
    loading.value = true;
    try {
      fetchedEvent.value = await api.getEvent(id);
    } finally {
      loading.value = false;
    }
  },
  { immediate: true },
);

const displayEvent = computed(() => {
  if (isPropMode.value) return props.event;
  return fetchedEvent.value;
});

const visibleToUser = computed(() => {
  if (isPropMode.value) return true;
  return !!store.selectedEventId;
});

const prettyPayload = computed(() => {
  const payload = displayEvent.value?.payloadJson;
  if (!payload) return '';
  try { return JSON.stringify(JSON.parse(payload), null, 2); }
  catch { return payload; }
});

// Button visibility
const showCausalButton = computed(() =>
  props.showCausalTreePivot &&
  resolvedTraceId.value !== null &&
  resolvedTraceId.value !== '0000000000000000',
);

const showTimelineButton = computed(() =>
  props.showTimelinePivot && !!resolvedSessionId.value,
);

// Navigation handlers
function pivotToCausalTree() {
  const eventId = isPropMode.value
    ? props.event!.eventId
    : (store.selectedEventId ?? null);
  if (eventId) {
    void router.push({ name: 'causal-by-event', params: { eventId } });
  }
}

function pivotToTimeline() {
  if (!resolvedSessionId.value || !isPropMode.value || !props.event) return;
  const t = new Date(props.event.publishWallclock).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: resolvedSessionId.value },
    query: {
      from: new Date(t - 2000).toISOString(),
      to:   new Date(t + 2000).toISOString(),
      select: props.event.eventId,
    },
  });
}

function onFilterToTrace() {
  if (!resolvedTraceId.value) return;
  store.applyFilter({ traceId: resolvedTraceId.value });
}

function onShowInScenario() {
  const sId = resolvedSessionId.value;
  if (!sId) return;
  void router.push(`/scenario/${sId}`);
}

async function onCopyEventId() {
  const eventId = isPropMode.value
    ? props.event!.eventId
    : (fetchedEvent.value?.eventId ?? null);
  if (!eventId) return;
  await navigator.clipboard.writeText(eventId);
}
</script>

<template>
  <div
    v-if="visibleToUser"
    class="event-inspector"
  >
    <div
      v-if="loading && !isPropMode"
      class="event-inspector__loading"
    >
      Loading…
    </div>
    <template v-else-if="displayEvent">
      <div class="event-inspector__header">
        <span class="event-inspector__topic">{{ displayEvent.topic }}</span>
        <span class="event-inspector__node">{{ displayEvent.publisherNode }}</span>
      </div>

      <pre class="event-inspector__payload">{{ prettyPayload }}</pre>

      <div class="event-inspector__actions">
        <button
          class="event-inspector__action"
          @click="onFilterToTrace"
        >
          Filter to this trace
        </button>
        <button
          class="event-inspector__action"
          @click="onShowInScenario"
        >
          Show in scenario
        </button>
        <button
          v-if="showCausalButton"
          class="event-inspector__action"
          @click="pivotToCausalTree"
        >
          Show causal tree
        </button>
        <button
          v-if="showTimelineButton"
          class="event-inspector__action"
          @click="pivotToTimeline"
        >
          Show in timeline
        </button>
        <button
          class="event-inspector__action event-inspector__action--disabled"
          disabled
        >
          Show entity history
          <!-- TODO Phase 7: enable entity history navigation -->
        </button>
        <button
          class="event-inspector__action"
          @click="onCopyEventId"
        >
          Copy event ID
        </button>
      </div>
    </template>
    <div
      v-else
      class="event-inspector__not-found"
    >
      Event not found
    </div>
  </div>
</template>

<style scoped>
.event-inspector { border-left: 2px solid #1976d2; padding: 8px 12px; background: #fafafa; }
.event-inspector__header { display: flex; gap: 8px; margin-bottom: 8px; font-weight: 600; }
.event-inspector__payload { background: #f5f5f5; padding: 8px; border-radius: 4px; font-size: 0.75rem; overflow: auto; max-height: 300px; }
.event-inspector__actions { display: flex; flex-direction: column; gap: 4px; margin-top: 8px; }
.event-inspector__action { text-align: left; background: none; border: 1px solid #ccc; border-radius: 4px; padding: 4px 8px; cursor: pointer; }
.event-inspector__action:hover:not(:disabled) { background: #e3f2fd; }
.event-inspector__action--disabled { opacity: 0.5; cursor: not-allowed; }
</style>
```

### 3.3 — Update `tracer-viewer/tests/unit/EventInspector.spec.ts`

**Update existing test** `eventInspector_showsCausalTree_buttonPresentButDisabled`:

This test now verifies that in store mode (no `showCausalTreePivot` prop), the causal tree button is ABSENT (not present-but-disabled). Replace the test:

OLD test body to find and replace:
```javascript
it('eventInspector_showsCausalTree_buttonPresentButDisabled', async () => {
```

NEW test:
```javascript
it('eventInspector_showsCausalTree_buttonAbsent_WhenShowCausalTreePivotIsFalse', async () => {
  const fakeEvent: EventDto = {
    eventId: 'AABBCCDD',
    traceId: 'TRACE-1',
    occurredAtUtc: '2026-01-01T10:00:00Z',
    topic: 'weapons.fire',
    publisherNode: 'node-1',
  };
  mockGetEvent.mockResolvedValueOnce(fakeEvent);

  const wrapper = await mountComponent();
  const store = useTimelineStore();
  store.selectedEventId = 'AABBCCDD';
  await flushPromises();

  // showCausalTreePivot defaults to false — button should be absent
  const allBtns = wrapper.findAll('.event-inspector__action');
  const causalBtn = allBtns.find((b) => b.text().includes('causal'));
  expect(causalBtn).toBeUndefined();
});
```

**Add 5 new tests** for TRC-P6-009 SC1-5 (at the end of the describe block):

```javascript
// --- TRC-P6-009 prop-mode tests ---

function makeCausalNode(overrides: Partial<import('@/types/causalTree').TraceNodeDto> = {}) {
  return {
    eventId: 'aabbccddeeff0011',
    traceId: '1122334455667788',
    publishWallclock: '2026-01-01T10:00:00.000Z',
    publisherNode: 'alpha-node',
    topic: 'weapons.fire',
    ...overrides,
  } as import('@/types/causalTree').TraceNodeDto;
}

async function mountWithEvent(
  node: import('@/types/causalTree').TraceNodeDto,
  extraProps: Record<string, unknown> = {},
) {
  const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
  const pinia = createPinia();
  setActivePinia(pinia);
  const { mount } = await import('@vue/test-utils');
  return mount(EventInspector, {
    global: { plugins: [pinia] },
    props: { event: node, ...extraProps },
  });
}

it('showCausalTreeButton_HiddenWhenTraceIdIsZero', async () => {
  const node = makeCausalNode({ traceId: '0000000000000000' });
  const wrapper = await mountWithEvent(node, { showCausalTreePivot: true });

  const allBtns = wrapper.findAll('.event-inspector__action');
  const causalBtn = allBtns.find((b) => b.text().includes('causal'));
  expect(causalBtn).toBeUndefined();
});

it('showCausalTreeButton_VisibleAndNavigates_WhenTraceIdNonZero', async () => {
  const node = makeCausalNode({ traceId: '1122334455667788' });
  const wrapper = await mountWithEvent(node, { showCausalTreePivot: true });

  const allBtns = wrapper.findAll('.event-inspector__action');
  const causalBtn = allBtns.find((b) => b.text().includes('causal'));
  expect(causalBtn).toBeTruthy();
  expect(causalBtn!.attributes('disabled')).toBeUndefined();

  await causalBtn!.trigger('click');
  expect(mockRouterPush).toHaveBeenCalledWith({
    name: 'causal-by-event',
    params: { eventId: node.eventId },
  });
});

it('pivotToTimeline_PushesTimelineRouteWithSelectAndWindow', async () => {
  const node = makeCausalNode({ publishWallclock: '2026-06-01T12:00:00.000Z' });
  const wrapper = await mountWithEvent(node, {
    showTimelinePivot: true,
    sessionId: 'sess-abc',
  });

  const allBtns = wrapper.findAll('.event-inspector__action');
  const timelineBtn = allBtns.find((b) => b.text().includes('timeline'));
  expect(timelineBtn).toBeTruthy();

  await timelineBtn!.trigger('click');

  expect(mockRouterPush).toHaveBeenCalledWith(
    expect.objectContaining({
      name: 'timeline',
      params: { sessionId: 'sess-abc' },
      query: expect.objectContaining({ select: node.eventId }),
    }),
  );
});

it('pivotToScenario_PushesScenarioRouteWithSessionId', async () => {
  const node = makeCausalNode();
  const wrapper = await mountWithEvent(node, { sessionId: 'sess-xyz' });

  const allBtns = wrapper.findAll('.event-inspector__action');
  const scenarioBtn = allBtns.find((b) => b.text().includes('scenario'));
  expect(scenarioBtn).toBeTruthy();

  await scenarioBtn!.trigger('click');
  expect(mockRouterPush).toHaveBeenCalledWith('/scenario/sess-xyz');
});

it('showTimelinePivotFalse_HidesTimelineButton', async () => {
  const node = makeCausalNode();
  const wrapper = await mountWithEvent(node, {
    showTimelinePivot: false,
    sessionId: 'sess-abc',
  });

  const allBtns = wrapper.findAll('.event-inspector__action');
  const timelineBtn = allBtns.find((b) => b.text().includes('timeline'));
  expect(timelineBtn).toBeUndefined();
});
```

### 3.4 — Update `tracer-viewer/src/views/CausalTreeView.vue`

Replace `CausalNodeInspector` with `EventInspector` and add `useCausalTreeUrl()`:

Changes:
1. Replace `import CausalNodeInspector from '@/components/CausalNodeInspector.vue'` with `import EventInspector from '@/components/EventInspector.vue'`
2. Add `import { useCausalTreeUrl } from '@/composables/useCausalTreeUrl'`
3. Call `useCausalTreeUrl()` in setup (after `useCausalTreeQuery()`)
4. Replace `<CausalNodeInspector ... :event="selectedNode" />` with:
   ```vue
   <EventInspector
     v-if="selectedNode"
     class="causal-tree-view__inspector"
     :event="selectedNode"
     :session-id="store.tree?.sessionId ?? null"
     :show-causal-tree-pivot="false"
     :show-timeline-pivot="true"
   />
   ```

### 3.5 — Update `tracer-viewer/tests/unit/CausalTreeView.spec.ts`

Add mock for `useCausalTreeUrl` and change stub from `CausalNodeInspector` to `EventInspector`:

At the top of the file (after the existing `vi.mock` for `useCausalTreeQuery`), add:
```javascript
vi.mock('@/composables/useCausalTreeUrl', () => ({
  useCausalTreeUrl: vi.fn(),
}));
```

In the `mountView()` stubs, change `CausalNodeInspector: true` to `EventInspector: true`.

---

## PART 4 — Frontend: useCausalTreeUrl composable (TRC-P6-010)

### 4.1 — Create `tracer-viewer/src/composables/useCausalTreeUrl.ts`

```typescript
// src/composables/useCausalTreeUrl.ts
// Bidirectional URL ↔ causalTreeStore binding.
// URL → Store: route params parsed immediately on mount.
// Store → URL: selectedEventId written as ?select= with 250ms debounce via router.replace.

import { watch, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useCausalTreeStore } from '@/stores/causalTreeStore';

const URL_DEBOUNCE_MS = 250;

export function useCausalTreeUrl() {
  const store  = useCausalTreeStore();
  const route  = useRoute();
  const router = useRouter();

  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  // --- URL → Store (immediate on mount + on route change) ---
  function applyRouteToStore(
    name: string | symbol | null | undefined,
    params: Record<string, string | string[]>,
    query: Record<string, string | string[]>,
  ) {
    const get = (k: string): string | undefined => {
      const v = query[k];
      return Array.isArray(v) ? v[0] : v;
    };
    const num = (k: string): number | undefined => {
      const v = get(k);
      if (!v) return undefined;
      const n = parseInt(v, 10);
      return isNaN(n) ? undefined : n;
    };

    if (name === 'causal-by-event') {
      const eventId = Array.isArray(params['eventId']) ? params['eventId'][0] : params['eventId'];
      if (!eventId) return;
      const mode = get('mode');
      if (mode === 'ancestors') {
        store.openAncestors(eventId, num('maxDepth'));
      } else if (mode === 'descendants') {
        store.openDescendants(eventId, num('maxDepth'), num('maxNodes'));
      } else {
        store.openByEvent(eventId, num('maxEvents'));
      }
    } else if (name === 'causal-by-trace') {
      const traceId = Array.isArray(params['traceId']) ? params['traceId'][0] : params['traceId'];
      if (!traceId) return;
      store.openTrace(traceId, num('maxEvents'));
      const select = get('select');
      if (select) {
        store.selectedEventId = select;
      }
    }
  }

  // Apply immediately on mount
  applyRouteToStore(
    route.name,
    route.params as Record<string, string | string[]>,
    route.query as Record<string, string | string[]>,
  );

  // Watch for route navigation (back/forward, router.push)
  const stopRouteWatch = watch(
    () => ({ name: route.name, params: { ...route.params }, query: { ...route.query } }),
    ({ name, params, query }) => applyRouteToStore(name, params, query),
  );

  // --- Store → URL (debounced replace of ?select param) ---
  function scheduleSelectWrite() {
    if (debounceTimer !== null) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      if (!store.selectedEventId) return;
      void router.replace({
        query: { ...route.query, select: store.selectedEventId },
      });
    }, URL_DEBOUNCE_MS);
  }

  const stopSelectWatch = watch(
    () => store.selectedEventId,
    (id) => {
      if (id) scheduleSelectWrite();
    },
  );

  onUnmounted(() => {
    stopRouteWatch();
    stopSelectWatch();
    if (debounceTimer !== null) clearTimeout(debounceTimer);
  });
}
```

---

## PART 5 — Tests (TRC-P6-010)

### 5.1 — Create `tracer-viewer/tests/unit/useCausalTreeUrl.spec.ts`

```typescript
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { reactive, nextTick } from 'vue';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';

// Reactive mock route so watchers fire on changes
const mockRoute = reactive({
  name: '' as string | null,
  params: {} as Record<string, string>,
  query:  {} as Record<string, string>,
});

const mockReplace = vi.fn();
const mockPush    = vi.fn();

vi.mock('vue-router', () => ({
  useRoute:  vi.fn(() => mockRoute),
  useRouter: vi.fn(() => ({ replace: mockReplace, push: mockPush })),
}));

import { useCausalTreeUrl } from '../../src/composables/useCausalTreeUrl';

describe('useCausalTreeUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    mockReplace.mockReset();
    mockPush.mockReset();
    mockRoute.name   = null;
    mockRoute.params = {};
    mockRoute.query  = {};
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('causalByEvent_NoMode_CallsOpenByEvent', () => {
    const store = useCausalTreeStore();
    const openByEventSpy = vi.spyOn(store, 'openByEvent');

    mockRoute.name = 'causal-by-event';
    mockRoute.params = { eventId: 'aabbccddeeff0011' };

    useCausalTreeUrl();

    expect(openByEventSpy).toHaveBeenCalledWith('aabbccddeeff0011', undefined);
  });

  it('causalByEvent_ModeAncestors_CallsOpenAncestorsWithMaxDepth', () => {
    const store = useCausalTreeStore();
    const openAncestorsSpy = vi.spyOn(store, 'openAncestors');

    mockRoute.name = 'causal-by-event';
    mockRoute.params = { eventId: 'aabbccddeeff0011' };
    mockRoute.query = { mode: 'ancestors', maxDepth: '20' };

    useCausalTreeUrl();

    expect(openAncestorsSpy).toHaveBeenCalledWith('aabbccddeeff0011', 20);
  });

  it('causalByEvent_ModeDescendants_CallsOpenDescendantsWithParsedParams', () => {
    const store = useCausalTreeStore();
    const openDescendantsSpy = vi.spyOn(store, 'openDescendants');

    mockRoute.name = 'causal-by-event';
    mockRoute.params = { eventId: 'aabbccddeeff0011' };
    mockRoute.query = { mode: 'descendants', maxDepth: '15', maxNodes: '300' };

    useCausalTreeUrl();

    expect(openDescendantsSpy).toHaveBeenCalledWith('aabbccddeeff0011', 15, 300);
  });

  it('causalByTrace_CallsOpenTrace', () => {
    const store = useCausalTreeStore();
    const openTraceSpy = vi.spyOn(store, 'openTrace');

    mockRoute.name = 'causal-by-trace';
    mockRoute.params = { traceId: '1122334455667788' };

    useCausalTreeUrl();

    expect(openTraceSpy).toHaveBeenCalledWith('1122334455667788', undefined);
  });

  it('causalByTrace_WithSelectParam_SetsSelectedEventId', async () => {
    const store = useCausalTreeStore();

    mockRoute.name = 'causal-by-trace';
    mockRoute.params = { traceId: '1122334455667788' };
    mockRoute.query = { select: 'ffff000011112222' };

    useCausalTreeUrl();
    await nextTick();

    expect(store.selectedEventId).toBe('ffff000011112222');
  });

  it('selectEventId_WritesSelectQueryParamViaRouterReplace', async () => {
    const store = useCausalTreeStore();
    mockRoute.name = null; // no route match yet

    useCausalTreeUrl();

    store.selectedEventId = 'ffff000011112222';
    await nextTick();

    // Before debounce: no call
    expect(mockReplace).not.toHaveBeenCalled();

    // Advance past debounce
    await vi.advanceTimersByTimeAsync(300);

    expect(mockReplace).toHaveBeenCalledTimes(1);
    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query.select).toBe('ffff000011112222');
  });
});
```

### 5.2 — Create `tracer-viewer/tests/unit/router.spec.ts`

```typescript
import { describe, it, expect } from 'vitest';
import router from '../../src/router/index';

describe('router', () => {
  it('causalByEventRoute_IsLazyLoaded', () => {
    const route = router.getRoutes().find(r => r.name === 'causal-by-event');
    expect(route).toBeDefined();
    // Component should be a function (dynamic import) not a static component object
    expect(typeof route!.components?.default).toBe('function');
  });
});
```

---

## PART 6 — Build verification

After implementing everything, run both test suites:

```powershell
# Frontend
cd d:\Work\Tracer\tracer-viewer ; npx vitest run

# Backend
cd d:\Work\Tracer ; dotnet test tests\Tracer.Tests.Unit -c Release --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```

Expected:
- Frontend: ~158 tests, 0 failures
- Backend: ~351 tests, 0 failures

---

## Critical notes

1. **`TraceTree.SessionId` is non-required**: Existing tests that create `TraceTree` via object initializer (like in `TraceDtoMapperTests.MakeTree`) do NOT need to set `SessionId` — it defaults to `""`.

2. **`TraceTreeDto.SessionId` IS required**: The mapper MUST set it. Any existing test that creates `TraceTreeDto` directly will need to add `SessionId = ""` if it uses object initializer. Check if any existing tests do this.

3. **`EventInspector.vue` existing tests**: The 7 existing store-mode tests must still pass. The test `eventInspector_showsCausalTree_buttonPresentButDisabled` must be renamed and updated as shown above. Also note that in the new implementation, `visibleToUser` returns `true` when `isPropMode = true` (even without a `store.selectedEventId`), so the first existing test `eventInspector_noSelectedEvent_rendersNothing` should still pass because it mounts without the `event` prop (store mode) and doesn't set `store.selectedEventId`.

4. **`useCausalTreeUrl.ts` does NOT call `useCausalTreeQuery`** — that's a separate composable. The URL composable only dispatches store actions and writes the select param to URL.

5. **`CausalTreeView.spec.ts` stubs**: After adding `useCausalTreeUrl` import to the view, the tests will fail unless `useCausalTreeUrl` is mocked. Add `vi.mock('@/composables/useCausalTreeUrl', ...)` right after the existing mock for `useCausalTreeQuery`.

6. **Router test**: The router already has `causal-by-event` and `causal-by-trace` routes (added in BATCH-32). The test just verifies lazy loading.

7. **`TraceQueryServiceTests.GetTraceTree_SessionIdResolved` test**: The `sessionStart` event's `PayloadJson` must contain the `sessionId` key. The `eventTime` parameter in the `DuckDBParameter` takes a `DateTime` (UTC). Use `eventTime.Value.UtcDateTime`.

8. **`DuckDBParameter` for DateTime**: In the ResolveSessionId method, pass `eventTime.Value.UtcDateTime` (not DateTimeOffset) as the parameter value, to match how DuckDB stores TIMESTAMP_NS values. Check consistency with how `publish_wallclock` comparison works in other queries.

9. **Compilation**: After adding `ResolveSessionId` to `TraceQueryService.cs`, ensure `using Tracer.Storage.DuckDB.MultiInterval;` is present in the file (the `PooledMultiIntervalConnection` type). Check the existing usings.

---

## Report back with:
- Test results (frontend counts + backend counts)
- Complete list of files created/modified
- Any deviations from instructions
