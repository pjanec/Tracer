import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { TriggerEvaluationDto } from '../../src/api/tracerApiClient';

const mockRouterPush = vi.fn();
const mockListTriggerEvaluations = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: { listTriggerEvaluations: mockListTriggerEvaluations },
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { sessionId: 'sess-1' } }),
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeEval(override?: Partial<TriggerEvaluationDto>): TriggerEvaluationDto {
  return {
    eventId: 'evt-abc',
    evaluatedAtUtc: '2026-01-01T10:00:00.000Z',
    publisherNode: 'node-1',
    traceId: 'trace-1',
    triggerId: 'trigger-1',
    inputs: '{"speed":10}',
    result: 'Fired',
    ...override,
  };
}

describe('TriggerEvalRow', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockRouterPush.mockReset();
    mockRouterPush.mockResolvedValue(undefined);
    mockListTriggerEvaluations.mockReset();
  });

  it('TriggerEvalRow_TimelineButton_Navigates', async () => {
    const { default: TriggerEvalRow } = await import('../../src/components/TriggerEvalRow.vue');
    const evaluation = makeEval({ eventId: 'evt-xyz', evaluatedAtUtc: '2026-01-01T10:00:00.000Z' });
    const wrapper = mount(TriggerEvalRow, { props: { evaluation, sessionId: 'sess-1' } });

    await wrapper.find('.trigger-eval-row__actions').find('button').trigger('click');
    await flushPromises();

    expect(mockRouterPush).toHaveBeenCalledOnce();
    const call = mockRouterPush.mock.calls[0][0];
    expect(call.name).toBe('timeline');
    expect(call.query.from).toContain('2026-01-01T09:59:55');
    expect(call.query.to).toContain('2026-01-01T10:00:05');
    expect(call.query.select).toBe('evt-xyz');
  });

  it('TriggerEvalRow_TreeButton_Navigates', async () => {
    const { default: TriggerEvalRow } = await import('../../src/components/TriggerEvalRow.vue');
    const evaluation = makeEval({ eventId: 'evt-tree' });
    const wrapper = mount(TriggerEvalRow, { props: { evaluation, sessionId: 'sess-1' } });

    const buttons = wrapper.find('.trigger-eval-row__actions').findAll('button');
    await buttons[1].trigger('click');
    await flushPromises();

    expect(mockRouterPush).toHaveBeenCalledOnce();
    const call = mockRouterPush.mock.calls[0][0];
    expect(call.name).toBe('causal-by-event');
    expect(call.params.eventId).toBe('evt-tree');
  });

  it('TriggerEvalRow_InlineExpansion_TogglesOnClick', async () => {
    const { default: TriggerEvalRow } = await import('../../src/components/TriggerEvalRow.vue');
    const wrapper = mount(TriggerEvalRow, {
      props: { evaluation: makeEval(), sessionId: 'sess-1' },
    });

    // Expansion panel not shown initially
    expect(wrapper.find('.trigger-eval-row__inputs').exists()).toBe(false);

    // Click the row to expand
    await wrapper.find('.trigger-eval-row').trigger('click');
    expect(wrapper.find('.trigger-eval-row__inputs').exists()).toBe(true);

    // Click again to collapse
    await wrapper.find('.trigger-eval-row').trigger('click');
    expect(wrapper.find('.trigger-eval-row__inputs').exists()).toBe(false);
  });

  it('TriggerEvalRow_InputsPanel_ShowsRawJson', async () => {
    const { default: TriggerEvalRow } = await import('../../src/components/TriggerEvalRow.vue');
    const evaluation = makeEval({ inputs: '{"speed":10}' });
    const wrapper = mount(TriggerEvalRow, { props: { evaluation, sessionId: 'sess-1' } });

    await wrapper.find('.trigger-eval-row').trigger('click');
    const pre = wrapper.find('.trigger-eval-row__inputs');
    expect(pre.text()).toContain('"speed"');
  });

  it('TriggerEvalRow_TriggerIdFilter_Refetches', async () => {
    mockListTriggerEvaluations.mockResolvedValue([]);
    const { default: TriggerEvalView } = await import('../../src/views/TriggerEvalView.vue');
    const wrapper = mount(TriggerEvalView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    // Load some evals so the trigger select has options
    const evals = [makeEval({ triggerId: 'trig-x' }), makeEval({ triggerId: 'trig-y' })];
    mockListTriggerEvaluations.mockResolvedValue(evals);

    // Re-trigger initial load via direct vm call
    const vm = wrapper.vm as unknown as { reload: () => Promise<void> };
    await vm.reload();
    await flushPromises();

    // Now select a specific trigger
    const triggerSelect = wrapper.findAll('select')[0];
    await triggerSelect.setValue('trig-x');
    await flushPromises();

    const lastCall = mockListTriggerEvaluations.mock.calls[mockListTriggerEvaluations.mock.calls.length - 1][0];
    expect(lastCall.triggerId).toBe('trig-x');
  });
});
