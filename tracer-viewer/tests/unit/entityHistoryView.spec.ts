import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import { useEntityHistoryStore } from '../../src/stores/entityHistoryStore';
import type { EntitySummaryDto, EntityEventsDto, EntityEventDto } from '../../src/api/tracerApiClient';

const mockRouterPush = vi.fn();

// Mock vue-router — preserve real createRouter/createWebHistory for the router test below
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>();
  return {
    ...actual,
    useRouter: vi.fn(() => ({ push: mockRouterPush })),
  };
});

// Mock composables so they don't try to use Vue Router or make API calls
vi.mock('../../src/composables/useEntityHistoryQuery', () => ({
  useEntityHistoryQuery: vi.fn(),
}));
vi.mock('../../src/composables/useEntityHistoryUrl', () => ({
  useEntityHistoryUrl: vi.fn(() => ({
    fastStateTopic: { value: null },
    fastStateColumns: { value: [] },
  })),
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

function makeEntityEvent(override?: Partial<EntityEventDto>): EntityEventDto {
  return {
    eventId: 'evt-abc123',
    traceId: 'trace-ff00',
    occurredAtUtc: '2026-01-01T10:00:10.000Z', // t = 10000ms from epoch? No, just ISO
    topic: 'player.moved',
    publisherNode: 'node-1',
    ...override,
  };
}

describe('EntityHistoryView', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
    mockRouterPush.mockReset();
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

  // --- TRC-P7-018 SC-4..7: pivot action tests ---

  it('showInTimeline_NavigatesWithCorrectRoute', async () => {
    const store = useEntityHistoryStore();
    store.summary = makeSummary();
    const ev = makeEntityEvent({ occurredAtUtc: '2026-06-01T12:00:00.000Z', eventId: 'evt-timeline' });
    store.events = { entityId: 'ent-1', events: [ev], truncated: false };
    store.sessionId = 'sess-nav';
    store.selectedEventId = 'evt-timeline';

    const wrapper = mountView();
    const timelineBtn = wrapper.find('.entity-history-view__pivot-btn');
    expect(timelineBtn.exists()).toBe(true);
    await timelineBtn.trigger('click');

    const t = new Date('2026-06-01T12:00:00.000Z').getTime();
    expect(mockRouterPush).toHaveBeenCalledWith({
      name: 'timeline',
      params: { sessionId: 'sess-nav' },
      query: {
        from: new Date(t - 2000).toISOString(),
        to: new Date(t + 2000).toISOString(),
        select: 'evt-timeline',
      },
    });
  });

  it('showCausalTree_VisibleWhenTraceIdNonZero', async () => {
    const store = useEntityHistoryStore();
    store.summary = makeSummary();
    const ev = makeEntityEvent({ traceId: '42abcdef', eventId: 'evt-causal' });
    store.events = { entityId: 'ent-1', events: [ev], truncated: false };
    store.sessionId = 'sess-nav';
    store.selectedEventId = 'evt-causal';

    const wrapper = mountView();
    const pivotBtns = wrapper.findAll('.entity-history-view__pivot-btn');
    const causalBtn = pivotBtns.find((b) => b.text().includes('causal'));
    expect(causalBtn).toBeTruthy();
    expect(causalBtn!.attributes('disabled')).toBeUndefined();

    await causalBtn!.trigger('click');
    expect(mockRouterPush).toHaveBeenCalledWith({
      name: 'causal-by-event',
      params: { eventId: 'evt-causal' },
    });
  });

  it('showCausalTree_DisabledWhenTraceIdIsZero', async () => {
    const store = useEntityHistoryStore();
    store.summary = makeSummary();
    const ev = makeEntityEvent({ traceId: '0', eventId: 'evt-notrace' });
    store.events = { entityId: 'ent-1', events: [ev], truncated: false };
    store.sessionId = 'sess-nav';
    store.selectedEventId = 'evt-notrace';

    const wrapper = mountView();
    const pivotBtns = wrapper.findAll('.entity-history-view__pivot-btn');
    const causalBtn = pivotBtns.find((b) => b.text().includes('causal'));
    expect(causalBtn).toBeTruthy();
    expect(causalBtn!.attributes('disabled')).toBeDefined();
  });

  it('pivotActions_AbsentWhenNoSelectedEvent', async () => {
    const store = useEntityHistoryStore();
    store.summary = makeSummary();
    store.events = makeEvents();
    store.selectedEventId = null;

    const wrapper = mountView();
    expect(wrapper.find('.entity-history-view__pivot-actions').exists()).toBe(false);
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
