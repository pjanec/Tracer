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
