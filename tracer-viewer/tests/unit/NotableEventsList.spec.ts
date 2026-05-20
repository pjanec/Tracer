import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotableEventsList from '@/components/NotableEventsList.vue';
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

describe('NotableEventsList', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(api.getScenarioNotables).mockResolvedValue([]);
  });

  it('MergesInitialAndLiveEvents_LiveFirst', async () => {
    const eventA = makeEvent('A');
    const eventB = makeEvent('B');
    const eventC = makeEvent('C');

    vi.mocked(api.getScenarioNotables).mockResolvedValue([eventA, eventB]);

    const wrapper = mount(NotableEventsList, {
      global: { plugins: [createPinia()] },
      props: {
        sessionId: 'session-1',
        liveEvents: [eventC, eventA],
      },
    });

    await flushPromises();

    // allEvents should be: C, A, B (live first, deduplicated, then initial)
    const cards = wrapper.findAll('.notable-event-card');
    expect(cards.length).toBe(3);
    expect(cards[0].text()).toContain('Label-C');
    expect(cards[1].text()).toContain('Label-A');
    expect(cards[2].text()).toContain('Label-B');
  });

  it('DeduplicatesEventsByEventId', async () => {
    const eventX = makeEvent('X');
    vi.mocked(api.getScenarioNotables).mockResolvedValue([eventX]);

    const wrapper = mount(NotableEventsList, {
      global: { plugins: [createPinia()] },
      props: {
        sessionId: 'session-1',
        liveEvents: [eventX],
      },
    });

    await flushPromises();

    const cards = wrapper.findAll('.notable-event-card');
    expect(cards.length).toBe(1);
  });

  it('ShowsEmptyState_WhenNoEvents', async () => {
    vi.mocked(api.getScenarioNotables).mockResolvedValue([]);

    const wrapper = mount(NotableEventsList, {
      global: { plugins: [createPinia()] },
      props: {
        sessionId: 'session-1',
        liveEvents: [],
      },
    });

    await flushPromises();

    expect(wrapper.text()).toContain('No notable events yet.');
  });
});
