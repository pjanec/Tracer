# BATCH-46 Instructions

**Batch:** BATCH-46  
**Tasks:** TRC-P8-011 (AnnotationMarker.vue + Overlay Integration), TRC-P8-012 (AnnotationEditor.vue + AnnotationList.vue)  
**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish  
**Estimated Effort:** 8–10 hours  
**Dependencies:** BATCH-45 complete (annotationStore + useAnnotations already exist)  
**Report path:** `d:\WORK\Tracer\.dev\tracer\reports\BATCH-46-REPORT.md`  
**Working directory:** `d:\WORK\Tracer\tracer-viewer`

---

## 📋 Onboarding

### Required Reading (IN ORDER)

1. **Design:** `docs/tracer_phase8_design.md` §5.2 (AnnotationEditor), §5.3 (AnnotationMarker / overlay), §5.4 (Inspector integration)
2. **Task definitions:** `docs/TASK-DETAIL.md` §TRC-P8-011 (8 success conditions), §TRC-P8-012 (13 success conditions)
3. **Previous review:** `.dev/tracer/reviews/BATCH-45-REVIEW.md`
4. **Existing annotationStore:** `tracer-viewer/src/stores/annotationStore.ts` — understand `byEventId`, `byEntityId`, `byTraceId` getters
5. **Existing useAnnotations:** `tracer-viewer/src/composables/useAnnotations.ts` — understand the API
6. **EventInspector.vue:** `tracer-viewer/src/components/EventInspector.vue` — understand where to integrate the annotation button
7. **EventInspector.spec.ts:** `tracer-viewer/tests/unit/EventInspector.spec.ts` — understand existing tests, DO NOT break them
8. **Component patterns:** `tracer-viewer/src/components/FilterPanel.vue` (modal/overlay pattern)
9. **Test patterns:** `tracer-viewer/tests/unit/FilterPanel.spec.ts`

### Frontend test commands (from `d:\Work\Tracer\tracer-viewer`):

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm test:unit -- --reporter=verbose 2>&1 | Select-Object -Last 15
pnpm test:unit -- --reporter=verbose AnnotationMarker 2>&1 | Select-Object -Last 15
pnpm test:unit -- --reporter=verbose AnnotationEditor 2>&1 | Select-Object -Last 15
pnpm test:unit -- --reporter=verbose AnnotationList 2>&1 | Select-Object -Last 15
pnpm test:unit -- --reporter=verbose EventInspector 2>&1 | Select-Object -Last 15
```

---

## 🔄 MANDATORY WORKFLOW

1. Create `AnnotationMarker.vue` → write 5 unit tests → pass ✅
2. Integrate marker into `EventInspector.vue` + `EntityEventStrip.vue` + timeline/causal views → write 3 integration tests → pass ✅
3. Create `AnnotationEditor.vue` → write 8 unit tests → pass ✅
4. Create `AnnotationList.vue` → write 3 unit tests → pass ✅
5. Integrate `AnnotationList` + `AnnotationEditor` into `EventInspector.vue` → write 2 integration tests → pass ✅
6. Run full suite: 0 regressions ✅

---

## ✅ Task 1 — TRC-P8-011: AnnotationMarker.vue + Overlay Integration

**Design reference:** `docs/tracer_phase8_design.md` §5.3  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-011

### 1.1 Create `src/components/AnnotationMarker.vue`

The marker is a small badge/icon that appears when an annotation exists for a given target. It shows a tooltip on hover. Clicking it emits an `edit` event.

**Props:**
- `eventId?: string`
- `entityId?: string`
- `traceId?: string`
- `minPx?: number` — if provided, the marker is hidden when the element is below this pixel threshold (density suppression, default `8`)

**Behavior:**
- Reads from `annotationStore` using `byEventId(eventId)`, `byEntityId(entityId)`, or `byTraceId(traceId)` — whichever is provided
- If no matching annotation exists → render nothing (`v-if="hasAnnotation"`)
- Tooltip text: annotation's `title` if present, otherwise first line of `body`
- On click: emit `'edit'` event with the matched `AnnotationDto`
- Must have class `.annotation-marker` on the root element
- Must have `aria-label` for accessibility

```vue
<template>
  <button
    v-if="hasAnnotation"
    class="annotation-marker"
    :aria-label="`Annotation: ${tooltipText}`"
    :title="tooltipText"
    @click.stop="onMarkerClick"
  >
    📝
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useAnnotationStore } from '@/stores/annotationStore';
import type { AnnotationDto } from '@/api/tracerApiClient';

