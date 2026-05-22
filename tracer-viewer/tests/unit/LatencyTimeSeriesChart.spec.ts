import { describe, it, expect, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import type { LatencyTimeSeriesDto } from '../../src/api/tracerApiClient';

vi.mock('@/rendering/latencyTimeSeriesRenderer', () => ({
  renderTimeSeries: vi.fn(),
  hitTestTimeSeries: vi.fn((pts: unknown[], _x: number, _w: number) => (pts as unknown[]).length > 0 ? 2 : -1),
}));

vi.mock('@/rendering/histogramRenderer', () => ({
  renderHistogram: vi.fn(),
  formatMs: vi.fn(ms => `${ms} ms`),
}));

(globalThis as unknown as Record<string, unknown>).ResizeObserver = vi.fn(() => ({
  observe: vi.fn(),
  disconnect: vi.fn(),
  unobserve: vi.fn(),
}));

function makeTs(): LatencyTimeSeriesDto {
  return {
    bucketSize: '1 minute',
    points: [
      { bucketStartUtc: '2026-01-01T00:00:00Z', p50Ms: 2, p99Ms: 10, sampleCount: 50 },
      { bucketStartUtc: '2026-01-01T00:01:00Z', p50Ms: 3, p99Ms: 12, sampleCount: 60 },
      { bucketStartUtc: '2026-01-01T00:02:00Z', p50Ms: 2.5, p99Ms: 11, sampleCount: 55 },
    ],
  };
}

describe('LatencyTimeSeriesChart', () => {
  it('LatencyTimeSeriesChart_HoverShowsTooltip', async () => {
    const { default: LatencyTimeSeriesChart } = await import('../../src/components/LatencyTimeSeriesChart.vue');
    const wrapper = mount(LatencyTimeSeriesChart, {
      props: { timeseries: makeTs(), loading: false },
    });

    // Simulate mouse move — hitTestTimeSeries returns index 2
    await wrapper.find('.latency-timeseries-chart__canvas').trigger('mousemove', { clientX: 200, clientY: 50 });
    await flushPromises();

    // Tooltip should be visible
    expect(wrapper.find('.latency-timeseries-chart__tooltip').exists()).toBe(true);
    wrapper.unmount();
  });

  it('LatencyTimeSeriesChart_LoadingState_ShowsIndicator', async () => {
    const { default: LatencyTimeSeriesChart } = await import('../../src/components/LatencyTimeSeriesChart.vue');
    const wrapper = mount(LatencyTimeSeriesChart, {
      props: { timeseries: null, loading: true },
    });

    expect(wrapper.find('.latency-timeseries-chart__loading').exists()).toBe(true);
    wrapper.unmount();
  });
});
