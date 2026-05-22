import { defineStore } from 'pinia';
import type { AnnotationDto } from '@/api/tracerApiClient';

export const useAnnotationStore = defineStore('annotations', {
  state: () => ({
    // keyed by annotationId
    _map: {} as Record<string, AnnotationDto>,
  }),
  getters: {
    all: (state): AnnotationDto[] => Object.values(state._map),
    byEventId: (state) => (eventId: string): AnnotationDto[] =>
      Object.values(state._map).filter(a => a.eventId === eventId),
    byEntityId: (state) => (entityId: string): AnnotationDto[] =>
      Object.values(state._map).filter(a => a.entityId === entityId),
    byTraceId: (state) => (traceId: string): AnnotationDto[] =>
      Object.values(state._map).filter(a => a.traceId === traceId),
  },
  actions: {
    load(annotations: AnnotationDto[]) {
      // Replace map entirely — prevents duplication on double-load
      const next: Record<string, AnnotationDto> = {};
      for (const a of annotations) next[a.annotationId] = a;
      this._map = next;
    },
    upsert(annotation: AnnotationDto) {
      this._map = { ...this._map, [annotation.annotationId]: annotation };
    },
    remove(annotationId: string) {
      const { [annotationId]: _removed, ...rest } = this._map;
      this._map = rest;
    },
    clear() {
      this._map = {};
    },
  },
});
