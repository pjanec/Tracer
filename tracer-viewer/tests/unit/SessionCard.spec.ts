import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises, RouterLinkStub } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createRouter, createMemoryHistory } from 'vue-router';
import type { SessionDto } from '../../src/api/tracerApiClient';

const mockBuildBundle = vi.fn();
vi.mock('@/api/tracerApiClient', () => ({
  api: { buildBundle: mockBuildBundle },
}));

function makeSession(override?: Partial<SessionDto>): SessionDto {
  return {
    sessionId: 'sess-1',
    scenarioId: 'sc-1',
    startUtc: '2026-01-01T00:00:00Z',
    status: 'completed',
    participatingNodes: ['node-1'],
    eventCount: 42,
    ...override,
  };
}

describe('SessionCard', () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof createRouter>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let pushSpy!: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    mockBuildBundle.mockReset();
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'home', component: { template: '<div/>' } },
        { path: '/v/timeline/:sessionId', name: 'timeline', component: { template: '<div/>' } },
        { path: '/scenario/:sessionId', name: 'scenario', component: { template: '<div/>' } },
        { path: '/v/entities/:sessionId', name: 'entity-picker', component: { template: '<div/>' } },
      ],
    });
    pushSpy = vi.spyOn(router, 'push');
  });

  it('buildBundle_showsProgressThenDownloadLink', async () => {
    // First call: simulate in-progress (resolves after we check intermediate state)
    let resolveBuild!: (value: { bundleId: string }) => void;
    mockBuildBundle.mockReturnValue(
      new Promise<{ bundleId: string }>((resolve) => { resolveBuild = resolve; }),
    );

    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const wrapper = mount(SessionCard, {
      props: { sessionId: 'sess-1' },
      global: {
        plugins: [pinia, router],
        stubs: { RouterLink: RouterLinkStub },
      },
    });

    // Click "Build bundle"
    await wrapper.find('.session-card__build-btn').trigger('click');

    // Should show progress indicator
    expect(wrapper.find('.session-card__progress').exists()).toBe(true);
    expect(wrapper.find('.session-card__download').exists()).toBe(false);

    // Now resolve the build
    resolveBuild({ bundleId: 'new-bundle-abc' });
    await flushPromises();

    // Should now show download link
    expect(wrapper.find('.session-card__download').exists()).toBe(true);
    expect(wrapper.find('.session-card__download').attributes('href'))
      .toContain('new-bundle-abc');
  });

  it('SessionCard_Engineer_RoutesToTimeline', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('engineer');

    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ sessionId: 's1' }) },
      global: {
        plugins: [pinia, router],
        stubs: { RouterLink: RouterLinkStub },
      },
    });

    await wrapper.trigger('click');
    expect(pushSpy).toHaveBeenCalledWith({ name: 'timeline', params: { sessionId: 's1' } });
  });

  it('SessionCard_ScenarioAuthor_RoutesToScenario', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('scenario-author');

    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ sessionId: 's2' }) },
      global: {
        plugins: [pinia, router],
        stubs: { RouterLink: RouterLinkStub },
      },
    });

    await wrapper.trigger('click');
    expect(pushSpy).toHaveBeenCalledWith({ name: 'scenario', params: { sessionId: 's2' } });
  });

  it('SessionCard_Operator_RoutesToScenario', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('operator');

    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const wrapper = mount(SessionCard, {
      props: { session: makeSession({ sessionId: 's3' }) },
      global: {
        plugins: [pinia, router],
        stubs: { RouterLink: RouterLinkStub },
      },
    });

    await wrapper.trigger('click');
    expect(pushSpy).toHaveBeenCalledWith({ name: 'scenario', params: { sessionId: 's3' } });
  });
});

