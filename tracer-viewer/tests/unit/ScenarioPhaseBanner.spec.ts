import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ScenarioPhaseBanner from '@/components/ScenarioPhaseBanner.vue';
import type { SessionDto, ScenarioPhaseDto } from '@/api/tracerApiClient';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getScenarioPhases: vi.fn().mockResolvedValue([]),
  },
}));

import { api } from '@/api/tracerApiClient';

const stubSession: SessionDto = {
  sessionId: 'sess-1',
  scenarioId: 'CombatEngagement',
  startUtc: '2025-01-01T00:00:00Z',
  status: 'Active',
  participatingNodes: [],
  eventCount: 0,
};

function makePhase(name: string, status: string, endedAtUtc?: string): ScenarioPhaseDto {
  return {
    phaseName: name,
    startedAtUtc: '2025-01-01T00:00:00Z',
    endedAtUtc,
    status,
  };
}

describe('ScenarioPhaseBanner', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(api.getScenarioPhases).mockResolvedValue([]);
  });

  it('RendersOneRowPerPhase', async () => {
    vi.mocked(api.getScenarioPhases).mockResolvedValue([
      makePhase('opening', 'Completed', '2025-01-01T00:05:00Z'),
      makePhase('engagement', 'Active'),
    ]);

    const wrapper = mount(ScenarioPhaseBanner, {
      global: { plugins: [createPinia()] },
      props: { session: stubSession },
    });

    await flushPromises();

    const rows = wrapper.findAll('.scenario-phase-banner__row');
    expect(rows.length).toBe(2);
  });

  it('ActivePhase_OmitsEndTime', async () => {
    vi.mocked(api.getScenarioPhases).mockResolvedValue([
      makePhase('engagement', 'Active'),
    ]);

    const wrapper = mount(ScenarioPhaseBanner, {
      global: { plugins: [createPinia()] },
      props: { session: stubSession },
    });

    await flushPromises();

    expect(wrapper.find('.scenario-phase-banner__end').exists()).toBe(false);
  });

  it('CompletedPhase_ShowsFormattedEndTime', async () => {
    vi.mocked(api.getScenarioPhases).mockResolvedValue([
      makePhase('opening', 'Completed', '2025-01-01T12:00:00Z'),
    ]);

    const wrapper = mount(ScenarioPhaseBanner, {
      global: { plugins: [createPinia()] },
      props: { session: stubSession },
    });

    await flushPromises();

    const endEl = wrapper.find('.scenario-phase-banner__end');
    expect(endEl.exists()).toBe(true);
    expect(endEl.text().length).toBeGreaterThan(0);
  });
});
