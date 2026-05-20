import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotableEventsFeed from '@/components/NotableEventsFeed.vue';
import type { NotableEventDto } from '@/api/tracerApiClient';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getScenarioNotables: vi.fn().mockResolvedValue([]),
  },
}));

import { api } from '@/api/tracerApiClient';

function makeEvent(id: string): NotableEventDto {
  return {
    eventId: id,
    traceId: `trace-${id}`,
    occurredAtUtc: '2025-01-01T00:00:00Z',
    topic: 'combat.event',
    notableLabel: `Label-${id}`,
  };
}

describe('NotableEventsFeed', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(api.getScenarioNotables).mockResolvedValue([]);
  });

  it('OnMount_CallsGetScenarioNotables_ViaApi', async () => {
    mount(NotableEventsFeed, {
      global: { plugins: [createPinia()] },
      props: { sessionId: 'sess-1', liveEvents: [] },
    });

    await flushPromises();
    expect(api.getScenarioNotables).toHaveBeenCalledWith('sess-1', 100);
  });

  it('ApiError_LoadingSetFalse_ListRemainsEmpty', async () => {
    vi.mocked(api.getScenarioNotables).mockRejectedValueOnce(new Error('Network error'));

    const wrapper = mount(NotableEventsFeed, {
      global: { plugins: [createPinia()] },
      props: { sessionId: 'sess-1', liveEvents: [] },
    });

    await flushPromises();

    // loading is false (finally block ran) and list is empty
    expect(wrapper.find('.notables-feed__loading').exists()).toBe(false);
    expect(wrapper.text()).toContain('No notable events yet.');
  });

  it('InitialLoad_PopulatesInitialEvents', async () => {
    const eventA = makeEvent('A');
    const eventB = makeEvent('B');
    vi.mocked(api.getScenarioNotables).mockResolvedValue([eventA, eventB] as never);

    const wrapper = mount(NotableEventsFeed, {
      global: { plugins: [createPinia()] },
      props: { sessionId: 'sess-1', liveEvents: [] },
    });

    await flushPromises();

    expect(wrapper.findAll('.notable-event-card').length).toBe(2);
  });

  it('LiveAndInitial_MergedInCorrectOrder', async () => {
    const eventB = makeEvent('B');
    const eventC = makeEvent('C');
    vi.mocked(api.getScenarioNotables).mockResolvedValue([eventB] as never);

    const wrapper = mount(NotableEventsFeed, {
      global: { plugins: [createPinia()] },
      props: { sessionId: 'sess-1', liveEvents: [eventC, eventB] },
    });

    await flushPromises();

    // allEvents should be: C first (live), then B (initial, deduplicated)
    const cards = wrapper.findAll('.notable-event-card');
    expect(cards.length).toBe(2);
    expect(cards[0].text()).toContain('Label-C');
    expect(cards[1].text()).toContain('Label-B');
  });
});
