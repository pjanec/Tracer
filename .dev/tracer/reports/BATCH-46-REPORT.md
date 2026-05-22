# BATCH-46 Report

**Batch:** BATCH-46  
**Tasks:** TRC-P8-011, TRC-P8-012  
**Status:** ✅ Complete  
**Tests:** 21 new tests added, 287/287 total passing (0 regressions)

---

## Files Created / Modified

| File | Action | Description |
|------|--------|-------------|
| `src/components/AnnotationMarker.vue` | Created | Badge button that renders when annotation exists for a target |
| `src/components/AnnotationEditor.vue` | Created | Modal form for creating/editing annotations with title, body, tags |
| `src/components/AnnotationList.vue` | Created | List of annotation items with select/edit events |
| `src/components/EventInspector.vue` | Modified | Added AnnotationMarker, AnnotationList, AnnotationEditor, Add Note button |
| `src/components/EntityEventStrip.vue` | Modified | Added AnnotationMarker overlay for each event in the strip |
| `tests/unit/AnnotationMarker.spec.ts` | Created | 5 unit tests |
| `tests/unit/AnnotationEditor.spec.ts` | Created | 8 unit tests |
| `tests/unit/AnnotationList.spec.ts` | Created | 3 unit tests |
| `tests/unit/EventInspector.spec.ts` | Modified | 5 integration tests added |

---

## Test Results

| Suite | Tests | Status |
|-------|-------|--------|
| `AnnotationMarker.spec.ts` | 5/5 | ✅ |
| `AnnotationEditor.spec.ts` | 8/8 | ✅ |
| `AnnotationList.spec.ts` | 3/3 | ✅ |
| `EventInspector.spec.ts` (additions) | 5/5 | ✅ |
| **Full suite** | **287/287** | ✅ |

---

## Issues Encountered and How Resolved

### 1. EventInspector.vue template had special character in "Loading" text
The "Loading…" text contained a non-ASCII ellipsis character. This caused a replace failure when trying to match the template. Resolved by using targeted, smaller replacements (header, actions end, closing div) instead of replacing the entire template block.

### 2. EntityEventStrip is canvas-based — no per-event DOM elements
The instructions assumed a `v-for` in the template, but `EntityEventStrip` renders events entirely on a canvas. Resolution: added a `div.entity-event-strip__annotation-overlay` containing a `v-for` over `events.events`, rendering `AnnotationMarker` for each event. Vue's reactivity ensures only events with annotations render visible markers (`v-if="hasAnnotation"` inside the marker).

### 3. EntityEventStrip test in EventInspector.spec.ts needed additional module mocks
`ResizeObserver` is not available in jsdom. Without mocking `useResizeObserver`, mounting `EntityEventStrip` throws `ReferenceError: ResizeObserver is not defined`. Resolution: added `vi.mock('@/composables/useResizeObserver', ...)` and `vi.mock('@/rendering/eventStripRenderer', ...)` to `EventInspector.spec.ts`. These mocks are hoisted by Vitest and don't affect existing EventInspector tests (EventInspector.vue does not import those modules).

### 4. Canvas getContext error is non-fatal
The EntityEventStrip test logs a jsdom "Not implemented: HTMLCanvasElement.prototype.getContext" error to stderr. This is because `scheduleRender` runs via the stubbed synchronous RAF and tries to get a 2D context. The rendering bails out gracefully (early return), the test still passes. The annotation marker DOM overlay is independent of canvas rendering.

### 5. Existing `flushPromises` import from `@vue/test-utils` preserved
When updating the imports in `EventInspector.spec.ts`, care was taken to keep the existing `flushPromises` import from `@vue/test-utils` intact (existing tests use it). `nextTick` was added from `'vue'`.

---

## Design Decisions

- **AnnotationMarker placement in EventInspector**: Placed inside `event-inspector__header` div alongside topic/node spans. This is visually natural and keeps the marker associated with the event identity.

- **"Add note" button placement**: Added inside `event-inspector__actions` div (alongside existing action buttons) for visual consistency, rather than as a floating element.

- **AnnotationEditor modal**: Placed at the bottom of `event-inspector` outer div (outside the `v-else-if="displayEvent"` block) so it can remain visible as a fixed overlay even if display state changes.

- **EntityEventStrip overlay**: The annotation markers overlay is a DOM div within the strip component. It's separate from the canvas layer. In a future iteration, the overlay could be positioned to align markers with canvas x-coordinates.

- **TRC-P8-011 SC-6/SC-7 deferred**: Canvas integration tests for `TimelineView` and `CausalTreeView` were deferred to TRC-P8-018 per batch instructions.

---

## Suggested Git Commit Message

```
feat(annotations): add AnnotationMarker, AnnotationEditor, AnnotationList (TRC-P8-011, TRC-P8-012)

- Create AnnotationMarker.vue: badge that renders from annotationStore by eventId/entityId/traceId
- Create AnnotationEditor.vue: modal form with title, body, tags, delete in edit mode
- Create AnnotationList.vue: scrollable list of annotations with select/edit events
- Integrate AnnotationMarker into EventInspector header and EntityEventStrip overlay
- Integrate AnnotationList + AnnotationEditor into EventInspector (Add note flow)
- Add emit annotation-edit to EventInspector and EntityEventStrip
- 21 new tests (5 marker, 8 editor, 3 list, 5 integration); 287/287 total passing
```