const props = withDefaults(defineProps<{
  eventId?: string;
  entityId?: string;
  traceId?: string;
  minPx?: number;
}>(), { minPx: 8 });

const emit = defineEmits<{
  edit: [annotation: AnnotationDto];
}>();

const store = useAnnotationStore();

const matchingAnnotations = computed<AnnotationDto[]>(() => {
  if (props.eventId) return store.byEventId(props.eventId);
  if (props.entityId) return store.byEntityId(props.entityId);
  if (props.traceId) return store.byTraceId(props.traceId);
  return [];
});

const hasAnnotation = computed(() => matchingAnnotations.value.length > 0);

const firstAnnotation = computed(() => matchingAnnotations.value[0] ?? null);

const tooltipText = computed(() => {
  const ann = firstAnnotation.value;
  if (!ann) return '';
  if (ann.title) return ann.title;
  return ann.body.split('\n')[0] ?? '';
});

function onMarkerClick() {
  if (firstAnnotation.value) {
    emit('edit', firstAnnotation.value);
  }
}
</script>

<style lang="scss">
.annotation-marker {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 1.25rem;
  height: 1.25rem;
  padding: 0;
  border: none;
  border-radius: 3px;
  background: var(--c-accent, #3b82f6);
  color: #fff;
  cursor: pointer;
  font-size: 0.625rem;
  line-height: 1;
  opacity: 0.85;
  transition: opacity 150ms ease;

  &:hover {
    opacity: 1;
  }

  &:focus-visible {
    outline: 2px solid var(--c-accent);
    outline-offset: 2px;
  }
}
</style>
```

### 1.2 Tests — `tests/unit/AnnotationMarker.spec.ts` (5 tests)

Write these 5 tests:

1. **`Marker_RendersWhenAnnotationExists`** — Seed `annotationStore` with one annotation for `eventId='AAAA'`. Mount `<AnnotationMarker eventId="AAAA" />`. Assert: `.annotation-marker` element exists.

2. **`Marker_HiddenWhenNoAnnotation`** — Seed store with no annotations for `eventId='BBBB'`. Mount `<AnnotationMarker eventId="BBBB" />`. Assert: `.annotation-marker` does NOT exist.

3. **`Marker_Tooltip_ShowsAnnotationTitle`** — Seed with annotation `title='Suspicious spike'`, no body. Mount `<AnnotationMarker eventId="T1" />`. Assert: the `title` attribute of the marker button contains `"Suspicious spike"`.

4. **`Marker_Tooltip_FallsBackToBodyFirstLine`** — Seed with annotation `title=undefined`, `body='First line\nSecond line'`. Mount marker. Assert: the `title` attribute is `"First line"`.

5. **`Marker_Click_EmitsEditEvent`** — Seed with annotation for `eventId='CCCC'`. Mount marker. Click the `.annotation-marker` button. Assert: component emitted `'edit'` event; emitted payload has `annotationId` matching the seeded annotation.

### 1.3 Integration into EventInspector.vue

Read `EventInspector.vue` fully first. Then add `<AnnotationMarker>` into the inspector.

After the payload/actions section, add the annotation marker next to the event ID or topic header. The exact placement is flexible — add it near the header of the event inspector, showing the marker when `displayEvent?.eventId` is available:

```vue
<AnnotationMarker
  v-if="displayEvent"
  :event-id="displayEvent.eventId"
  @edit="onAnnotationEdit"
/>
```

Add the handler and import:
```typescript
import AnnotationMarker from '@/components/AnnotationMarker.vue';

function onAnnotationEdit(annotation: AnnotationDto) {
  // Will open AnnotationEditor (TRC-P8-012) — emit an event or set local state
  // For now: emit the annotation upward
  emit('annotation-edit', annotation);
}
```

Add to `defineEmits`: `'annotation-edit': [annotation: AnnotationDto]`

### 1.4 Integration into EntityEventStrip.vue

Read `EntityEventStrip.vue` first. For each event row in the event strip, add an `<AnnotationMarker>` if the event has an annotation.

Find where each event is rendered (likely a `v-for` over events). Add:
```vue
<AnnotationMarker :event-id="event.eventId" @edit="$emit('annotation-edit', $event)" />
```

Add `'annotation-edit': [annotation: AnnotationDto]` to `defineEmits`.

### 1.5 Integration tests (3 tests) — in `tests/unit/EventInspector.spec.ts`

Add to the existing test file. Read it first to understand the existing setup.

Write these 3 integration tests:

6. **`Inspector_AnnotationMarker_VisibleWhenAnnotationExists`** — Seed `annotationStore` with annotation for `eventId = 'some-event-id'`. Mount `EventInspector` with an event whose `eventId` matches. Assert: `.annotation-marker` element is present.

7. **`Inspector_AnnotationMarker_HiddenWhenNoAnnotation`** — Empty `annotationStore`. Mount `EventInspector`. Assert: `.annotation-marker` NOT present.

8. **`EntityEventStrip_AnnotationMarker_Visible`** — Read `entityEventStrip.spec.ts` to understand how to mount `EntityEventStrip`. Seed `annotationStore` with annotation for one of the events in the strip. Assert: at least one `.annotation-marker` is present.

> Note for SC-6 through SC-8 from TRC-P8-011: The integration tests for `TimelineView` and `CausalTreeView` (SC-6 and SC-7) are complex canvas integration tests that require a running DuckDB backend. Skip those for this batch — mark them as deferred to TRC-P8-018. Focus on `EventInspector` (SC-6 in spec → use it as "EventInspector overlay visible") and `EntityEventStrip` (SC-8 in spec).

---

## ✅ Task 2 — TRC-P8-012: AnnotationEditor.vue + AnnotationList.vue

**Design reference:** `docs/tracer_phase8_design.md` §5.2, §5.4  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-012

### 2.1 Create `src/components/AnnotationEditor.vue`

**Props:**
- `visible: boolean` — whether the modal is open
- `initial?: AnnotationDto | null` — pre-populated annotation for edit mode; null = create mode

**Emits:**
- `save: [payload: { body: string; title?: string; tags: string[] }]`
- `cancel: []`
- `delete: [annotationId: string]`

**Behavior:**
- Textarea `body` is the primary input (required); Save button disabled when empty
- Title input (optional)
- Tags: displayed as chips; type + Enter or comma to add; × to remove
- Author: read from `localStorage['tracer:authorName']` — shown as read-only info text
- Delete button: ONLY visible in edit mode (`initial !== null`)
- autofocus on the body textarea when opened
- Cancel button always emits `cancel` without saving

```vue
<template>
  <div v-if="visible" class="annotation-editor">
    <div class="annotation-editor__backdrop" @click.self="onCancel" />
    <div class="annotation-editor__dialog" role="dialog" aria-modal="true">
      <h2 class="annotation-editor__heading">
        {{ initial ? 'Edit annotation' : 'Add annotation' }}
      </h2>

      <div class="annotation-editor__field">
        <label class="annotation-editor__label">Title (optional)</label>
        <input
          v-model="localTitle"
          class="annotation-editor__title-input"
          type="text"
          placeholder="Short summary…"
        />
      </div>

      <div class="annotation-editor__field">
        <label class="annotation-editor__label">Note</label>
        <textarea
          ref="bodyRef"
          v-model="localBody"
          class="annotation-editor__body"
          rows="4"
          placeholder="Write your note here…"
        />
      </div>

      <div class="annotation-editor__field">
        <label class="annotation-editor__label">Tags</label>
        <div class="annotation-editor__tags">
          <span
            v-for="tag in localTags"
            :key="tag"
            class="annotation-editor__tag"
          >
            {{ tag }}
            <button class="annotation-editor__tag-remove" @click="removeTag(tag)">×</button>
          </span>
          <input
            v-model="tagInput"
            class="annotation-editor__tag-input"
            placeholder="Add tag…"
            @keydown.enter.prevent="addTag"
            @keydown.comma.prevent="addTag"
          />
        </div>
      </div>

      <div v-if="author" class="annotation-editor__author">
        Author: {{ author }}
      </div>

      <div class="annotation-editor__actions">
        <button
          class="annotation-editor__delete"
          v-if="initial"
          @click="onDelete"
        >
          Delete
        </button>
        <button class="annotation-editor__cancel" @click="onCancel">
          Cancel
        </button>
        <button
          class="annotation-editor__save"
          :disabled="!localBody.trim()"
          @click="onSave"
        >
          Save
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick } from 'vue';
import type { AnnotationDto } from '@/api/tracerApiClient';

