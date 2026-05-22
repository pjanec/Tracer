# BATCH-38 Report

**Batch:** BATCH-38  
**Status:** COMPLETED  
**Date:** 2025-01-XX  
**TypeScript check:** ✅ `pnpm tsc --noEmit` — clean (0 errors)  
**Unit tests:** ✅ `pnpm test:unit --run` — 207 passed, 0 failed (45 test files)

---

## Tasks Implemented

### TRC-P7-011 — EntityLifecycleRibbon

**New file:** `tracer-viewer/src/utils/lifecycleClassifier.ts`  
- Exports `LifecycleKind` union type (`'spawn' | 'ownership' | 'destruction'`)  
- `classifyLifecycleEvent(topic)` splits on `.`, lowercases the last segment, matches against three keyword sets  
- Returns `null` for non-lifecycle topics

**Replaced stub:** `tracer-viewer/src/components/EntityLifecycleRibbon.vue`  
- Pure CSS layout (`position: relative` + absolute children) — no canvas  
- `lifecycleEvents` computed: filters + sorts by `occurredAtUtc` ascending  
- `ownershipBands` computed: one band per spawn/ownership event, extending to the next or right edge (100%)  
- Marker `left: xPct(event)%` uses the `occurredAtUtc` timestamp, clamped to [0, 100]  
- No emit — lifecycle event selection deferred to a future phase  
- CSS class prefix `entity-lifecycle-ribbon__` throughout  

### TRC-P7-012 — EntityEventStrip

**New file:** `tracer-viewer/src/rendering/eventStripRenderer.ts`  
- `renderEventStrip(ctx, input)` draws one `arc` per event; out-of-range events skipped  
- Node colors via existing `buildNodeColorMap`; uses `event.occurredAtUtc` for x-position  
- Selected event gets a white ring via a second `arc` + `stroke`  
- Returns `EventStripHitEntry[]` (eventId + x) for click handling

**Replaced stub:** `tracer-viewer/src/components/EntityEventStrip.vue`  
- Canvas-based with DPI scaling via `window.devicePixelRatio`  
- Uses `useResizeObserver` and RAF scheduling (`scheduleRender`)  
- Click handler finds nearest hit entry within `THRESHOLD_PX = 8`; emits `select(null)` on miss  
- Emit type changed from `[eventId: string]` to `[eventId: string | null]` (supports deselect)  
- Shows truncation notice when `events.truncated` is true

### TRC-P7-013 — SlowStateChart

**New file:** `tracer-viewer/src/rendering/slowStateChartRenderer.ts`  
- `detectFields(payloadJsonSamples[])`: inspects up to 20 samples, classifies each JSON field as `numeric` (all values `typeof number`) or `categorical`; returns sorted by preferred-name arrays  
- `renderNumericLine()`: stepped (last-value-held) line chart; collapses degenerate `valRange` to avoid division by zero  
- `renderCategoricalBands()`: one filled rect per sample, extending to next sample's x or right edge; text label when band > 30px  

**Replaced stub:** `tracer-viewer/src/components/SlowStateChart.vue`  
- `selectedField` auto-set to first detected field when `samples` prop changes  
- `<select>` shown only when > 1 field detected  
- Canvas-based with DPI scaling, RAF scheduling, `useResizeObserver`  
- Click handler uses `getBoundingClientRect().width` (not `clientWidth`) so hit test is layout-accurate  
- Emit type changed from `[eventId: string]` to `[sample: SlowStateSampleDto]`

---

## Test Files Created (6)

| File | Tests | Description |
|------|-------|-------------|
| `tests/unit/lifecycleClassifier.spec.ts` | 4 | Spawn/ownership/destruction suffix matching; null for unrelated topics |
| `tests/unit/entityLifecycleRibbon.spec.ts` | 4 | Marker count by kind; x-position at 50%; no markers when no lifecycle events; two bands on spawn+ownership |
| `tests/unit/eventStripRenderer.spec.ts` | 3 | Marker x at 250px; selected-event ring; zero-events no-throw |
| `tests/unit/entityEventStrip.spec.ts` | 4 | Near-click emits eventId; far-click emits null; truncated notice shown/hidden |
| `tests/unit/slowStateChartRenderer.spec.ts` | 5 | Numeric y-coords; single-sample right-edge extension; same-value no-throw; categorical band width; empty no-throw |
| `tests/unit/slowStateChart.spec.ts` | 4 | detectFields classification; preferred-field auto-select; click emits select-event; EntityHistoryView zero charts when no slow state |

---

## Issues Encountered

### 1. DTO field name mismatch (batch instructions vs actual API)
The batch instructions referenced `publishWallclock` and `payload` on `EntityEventDto`, but the actual `tracerApiClient.ts` uses `occurredAtUtc` and `payloadJson` (optional). All implementations use the correct field names from the actual interface.

### 2. Synchronous RAF in jsdom tests
Vue's `watchEffect` first runs during setup with `canvasRef.value = null` (DOM not yet built), so the RAF callback returns early. After mount, when `canvasRef.value` becomes non-null, the effect re-queues. Without `await nextTick()` before the click trigger, `hitEntries.value` was still `[]`. Fixed by inserting `await nextTick()` in `clickNearMarkerEmitsSelectWithEventId`.

### 3. Vue Proxy wraps props — `toBe` vs `toStrictEqual`
When a `SlowStateSampleDto` is accessed via `props.samples`, Vue 3 wraps it in a reactive Proxy. The emitted value is the Proxy, not the raw object, so `toBe` (reference equality) fails. Changed to `toStrictEqual` for the `select-event` emission assertion.

### 4. RAF sync mock interaction with `rafId`
The sync RAF mock (`vi.stubGlobal('requestAnimationFrame', cb => { cb(0); return 0; })`) calls the callback before the assignment `rafId = ...` completes. This means `rafId` may transiently hold `0` after the first `scheduleRender`. The component's `cancelAnimationFrame` guard (`if (rafId !== null)`) still functions correctly because `0 !== null` and our `cancelAnimationFrame` mock is a no-op.

---

## Design Decisions Beyond the Spec

1. **`detectFields` maxSamples defaults to 20**: Prevents performance issues with large datasets; the batch spec did not specify a default, 20 was chosen as a reasonable limit.

2. **`payloadJson` field for `SlowStateSampleDto` is `required` (not optional)**: Matches the actual DTO interface. `EntityEventDto.payloadJson` is optional — `SlowStateChart` only skips samples that fail JSON parse or lack the selected field, rather than treating `undefined` specially.

3. **`renderNumericLine` degenerate-range guard**: When all values are identical (`maxVal === minVal`), `valRange` collapses to `1e-9` with a half-range offset applied to both min and max, placing all points at the vertical midpoint rather than triggering division-by-zero.

4. **No FastStateDrillDown implementation**: Deferred to BATCH-39 as instructed.

---

## Weak Points Spotted in the Codebase

- `entityHistoryView.spec.ts > entityHistory router` test: calls `vi.resetModules()` which breaks all module mocks globally (producing `HTMLCanvasElement.prototype.getContext` console errors for `SlowStateChart` and `EntityEventStrip`). These are cosmetic stderr warnings — tests still pass — but indicate that router-level tests should either isolate module mocks or stub canvas components.

- `tests/setup.ts` stubs `ResizeObserver` globally but does **not** stub `HTMLCanvasElement.prototype.getContext`. Any test that mounts a canvas component without explicitly mocking `getContext` will produce a jsdom `Not implemented` console error (though not a test failure, since the component guards `if (!ctx) return`).
