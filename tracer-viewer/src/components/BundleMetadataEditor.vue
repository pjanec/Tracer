<script setup lang="ts">
import { ref } from 'vue';
import type { BundleLibraryEntryDto, UpdateBundleMetadataDto } from '@/types/bundle';

const props = defineProps<{ bundle: BundleLibraryEntryDto }>();

const emit = defineEmits<{
  save: [dto: UpdateBundleMetadataDto];
  cancel: [];
}>();

const label = ref(props.bundle.label ?? '');
const description = ref(props.bundle.description ?? '');
const tagsInput = ref(props.bundle.tags.join(', '));
const isArchived = ref(props.bundle.isArchived);

function save() {
  const tags = tagsInput.value
    .split(',')
    .map(t => t.trim())
    .filter(t => t.length > 0);
  emit('save', {
    label: label.value || undefined,
    description: description.value || undefined,
    tags,
    isArchived: isArchived.value,
  });
}
</script>

<template>
  <div class="bundle-metadata-editor">
    <div class="bundle-metadata-editor__overlay" @click="emit('cancel')" />
    <div class="bundle-metadata-editor__dialog">
      <h3 class="bundle-metadata-editor__title">Edit bundle</h3>

      <label class="bundle-metadata-editor__field">
        <span class="bundle-metadata-editor__field-label">Label</span>
        <input v-model="label" type="text" class="bundle-metadata-editor__input" />
      </label>

      <label class="bundle-metadata-editor__field">
        <span class="bundle-metadata-editor__field-label">Description</span>
        <textarea v-model="description" class="bundle-metadata-editor__textarea" rows="3" />
      </label>

      <label class="bundle-metadata-editor__field">
        <span class="bundle-metadata-editor__field-label">Tags (comma-separated)</span>
        <input v-model="tagsInput" type="text" class="bundle-metadata-editor__input" />
      </label>

      <label class="bundle-metadata-editor__field bundle-metadata-editor__field--checkbox">
        <input v-model="isArchived" type="checkbox" />
        Archived
      </label>

      <div class="bundle-metadata-editor__actions">
        <button class="bundle-metadata-editor__btn bundle-metadata-editor__btn--primary" @click="save">Save</button>
        <button class="bundle-metadata-editor__btn" @click="emit('cancel')">Cancel</button>
      </div>
    </div>
  </div>
</template>

<style lang="scss">
.bundle-metadata-editor {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;

  &__overlay {
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
  }

  &__dialog {
    position: relative;
    background: var(--c-bg-surface);
    border: 1px solid var(--c-border);
    border-radius: 8px;
    padding: 1.5rem;
    width: 480px;
    z-index: 1;
    display: flex;
    flex-direction: column;
    gap: 1rem;
  }

  &__title { margin: 0; font-size: 1rem; }

  &__field {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    &--checkbox { flex-direction: row; align-items: center; gap: 0.5rem; }
  }

  &__field-label { font-size: 0.8rem; font-weight: 600; color: var(--c-text-muted); }

  &__input, &__textarea {
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
    font-family: inherit;
    resize: vertical;
  }

  &__actions { display: flex; gap: 0.5rem; }

  &__btn {
    padding: 0.35rem 0.8rem;
    font-size: 0.85rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
    &:hover { background: var(--c-bg-surface); }
    &--primary { background: var(--c-accent); color: white; border-color: var(--c-accent); &:hover { opacity: 0.85; } }
  }
}
</style>
