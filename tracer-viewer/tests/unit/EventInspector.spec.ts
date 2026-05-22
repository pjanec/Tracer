import { describe, it, expect, beforeEach, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import type { EventDto } from '../../src/api/tracerApiClient';

const mockGetEvent = vi.fn<(id: string) => Promise<EventDto | null>>();
const mockRouterPush = vi.fn();

vi.mock('vue-router', () => ({
  useRouter: vi.fn(() => ({ push: mockRouterPush })),
}));

vi.mock('@/api/tracerApiClient', () => ({
  api: { getEvent: mockGetEvent },
}));

describe('EventInspector', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockGetEvent.mockReset();
    mockRouterPush.mockReset();
  });

  async function mountComponent() {
    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const pinia = createPinia();
    setActivePinia(pinia);
    const { mount } = await import('@vue/test-utils');
    return mount(EventInspector, { global: { plugins: [pinia] } });
  }

  it('eventInspector_noSelectedEvent_rendersNothing', async () => {
    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = null;
    await flushPromises();

    expect(wrapper.find('.event-inspector').exists()).toBe(false);
  });

  it('eventInspector_loadsEventOnSelection', async () => {
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    expect(mockGetEvent).toHaveBeenCalledWith('AABBCCDD');
    expect(wrapper.text()).toContain('weapons.fire');
    expect(wrapper.text()).toContain('node-1');
  });

  it('eventInspector_showsPayloadJson_prettyPrinted', async () => {
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
      payloadJson: '{"damage":42}',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    expect(wrapper.find('.event-inspector__payload').text()).toContain('42');
  });

  it('eventInspector_filterToTrace_updatesStore', async () => {
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    const btns = wrapper.findAll('.event-inspector__action');
    const filterBtn = btns.find((b) => b.text().includes('Filter'));
    await filterBtn!.trigger('click');

    expect(store.filter.traceId).toBe('TRACE-1');
  });

  it('eventInspector_showInScenario_navigates', async () => {
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.sessionId = 'session-42';
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    const btns = wrapper.findAll('.event-inspector__action');
    const scenarioBtn = btns.find((b) => b.text().includes('scenario'));
    await scenarioBtn!.trigger('click');
    await flushPromises();

    expect(mockRouterPush).toHaveBeenCalledWith('/scenario/session-42');
  });

  it('eventInspector_showsCausalTree_buttonAbsent_WhenShowCausalTreePivotIsFalse', async () => {
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    // showCausalTreePivot defaults to false — button should be absent
    const allBtns = wrapper.findAll('.event-inspector__action');
    const causalBtn = allBtns.find((b) => b.text().includes('causal'));
    expect(causalBtn).toBeUndefined();
  });

  it('eventInspector_showsEntityHistory_buttonAbsentByDefault', async () => {
    // After TRC-P7-018: the stub disabled button is replaced by a conditional button.
    // When showEntityHistoryPivot is false (default), no entity history button is rendered.
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    // showEntityHistoryPivot defaults to false — button should be absent entirely
    const allBtns = wrapper.findAll('.event-inspector__action');
    const entityBtn = allBtns.find((b) => b.text().includes('entity history'));
    expect(entityBtn).toBeUndefined();
  });

  it('eventInspector_copyEventId_writesToClipboard', async () => {
    const fakeEvent: EventDto = {
      eventId: 'AABBCCDD',
      traceId: 'TRACE-1',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'weapons.fire',
      publisherNode: 'node-1',
    };
    mockGetEvent.mockResolvedValueOnce(fakeEvent);

    const writeTextMock = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: writeTextMock }, configurable: true });

    const wrapper = await mountComponent();
    const store = useTimelineStore();
    store.selectedEventId = 'AABBCCDD';
    await flushPromises();

    const btns = wrapper.findAll('.event-inspector__action');
    const copyBtn = btns.find((b) => b.text().includes('Copy'));
    await copyBtn!.trigger('click');
    await flushPromises();

    expect(writeTextMock).toHaveBeenCalledWith('AABBCCDD');
  });

  // --- TRC-P6-009 prop-mode tests ---

  function makeCausalNode(overrides: Partial<import('@/types/causalTree').TraceNodeDto> = {}) {
    return {
      eventId: 'aabbccddeeff0011',
      traceId: '1122334455667788',
      publishWallclock: '2026-01-01T10:00:00.000Z',
      publisherNode: 'alpha-node',
      topic: 'weapons.fire',
      ...overrides,
    } as import('@/types/causalTree').TraceNodeDto;
  }

  async function mountWithEvent(
    node: import('@/types/causalTree').TraceNodeDto,
    extraProps: Record<string, unknown> = {},
  ) {
    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const pinia = createPinia();
    setActivePinia(pinia);
    const { mount } = await import('@vue/test-utils');
    return mount(EventInspector, {
      global: { plugins: [pinia] },
      props: { event: node, ...extraProps },
    });
  }

  it('showCausalTreeButton_HiddenWhenTraceIdIsZero', async () => {
    const node = makeCausalNode({ traceId: '0000000000000000' });
    const wrapper = await mountWithEvent(node, { showCausalTreePivot: true });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const causalBtn = allBtns.find((b) => b.text().includes('causal'));
    expect(causalBtn).toBeUndefined();
  });

  it('showCausalTreeButton_VisibleAndNavigates_WhenTraceIdNonZero', async () => {
    const node = makeCausalNode({ traceId: '1122334455667788' });
    const wrapper = await mountWithEvent(node, { showCausalTreePivot: true });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const causalBtn = allBtns.find((b) => b.text().includes('causal'));
    expect(causalBtn).toBeTruthy();
    expect(causalBtn!.attributes('disabled')).toBeUndefined();

    await causalBtn!.trigger('click');
    expect(mockRouterPush).toHaveBeenCalledWith({
      name: 'causal-by-event',
      params: { eventId: node.eventId },
    });
  });

  it('pivotToTimeline_PushesTimelineRouteWithSelectAndWindow', async () => {
    const node = makeCausalNode({ publishWallclock: '2026-06-01T12:00:00.000Z' });
    const wrapper = await mountWithEvent(node, {
      showTimelinePivot: true,
      sessionId: 'sess-abc',
    });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const timelineBtn = allBtns.find((b) => b.text().includes('timeline'));
    expect(timelineBtn).toBeTruthy();

    await timelineBtn!.trigger('click');

    expect(mockRouterPush).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'timeline',
        params: { sessionId: 'sess-abc' },
        query: expect.objectContaining({ select: node.eventId }),
      }),
    );
  });

  it('pivotToScenario_PushesScenarioRouteWithSessionId', async () => {
    const node = makeCausalNode();
    const wrapper = await mountWithEvent(node, { sessionId: 'sess-xyz' });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const scenarioBtn = allBtns.find((b) => b.text().includes('scenario'));
    expect(scenarioBtn).toBeTruthy();

    await scenarioBtn!.trigger('click');
    expect(mockRouterPush).toHaveBeenCalledWith('/scenario/sess-xyz');
  });

  it('showTimelinePivotFalse_HidesTimelineButton', async () => {
    const node = makeCausalNode();
    const wrapper = await mountWithEvent(node, {
      showTimelinePivot: false,
      sessionId: 'sess-abc',
    });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const timelineBtn = allBtns.find((b) => b.text().includes('timeline'));
    expect(timelineBtn).toBeUndefined();
  });

  // --- TRC-P7-018: entity history pivot tests ---

  it('entityHistoryButton_VisibleWhenEntityIdPresent', async () => {
    const node = makeCausalNode({ entityId: 'ent-1' });
    const wrapper = await mountWithEvent(node, {
      showEntityHistoryPivot: true,
      sessionId: 'sess-abc',
    });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const entityBtn = allBtns.find((b) => b.text().includes('entity history'));
    expect(entityBtn).toBeTruthy();
    expect(entityBtn!.attributes('disabled')).toBeUndefined();
  });

  it('entityHistoryButton_AbsentWhenEntityIdNull', async () => {
    const node = makeCausalNode({ entityId: null });
    const wrapper = await mountWithEvent(node, {
      showEntityHistoryPivot: true,
      sessionId: 'sess-abc',
    });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const entityBtn = allBtns.find((b) => b.text().includes('entity history'));
    expect(entityBtn).toBeUndefined();
  });

  it('pivotToEntityHistory_NavigatesToEntityHistoryView', async () => {
    const node = makeCausalNode({ entityId: 'ent-42' });
    const wrapper = await mountWithEvent(node, {
      showEntityHistoryPivot: true,
      sessionId: 'sess-xyz',
    });

    const allBtns = wrapper.findAll('.event-inspector__action');
    const entityBtn = allBtns.find((b) => b.text().includes('entity history'));
    expect(entityBtn).toBeTruthy();

    await entityBtn!.trigger('click');

    expect(mockRouterPush).toHaveBeenCalledWith({
      name: 'entity-history',
      params: { entityId: 'ent-42' },
      query: { session: 'sess-xyz' },
    });
  });
});
