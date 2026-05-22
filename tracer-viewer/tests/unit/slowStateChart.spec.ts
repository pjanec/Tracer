import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import { detectFields } from '../../src/rendering/slowStateChartRenderer';
import type { SlowStateSampleDto } from '../../src/api/tracerApiClient';

// ── Module mocks (hoisted) ───────────────────────────────────────────────────

vi.mock('@/composables/useResizeObserver', () => ({
  useResizeObserver: vi.fn(),
}));

vi.mock('../../src/composables/useEntityHistoryQuery', () => ({
  useEntityHistoryQuery: vi.fn(),
}));
vi.mock('../../src/composables/useEntityHistoryUrl', () => ({
  useEntityHistoryUrl: vi.fn(() => ({
    fastStateTopic: { value: null },
    fastStateColumns: { value: [] },
  })),
}));

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getEntitySummary:       vi.fn(),
    getEntityEvents:        vi.fn(),
    getEntitySlowState:     vi.fn(),
    getEntityFastStateTopics: vi.fn(),
  },
}));

// ── Synchronous RAF so scheduleRender runs during mount ───────────────────────

vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
  cb(0);
  return 0;
});
vi.stubGlobal('cancelAnimationFrame', vi.fn());

// ── Late imports after mocks ─────────────────────────────────────────────────

import SlowStateChart from '../../src/components/SlowStateChart.vue';
import EntityHistoryView from '../../src/views/EntityHistoryView.vue';
import { useEntityHistoryStore } from '../../src/stores/entityHistoryStore';

// ── Helpers ──────────────────────────────────────────────────────────────────

const BASE_TIME_RANGE = {
  from: new Date('2026-01-01T10:00:00.000Z'),
  to: new Date('2026-01-01T10:00:01.000Z'),
};

function makeSample(overrides: Partial<SlowStateSampleDto> = {}): SlowStateSampleDto {
  return {
    topic: 'pos',
    occurredAtUtc: '2026-01-01T10:00:00.500Z',
    payloadJson: '{}',
    ...overrides,
  };
}

// ── Tests ────────────────────────────────────────────────────────────────────

describe('slowStateChart', () => {
  let getContextSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();

    // Prevent canvas rendering from bailing out due to null ctx
    getContextSpy = vi
      .spyOn(HTMLCanvasElement.prototype, 'getContext')
      .mockReturnValue({
        clearRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(),
        stroke: vi.fn(), fillRect: vi.fn(), fillText: vi.fn(), scale: vi.fn(),
        strokeStyle: '', fillStyle: '', lineWidth: 0, font: '', textAlign: '',
        textBaseline: '',
      } as unknown as CanvasRenderingContext2D);
  });

  afterEach(() => {
    getContextSpy?.mockRestore();
  });

  // SC-6: detectFields correctly classifies field types
  it('detectFields_ClassifiesNumericAndCategorical', () => {
    const fields = detectFields(['{"health": 100, "state": "idle"}']);

    const healthField = fields.find(f => f.name === 'health');
    const stateField  = fields.find(f => f.name === 'state');

    expect(healthField?.kind).toBe('numeric');
    expect(stateField?.kind).toBe('categorical');
  });

  // SC-7: preferred field auto-selected (value beats x; value is in NUMERIC_PREFERRED)
  it('preferredFieldAutoSelected_ValueBeatsX', async () => {
    const sample = makeSample({ payloadJson: '{"x": 1, "value": 5, "state": "a"}' });

    const wrapper = mount(SlowStateChart, {
      props: {
        topic: 'pos',
        samples: [sample],
        timeRange: BASE_TIME_RANGE,
      },
    });

    await nextTick();

    // With 3 fields (x, value, state), the select dropdown is shown
    const select = wrapper.find('.slow-state-chart__field-select');
    expect(select.exists()).toBe(true);
    // 'value' is preferred over 'x' per NUMERIC_PREFERRED ordering
    expect((select.element as HTMLSelectElement).value).toBe('value');
  });

  // SC-8: click closest sample emits select-event with that sample
  it('clickClosestSampleEmitsSelectEvent', async () => {
    const sample = makeSample({
      occurredAtUtc: '2026-01-01T10:00:00.500Z', // 500ms → x=500 on 1000px canvas
      payloadJson: '{"value": 7}',
    });

    const wrapper = mount(SlowStateChart, {
      props: {
        topic: 'pos',
        samples: [sample],
        timeRange: BASE_TIME_RANGE,
      },
    });

    const canvas = wrapper.find('canvas');
    // Mock getBoundingClientRect so the click handler sees a 1000px-wide canvas at left=0
    Object.defineProperty(canvas.element, 'getBoundingClientRect', {
      value: () => ({
        left: 0, top: 0, width: 1000, height: 60, right: 1000, bottom: 60,
      }),
      configurable: true,
    });

    // Click at clientX=500 → canvas x=500; sample is at t=500ms → sx=500 → dist=0 ≤ 10
    await canvas.trigger('click', { clientX: 500, clientY: 30 });

    const emitted = wrapper.emitted('select-event') as unknown[][];
    expect(emitted).toBeTruthy();
    // Vue wraps props in a Proxy; use deep equality instead of reference equality
    expect(emitted[0][0]).toStrictEqual(sample);
  });

  // SC-9: EntityHistoryView renders zero SlowStateChart components when slowStateByTopic is empty
  it('entityHistoryView_ZeroSlowStateCharts_WhenNoSlowStateByTopic', () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const store = useEntityHistoryStore();
    store.loading = false;
    store.error = null;
    store.summary = {
      entityId: 'ent-1',
      firstSeenUtc: '2026-01-01T10:00:00.000Z',
      lastSeenUtc: '2026-01-01T11:00:00.000Z',
      eventCount: 0,
      topics: [],
    };
    store.events = { entityId: 'ent-1', events: [], truncated: false };
    store.slowStateByTopic = {};
    store.fastStateTopics = [];

    const wrapper = mount(EntityHistoryView, { global: { plugins: [pinia] } });

    expect(wrapper.findAll('.slow-state-chart').length).toBe(0);
  });
});
