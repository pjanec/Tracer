import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { LatencyPairSummaryDto, LatencyBudgetDto } from '../../src/api/tracerApiClient';

const mockGetSession = vi.fn();
const mockGetLatencyPairs = vi.fn();
const mockGetLatencyBudgets = vi.fn();
const mockGetLatencyDistribution = vi.fn();
const mockGetLatencyTimeSeries = vi.fn();
const mockGetLatencyOutliers = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getSession: mockGetSession,
    getLatencyPairs: mockGetLatencyPairs,
    getLatencyBudgets: mockGetLatencyBudgets,
    getLatencyDistribution: mockGetLatencyDistribution,
    getLatencyTimeSeries: mockGetLatencyTimeSeries,
    getLatencyOutliers: mockGetLatencyOutliers,
  },
}));

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {} }),
}));

// Stub child components to simplify rendering
vi.mock('@/components/BundleModeRequiredBanner.vue', () => ({
  default: { template: '<div class="bundle-mode-required-banner">Banner</div>', props: ['detail'] },
}));

vi.mock('@/components/PublisherSubscriberMatrix.vue', () => ({
  default: {
    template: '<div class="publisher-subscriber-matrix"><slot/></div>',
    props: ['pairs', 'budgets', 'selectedPair'],
    emits: ['select'],
  },
}));

vi.mock('@/components/LatencyDistributionChart.vue', () => ({
  default: { template: '<div class="latency-distribution-chart"/>', props: ['distribution', 'budget', 'loading'] },
}));

vi.mock('@/components/LatencyTimeSeriesChart.vue', () => ({
  default: { template: '<div class="latency-timeseries-chart"/>', props: ['timeseries', 'loading'] },
}));

vi.mock('@/components/LatencyOutliersTable.vue', () => ({
  default: { template: '<div class="latency-outliers-table"/>', props: ['outliers', 'sessionId'] },
}));

function makeSession() {
  return {
    sessionId: 's1',
    scenarioId: 'sc1',
    startUtc: '2026-01-01T10:00:00Z',
    endUtc: '2026-01-01T11:00:00Z',
    status: 'completed',
    participatingNodes: [],
    eventCount: 1000,
  };
}

function makePair(subscriberNode: string): LatencyPairSummaryDto {
  return {
    topic: 'weapons.fire',
    publisherNode: 'node-A',
    subscriberNode,
    sampleCount: 100,
    p50Ms: 3,
    p99Ms: 15,
    maxMs: 100,
  };
}

describe('ReplicationLatencyView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockGetSession.mockReset();
    mockGetLatencyPairs.mockReset();
    mockGetLatencyBudgets.mockReset();
    mockGetLatencyDistribution.mockReset();
    mockGetLatencyTimeSeries.mockReset();
    mockGetLatencyOutliers.mockReset();
    mockGetLatencyDistribution.mockResolvedValue({
      sampleCount: 0, p50Ms: 0, p90Ms: 0, p99Ms: 0, p999Ms: 0,
      maxMs: 0, minMs: 0, meanMs: 0, stddevMs: 0, buckets: [],
    });
    mockGetLatencyTimeSeries.mockResolvedValue({ bucketSize: '1 minute', points: [] });
    mockGetLatencyOutliers.mockResolvedValue({ outliers: [], budgetsUsed: [] });
  });

  it('ReplicationLatencyView_MountsWithPairList', async () => {
    const pairs = [makePair('node-B'), makePair('node-C'), makePair('node-D')];
    mockGetSession.mockResolvedValue(makeSession());
    mockGetLatencyPairs.mockResolvedValue(pairs);
    mockGetLatencyBudgets.mockResolvedValue({ budgets: [] });

    const { default: ReplicationLatencyView } = await import('../../src/views/ReplicationLatencyView.vue');
    const wrapper = mount(ReplicationLatencyView, { props: { sessionId: 's1' } });
    await flushPromises();

    const matrix = wrapper.findComponent({ name: 'PublisherSubscriberMatrix' });
    expect((matrix.props('pairs') as LatencyPairSummaryDto[]).length).toBe(3);
    wrapper.unmount();
  });

  it('ReplicationLatencyView_409_ShowsBanner', async () => {
    const err = Object.assign(new Error('409'), { status: 409 });
    mockGetSession.mockResolvedValue(makeSession());
    mockGetLatencyPairs.mockRejectedValue(err);
    mockGetLatencyBudgets.mockRejectedValue(err);

    const { default: ReplicationLatencyView } = await import('../../src/views/ReplicationLatencyView.vue');
    const wrapper = mount(ReplicationLatencyView, { props: { sessionId: 's1' } });
    await flushPromises();

    expect(wrapper.find('.bundle-mode-required-banner').exists()).toBe(true);
    expect(wrapper.find('.replication-latency-view__panels').exists()).toBe(false);
    wrapper.unmount();
  });

  it('ReplicationLatencyView_SelectPair_UpdatesComposableFilter', async () => {
    const pairs = [makePair('node-B'), makePair('node-C'), makePair('node-D')];
    mockGetSession.mockResolvedValue(makeSession());
    mockGetLatencyPairs.mockResolvedValue(pairs);
    mockGetLatencyBudgets.mockResolvedValue({ budgets: [] });

    const { default: ReplicationLatencyView } = await import('../../src/views/ReplicationLatencyView.vue');
    const wrapper = mount(ReplicationLatencyView, { props: { sessionId: 's1' } });
    await flushPromises();

    // Emit select from matrix
    const matrix = wrapper.findComponent({ name: 'PublisherSubscriberMatrix' });
    await matrix.vm.$emit('select', pairs[1]);
    await flushPromises();

    // The selected pair is now set — the distribution API should have been called with the topic filter
    const calls = mockGetLatencyDistribution.mock.calls;
    const lastCall = calls[calls.length - 1];
    expect(lastCall[0].topic).toBe(pairs[1].topic);
    wrapper.unmount();
  });

  it('ReplicationLatencyView_ClearPair_ResetsFilter', async () => {
    const pairs = [makePair('node-B'), makePair('node-C')];
    mockGetSession.mockResolvedValue(makeSession());
    mockGetLatencyPairs.mockResolvedValue(pairs);
    mockGetLatencyBudgets.mockResolvedValue({ budgets: [] });

    const { default: ReplicationLatencyView } = await import('../../src/views/ReplicationLatencyView.vue');
    const wrapper = mount(ReplicationLatencyView, { props: { sessionId: 's1' } });
    await flushPromises();

    // Select a pair then clear
    const matrix = wrapper.findComponent({ name: 'PublisherSubscriberMatrix' });
    await matrix.vm.$emit('select', pairs[0]);
    await flushPromises();

    const clearBtn = wrapper.find('.replication-latency-view__clear-btn');
    expect(clearBtn.exists()).toBe(true);
    await clearBtn.trigger('click');
    await flushPromises();

    expect(wrapper.find('.replication-latency-view__clear-btn').exists()).toBe(false);
    wrapper.unmount();
  });
});
