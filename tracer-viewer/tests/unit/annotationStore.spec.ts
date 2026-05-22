import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useAnnotationStore } from '../../src/stores/annotationStore';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(override?: Partial<AnnotationDto>): AnnotationDto {
  return {
    annotationId: 'ann-1',
    sessionId: 'sess-1',
    kind: 'Event',
    eventId: 'evt-1',
    body: 'Test body',
    tags: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...override,
  };
}

describe('annotationStore', () => {
  beforeEach(() => { setActivePinia(createPinia()); });

  it('annotationStore_ByEventId_ReturnsCorrectAnnotations', () => {
    const store = useAnnotationStore();
    store.load([
      makeAnnotation({ annotationId: 'a1', eventId: 'A' }),
      makeAnnotation({ annotationId: 'a2', eventId: 'A' }),
      makeAnnotation({ annotationId: 'a3', eventId: 'B' }),
    ]);
    expect(store.byEventId('A')).toHaveLength(2);
    expect(store.byEventId('B')).toHaveLength(1);
    expect(store.byEventId('C')).toHaveLength(0);
  });

  it('annotationStore_NoDuplicatesOnDoubleLoad', () => {
    const store = useAnnotationStore();
    const data = [
      makeAnnotation({ annotationId: 'a1' }),
      makeAnnotation({ annotationId: 'a2' }),
    ];
    store.load(data);
    store.load(data);
    expect(store.all).toHaveLength(2);
  });

  it('annotationStore_IsEmpty_WhenNoSessionLoaded', () => {
    const store = useAnnotationStore();
    expect(store.byEventId('any-id')).toHaveLength(0);
    expect(store.all).toHaveLength(0);
  });

  it('annotationStore_Remove_DeletesEntry', () => {
    const store = useAnnotationStore();
    store.load([makeAnnotation({ annotationId: 'a1' })]);
    store.remove('a1');
    expect(store.all).toHaveLength(0);
  });
});
