# BATCH-40 Instructions — Phase 7: Cross-View Navigation Pivots + Entity Picker (TRC-P7-018, TRC-P7-019)

**Target:** Coder Sub-agent  
**Batch:** BATCH-40  
**Tasks:** TRC-P7-018, TRC-P7-019  
**Design reference:** `docs/tracer_phase7_design.md` §11.3, `docs/TASK-DETAIL.md` §TRC-P7-018, §TRC-P7-019  
**Report path:** `.dev/tracer/reports/BATCH-40-REPORT.md` (at workspace root, NOT inside `tracer-viewer\`)

---

## 1. Onboarding

**Read before starting (in order):**
1. `docs/TASK-DETAIL.md` — sections TRC-P7-018 and TRC-P7-019
2. `docs/tracer_phase7_design.md` §11.3 (Cross-view pivots), §11.5 (EntityPickerView)
3. `.dev/tracer/reports/BATCH-39-REPORT.md` — recent context
4. `tracer-viewer/src/components/EventInspector.vue` — current props + buttons
5. `tracer-viewer/src/views/CausalTreeView.vue` — how EventInspector is used
6. `tracer-viewer/src/views/EntityHistoryView.vue` — where to add pivot buttons
7. `tracer-viewer/src/stores/entityHistoryStore.ts` — `entityId`, `sessionId`, `events`, `selectedEventId`
8. `tracer-viewer/src/views/SessionBrowserView.vue` — session list
9. `tracer-viewer/src/components/SessionCard.vue` — session card component
10. `tracer-viewer/src/router/index.ts` — existing named routes
11. `tracer-viewer/src/api/tracerApiClient.ts` — `EntityListDto`, `getEntityList` method
12. `tracer-viewer/tests/unit/EventInspector.spec.ts` (if it exists) — existing inspector tests

**Key existing types:**
```typescript
// TraceNodeDto (from @/types/causalTree) — used in CausalTreeView EventInspector
// Has: eventId, traceId, entityId?: string, publishWallclock, topic, ...

// EntityEventDto (from tracerApiClient.ts)
// Has: eventId, traceId: string, occurredAtUtc, topic, publisherNode
// traceId === '0' means no trace

// SlowStateSampleDto (from tracerApiClient.ts)
// Has: topic, occurredAtUtc, payloadJson, traceId?: string

// EntityListDto: { entities: EntitySummaryDto[], count: number }
```

**Existing named routes:** `'sessions'`, `'scenario'`, `'timeline'`, `'causal-by-trace'`, `'causal-by-event'`, `'entity-history'`

---

## 2. Task 1 — TRC-P7-018: Cross-View Navigation Pivots

### 2.1 Modify `EventInspector.vue`

**Current props:**
```typescript
showCausalTreePivot?: boolean;
showTimelinePivot?: boolean;
```

**Add:**
```typescript
showEntityHistoryPivot?: boolean;
```

**Add button in template** (replace the disabled stub button):
```html
<!-- Remove this disabled stub button: -->
<!-- <button class="event-inspector__action event-inspector__action--disabled" disabled>
  Show entity history
</button> -->

<!-- Replace with: -->
<button
  v-if="showEntityHistoryButton"
  class="event-inspector__action"
  @click="pivotToEntityHistory"
>
  Show entity history
</button>
```

**Add computed:**
```typescript
const showEntityHistoryButton = computed(() =>
  props.showEntityHistoryPivot &&
  !!displayEvent.value?.entityId &&
  !!resolvedSessionId.value,
);
```

Note: `displayEvent.value` may be `TraceNodeDto | ApiEventDto | null`. Only `TraceNodeDto` has `entityId`. TypeScript will complain — use a type assertion or a helper:
```typescript
function getEntityId(event: unknown): string | null {
  if (typeof event === 'object' && event !== null && 'entityId' in event) {
    const v = (event as Record<string, unknown>)['entityId'];
    return typeof v === 'string' && v ? v : null;
  }
  return null;
}

const showEntityHistoryButton = computed(() =>
  props.showEntityHistoryPivot &&
  !!getEntityId(displayEvent.value) &&
  !!resolvedSessionId.value,
);
```

**Add handler:**
```typescript
function pivotToEntityHistory() {
  const entityId = getEntityId(displayEvent.value);
  if (!entityId || !resolvedSessionId.value) return;
  void router.push({
    name: 'entity-history',
    params: { entityId },
    query: { session: resolvedSessionId.value },
  });
}
```

### 2.2 Modify `CausalTreeView.vue`

Enable the entity history pivot in the EventInspector:
```html
<EventInspector
  v-if="selectedNode"
  class="causal-tree-view__inspector"
  :event="selectedNode"
  :session-id="store.tree?.sessionId ?? null"
  :show-causal-tree-pivot="false"
  :show-timeline-pivot="true"
  :show-entity-history-pivot="true"
/>
```

The button will only show when `selectedNode.entityId` is non-null (checked inside EventInspector).

### 2.3 Modify `EntityHistoryView.vue`

Add pivot buttons for the selected entity event. When `store.selectedEventId` is set, show action buttons to navigate to Timeline and Causal Tree.

**Add to `<script setup>`:**
```typescript
import { computed } from 'vue';
import { useRouter } from 'vue-router';

const router = useRouter();

// Find the full event object for the currently selected event
const selectedEvent = computed(() => {
  if (!store.selectedEventId || !store.events) return null;
  return store.events.events.find(e => e.eventId === store.selectedEventId) ?? null;
});

function pivotToTimeline() {
  const ev = selectedEvent.value;
  if (!ev || !store.sessionId) return;
  const t = new Date(ev.occurredAtUtc).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: store.sessionId },
    query: {
      from: new Date(t - 2000).toISOString(),
      to: new Date(t + 2000).toISOString(),
      select: ev.eventId,
    },
  });
}