const props = defineProps<{
  visible: boolean;
  initial?: AnnotationDto | null;
}>();

const emit = defineEmits<{
  save: [payload: { body: string; title?: string; tags: string[] }];
  cancel: [];
  delete: [annotationId: string];
}>();

const localTitle = ref('');
const localBody = ref('');
const localTags = ref<string[]>([]);
const tagInput = ref('');
const bodyRef = ref<HTMLTextAreaElement | null>(null);

const author = localStorage.getItem('tracer:authorName') ?? '';

// Populate from initial prop when it changes
watch(
  () => props.initial,
  (ann) => {
    localTitle.value = ann?.title ?? '';
    localBody.value = ann?.body ?? '';
    localTags.value = [...(ann?.tags ?? [])];
    tagInput.value = '';
  },
  { immediate: true },
);

// Autofocus on open
watch(
  () => props.visible,
  async (v) => {
    if (v) {
      await nextTick();
      bodyRef.value?.focus();
    }
  },
);

function addTag() {
  const t = tagInput.value.replace(',', '').trim();
  if (t && !localTags.value.includes(t)) localTags.value.push(t);
  tagInput.value = '';
}

function removeTag(tag: string) {
  localTags.value = localTags.value.filter(t => t !== tag);
}

function onSave() {
  if (!localBody.value.trim()) return;
  emit('save', {
    body: localBody.value,
    title: localTitle.value || undefined,
    tags: localTags.value,
  });
}

