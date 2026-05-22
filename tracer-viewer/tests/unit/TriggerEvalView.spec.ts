import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { TriggerEvaluationDto } from '../../src/api/tracerApiClient';

const mockListTriggerEvaluations = vi.fn();
const mockRouterPush = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: { listTriggerEvaluations: mockListTriggerEvaluations },
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { sessionId: 'sess-1' } }),
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeEval(override?: Partial<TriggerEvaluationDto>): TriggerEvaluationDto {
  return {
    eventId: `evt-${Math.random()}`,
    evaluatedAtUtc: '2026-01-01T10:00:00.000Z',
    publisherNode: 'node-1',
    traceId: 'trace-1',
    triggerId: 'trigger-1',
    inputs: '{}',
    result: 'Fired',
    ...override,
  };
}

describe('TriggerEvalView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockListTriggerEvaluations.mockReset();
    mockRouterPush.mockReset();
  });

  it('TriggerEvalView_LoadsOnMount', async () => {
    const evals = Array.from({ length: 5 }, (_, i) =>
      makeEval({ eventId: `evt-${i}`, triggerId: `t-${i}` }),
    );
    mockListTriggerEvaluations.mockResolvedValue(evals);
    const { default: TriggerEvalView } = await import('../../src/views/TriggerEvalView.vue');
    const wrapper = mount(TriggerEvalView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    // Each TriggerEvalRow renders 1 <tr> when not expanded, so 5 rows in tbody
    const rows = wrapper.find('tbody').findAll('tr');
    expect(rows.length).toBe(5);
  });

  it('TriggerEvalView_LoadingState', async () => {
    let resolveApi!: (v: TriggerEvaluationDto[]) => void;
    mockListTriggerEvaluations.mockReturnValue(
      new Promise<TriggerEvaluationDto[]>((r) => { resolveApi = r; }),
    );
    const { default: TriggerEvalView } = await import('../../src/views/TriggerEvalView.vue');
    const wrapper = mount(TriggerEvalView, { props: { sessionId: 'sess-1' } });
    await wrapper.vm.$nextTick();

    expect(wrapper.find('.trigger-eval-view__loading').exists()).toBe(true);

    resolveApi([]);
    await flushPromises();
  });

  it('TriggerEvalView_EmptyState', async () => {
    mockListTriggerEvaluations.mockResolvedValue([]);
    const { default: TriggerEvalView } = await import('../../src/views/TriggerEvalView.vue');
    const wrapper = mount(TriggerEvalView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    expect(wrapper.find('.trigger-eval-view__empty').exists()).toBe(true);
  });

  it('TriggerEvalView_ResultFilterChange_Refetches', async () => {
    mockListTriggerEvaluations.mockResolvedValue([]);
    const { default: TriggerEvalView } = await import('../../src/views/TriggerEvalView.vue');
    const wrapper = mount(TriggerEvalView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    const selects = wrapper.findAll('select');
    // Second select is result filter
    await selects[1].setValue('fired');
    await flushPromises();

    const lastCall = mockListTriggerEvaluations.mock.calls[mockListTriggerEvaluations.mock.calls.length - 1][0];
    expect(lastCall.result).toBe('fired');
  });

  it('TriggerEvalView_DistinctTriggerIds_PopulateSelect', async () => {
    const evals = [
      makeEval({ triggerId: 'trig-a' }),
      makeEval({ triggerId: 'trig-a' }),
      makeEval({ triggerId: 'trig-b' }),
      makeEval({ triggerId: 'trig-c' }),
      makeEval({ triggerId: 'trig-b' }),
    ];
    mockListTriggerEvaluations.mockResolvedValue(evals);
    const { default: TriggerEvalView } = await import('../../src/views/TriggerEvalView.vue');
    const wrapper = mount(TriggerEvalView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    const triggerSelect = wrapper.findAll('select')[0];
    const options = triggerSelect.findAll('option');
    // 1 "All triggers" + 3 distinct
    expect(options.length).toBe(4);
  });

  it('TriggerEvalRow_FiredPill_HasCorrectClass', async () => {
    const { default: TriggerEvalRow } = await import('../../src/components/TriggerEvalRow.vue');
    const wrapper = mount(TriggerEvalRow, {
      props: {
        evaluation: makeEval({ result: 'Fired' }),
        sessionId: 'sess-1',
      },
    });
    expect(wrapper.find('.trigger-eval-view__pill--Fired').exists()).toBe(true);
  });

  it('TriggerEvalRow_NotFiredPill_HasCorrectClass', async () => {
    const { default: TriggerEvalRow } = await import('../../src/components/TriggerEvalRow.vue');
    const wrapper = mount(TriggerEvalRow, {
      props: {
        evaluation: makeEval({ result: 'NotFired' }),
        sessionId: 'sess-1',
      },
    });
    expect(wrapper.find('.trigger-eval-view__pill--NotFired').exists()).toBe(true);
  });
});
