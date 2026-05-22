import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, nextTick } from 'vue';
import { mount } from '@vue/test-utils';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';
import { useCausalTreeLayout } from '../../src/composables/useCausalTreeLayout';
import type { TraceTreeDto, TraceNodeDto } from '../../src/types/causalTree';

function makeTree(n: number): TraceTreeDto {
  const nodes: TraceNodeDto[] = Array.from({ length: n }, (_, i) => ({
    eventId: `evt-${i}`,
    traceId: 'trace-1',
    publishWallclock: `2026-01-01T10:0${i % 10}:00.000Z`,
    publisherNode: 'node-a',
    topic: 'test',
  }));
  return {
    traceId: 'trace-1',
    sessionId: '',
    nodes,
    edges: [],
    rootEventIds: nodes.map(n => n.eventId),
    leafEventIds: nodes.map(n => n.eventId),
    summary: {
      traceId: 'trace-1', totalEvents: n, truncated: false, totalSpanMs: 0,
      participatingNodes: ['node-a'], rootCount: n, leafCount: n,
    },
  };
}

describe('useCausalTreeLayout', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it('layoutUpdates_WhenTreePropChanges', async () => {
    const store = useCausalTreeStore();
    let layoutRef: ReturnType<typeof useCausalTreeLayout>['layoutResult'];

    const wrapper = mount(defineComponent({
      setup() {
        const { layoutResult } = useCausalTreeLayout();
        layoutRef = layoutResult;
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    // Initially null
    expect(layoutRef!.value).toBeNull();

    // Set tree with 5 nodes
    store.tree = makeTree(5);
    await nextTick();
    expect(layoutRef!.value).not.toBeNull();
    expect(layoutRef!.value!.nodes.size).toBe(5);

    // Change to 10 nodes
    store.tree = makeTree(10);
    await nextTick();
    expect(layoutRef!.value!.nodes.size).toBe(10);

    wrapper.unmount();
  });
});
