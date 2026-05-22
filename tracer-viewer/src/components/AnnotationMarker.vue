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