function onCancel() {
  emit('cancel');
}

function onDelete() {
  if (props.initial) emit('delete', props.initial.annotationId);
}
</script>

<style lang="scss">
.annotation-editor {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;

  &__backdrop {
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
  }

  &__dialog {
    position: relative;
    background: var(--c-bg-surface);
    border-radius: 8px;
    padding: 1.5rem;
    width: min(480px, 90vw);
    display: flex;
    flex-direction: column;
    gap: 1rem;
    max-height: 90vh;
    overflow-y: auto;
  }

  &__heading {
    font-size: 1.125rem;
    font-weight: 600;
    margin: 0;
  }

  &__field {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }

  &__label {
    font-size: 0.875rem;
    color: var(--c-text-muted);
  }

  &__title-input,
  &__body,
  &__tag-input {
    border: 1px solid var(--c-bg-subtle);
    border-radius: 4px;
    padding: 0.5rem;
    background: transparent;
    color: var(--c-text);
    font-size: 0.875rem;
  }

  &__body {
    resize: vertical;
    min-height: 6rem;
  }

  &__tags {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem;
    align-items: center;
    border: 1px solid var(--c-bg-subtle);
    border-radius: 4px;
    padding: 0.375rem;
  }

  &__tag {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    padding: 0.125rem 0.5rem;
    background: var(--c-accent, #3b82f6);
    color: #fff;
    border-radius: 999px;
    font-size: 0.75rem;
  }

  &__tag-remove {
    background: none;
    border: none;
    color: inherit;
    cursor: pointer;
    padding: 0;
    line-height: 1;
  }

  &__tag-input {
    border: none;
    outline: none;
    flex: 1;
    min-width: 80px;
    padding: 0.125rem 0;
  }

  &__author {
    font-size: 0.75rem;
    color: var(--c-text-muted);
  }

  &__actions {
    display: flex;
    gap: 0.5rem;
    justify-content: flex-end;
  }

  &__save,
  &__cancel,
  &__delete {
    padding: 0.375rem 0.875rem;
    border-radius: 4px;
    border: none;
    cursor: pointer;
    font-size: 0.875rem;
  }

  &__save {
    background: var(--c-accent, #3b82f6);
    color: #fff;

    &:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }
  }

  &__cancel {
    background: var(--c-bg-subtle);
    color: var(--c-text);
  }

  &__delete {
    background: var(--c-danger, #ef4444);
    color: #fff;
    margin-right: auto;
  }
}
</style>
```

### 2.2 Create `src/components/AnnotationList.vue`

**Props:**
- `annotations: AnnotationDto[]`

**Emits:**
- `select: [annotation: AnnotationDto]`
- `edit: [annotation: AnnotationDto]`

```vue
<template>
  <div class="annotation-list">
    <div
      v-for="ann in annotations"
      :key="ann.annotationId"
      class="annotation-list__item"
      role="button"
      tabindex="0"
      @click="$emit('select', ann)"
      @keydown.enter="$emit('select', ann)"
    >
      <div class="annotation-list__content">
        <p class="annotation-list__excerpt">{{ titleOrExcerpt(ann) }}</p>
        <div class="annotation-list__meta">
          <span v-if="ann.author" class="annotation-list__author">{{ ann.author }}</span>
          <span class="annotation-list__time">{{ formatRelativeTime(ann.createdAtUtc) }}</span>
        </div>
      </div>
      <button
        class="annotation-list__edit-btn"
        @click.stop="$emit('edit', ann)"
      >
        Edit
      </button>
    </div>
    <p v-if="annotations.length === 0" class="annotation-list__empty">
      No annotations yet.
    </p>
  </div>
</template>

<script setup lang="ts">
import type { AnnotationDto } from '@/api/tracerApiClient';

defineProps<{ annotations: AnnotationDto[] }>();
defineEmits<{
  select: [annotation: AnnotationDto];
  edit: [annotation: AnnotationDto];
}>();

function titleOrExcerpt(ann: AnnotationDto): string {
  if (ann.title) return ann.title;
  const maxLen = 80;
  return ann.body.length > maxLen ? ann.body.slice(0, maxLen) + '…' : ann.body;
}

function formatRelativeTime(isoStr: string): string {
  const diff = Date.now() - new Date(isoStr).getTime();
  const minutes = Math.floor(diff / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}
</script>

<style lang="scss">
.annotation-list {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  max-height: 20rem;
  overflow-y: auto;

  &__item {
    display: flex;
    align-items: flex-start;
    gap: 0.5rem;
    padding: 0.5rem;
    border-radius: 4px;
    cursor: pointer;
    border: 1px solid transparent;

    &:hover {
      background: var(--c-bg-subtle);
      border-color: var(--c-bg-subtle);
    }
  }

  &__content {
    flex: 1;
    min-width: 0;
  }

  &__excerpt {
    margin: 0;
    font-size: 0.875rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  &__meta {
    display: flex;
    gap: 0.5rem;
    font-size: 0.75rem;
    color: var(--c-text-muted);
    margin-top: 0.125rem;
  }

  &__edit-btn {
    background: none;
    border: 1px solid var(--c-bg-subtle);
    border-radius: 4px;
    padding: 0.125rem 0.5rem;
    font-size: 0.75rem;
    cursor: pointer;
    color: var(--c-text-muted);
    white-space: nowrap;

    &:hover {
      background: var(--c-bg-subtle);
    }
  }

  &__empty {
    color: var(--c-text-muted);
    font-size: 0.875rem;
    text-align: center;
    padding: 1rem;
  }
}
</style>
```

### 2.3 Integrate AnnotationList + AnnotationEditor into EventInspector.vue

Add to `EventInspector.vue`:

1. **Show annotation list** for the current event below the existing actions section:

```vue
<AnnotationList
  v-if="displayEvent"
  :annotations="eventAnnotations"
  @edit="openEditorForAnnotation"
/>
```

2. **"Add note" button** visible when `displayEvent` is set:

```vue
<button
  v-if="displayEvent"
  class="event-inspector__add-note"
  @click="openEditorNew"
>
  Add note
</button>
```

3. **AnnotationEditor** modal:

```vue
<AnnotationEditor
  :visible="editorVisible"
  :initial="editorAnnotation"
  @save="onAnnotationSave"
  @cancel="editorVisible = false"
  @delete="onAnnotationDelete"
/>
```

4. Add script logic:

```typescript
import AnnotationList from '@/components/AnnotationList.vue';
import AnnotationEditor from '@/components/AnnotationEditor.vue';
import { useAnnotationStore } from '@/stores/annotationStore';
import { useAnnotations } from '@/composables/useAnnotations';
import { computed, ref } from 'vue';
import type { AnnotationDto } from '@/api/tracerApiClient';

const annotationStore = useAnnotationStore();
const sessionIdRef = computed(() => props.sessionId ?? null);  // EventInspector needs sessionId prop (add it)
const { create, update, remove } = useAnnotations(sessionIdRef);

const editorVisible = ref(false);
const editorAnnotation = ref<AnnotationDto | null>(null);

const eventAnnotations = computed(() =>
  displayEvent.value ? annotationStore.byEventId(displayEvent.value.eventId) : []
);

function openEditorNew() {
  editorAnnotation.value = null;
  editorVisible.value = true;
}

function openEditorForAnnotation(ann: AnnotationDto) {
  editorAnnotation.value = ann;
  editorVisible.value = true;
}

async function onAnnotationSave(payload: { body: string; title?: string; tags: string[] }) {
  if (editorAnnotation.value) {
    await update(editorAnnotation.value.annotationId, payload.body, payload.title, payload.tags);
  } else if (displayEvent.value) {
    await create(payload.body, 'Event', { eventId: displayEvent.value.eventId }, payload.title, payload.tags);
  }
  editorVisible.value = false;
}

async function onAnnotationDelete(annotationId: string) {
  await remove(annotationId);
  editorVisible.value = false;
}
```

**`sessionId` prop:** `EventInspector` may not have a `sessionId` prop yet. Add it:
```typescript
const props = defineProps<{
  // ... existing props ...
  sessionId?: string | null;
}>();
```

> Study the existing EventInspector.vue fully before making changes. It may already pass a sessionId from the parent view. If it does not, add the prop with `null` default and update callers where sessionId is known. Do not break existing callers if they don't pass sessionId yet — make it optional.

---

## 🧪 Tests

### 2.4 Tests — `tests/unit/AnnotationEditor.spec.ts` (8 tests)

```typescript
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AnnotationEditor from '../../src/components/AnnotationEditor.vue';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(override?: Partial<AnnotationDto>): AnnotationDto {
  return {
    annotationId: 'ann-1',
    sessionId: 'sess-1',
    kind: 'Event',
    eventId: 'evt-1',
    body: 'test body',
    tags: ['foo'],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...override,
  };
}

describe('AnnotationEditor', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('Editor_SaveDisabled_WhenBodyBlank', () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    const saveBtn = wrapper.find('.annotation-editor__save');
    expect(saveBtn.attributes('disabled')).toBeDefined();
  });

  it('Editor_SaveEnabled_WhenBodyFilled', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    await wrapper.find('.annotation-editor__body').setValue('some text');
    const saveBtn = wrapper.find('.annotation-editor__save');
    expect(saveBtn.attributes('disabled')).toBeUndefined();
  });

  it('Editor_PopulatesFromInitialProp', () => {
    const ann = makeAnnotation({ body: 'hello', title: 'world', tags: ['foo'] });
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: ann } });
    expect((wrapper.find('.annotation-editor__body').element as HTMLTextAreaElement).value).toBe('hello');
    expect((wrapper.find('.annotation-editor__title-input').element as HTMLInputElement).value).toBe('world');
    expect(wrapper.text()).toContain('foo');
  });

  it('Editor_DeleteButton_HiddenInCreateMode', () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    expect(wrapper.find('.annotation-editor__delete').exists()).toBe(false);
  });

  it('Editor_DeleteButton_VisibleInEditMode', () => {
    const ann = makeAnnotation();
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: ann } });
    expect(wrapper.find('.annotation-editor__delete').exists()).toBe(true);
  });

  it('Editor_EmitsSaveWithCorrectData', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    await wrapper.find('.annotation-editor__body').setValue('test body');
    await wrapper.find('.annotation-editor__title-input').setValue('test title');
    await wrapper.find('.annotation-editor__save').trigger('click');
    expect(wrapper.emitted('save')).toBeTruthy();
    const [payload] = wrapper.emitted('save')![0] as [{ body: string; title?: string; tags: string[] }];
    expect(payload.body).toBe('test body');
    expect(payload.title).toBe('test title');
    expect(payload.tags).toEqual([]);
  });

  it('Editor_TagManagement_AddAndRemove', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    // Type a tag and press Enter
    const tagInput = wrapper.find('.annotation-editor__tag-input');
    await tagInput.setValue('foo');
    await tagInput.trigger('keydown', { key: 'Enter' });
    expect(wrapper.text()).toContain('foo');
    // Remove the tag
    await wrapper.find('.annotation-editor__tag-remove').trigger('click');
    expect(wrapper.findAll('.annotation-editor__tag')).toHaveLength(0);
  });

  it('Editor_CancelEmitsCancel', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    await wrapper.find('.annotation-editor__cancel').trigger('click');
    expect(wrapper.emitted('cancel')).toBeTruthy();
    expect(wrapper.emitted('save')).toBeFalsy();
  });
});
```

### 2.5 Tests — `tests/unit/AnnotationList.spec.ts` (3 tests)

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AnnotationList from '../../src/components/AnnotationList.vue';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(id: string): AnnotationDto {
  return {
    annotationId: id,
    sessionId: 'sess-1',
    kind: 'Event',
    body: `Body of ${id}`,
    tags: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
  };
}

describe('AnnotationList', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('List_RendersAnnotations', () => {
    const wrapper = mount(AnnotationList, {
      props: { annotations: [makeAnnotation('a1'), makeAnnotation('a2')] },
    });
    expect(wrapper.findAll('.annotation-list__item')).toHaveLength(2);
  });

  it('List_ClickRowEmitsSelect', async () => {
    const ann = makeAnnotation('a1');
    const wrapper = mount(AnnotationList, { props: { annotations: [ann] } });
    await wrapper.find('.annotation-list__item').trigger('click');
    expect(wrapper.emitted('select')).toBeTruthy();
    expect(wrapper.emitted('select')![0][0]).toMatchObject({ annotationId: 'a1' });
  });

  it('List_EditButtonEmitsEdit', async () => {
    const ann = makeAnnotation('a1');
    const wrapper = mount(AnnotationList, { props: { annotations: [ann] } });
    await wrapper.find('.annotation-list__edit-btn').trigger('click');
    expect(wrapper.emitted('edit')).toBeTruthy();
    expect(wrapper.emitted('edit')![0][0]).toMatchObject({ annotationId: 'a1' });
  });
});
```

