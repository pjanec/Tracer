<template>
  <div class="save-view-button">
    <button class="save-view-button__bookmark" @click="onBookmarkClick" title="Bookmark current view">
      🔖
    </button>
    <button class="save-view-button__open-dialog" @click="dialogOpen = true">
      Save view
    </button>

    <!-- Inline save dialog -->
    <div v-if="dialogOpen" class="save-view-dialog">
      <div class="save-view-dialog__backdrop" @click.self="dialogOpen = false" />
      <div class="save-view-dialog__box">
        <h3>Save view</h3>
        <input
          v-model="dialogLabel"
          class="save-view-dialog__label-input"
          placeholder="View name…"
          type="text"
        />
        <textarea
          v-model="dialogDescription"
          class="save-view-dialog__desc"
          placeholder="Description (optional)"
          rows="2"
        />
        <div class="save-view-dialog__actions">
          <button @click="dialogOpen = false">Cancel</button>
          <button
            :disabled="!dialogLabel.trim()"
            class="save-view-dialog__save"
            @click="onDialogSave"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import { usePersona } from '@/composables/usePersona';

const props = defineProps<{
  sessionId: string;
  viewType: string;
}>();

const route = useRoute();
const { persona } = usePersona();

const dialogOpen = ref(false);
const dialogLabel = ref('');
const dialogDescription = ref('');

function autoLabel(): string {
  const parts: string[] = [];
  const q = route.query;
  if (q.topic) parts.push(String(Array.isArray(q.topic) ? q.topic[0] : q.topic));
  if (q.trace) parts.push(`trace:${String(q.trace)}`);
  if (q.entity) parts.push(`entity:${String(q.entity)}`);
  if (parts.length === 0) parts.push(props.viewType);
  parts.push(new Date().toISOString().slice(11, 19));
  return parts.join(' · ');
}

async function onBookmarkClick() {
  await api.createSavedView({
    sessionId: props.sessionId,
    kind: 'Bookmark',
    viewType: props.viewType,
    url: route.fullPath,
    label: autoLabel(),
    persona: persona.value,
    author: localStorage.getItem('tracer:authorName') ?? undefined,
  });
}

async function onDialogSave() {
  await api.createSavedView({
    sessionId: props.sessionId,
    kind: 'SavedView',
    viewType: props.viewType,
    url: route.fullPath,
    label: dialogLabel.value.trim(),
    description: dialogDescription.value.trim() || undefined,
    persona: persona.value,
    author: localStorage.getItem('tracer:authorName') ?? undefined,
  });
  dialogOpen.value = false;
  dialogLabel.value = '';
  dialogDescription.value = '';
}
</script>

<style lang="scss">
.save-view-button {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  position: relative;

  &__bookmark {
    background: none;
    border: none;
    cursor: pointer;
    font-size: 1rem;
    padding: 0.25rem;
  }

  &__open-dialog {
    background: none;
    border: 1px solid var(--c-bg-subtle);
    border-radius: 4px;
    cursor: pointer;
    padding: 0.25rem 0.5rem;
    font-size: 0.85rem;
  }
}

.save-view-dialog {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;

  &__backdrop {
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.4);
  }

  &__box {
    position: relative;
    background: var(--c-bg-surface, #fff);
    border-radius: 8px;
    padding: 1.5rem;
    width: 24rem;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;

    h3 {
      margin: 0;
    }
  }

  &__label-input,
  &__desc {
    width: 100%;
    box-sizing: border-box;
    padding: 0.5rem;
    border: 1px solid var(--c-bg-subtle, #ccc);
    border-radius: 4px;
  }

  &__actions {
    display: flex;
    gap: 0.5rem;
    justify-content: flex-end;
  }

  &__save {
    &:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
  }
}
</style>
