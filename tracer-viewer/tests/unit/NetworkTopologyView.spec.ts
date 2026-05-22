import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { NetworkTopologyDto } from '../../src/api/tracerApiClient';

const mockGetSession = vi.fn();
const mockGetNetworkTopology = vi.fn();
const mockRouterPush = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getSession: mockGetSession,
    getNetworkTopology: mockGetNetworkTopology,
  },
}));

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
  useRoute: () => ({ params: {} }),
}));

vi.mock('@/components/BundleModeRequiredBanner.vue', () => ({
  default: { template: '<div class="bundle-mode-required-banner">Banner</div>', props: ['detail'] },
}));

vi.mock('@/components/NetworkGraphCanvas.vue', () => ({
  default: {
    template: '<canvas class="network-graph-canvas__canvas"/>',
    props: ['nodes', 'edges', 'selectedEdge'],
    emits: ['select-edge'],
  },
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

function makeTopology(): NetworkTopologyDto {
  return {
    nodes: ['node-A', 'node-B', 'node-C'],
    edges: [
      {
        topic: 'weapons.fire',
        publisherNode: 'node-A',
        subscriberNode: 'node-B',
        messageCount: 500,
        firstSeenUtc: '2026-01-01T10:00:00Z',
        lastSeenUtc: '2026-01-01T11:00:00Z',
      },
    ],
  };
}

describe('NetworkTopologyView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockGetSession.mockReset();
    mockGetNetworkTopology.mockReset();
    mockRouterPush.mockReset();
  });

  it('NetworkTopologyView_409_ShowsBanner', async () => {
    const err = Object.assign(new Error('409'), { status: 409 });
    mockGetSession.mockResolvedValue(makeSession());
    mockGetNetworkTopology.mockRejectedValue(err);

    const { default: NetworkTopologyView } = await import('../../src/views/NetworkTopologyView.vue');
    const wrapper = mount(NetworkTopologyView, { props: { sessionId: 's1' } });
    await flushPromises();

    expect(wrapper.find('.bundle-mode-required-banner').exists()).toBe(true);
    wrapper.unmount();
  });

  it('NetworkTopologyView_DrillIntoEdge_NavigatesCorrectly', async () => {
    mockGetSession.mockResolvedValue(makeSession());
    mockGetNetworkTopology.mockResolvedValue(makeTopology());

    const { default: NetworkTopologyView } = await import('../../src/views/NetworkTopologyView.vue');
    const wrapper = mount(NetworkTopologyView, { props: { sessionId: 's1' } });
    await flushPromises();

    // Simulate edge selection
    const canvas = wrapper.findComponent({ name: 'NetworkGraphCanvas' });
    await canvas.vm.$emit('select-edge', { from: 'node-A', to: 'node-B' });
    await flushPromises();

    // Should show side panel with "Latency →" button
    const latencyBtn = wrapper.find('.network-topology-view__latency-btn');
    expect(latencyBtn.exists()).toBe(true);

    await latencyBtn.trigger('click');

    expect(mockRouterPush).toHaveBeenCalledOnce();
    const arg = mockRouterPush.mock.calls[0][0];
    expect(arg.name).toBe('replication-latency');
    expect(arg.params.sessionId).toBe('s1');
    expect(arg.query.publisherNode).toBe('node-A');
    expect(arg.query.subscriberNode).toBe('node-B');
    expect(arg.query.topic).toBe('weapons.fire');
    wrapper.unmount();
  });
});
