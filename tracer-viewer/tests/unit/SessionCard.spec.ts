import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import SessionCard from '@/components/SessionCard.vue';
import type { SessionDto } from '@/api/tracerApiClient';

function makeSession(overrides: Partial<SessionDto> = {}): SessionDto {
  return {
    sessionId: 'sess-1',
    scenarioId: 'CombatEngagement',
    startUtc: '2025-01-01T12:00:00Z',
    status: 'Active',
    participatingNodes: ['alpha', 'beta'],
    eventCount: 42,
    ...overrides,
  };
}

describe('SessionCard', () => {
  it('RendersScenarioId', () => {
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ scenarioId: 'CombatEngagement' }) },
    });

    expect(wrapper.find('.session-card__scenario').text()).toBe('CombatEngagement');
  });

  it('RendersFormattedStartUtc', () => {
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ startUtc: '2025-01-01T12:00:00Z' }) },
    });

    // formatTime returns a locale string; just assert something is rendered
    expect(wrapper.find('.session-card__time').text().length).toBeGreaterThan(0);
  });

  it('RendersStatusBadge', () => {
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ status: 'Active' }) },
    });

    const badge = wrapper.find('.session-card__status');
    expect(badge.exists()).toBe(true);
    expect(badge.text()).toBe('Active');
  });

  it('RendersEventCount', () => {
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ eventCount: 42 }) },
    });

    expect(wrapper.text()).toContain('42');
  });

  it('RendersNodeCount', () => {
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ participatingNodes: ['alpha', 'beta'] }) },
    });

    expect(wrapper.text()).toContain('2 node(s)');
  });
});
