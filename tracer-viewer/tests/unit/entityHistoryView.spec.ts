import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent } from 'vue';
import { mount } from '@vue/test-utils';
import { useEntityHistoryStore } from '../../src/stores/entityHistoryStore';
import type { EntitySummaryDto, EntityEventsDto } from '../../src/api/tracerApiClient';

// Mock composables so they don't try to use Vue Router or make API calls
vi.mock('../../src/composables/useEntityHistoryQuery', () => ({
  useEntityHistoryQuery: vi.fn(),
}));
vi.mock('../../src/composables/useEntityHistoryUrl', () => ({
  useEntityHistoryUrl: vi.fn(),
}));

// Mock API to prevent network calls
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getEntitySummary: vi.fn(),
    getEntityEvents: vi.fn(),
    getEntitySlowState: vi.fn(),
    getEntityFastStateTopics: vi.fn(),
  },
}));

import EntityHistoryView from '../../src/views/EntityHistoryView.vue';

function makeSummary(override?: Partial<EntitySummaryDto>): EntitySummaryDto {
  return {
    entityId: 'ent-1',
    firstSeenUtc: '2026-01-01T10:00:00.000Z',
    lastSeenUtc: '2026-01-01T11:00:00.000Z',
    eventCount: 5,
    topics: [],
    ...override,
  };
}

function makeEvents(): EntityEventsDto {
  return { entityId: 'ent-1', events: [], truncated: false };
}

describe('EntityHistoryView', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  function mountView() {
    return mount(EntityHistoryView, { global: { plugins: [pinia] } });
  }

  it('rendersLoadingState_WhenLoadingAndNoSummary', () => {
    const store = useEntityHistoryStore();
    store.loading = true;
    store.summary = null;

    const wrapper = mountView();

    expect(wrapper.find('.entity-history-view__loading').exists()).toBe(true);
    expect(wrapper.find('.entity-history-view__error').exists()).toBe(false);
  });

  it('rendersErrorState_WithRetryButton_WhenErrorAndNoSummary', async () => {
    const store = useEntityHistoryStore();
    store.loading = false;
    store.error = 'Entity not found';
    store.summary = null;

    const wrapper = mountView();

    expect(wrapper.find('.entity-history-view__error').exists()).toBe(true);
    const retryBtn = wrapper.find('.entity-history-view__retry');
    expect(retryBtn.exists()).toBe(true);

    // Clicking retry calls store.retry()
    const retrySpy = vi.spyOn(store, 'retry');
    await retryBtn.trigger('click');
    expect(retrySpy).toHaveBeenCalled();
  });

  it('rendersPanelStack_WhenSummaryAndTwoSlowStateTopics', async () => {
    const store = useEntityHistoryStore();
    store.loading = false;
    store.error = null;
    store.summary = makeSummary();
    store.events = makeEvents();
    store.slowStateByTopic = {
      'topic-a': [],
      'topic-b': [],
    };
    store.fastStateTopics = [];

    const wrapper = mountView();

    expect(wrapper.find('.entity-summary-strip').exists()).toBe(true);
    expect(wrapper.find('.entity-lifecycle-ribbon').exists()).toBe(true);
    const slowStateCharts = wrapper.findAll('.slow-state-chart');
    expect(slowStateCharts.length).toBe(2);
    expect(wrapper.find('.entity-event-strip').exists()).toBe(true);
    expect(wrapper.find('.fast-state-drill-down').exists()).toBe(true);
  });

  it('smokeTest_MountsWithoutError_WhenEmptySlowState', () => {
    const store = useEntityHistoryStore();
    store.summary = makeSummary();
    store.events = makeEvents();
    store.slowStateByTopic = {};

    expect(() => mountView()).not.toThrow();
  });
});

describe('entityHistoryStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('setEntity_ClearsPriorData', () => {
    const store = useEntityHistoryStore();
    store.summary = makeSummary();
    store.events = makeEvents();
    store.slowStateByTopic = { a: [] };
    store.fastStateTopics = ['t1'];
    store.selectedEventId = 'evt-1';
    store.error = 'old error';

    store.setEntity('ent-new', 'sess-new');

    expect(store.entityId).toBe('ent-new');
    expect(store.sessionId).toBe('sess-new');
    expect(store.summary).toBeNull();
    expect(store.events).toBeNull();
    expect(store.slowStateByTopic).toEqual({});
    expect(store.fastStateTopics).toEqual([]);
    expect(store.selectedEventId).toBeNull();
    expect(store.error).toBeNull();
  });

  it('setSummary_DefaultsTimeRange_WhenFromEqualsTo', () => {
    const store = useEntityHistoryStore();
    // Default state: from === to (both are new Date() at roughly same time)
    // Force them to be exactly equal:
    const now = new Date();
    store.timeRange = { from: now, to: now };

    store.setSummary(makeSummary({
      firstSeenUtc: '2026-01-01T10:00:00.000Z',
      lastSeenUtc: '2026-01-01T11:00:00.000Z',
    }));

    expect(store.timeRange.from.toISOString()).toBe('2026-01-01T10:00:00.000Z');
    expect(store.timeRange.to.toISOString()).toBe('2026-01-01T11:00:00.000Z');
  });

  it('setSummary_DoesNotOverrideExplicitTimeRange_WhenFromDiffersFromTo', () => {
    const store = useEntityHistoryStore();
    store.setTimeRange(
      new Date('2026-01-01T09:00:00.000Z'),
      new Date('2026-01-01T10:30:00.000Z'),
    );

    store.setSummary(makeSummary({
      firstSeenUtc: '2026-01-01T10:00:00.000Z',
      lastSeenUtc: '2026-01-01T11:00:00.000Z',
    }));

    // User-set range must be preserved
    expect(store.timeRange.from.toISOString()).toBe('2026-01-01T09:00:00.000Z');
    expect(store.timeRange.to.toISOString()).toBe('2026-01-01T10:30:00.000Z');
  });
});

describe('entityHistory router', () => {
  it('routerResolve_EntityHistory_HasCorrectPath', async () => {
    vi.resetModules();
    const router = (await import('../../src/router/index')).default;
    const resolved = router.resolve({ name: 'entity-history', params: { entityId: 'e1' } });
    expect(resolved.href).toBe('/v/entity/e1');
  });
});
