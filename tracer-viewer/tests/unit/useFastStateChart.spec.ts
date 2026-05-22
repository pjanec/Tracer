import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, ref, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import type { FastStateTopicSchemaDto, EntityFastStateDto } from '../../src/api/tracerApiClient';

// ── Module-level mocks ────────────────────────────────────────────────────────

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getEntityFastStateSchema: vi.fn(),
    getEntityFastState: vi.fn(),
  },
}));

// ── Late imports ─────────────────────────────────────────────────────────────

import { useFastStateChart } from '../../src/composables/useFastStateChart';

// ── Helpers ───────────────────────────────────────────────────────────────────

const BASE_TIME_RANGE = { from: new Date('2026-01-01T10:00:00Z'), to: new Date('2026-01-01T11:00:00Z') };

function makeSchema(columns = [{ name: 'x', isNumeric: true }, { name: 'state', isNumeric: false }]): FastStateTopicSchemaDto {
  return { entityId: 'ent-1', topic: 'transforms', columns };
}

function makeData(): EntityFastStateDto {
  return {
    entityId: 'ent-1', topic: 'transforms', columns: ['x'],
    samples: [{ ts: '2026-01-01T10:00:00Z', values: { x: 1 } }],
    totalSamples: 1, downsampled: false,
  };
}

