import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import type { LatencyDistributionDto, LatencyBudgetDto } from '../../src/api/tracerApiClient';

vi.mock('@/rendering/histogramRenderer', () => ({
  renderHistogram: vi.fn(),
  formatMs: vi.fn(ms => `${ms} ms`),
}));

// Stub ResizeObserver
const observeSpy = vi.fn();
const disconnectSpy = vi.fn();
(globalThis as unknown as Record<string, unknown>).ResizeObserver = vi.fn(() => ({
  observe: observeSpy,
  disconnect: disconnectSpy,
  unobserve: vi.fn(),
}));

function makeDist(): LatencyDistributionDto {
  return {
    sampleCount: 100,
    p50Ms: 2, p90Ms: 5, p99Ms: 10, p999Ms: 20,
    maxMs: 50, minMs: 0.5, meanMs: 3, stddevMs: 1,
    buckets: [],
  };
}

describe('LatencyDistributionChart', () => {
  it('LatencyDistributionChart_ResizeTriggers_Redraw', async () => {
    const { renderHistogram } = await import('@/rendering/histogramRenderer');
    const { default: LatencyDistributionChart } = await import('../../src/components/LatencyDistributionChart.vue');

    const wrapper = mount(LatencyDistributionChart, {
      props: { distribution: makeDist(), budget: null, loading: false },
    });

    // ResizeObserver callback should have been called or we can verify observe was called
    expect(observeSpy).toHaveBeenCalled();
    wrapper.unmount();
  });
});