function pivotToCausalTree() {
  const ev = selectedEvent.value;
  if (!ev || !ev.traceId || ev.traceId === '0') return;
  void router.push({ name: 'causal-by-event', params: { eventId: ev.eventId } });
}

const canPivotToCausal = computed(() =>
  !!selectedEvent.value?.traceId && selectedEvent.value.traceId !== '0',
);
```

**Add to template** (after `<EntityEventStrip>`, before `<FastStateDrillDown>`):
```html
<!-- Event pivot actions -->
<div v-if="selectedEvent" class="entity-history-view__pivot-actions">
  <button class="entity-history-view__pivot-btn" @click="pivotToTimeline">
    Show in timeline
  </button>
  <button
    class="entity-history-view__pivot-btn"
    :disabled="!canPivotToCausal"
    :class="{ 'entity-history-view__pivot-btn--disabled': !canPivotToCausal }"
    @click="pivotToCausalTree"
  >
    Show causal tree
  </button>
</div>
```

### 2.4 Tests

**Add to existing `tracer-viewer/tests/unit/EventInspector.spec.ts`** (or create if missing):

Satisfy TRC-P7-018 SC-1..3:
1. `entityHistoryButton_VisibleWhenEntityIdPresent` — mount with `event = { ..., entityId: 'e1' }` and `showEntityHistoryPivot = true`. Assert: entity history button rendered.
2. `entityHistoryButton_AbsentWhenEntityIdNull` — mount with `event = { ..., entityId: null }` and `showEntityHistoryPivot = true`. Assert: no entity history button.
3. `pivotToEntityHistory_NavigatesToEntityHistoryView` — click entity history button. Assert: `router.push` called with `{ name: 'entity-history', params: { entityId: 'e1' }, query: { session: ... } }`.

**Add to existing `tracer-viewer/tests/unit/entityHistoryView.spec.ts`**:

Satisfy TRC-P7-018 SC-4..7:
4. `showInTimeline_NavigatesWithCorrectRoute` — set `store.events` with one event at t=10000ms, `store.selectedEventId = event.eventId`. Assert: clicking "Show in timeline" pushes `{ name: 'timeline', params: { sessionId }, query: { from: t-2000ms ISO, to: t+2000ms ISO, select: eventId } }`.
5. `showCausalTree_VisibleWhenTraceIdNonZero` — event with `traceId = '42abc...'` (non-zero). Assert: "Show causal tree" button enabled; clicking navigates to `{ name: 'causal-by-event', params: { eventId } }`.
6. `showCausalTree_DisabledWhenTraceIdIsZero` — event with `traceId = '0'`. Assert: "Show causal tree" button disabled.
7. `slowStateClickWithZeroTraceId_CausalButtonDisabled` — This test is checked via the `EntityHistoryView` pivot area (SC-7 refers to the same disable logic for trace_id=0).

---

## 3. Task 2 — TRC-P7-019: EntityPickerView + Session Browser Link

### 3.1 Modify `tracer-viewer/src/router/index.ts`

Add the entity picker route:
```typescript
{
  path: '/v/entities/:sessionId',
  name: 'entity-picker',
  component: () => import('@/views/EntityPickerView.vue'),
  props: true,
},
```

### 3.2 Create `tracer-viewer/src/views/EntityPickerView.vue`

**Props:**
```typescript
defineProps<{ sessionId: string }>();
```

**State:**
- `entities: EntitySummaryDto[]` — loaded from API
- `loading: boolean`
- `error: string | null`
- `filterText: string` (bound to text input)

**Load:** on `onMounted`, call `api.listEntities(sessionId)` or `api.getEntityList(sessionId)`. Check the actual API method name from `tracerApiClient.ts` — it should be something like `getEntityList`. Load result into `entities`.

**Client-side filter:**
```typescript
const filteredEntities = computed(() => {
  if (!filterText.value) return entities.value;
  const q = filterText.value.toLowerCase();
  return entities.value.filter(e =>
    e.entityId.toLowerCase().includes(q) ||
    (e.samplePlayerId?.toLowerCase().includes(q) ?? false) ||
    e.topics.some(t => t.toLowerCase().includes(q)),
  );
});
```

**Navigation on entity click:**
```typescript
function openEntity(entityId: string) {
  void router.push({
    name: 'entity-history',
    params: { entityId },
    query: { session: props.sessionId },
  });
}
```

**Template structure:**
```html
<div class="entity-picker">
  <h1>Entities — {{ sessionId }}</h1>
  <input v-model="filterText" placeholder="Filter entities..." class="entity-picker__filter" />

  <LoadingSpinner v-if="loading" />
  <ErrorMessage v-else-if="error" :message="error" />
  <div v-else-if="filteredEntities.length === 0" class="entity-picker__empty">
    No entities found.
  </div>
  <ul v-else class="entity-picker__list">
    <li
      v-for="entity in filteredEntities"
      :key="entity.entityId"
      class="entity-picker__item"
      @click="openEntity(entity.entityId)"
    >
      <span class="entity-picker__entity-id">{{ entity.entityId }}</span>
      <span class="entity-picker__event-count">{{ entity.eventCount.toLocaleString() }} events</span>
      <span v-if="entity.samplePlayerId" class="entity-picker__player">{{ entity.samplePlayerId }}</span>
      <span class="entity-picker__topics">
        {{ entity.topics.slice(0, 5).join(', ') }}
        <template v-if="entity.topics.length > 5">+{{ entity.topics.length - 5 }} more</template>
      </span>
    </li>
  </ul>
