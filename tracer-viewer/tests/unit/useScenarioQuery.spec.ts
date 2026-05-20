import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ref } from 'vue';
import { setActivePinia, createPinia } from 'pinia';
import { createApp } from 'vue';
import { flushPromises } from '@vue/test-utils';
import { useScenarioQuery } from '@/composables/useScenarioQuery';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getScenarioNotables: vi.fn().mockResolvedValue([]),
    getScenarioPhases: vi.fn().mockResolvedValue([]),
    getScenarioState: vi.fn().mockResolvedValue({
      currentPhase: 'opening',
      sessionElapsed: 'PT0S',
      totalEvents: 0,
      totalNotables: 0,
    }),
  },
}));

import { api } from '@/api/tracerApiClient';

function withSetup<T>(composable: () => T): [T, () => void] {
  let result!: T;
  const pinia = createPinia();
  setActivePinia(pinia);
  const app = createApp({
    setup() {
      result = composable();
      return () => null;
    },
  });
  app.use(pinia);
  app.mount(document.createElement('div'));
  return [result, () => app.unmount()];
}

describe('useScenarioQuery', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.mocked(api.getScenarioNotables).mockResolvedValue([]);
    vi.mocked(api.getScenarioPhases).mockResolvedValue([]);
    vi.mocked(api.getScenarioState).mockResolvedValue({
      currentPhase: 'opening',
      sessionElapsed: 'PT0S',
      totalEvents: 0,
      totalNotables: 0,
    });
  });

  it('Load_SetsLoadingTrueThenFalse', async () => {
    let resolveNotables!: (v: never[]) => void;
    const pendingNotables = new Promise<never[]>(res => { resolveNotables = res; });
    vi.mocked(api.getScenarioNotables).mockReturnValueOnce(pendingNotables as never);

    const sessionId = ref('sess-1');
    const [result, unmount] = withSetup(() => useScenarioQuery(sessionId));

    // loading is true while the call is in-flight
    expect(result.loading.value).toBe(true);

    resolveNotables([]);
    await flushPromises();

    expect(result.loading.value).toBe(false);
    unmount();
  });

  it('Load_PopulatesNotablesPhasesAndState', async () => {
    const notableEvent = {
      eventId: 'ev-1',
      traceId: 'tr-1',
      occurredAtUtc: '2025-01-01T00:00:00Z',
      topic: 'combat',
      notableLabel: 'Hit',
    };
    vi.mocked(api.getScenarioNotables).mockResolvedValue([notableEvent] as never);
    vi.mocked(api.getScenarioPhases).mockResolvedValue([{ phaseName: 'opening', startedAtUtc: '2025-01-01T00:00:00Z', status: 'Active' }] as never);
    vi.mocked(api.getScenarioState).mockResolvedValue({
      currentPhase: 'opening',
      sessionElapsed: 'PT10S',
      totalEvents: 1,
      totalNotables: 1,
    } as never);

    const sessionId = ref('sess-1');
    const [result, unmount] = withSetup(() => useScenarioQuery(sessionId));
    await flushPromises();

    expect(result.notables.value.length).toBe(1);
    expect(result.phases.value.length).toBe(1);
    expect(result.state.value?.currentPhase).toBe('opening');
    unmount();
  });

  it('Load_OnApiError_SetsErrorRefAndClearsLoading', async () => {
    vi.mocked(api.getScenarioNotables).mockRejectedValueOnce(new Error('Network error'));

    const sessionId = ref('sess-1');
    const [result, unmount] = withSetup(() => useScenarioQuery(sessionId));
    await flushPromises();

    expect(result.error.value).toBeTruthy();
    expect(result.loading.value).toBe(false);
    unmount();
  });

  it('ReactiveSessionId_ReloadsOnChange', async () => {
    const sessionId = ref('sess-1');
    const [, unmount] = withSetup(() => useScenarioQuery(sessionId));
    await flushPromises();

    const countBefore = vi.mocked(api.getScenarioNotables).mock.calls.length;

    sessionId.value = 'sess-2';
    await flushPromises();

    expect(vi.mocked(api.getScenarioNotables).mock.calls.length).toBeGreaterThan(countBefore);
    unmount();
  });
});
