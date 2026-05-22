import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';

vi.mock('@/rendering/networkGraphLayout', () => ({
  layoutGraph: vi.fn(() => ({ nodes: new Map([['A', { x: 100, y: 100 }], ['B', { x: 200, y: 200 }]]) })),
}));

vi.mock('@/rendering/networkGraphRenderer', () => ({
  renderGraph: vi.fn(),
}));

(globalThis as unknown as Record<string, unknown>).ResizeObserver = vi.fn(() => ({
  observe: vi.fn(),
  disconnect: vi.fn(),
  unobserve: vi.fn(),
}));

describe('NetworkGraphCanvas', () => {
  it('NetworkGraphCanvas_RendersCanvas', async () => {
    const { default: NetworkGraphCanvas } = await import('../../src/components/NetworkGraphCanvas.vue');
    const wrapper = mount(NetworkGraphCanvas, {
      props: {
        nodes: ['A', 'B', 'C'],
        edges: [
          { from: 'A', to: 'B', weight: 10 },
          { from: 'B', to: 'C', weight: 5 },
        ],
        selectedEdge: null,
      },
    });

    const canvas = wrapper.find('canvas');
    expect(canvas.exists()).toBe(true);
    wrapper.unmount();
  });
});