</div>
```

### 3.3 Modify `SessionBrowserView.vue` or `SessionCard.vue`

Add an "Entities" link to each session card. The cleanest approach: add it to `SessionBrowserView.vue` alongside each card, since `SessionCard.vue` already handles its own navigation.

In `SessionBrowserView.vue`, update the session card list to show an "Entities" link:
```html
<div
  v-else
  class="session-browser__list"
>
  <div v-for="s in sessions" :key="s.sessionId" class="session-browser__card-wrapper">
    <SessionCard
      :session="s"
      @click="openSession(s)"
    />
    <RouterLink
      :to="{ name: 'entity-picker', params: { sessionId: s.sessionId } }"
      class="session-browser__entities-link"
    >
      Entities
    </RouterLink>
  </div>
</div>
```

Or alternatively, add the link inside `SessionCard.vue` as an additional footer link:
```html
<!-- inside session-card template footer section -->
<footer class="session-card__footer">
  <span>{{ session.eventCount.toLocaleString() }} events</span>
  <span>{{ session.participatingNodes.length }} node(s)</span>
  <RouterLink
    v-if="session"
    :to="{ name: 'entity-picker', params: { sessionId: session.sessionId } }"
    class="session-card__entities-link"
    @click.stop
  >
    Entities
  </RouterLink>
</footer>
```

Use `@click.stop` to prevent the card's parent click handler from triggering navigation to the scenario view.

**Choose whichever approach is simpler and ensure existing `SessionCard` tests still pass.**

### 3.4 Check the actual `getEntityList` API method

Before implementing, verify the API method signature in `tracerApiClient.ts`. It should be something like:
```typescript
async getEntityList(sessionId: string, opts?: {...}): Promise<EntityListDto>
```

Use the correct method name. The `EntityListDto` has:
```typescript
interface EntityListDto {
  entities: EntitySummaryDto[];
  count: number;
}
```

### 3.5 Tests: `tracer-viewer/tests/unit/EntityPickerView.spec.ts`

Create tests satisfying TRC-P7-019 SC-1..7:
1. Loads and renders 3 entities (mock API returns 3) → 3 `li.entity-picker__item` elements
2. Loading state shown while API pending → spinner visible; list absent
3. Empty result → empty-state message; no spinner; no JS error
4. Filter hides non-matching entities → 3 entities, filter to match 1 → 1 item visible
5. Clicking entity navigates to `entity-history` route with correct params
6. Topics overflow: entity with 8 topics shows first 5 + "+3 more"
7. Entities link on SessionCard — mount `SessionBrowserView` (or check `SessionCard`) with one session; assert link to `/v/entities/{sessionId}` is present

Mock `@/api/tracerApiClient` at module level using `vi.mock`.

---

## 4. Constraints

- **TypeScript strict** — no `any`; use type guards for `entityId` on mixed event types
- `EventInspector` must pass existing tests (do not break existing behaviour)
- The disabled "Show entity history" stub button must be **replaced** (not appended to)
- Use `@click.stop` on Entities link inside SessionCard if it's wrapped in a clickable container
- All existing 230 tests must continue to pass

---

## 5. Build and Test Commands

```powershell
# TypeScript check
cd d:\Work\Tracer\tracer-viewer; pnpm tsc --noEmit

# Frontend tests
cd d:\Work\Tracer\tracer-viewer; pnpm test:unit --run

# Expected: 230 existing + ~20 new ≈ 250+ tests, all passing
```

No backend changes. Do not run the C# build.

---

## 6. Report

Write `.dev/tracer/reports/BATCH-40-REPORT.md` at `d:\WORK\Tracer\.dev\tracer\reports\BATCH-40-REPORT.md` (NOT inside `tracer-viewer\`). Standard format: files, test counts, TypeScript status, design decisions, issues, weak points, new debt items (DT-035+), suggested commit message.

**Do NOT commit.** Dev lead reviews, then commits.
