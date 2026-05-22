import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { nextTick } from 'vue';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import type { EntityEventsDto, EntityEventDto } from '../../src/api/tracerApiClient';

// ── Module mocks (hoisted) ───────────────────────────────────────────────────

vi.mock('@/rendering/eventStripRenderer', () => ({
  renderEventStrip: vi.fn().mockReturnValue([{ eventId: 'evt-1', x: 500 }]),
}));

vi.mock('@/composables/useResizeObserver', () => ({
  useResizeObserver: vi.fn(),
}));

// ── Synchronous RAF so watchEffect / scheduleRender runs immediately ─────────

vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
  cb(0);
  return 0;
});
vi.stubGlobal('cancelAnimationFrame', vi.fn());

// ── Import mocked module after vi.mock declaration ───────────────────────────

import { renderEventStrip } from '@/rendering/eventStripRenderer';
import EntityEventStrip from '../../src/components/EntityEventStrip.vue';

// ── Canvas context mock ──────────────────────────────────────────────────────

const mockCtx = {
  clearRect: vi.fn(),
  beginPath: vi.fn(),
  arc: vi.fn(),
  fill: vi.fn(),
  stroke: vi.fn(),
  scale: vi.fn(),
  fillStyle: '' as string,
  strokeStyle: '' as string,
  lineWidth: 0,
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeEvent(overrides: Partial<EntityEventDto> = {}): EntityEventDto {
  return {
    eventId: 'evt-1',
    traceId: '0000000000000000',
    occurredAtUtc: '2026-01-01T10:00:00.500Z',
    topic: 'entity.pos',
    publisherNode: 'node-A',
    ...overrides,
  };
}

function makeEventsDto(events: EntityEventDto[], truncated = false): EntityEventsDto {
  return { entityId: 'ent-1', events, truncated };
}

const BASE_TIME_RANGE = {
  from: new Date('2026-01-01T10:00:00.000Z'),
  to: new Date('2026-01-01T10:00:01.000Z'),
};

// ── Tests ────────────────────────────────────────────────────────────────────

describe('entityEventStrip', () => {
  let getContextSpy: { mockRestore(): void } | undefined;

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();

    // Make canvas.getContext return the mock context so scheduleRender doesn't bail out
    getContextSpy = vi
      .spyOn(HTMLCanvasElement.prototype, 'getContext')
      .mockReturnValue(mockCtx as unknown as CanvasRenderingContext2D);

    // Ensure renderEventStrip returns the fixed hit entry for click tests
    vi.mocked(renderEventStrip).mockReturnValue([{ eventId: 'evt-1', x: 500 }]);
  });

  afterEach(() => {
    getContextSpy?.mockRestore();
  });

  it('clickNearMarkerEmitsSelectWithEventId', async () => {
    const wrapper = mount(EntityEventStrip, {
      props: {
        events: makeEventsDto([makeEvent()]),
        timeRange: BASE_TIME_RANGE,
        selectedEventId: null,
      },
    });

    const canvas = wrapper.find('canvas');
    // Mock getBoundingClientRect so clientX=500 maps to canvas x=500
    Object.defineProperty(canvas.element, 'getBoundingClientRect', {
      value: () => ({
        left: 0, top: 0, width: 1000, height: 40, right: 1000, bottom: 40,
      }),
      configurable: true,
    });

    // Allow watchEffect to re-run now that canvasRef is non-null (scheduleRender populates hitEntries)
    await nextTick();

    await canvas.trigger('click', { clientX: 500, clientY: 20 });

    const emitted = wrapper.emitted('select') as unknown[][];
    expect(emitted).toBeTruthy();
    expect(emitted[0][0]).toBe('evt-1');
  });

  it('clickFarFromMarkerEmitsSelectNull', async () => {
    const wrapper = mount(EntityEventStrip, {
      props: {
        events: makeEventsDto([makeEvent()]),
        timeRange: BASE_TIME_RANGE,
        selectedEventId: null,
      },
    });

    const canvas = wrapper.find('canvas');
    Object.defineProperty(canvas.element, 'getBoundingClientRect', {
      value: () => ({
        left: 0, top: 0, width: 1000, height: 40, right: 1000, bottom: 40,
      }),
      configurable: true,
    });

    // Marker is at x=500; click at x=50 → distance 450 > THRESHOLD_PX=8
    await canvas.trigger('click', { clientX: 50, clientY: 20 });

    const emitted = wrapper.emitted('select') as unknown[][];
    expect(emitted).toBeTruthy();
    expect(emitted[0][0]).toBeNull();
  });

  it('truncatedTrueShowsTruncationNotice', () => {
    const wrapper = mount(EntityEventStrip, {
      props: {
        events: makeEventsDto([makeEvent()], true),
        timeRange: BASE_TIME_RANGE,
        selectedEventId: null,
      },
    });

    expect(wrapper.find('.entity-event-strip__truncated').exists()).toBe(true);
    expect(wrapper.text()).toContain('truncated');
  });

  it('truncatedFalseNoTruncationNotice', () => {
    const wrapper = mount(EntityEventStrip, {
      props: {
        events: makeEventsDto([makeEvent()], false),
        timeRange: BASE_TIME_RANGE,
        selectedEventId: null,
      },
    });

    expect(wrapper.find('.entity-event-strip__truncated').exists()).toBe(false);
  });
});

