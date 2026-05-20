import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ScenarioView from '@/views/ScenarioView.vue';
import { useSessionStore } from '@/stores/sessionStore';
import type { SessionDto } from '@/api/tracerApiClient';

vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: vi.fn(() => new Promise(() => {})),
}));

const stubSession: SessionDto = {
  sessionId: 'abc12345678901234',
  scenarioId: 'CombatEngagement',
  startUtc: '2025-01-01T00:00:00Z',
  status: 'Active',
  participatingNodes: ['alpha'],
  eventCount: 5,
};

describe('ScenarioView', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Load_CalledWithSessionId_OnMount', async () => {
    const sessionStore = useSessionStore();
    vi.spyOn(sessionStore, 'load').mockResolvedValue();

    mount(ScenarioView, {
      global: {
        plugins: [pinia],
        stubs: {
          ScenarioStatePanel: true,
          ScenarioPhaseBanner: true,
          NotableEventsFeed: true,
          LiveIndicator: true,
          LoadingSpinner: true,
        },
      },
      props: { sessionId: 'abc' },
    });

    await flushPromises();
    expect(sessionStore.load).toHaveBeenCalledWith('abc');
  });

  it('Load_CalledAgain_OnSessionIdChange', async () => {
    const sessionStore = useSessionStore();
    vi.spyOn(sessionStore, 'load').mockResolvedValue();

    const wrapper = mount(ScenarioView, {
      global: {
        plugins: [pinia],
        stubs: {
          ScenarioStatePanel: true,
          ScenarioPhaseBanner: true,
          NotableEventsFeed: true,
          LiveIndicator: true,
          LoadingSpinner: true,
        },
      },
      props: { sessionId: 'abc' },
    });

    await flushPromises();
    await wrapper.setProps({ sessionId: 'def' });
    await flushPromises();

    expect(sessionStore.load).toHaveBeenCalledWith('def');
  });

  it('RefreshTimer_InvokesRefreshState_Every5s', async () => {
    vi.useFakeTimers();

    const sessionStore = useSessionStore();
    vi.spyOn(sessionStore, 'load').mockResolvedValue();
    vi.spyOn(sessionStore, 'refreshState').mockResolvedValue();

    mount(ScenarioView, {
      global: {
        plugins: [pinia],
        stubs: {
          ScenarioStatePanel: true,
          ScenarioPhaseBanner: true,
          NotableEventsFeed: true,
          LiveIndicator: true,
          LoadingSpinner: true,
        },
      },
      props: { sessionId: 'abc' },
    });

    await vi.advanceTimersByTimeAsync(5000);
    expect(sessionStore.refreshState).toHaveBeenCalled();
  });

  it('RefreshTimer_ClearedOnUnmount', async () => {
    vi.useFakeTimers();

    const sessionStore = useSessionStore();
    vi.spyOn(sessionStore, 'load').mockResolvedValue();
    vi.spyOn(sessionStore, 'refreshState').mockResolvedValue();

    const wrapper = mount(ScenarioView, {
      global: {
        plugins: [pinia],
        stubs: {
          ScenarioStatePanel: true,
          ScenarioPhaseBanner: true,
          NotableEventsFeed: true,
          LiveIndicator: true,
          LoadingSpinner: true,
        },
      },
      props: { sessionId: 'abc' },
    });

    await wrapper.unmount();
    await vi.advanceTimersByTimeAsync(5000);
    expect(sessionStore.refreshState).not.toHaveBeenCalled();
  });

  it('ShowsSpinner_WhileLoadingNoSession', async () => {
    const sessionStore = useSessionStore();
    vi.spyOn(sessionStore, 'load').mockImplementation(async () => {
      sessionStore.loading = true;
    });

    const wrapper = mount(ScenarioView, {
      global: {
        plugins: [pinia],
        stubs: {
          ScenarioStatePanel: true,
          ScenarioPhaseBanner: true,
          NotableEventsFeed: true,
          LiveIndicator: true,
          LoadingSpinner: true,
        },
      },
      props: { sessionId: 'abc' },
    });

    await flushPromises();

    expect(wrapper.find('.scenario-view__grid').exists()).toBe(false);
    expect(wrapper.html()).toContain('loading-spinner-stub');
  });

  it('ShowsGrid_WhenSessionIsLoaded', async () => {
    const sessionStore = useSessionStore();
    vi.spyOn(sessionStore, 'load').mockImplementation(async () => {
      sessionStore.current = stubSession;
      sessionStore.loading = false;
    });

    const wrapper = mount(ScenarioView, {
      global: {
        plugins: [pinia],
        stubs: {
          ScenarioStatePanel: true,
          ScenarioPhaseBanner: true,
          NotableEventsFeed: true,
          LiveIndicator: true,
          LoadingSpinner: true,
        },
      },
      props: { sessionId: 'abc' },
    });

    await flushPromises();

    expect(wrapper.find('.scenario-view__grid').exists()).toBe(true);
  });
});
