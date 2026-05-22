<script setup lang="ts">
import { computed } from 'vue';
import type { BundleLibraryEntryDto } from '@/types/bundle';
import { formatBytes, formatRelative, formatDateRange } from '@/utils/format';

const props = defineProps<{ bundle: BundleLibraryEntryDto }>();

const emit = defineEmits<{
  open: [];
  edit: [];
  delete: [];
  archive: [];
  export: [];
}>();

const isStale = computed(() => {
  if (!props.bundle.lastOpenedAtUtc) return true;
  const daysSince = (Date.now() - new Date(props.bundle.lastOpenedAtUtc).getTime()) / (1000 * 60 * 60 * 24);
  return daysSince >= 30;
});

const sessionRange = computed(() =>
  formatDateRange(props.bundle.sessionStartUtc, props.bundle.sessionEndUtc),
);
</script>

<template>
  <div class="bundle-card" :class="{ 'bundle-card--archived': bundle.isArchived }">
    <div class="bundle-card__header">
      <h3 class="bundle-card__label">{{ bundle.label ?? bundle.bundleId }}</h3>
      <div class="bundle-card__badges">
        <span v-if="bundle.isArchived" class="bundle-card__badge bundle-card__badge--archived">Archived</span>
        <span v-if="isStale && !bundle.isArchived" class="bundle-card__badge bundle-card__badge--stale">Stale</span>
      </div>
    </div>

    <p v-if="bundle.description" class="bundle-card__description">{{ bundle.description }}</p>

    <div class="bundle-card__meta">
      <span class="bundle-card__range">{{ sessionRange }}</span>
      <span class="bundle-card__size">{{ formatBytes(bundle.sizeBytes) }}</span>
    </div>

    <div v-if="bundle.lastOpenedAtUtc" class="bundle-card__last-opened">
      Last opened: {{ formatRelative(bundle.lastOpenedAtUtc) }}
    </div>

    <div v-if="bundle.tags.length" class="bundle-card__tags">
      <span v-for="tag in bundle.tags" :key="tag" class="bundle-card__tag">{{ tag }}</span>
    </div>

    <div class="bundle-card__actions">
      <button class="bundle-card__btn bundle-card__btn--primary" @click="emit('open')">Open</button>
      <button class="bundle-card__btn" @click="emit('edit')">Edit</button>
      <button class="bundle-card__btn" @click="emit('export')">Export</button>
      <button class="bundle-card__btn" @click="emit('archive')">
        {{ bundle.isArchived ? 'Unarchive' : 'Archive' }}
      </button>
      <button class="bundle-card__btn bundle-card__btn--danger" @click="emit('delete')">Delete</button>
    </div>
  </div>
</template>

<style lang="scss">
.bundle-card {
  background: var(--c-bg-surface);
  border: 1px solid var(--c-border);
  border-radius: 8px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;

  &--archived { opacity: 0.6; }

  &__header { display: flex; align-items: flex-start; gap: 0.5rem; }
  &__label { margin: 0; font-size: 0.95rem; font-weight: 600; flex: 1; }
  &__badges { display: flex; gap: 0.25rem; flex-wrap: wrap; }

  &__badge {
    font-size: 0.65rem;
    padding: 0.1rem 0.35rem;
    border-radius: 3px;
    &--archived { background: var(--c-bg-subtle); color: var(--c-text-muted); border: 1px solid var(--c-border); }
    &--stale { background: rgba(255, 165, 0, 0.15); color: orange; border: 1px solid orange; }
  }

  &__description {
    margin: 0;
    font-size: 0.8rem;
    color: var(--c-text-muted);
  }

  &__meta {
    display: flex;
    gap: 1rem;
    font-size: 0.8rem;
    color: var(--c-text-muted);
  }

  &__last-opened { font-size: 0.75rem; color: var(--c-text-muted); }

  &__tags { display: flex; gap: 0.25rem; flex-wrap: wrap; }

  &__tag {
    font-size: 0.7rem;
    padding: 0.1rem 0.35rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 3px;
    color: var(--c-text-muted);
  }

  &__actions { display: flex; gap: 0.4rem; flex-wrap: wrap; margin-top: 0.25rem; }

  &__btn {
    padding: 0.25rem 0.6rem;
    font-size: 0.75rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;

    &:hover { background: var(--c-bg-surface); }
    &--primary { background: var(--c-accent); color: white; border-color: var(--c-accent); &:hover { opacity: 0.85; } }
    &--danger { color: var(--c-danger, #f87171); &:hover { background: var(--c-danger, #f87171); color: white; } }
  }
}
</style>
