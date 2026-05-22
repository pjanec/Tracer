import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, ref, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

// Mock the API module — MUST be before importing the composable
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listAnnotations: vi.fn(),
    createAnnotation: vi.fn(),
    updateAnnotation: vi.fn(),
    deleteAnnotation: vi.fn(),
  },
}));

import { useAnnotations } from '../../src/composables/useAnnotations';

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

describe('useAnnotations', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it('useAnnotations_LoadsOnMount', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([
      makeAnnotation({ annotationId: 'a1' }),
      makeAnnotation({ annotationId: 'a2' }),
    ]);

    const sessionId = ref<string | null>('sess-1');

    mount(defineComponent({
      setup() {
        useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();
    const { useAnnotationStore } = await import('../../src/stores/annotationStore');
    const store = useAnnotationStore();
    expect(store.all).toHaveLength(2);
  });

  it('useAnnotations_ReloadsOnSessionIdChange', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([]);

    const sessionId = ref<string | null>('sess-1');

    mount(defineComponent({
      setup() {
        useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();
    expect(api.listAnnotations).toHaveBeenCalledWith('sess-1');

    sessionId.value = 'sess-2';
    await nextTick();
    await flushPromises();
    expect(api.listAnnotations).toHaveBeenCalledWith('sess-2');
  });

  it('useAnnotations_Create_AddsToLocalList', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([]);
    const created = makeAnnotation({ annotationId: 'new-1', body: 'new body' });
    (api.createAnnotation as ReturnType<typeof vi.fn>).mockResolvedValue(created);

    const sessionId = ref<string | null>('sess-1');
    let composable: ReturnType<typeof useAnnotations>;

    mount(defineComponent({
      setup() {
        composable = useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();

    const { useAnnotationStore } = await import('../../src/stores/annotationStore');
    const store = useAnnotationStore();
    const beforeCount = store.all.length;

    await composable!.create('new body', 'Event', { eventId: 'X' });

    expect(store.all.length).toBe(beforeCount + 1);
    expect(store.all.some(a => a.annotationId === 'new-1')).toBe(true);
  });

  it('useAnnotations_Remove_DeletesLocalEntry', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const ann = makeAnnotation({ annotationId: 'del-1' });
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([ann]);
    (api.deleteAnnotation as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);

    const sessionId = ref<string | null>('sess-1');
    let composable: ReturnType<typeof useAnnotations>;

    mount(defineComponent({
      setup() {
        composable = useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();

    const { useAnnotationStore } = await import('../../src/stores/annotationStore');
    const store = useAnnotationStore();
    expect(store.all).toHaveLength(1);

    await composable!.remove('del-1');
    expect(store.all).toHaveLength(0);
  });
});