function mountWithChart(setup: () => ReturnType<typeof useFastStateChart>, pinia: ReturnType<typeof createPinia>) {
  let result!: ReturnType<typeof useFastStateChart>;
  const wrapper = mount(defineComponent({
    setup() {
      result = setup();
      return {};
    },
    template: '<div/>',
  }), { global: { plugins: [pinia] } });
  return { wrapper, result: result! };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('useFastStateChart', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // SC-1: Topic change triggers schema fetch
  it('topicChange_TriggersSchemaFetch', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mockResolvedValue(makeSchema());
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockResolvedValue(makeData());

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>(null);
    const selectedColumns = ref<string[]>([]);
    const timeRange = ref(BASE_TIME_RANGE);

    mountWithChart(() => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange), pinia);

    selectedTopic.value = 'transforms';
    await flushPromises();

    expect(api.getEntityFastStateSchema).toHaveBeenCalledWith(
      'ent-1', 'transforms', 'sess-1', expect.anything(),
    );
  });

  // SC-2: Topic change clears previous data and columns
  it('topicChange_ClearsPreviousDataAndColumns', async () => {
    const { api } = await import('@/api/tracerApiClient');

    let resolveSchema!: (v: FastStateTopicSchemaDto) => void;
    const schemaDeferred = new Promise<FastStateTopicSchemaDto>(r => { resolveSchema = r; });
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>)
      .mockReturnValueOnce(schemaDeferred)
      .mockResolvedValue(makeSchema());
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockResolvedValue(makeData());

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>('pos');
    const selectedColumns = ref<string[]>(['y']);
    const timeRange = ref(BASE_TIME_RANGE);

    const { result } = mountWithChart(
      () => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange),
      pinia,
    );

    // Wait for first schema fetch to start
    await nextTick();

    // Change topic — should clear columns and data before new schema arrives
    selectedTopic.value = 'transforms';
    await nextTick();

    expect(selectedColumns.value).toEqual([]);
    expect(result.data.value).toBeNull();

    // Let schema resolve
    resolveSchema(makeSchema());
    await flushPromises();
  });

  // SC-3: Column change does NOT refetch schema
  it('columnChange_DoesNotRefetchSchema', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mockResolvedValue(makeSchema());
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockResolvedValue(makeData());

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>('transforms');
    const selectedColumns = ref<string[]>(['x']);
    const timeRange = ref(BASE_TIME_RANGE);

    mountWithChart(
      () => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange),
      pinia,
    );

    await flushPromises();
    const schemaCallCount = (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mock.calls.length;

    // Change only columns
    selectedColumns.value = ['x', 'y'];
    await flushPromises();

    expect((api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mock.calls.length).toBe(schemaCallCount);
  });

  // SC-4: Data fetch triggered after schema resolves and columns auto-selected
  it('dataFetchTriggered_AfterSchemaAndAutoSelect', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mockResolvedValue(
      makeSchema([{ name: 'x', isNumeric: true }]),
    );
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockResolvedValue(makeData());

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>(null);
    const selectedColumns = ref<string[]>([]);
    const timeRange = ref(BASE_TIME_RANGE);

    mountWithChart(
      () => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange),
      pinia,
    );

    selectedTopic.value = 'transforms';
    await flushPromises();

    // Auto-select should have picked 'x', and then data fetch triggered
    expect(api.getEntityFastState).toHaveBeenCalledWith(
      'ent-1', 'transforms', 'sess-1',
      expect.any(Date), expect.any(Date),
      expect.arrayContaining(['x']),
      expect.anything(),
    );
  });

  // SC-5: TimeRange change triggers data refetch
  it('timeRangeChange_TriggersDataRefetch', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mockResolvedValue(makeSchema());
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockResolvedValue(makeData());

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>('transforms');
    const selectedColumns = ref<string[]>(['x']);
    const timeRange = ref(BASE_TIME_RANGE);

    mountWithChart(
      () => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange),
      pinia,
    );
    await flushPromises();

    const callsBefore = (api.getEntityFastState as ReturnType<typeof vi.fn>).mock.calls.length;

    // Change time range
    timeRange.value = {
      from: new Date('2026-01-02T10:00:00Z'),
      to: new Date('2026-01-02T11:00:00Z'),
    };
    await flushPromises();

    expect((api.getEntityFastState as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(callsBefore);
  });

  // SC-6: dataLoading true while fetch pending; false after resolution
  it('dataLoading_TrueWhileFetchPending_FalseAfterResolution', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mockResolvedValue(
      makeSchema([{ name: 'x', isNumeric: true }]),
    );

    let resolveData!: (v: EntityFastStateDto) => void;
    const dataDeferred = new Promise<EntityFastStateDto>(r => { resolveData = r; });
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockReturnValue(dataDeferred);

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>(null);
    const selectedColumns = ref<string[]>([]);
    const timeRange = ref(BASE_TIME_RANGE);

    const { result } = mountWithChart(
      () => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange),
      pinia,
    );

    selectedTopic.value = 'transforms';
    await flushPromises(); // schema resolves, columns auto-selected, data fetch starts

    expect(result.dataLoading.value).toBe(true);

    resolveData(makeData());
    await flushPromises();

    expect(result.dataLoading.value).toBe(false);
  });

  // SC-7: Composable initialises selectedColumns from external state correctly
  it('initialSelectedColumns_UsedWhenProvided', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntityFastStateSchema as ReturnType<typeof vi.fn>).mockResolvedValue(makeSchema());
    (api.getEntityFastState as ReturnType<typeof vi.fn>).mockResolvedValue(makeData());

    const entityId = ref<string | null>('ent-1');
    const sessionId = ref<string | null>('sess-1');
    const selectedTopic = ref<string | null>('transforms');
    // Pre-set columns (e.g. restored from URL)
    const selectedColumns = ref<string[]>(['x']);
    const timeRange = ref(BASE_TIME_RANGE);

    mountWithChart(
      () => useFastStateChart(entityId, sessionId, selectedTopic, selectedColumns, timeRange),
      pinia,
    );
    await flushPromises();

    // Schema was fetched (topic change triggered it)
    expect(api.getEntityFastStateSchema).toHaveBeenCalled();
    // Data fetched with the pre-set column
    expect(api.getEntityFastState).toHaveBeenCalledWith(
      'ent-1', 'transforms', 'sess-1',
      expect.any(Date), expect.any(Date),
      expect.arrayContaining(['x']),
      expect.anything(),
    );
  });
});
