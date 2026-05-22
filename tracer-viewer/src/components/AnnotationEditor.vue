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