### 2.6 Integration tests (2 tests) — in `tests/unit/EventInspector.spec.ts`

Add to the existing `EventInspector.spec.ts` file. Study it first.

```typescript
it('Inspector_ShowsAddNoteButton', () => {
  // Mount EventInspector with a displayEvent (however the existing tests set up a displayed event)
  // Assert: .event-inspector__add-note button exists
  // Look at how existing tests mount EventInspector and follow the same pattern
});

it('Inspector_OpenEditor_OnAddNote', async () => {
  // Mount EventInspector with a displayEvent
  // Click .event-inspector__add-note
  // Assert: AnnotationEditor component is visible (visible prop is true or the element exists)
});
```

> Adapt these to the specific mounting pattern in the existing spec. If the existing tests use `props.eventId` to trigger an event fetch, use the same approach.

---

## ⚠️ Quality Standards

- `AnnotationEditor__save` must have `disabled` attribute when body is empty — not just CSS disabled
- `AnnotationMarker` must not render when store has 0 matching annotations
- Do NOT break any existing tests
- Run full suite at end to verify 0 regressions
- The `minPx` prop on `AnnotationMarker` is a design hint — implementing the pixel-threshold logic would require parent knowledge. For this batch, accept the prop but don't implement canvas density suppression (canvas integration is out of scope here). Just ensure the prop is accepted without TypeScript errors.

---

## 📊 Expected Test Counts

| Suite | New Tests |
|-------|-----------|
| AnnotationMarker.spec.ts | 5 |
| EventInspector.spec.ts (additions) | 5 (3 marker + 2 editor) |
| AnnotationEditor.spec.ts | 8 |
| AnnotationList.spec.ts | 3 |
| **Total** | **21** |

---

## 📝 Report Requirements

Write to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-46-REPORT.md`

Include:
- Files created/modified table
- Test results per file (counts + pass/fail)
- Issues encountered and how resolved
- Design decisions
- Suggested git commit message
