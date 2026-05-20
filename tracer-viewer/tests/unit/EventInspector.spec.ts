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

  it('eventInspector_showsCausalTree_buttonPresentButDisabled', async () => {
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

    const btns = wrapper.findAll('.event-inspector__action--disabled');
    const causalBtn = btns.find((b) => b.text().includes('causal'));
    expect(causalBtn).toBeTruthy();
    expect(causalBtn!.attributes('disabled')).toBeDefined();
  });

  it('eventInspector_showsEntityHistory_buttonPresentButDisabled', async () => {
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

    const btns = wrapper.findAll('.event-inspector__action--disabled');
    const entityBtn = btns.find((b) => b.text().includes('entity'));
    expect(entityBtn).toBeTruthy();
    expect(entityBtn!.attributes('disabled')).toBeDefined();
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
});
