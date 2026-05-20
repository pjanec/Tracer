import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import ScenarioStatePanel from '@/components/ScenarioStatePanel.vue';
import type { SessionDto, ScenarioStateDto } from '@/api/tracerApiClient';

function makeSession(overrides: Partial<SessionDto> = {}): SessionDto {
  return {
    sessionId: 'sess-1',
    scenarioId: 'CombatEngagement',
    startUtc: '2025-01-01T00:00:00Z',
    status: 'Active',
    participatingNodes: [],
    eventCount: 0,
    ...overrides,
  };
}

function makeState(overrides: Partial<ScenarioStateDto> = {}): ScenarioStateDto {
  return {
    currentPhase: 'unknown',
    sessionElapsed: 'PT0S',
    totalEvents: 0,
    totalNotables: 0,
    ...overrides,
  };
}

describe('ScenarioStatePanel', () => {
  it('ShowsCurrentPhase', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeSession(),
        state: makeState({ currentPhase: 'engagement' }),
      },
    });

    const phaseEl = wrapper.find('.scenario-state-panel__value--phase');
    expect(phaseEl.text()).toContain('engagement');
  });

  it('ShowsElapsedTime', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeSession(),
        state: makeState({ sessionElapsed: 'PT5M30S' }),
      },
    });

    expect(wrapper.text()).toContain('PT5M30S');
  });

  it('NullState_ShowsDashes', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeSession(),
        state: null,
      },
    });

    const dashes = wrapper.text().match(/—/g);
    expect(dashes).not.toBeNull();
    expect(dashes!.length).toBeGreaterThanOrEqual(2);
  });

  it('StatusActive_AppliesActiveClass', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeSession({ status: 'Active' }),
        state: makeState(),
      },
    });

    const statusEl = wrapper.find('.scenario-state-panel__value--status');
    expect(statusEl.classes()).toContain('scenario-state-panel__value--active');
  });

  it('StatusCompleted_AppliesCompletedClass', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeSession({ status: 'Completed' }),
        state: makeState(),
      },
    });

    const statusEl = wrapper.find('.scenario-state-panel__value--status');
    expect(statusEl.classes()).toContain('scenario-state-panel__value--completed');
  });

  it('RendersAllParticipatingNodes', () => {
    const wrapper = mount(ScenarioStatePanel, {
      props: {
        session: makeSession({ participatingNodes: ['alpha', 'beta', 'gamma'] }),
        state: makeState(),
      },
    });

    const nodes = wrapper.findAll('.scenario-state-panel__node');
    expect(nodes.length).toBe(3);
  });
});
