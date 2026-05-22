import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ref, nextTick, defineComponent } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import type { LatencyDistributionDto } from '../../src/api/tracerApiClient';

const mockGetLatencyDistribution = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: { getLatencyDistribution: mockGetLatencyDistribution },
}));

function makeDistribution(): LatencyDistributionDto {
  return {
    sampleCount: 100,
    p50Ms: 2, p90Ms: 5, p99Ms: 10, p999Ms: 20,
    maxMs: 50, minMs: 0.5, meanMs: 3, stddevMs: 1,
    buckets: [],
  };
}

describe('useLatencyDistribution', () => {
  beforeEach(() => {
    mockGetLatencyDistribution.mockReset();
  });

  it('FilterChange_RefetchesCalled', async () => {
    mockGetLatencyDistribution.mockResolvedValue(makeDistribution());
    const { useLatencyDistribution } = await import('../../src/composables/useLatencyDistribution');
    const filter = ref({ from: '2026-01-01T00:00:00Z', to: '2026-01-01T01:00:00Z' });
    useLatencyDistribution(filter);
    await flushPromises();

    expect(mockGetLatencyDistribution).toHaveBeenCalledTimes(1);

    filter.value = { from: '2026-01-01T02:00:00Z', to: '2026-01-01T03:00:00Z' };
    await flushPromises();

    expect(mockGetLatencyDistribution).toHaveBeenCalledTimes(2);
  });

  it('FilterChange_AbortsPreviousRequest', async () => {
    let capturedSignal: AbortSignal | undefined;
    mockGetLatencyDistribution.mockImplementation(
      (_params: unknown, signal?: AbortSignal) => {
        capturedSignal = signal;
        return new Promise(() => {}); // never resolves
      },
    );

    const { useLatencyDistribution } = await import('../../src/composables/useLatencyDistribution');
    const filter = ref({ from: '2026-01-01T00:00:00Z', to: '2026-01-01T01:00:00Z' });
    useLatencyDistribution(filter);
    await nextTick();

    const firstSignal = capturedSignal!;
    expect(firstSignal.aborted).toBe(false);

    filter.value = { from: '2026-01-01T02:00:00Z', to: '2026-01-01T03:00:00Z' };
    await nextTick();

    expect(firstSignal.aborted).toBe(true);
  });

  it('On409_ErrorStatusSet_DataNull', async () => {
    const apiErr = Object.assign(new Error('409'), { status: 409 });
    mockGetLatencyDistribution.mockRejectedValue(apiErr);

    const { useLatencyDistribution } = await import('../../src/composables/useLatencyDistribution');
    const filter = ref({ from: '2026-01-01T00:00:00Z', to: '2026-01-01T01:00:00Z' });
    const { distribution, error } = useLatencyDistribution(filter);
    await flushPromises();

    expect(error.value?.status).toBe(409);
    expect(distribution.value).toBeNull();
  });

  it('OnUnmount_RequestAborted', async () => {
    let capturedSignal: AbortSignal | undefined;
    mockGetLatencyDistribution.mockImplementation(
      (_params: unknown, signal?: AbortSignal) => {
        capturedSignal = signal;
        return new Promise(() => {}); // never resolves
      },
    );

    const { useLatencyDistribution } = await import('../../src/composables/useLatencyDistribution');

    const wrapper = mount(
      defineComponent({
        setup() {
          const filter = ref({ from: '2026-01-01T00:00:00Z', to: '2026-01-01T01:00:00Z' });
          useLatencyDistribution(filter);
          return {};
        },
        template: '<div/>',
      }),
    );
    await flushPromises();

    const sig = capturedSignal!;
    expect(sig.aborted).toBe(false);
    wrapper.unmount();
    expect(sig.aborted).toBe(true);
  });
});
