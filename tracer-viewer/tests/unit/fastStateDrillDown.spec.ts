import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount, flushPromises } from '@vue/test-utils';
import { ref } from 'vue';
import type { FastStateTopicSchemaDto, EntityFastStateDto } from '../../src/api/tracerApiClient';

// ── Module-level mocks (hoisted) ─────────────────────────────────────────────

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getEntityFastStateSchema: vi.fn(),
    getEntityFastState: vi.fn(),
  },
}));

vi.mock('@/composables/useResizeObserver', () => ({
  useResizeObserver: vi.fn(),
}));

// Mock useEntityHistoryUrl to return controllable refs
const mockFastStateTopic = ref<string | null>(null);
const mockFastStateColumns = ref<string[]>([]);

vi.mock('@/composables/useEntityHistoryUrl', () => ({
  useEntityHistoryUrl: vi.fn(() => ({
    fastStateTopic: mockFastStateTopic,
    fastStateColumns: mockFastStateColumns,
  })),
}));

vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => { cb(0); return 0; });
vi.stubGlobal('cancelAnimationFrame', vi.fn());

// Mock canvas getContext to avoid jsdom limitation
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
  clearRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(),
  stroke: vi.fn(), fillRect: vi.fn(), fillText: vi.fn(), scale: vi.fn(),
  strokeStyle: '', fillStyle: '', lineWidth: 0, font: '', textAlign: '',
  textBaseline: '',
} as unknown as CanvasRenderingContext2D);

// ── Late imports after mocks ─────────────────────────────────────────────────

import FastStateDrillDown from '../../src/components/FastStateDrillDown.vue';

// ── Helpers ──────────────────────────────────────────────────────────────────

const BASE_TIME_RANGE = {
  from: new Date('2026-01-01T10:00:00.000Z'),
  to: new Date('2026-01-01T11:00:00.000Z'),
};

function makeSchema(overrides?: Partial<FastStateTopicSchemaDto>): FastStateTopicSchemaDto {
  return {
    entityId: 'ent-1',
    topic: 'transforms',
    columns: [
      { name: 'x', isNumeric: true },
      { name: 'label', isNumeric: false },
    ],
    ...overrides,
  };
}

function makeData(overrides?: Partial<EntityFastStateDto>): EntityFastStateDto {
  return {
    entityId: 'ent-1',
    topic: 'transforms',
    columns: ['x'],
    samples: [{ ts: '2026-01-01T10:00:00.000Z', values: { x: 1 } }],
    totalSamples: 1,
    downsampled: false,
    ...overrides,
  };
}

function mountDrillDown(availableTopics: string[] = ['transforms', 'pos']) {
  return mount(FastStateDrillDown, {
    props: {
      entityId: 'ent-1',
      sessionId: 'sess-1',
      availableTopics,
      timeRange: BASE_TIME_RANGE,
    },
    global: { plugins: [createPinia()] },
  });
}

// ── Tests ────────────────────────────────────────────────────────────────────

describe('fastStateDrillDown', () => {
  let mockApi: { getEntityFastStateSchema: ReturnType<typeof vi.fn>; getEntityFastState: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    mockFastStateTopic.value = null;
    mockFastStateColumns.value = [];

    const { api } = await import('@/api/tracerApiClient');
    mockApi = api as unknown as typeof mockApi;
    mockApi.getEntityFastStateSchema.mockResolvedValue(null);
    mockApi.getEntityFastState.mockResolvedValue(makeData());
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // SC-1: Collapsed by default
  it('collapsedByDefault', () => {
    const wrapper = mountDrillDown();
    expect(wrapper.find('.fast-state-drill-down__body').isVisible()).toBe(false);
  });

  // SC-2: Toggle button expands body
  it('toggleButtonExpandsBody', async () => {
    const wrapper = mountDrillDown();
    await wrapper.find('.fast-state-drill-down__toggle').trigger('click');
    expect(wrapper.find('.fast-state-drill-down__body').isVisible()).toBe(true);
  });

  // SC-3: No data hint when availableTopics = []
  it('noDataHint_WhenNoAvailableTopics', () => {
    const wrapper = mountDrillDown([]);
    expect(wrapper.find('.fast-state-drill-down__toggle').text()).toContain('no fast-state data');
  });

  // SC-4: Expand with no topics → body remains hidden
  it('expandWithNoTopics_BodyRemainsHidden', async () => {
    const wrapper = mountDrillDown([]);
    await wrapper.find('.fast-state-drill-down__toggle').trigger('click');
    expect(wrapper.find('.fast-state-drill-down__body').isVisible()).toBe(false);
  });

  // SC-5: Auto-selects first numeric column on topic selection
  it('autoSelectsFirstNumericColumn_OnTopicSelection', async () => {
    mockApi.getEntityFastStateSchema.mockResolvedValue(
      makeSchema({ columns: [{ name: 'x', isNumeric: true }, { name: 'label', isNumeric: false }] }),
    );
    mockApi.getEntityFastState.mockResolvedValue(makeData());

    mountDrillDown();

    // Simulate topic selection via the URL-synced ref
    mockFastStateTopic.value = 'transforms';
    await flushPromises();

    expect(mockApi.getEntityFastStateSchema).toHaveBeenCalledWith(
      'ent-1', 'transforms', 'sess-1', expect.anything(),
    );
    expect(mockFastStateColumns.value).toContain('x');
  });

  // SC-6: Downsampled notice shown when data.downsampled === true
  it('downsampledNotice_ShownWhenDownsampled', async () => {
    const schema = makeSchema();
    mockApi.getEntityFastStateSchema.mockResolvedValue(schema);
    mockApi.getEntityFastState.mockResolvedValue(
      makeData({ downsampled: true, totalSamples: 10000, samples: Array(5000).fill({ ts: '2026-01-01T10:00:00.000Z', values: { x: 1 } }) }),
    );

    const wrapper = mountDrillDown();
    await wrapper.find('.fast-state-drill-down__toggle').trigger('click');

    mockFastStateTopic.value = 'transforms';
    mockFastStateColumns.value = ['x'];
    await flushPromises();

    expect(wrapper.find('.fast-state-drill-down__downsampled-notice').exists()).toBe(true);
    expect(wrapper.find('.fast-state-drill-down__downsampled-notice').text()).toContain('downsampled');
  });
});
