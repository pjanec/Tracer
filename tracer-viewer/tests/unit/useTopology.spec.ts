import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ref } from 'vue';
import { flushPromises } from '@vue/test-utils';
import type { NetworkTopologyDto } from '../../src/api/tracerApiClient';

const mockGetNetworkTopology = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: { getNetworkTopology: mockGetNetworkTopology },
}));

function makeTopology(): NetworkTopologyDto {
  return { nodes: ['A', 'B'], edges: [] };
}

describe('useTopology', () => {
  beforeEach(() => {
    mockGetNetworkTopology.mockReset();
  });

  it('NoCallWhenFromIsNull', async () => {
    mockGetNetworkTopology.mockResolvedValue(makeTopology());
    const { useTopology } = await import('../../src/composables/useTopology');
    const filter = ref({ from: null, to: '2026-01-01T01:00:00Z' });
    useTopology(filter);
    await flushPromises();
    expect(mockGetNetworkTopology).not.toHaveBeenCalled();
  });

  it('CallsApiWhenBothFromAndToPresent', async () => {
    mockGetNetworkTopology.mockResolvedValue(makeTopology());
    const { useTopology } = await import('../../src/composables/useTopology');
    const filter = ref({ from: '2026-01-01T00:00:00Z', to: '2026-01-01T01:00:00Z' });
    const { topology } = useTopology(filter);
    await flushPromises();
    expect(mockGetNetworkTopology).toHaveBeenCalledOnce();
    expect(topology.value?.nodes).toEqual(['A', 'B']);
  });
});
