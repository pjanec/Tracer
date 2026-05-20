import { describe, it, expect } from 'vitest';
import { layout } from '../../src/rendering/causalTreeLayout';
import type { LayoutConfig } from '../../src/rendering/causalTreeLayout';
import type { TraceTreeDto, TraceNodeDto, TraceEdgeDto } from '../../src/types/causalTree';

const DEFAULT_CONFIG: LayoutConfig = {
  nodeRadiusPx: 14,
  hSpacingPx: 40,
  vSpacingPx: 80,
  paddingPx: 40,
};

function makeNode(eventId: string, traceId = 'trace-1', wallclock = '2026-01-01T10:00:00.000Z'): TraceNodeDto {
  return {
    eventId,
    traceId,
    publishWallclock: wallclock,
    publisherNode: 'node-a',
    topic: 'test.topic',
  };
}

function makeEdge(parentEventId: string, childEventId: string): TraceEdgeDto {
  return { parentEventId, childEventId, latencyMs: 5 };
}

function makeLinearChain(n: number): TraceTreeDto {
  const nodes: TraceNodeDto[] = [];
  const edges: TraceEdgeDto[] = [];
  for (let i = 0; i < n; i++) {
    nodes.push(makeNode(`node-${i}`));
    if (i > 0) edges.push(makeEdge(`node-${i - 1}`, `node-${i}`));
  }
  return {
    traceId: 'trace-1',
    nodes,
    edges,
    rootEventIds: ['node-0'],
    leafEventIds: [`node-${n - 1}`],
    summary: {
      traceId: 'trace-1',
      totalEvents: n,
      truncated: false,
      totalSpanMs: 0,
      participatingNodes: ['node-a'],
      rootCount: 1,
      leafCount: 1,
    },
  };
}

describe('causalTreeLayout', () => {
  it('layout_SingleRootLinearChain_LayersAreConsecutiveIntegers', () => {
    const tree = makeLinearChain(5);
    const result = layout(tree, DEFAULT_CONFIG);

    expect(result.nodes.size).toBe(5);
    const layers = [...result.nodes.values()].map(n => n.layer).sort((a, b) => a - b);
    expect(layers).toEqual([0, 1, 2, 3, 4]);
  });

  it('layout_MultiRootDag_EachNodeAssignedExactlyOnce', () => {
    // 3 roots (R1, R2, R3), 7 children distributed
    const nodes: TraceNodeDto[] = [];
    const edges: TraceEdgeDto[] = [];
    for (let i = 0; i < 3; i++) nodes.push(makeNode(`root-${i}`));
    for (let i = 0; i < 7; i++) {
      nodes.push(makeNode(`child-${i}`));
      edges.push(makeEdge(`root-${i % 3}`, `child-${i}`));
    }
    const tree: TraceTreeDto = {
      traceId: 'trace-multi',
      nodes,
      edges,
      rootEventIds: ['root-0', 'root-1', 'root-2'],
      leafEventIds: nodes.filter(n => n.eventId.startsWith('child')).map(n => n.eventId),
      summary: {
        traceId: 'trace-multi', totalEvents: 10, truncated: false, totalSpanMs: 0,
        participatingNodes: ['node-a'], rootCount: 3, leafCount: 7,
      },
    };

    const result = layout(tree, DEFAULT_CONFIG);
    expect(result.nodes.size).toBe(10);
  });

  it('layout_ConvergentNode_LayerIsOnePastMaxParentLayer', () => {
    // Two roots each pointing to one shared child
    const nodes: TraceNodeDto[] = [
      makeNode('root-a'),
      makeNode('root-b'),
      makeNode('child-c'),
    ];
    const edges: TraceEdgeDto[] = [
      makeEdge('root-a', 'child-c'),
      makeEdge('root-b', 'child-c'),
    ];
    const tree: TraceTreeDto = {
      traceId: 'trace-conv',
      nodes,
      edges,
      rootEventIds: ['root-a', 'root-b'],
      leafEventIds: ['child-c'],
      summary: {
        traceId: 'trace-conv', totalEvents: 3, truncated: false, totalSpanMs: 0,
        participatingNodes: ['node-a'], rootCount: 2, leafCount: 1,
      },
    };

    const result = layout(tree, DEFAULT_CONFIG);
    const rootLayerA = result.nodes.get('root-a')!.layer;
    const rootLayerB = result.nodes.get('root-b')!.layer;
    const childLayer = result.nodes.get('child-c')!.layer;

    expect(rootLayerA).toBe(0);
    expect(rootLayerB).toBe(0);
    expect(childLayer).toBe(1); // one past max parent layer
  });

  it('layout_NodesInSameLayer_HaveDistinctXCoordinates', () => {
    // 3 roots at layer 0, each with a child
    const nodes: TraceNodeDto[] = [];
    const edges: TraceEdgeDto[] = [];
    for (let i = 0; i < 3; i++) nodes.push(makeNode(`root-${i}`));
    for (let i = 0; i < 3; i++) {
      nodes.push(makeNode(`child-${i}`));
      edges.push(makeEdge(`root-${i}`, `child-${i}`));
    }
    const tree: TraceTreeDto = {
      traceId: 'trace-x', nodes, edges,
      rootEventIds: ['root-0', 'root-1', 'root-2'],
      leafEventIds: ['child-0', 'child-1', 'child-2'],
      summary: { traceId: 'trace-x', totalEvents: 6, truncated: false, totalSpanMs: 0,
        participatingNodes: ['node-a'], rootCount: 3, leafCount: 3 },
    };

    const result = layout(tree, DEFAULT_CONFIG);

    // Group by layer and check x coords are distinct within each layer
    const byLayer = new Map<number, number[]>();
    for (const node of result.nodes.values()) {
      if (!byLayer.has(node.layer)) byLayer.set(node.layer, []);
      byLayer.get(node.layer)!.push(node.x);
    }
    for (const [, xs] of byLayer) {
      const unique = new Set(xs);
      expect(unique.size).toBe(xs.length);
    }
  });

  it('layout_EdgeEndpoints_FromXMatchesParentX_ToXMatchesChildX', () => {
    const tree = makeLinearChain(3);
    const result = layout(tree, DEFAULT_CONFIG);

    for (const edge of result.edges) {
      const parent = result.nodes.get(edge.parentId)!;
      const child  = result.nodes.get(edge.childId)!;
      expect(edge.fromX).toBe(parent.x);
      expect(edge.toX).toBe(child.x);
    }
  });

  it('layout_EmptyTree_ReturnsZeroSizedResult', () => {
    const tree: TraceTreeDto = {
      traceId: 'trace-empty',
      nodes: [],
      edges: [],
      rootEventIds: [],
      leafEventIds: [],
      summary: { traceId: 'trace-empty', totalEvents: 0, truncated: false, totalSpanMs: 0,
        participatingNodes: [], rootCount: 0, leafCount: 0 },
    };

    const result = layout(tree, DEFAULT_CONFIG);

    expect(result.nodes.size).toBe(0);
    expect(result.edges.length).toBe(0);
    expect(result.widthPx).toBe(0);
    expect(result.heightPx).toBe(0);
  });

  it('layout_500NodeTree_CompletesUnder50ms', () => {
    // Linear chain of 500 nodes
    const tree = makeLinearChain(500);

    const t0 = performance.now();
    const result = layout(tree, DEFAULT_CONFIG);
    const elapsed = performance.now() - t0;

    expect(result.nodes.size).toBe(500);
    expect(elapsed).toBeLessThan(50);
  });

  it('layout_CycleDefense_ReturnsWithoutHanging', () => {
    // Two edges form a cycle: A→B and B→A. Layout must not infinite-loop.
    const nodes: TraceNodeDto[] = [makeNode('cycle-a'), makeNode('cycle-b')];
    const edges: TraceEdgeDto[] = [
      makeEdge('cycle-a', 'cycle-b'),
      makeEdge('cycle-b', 'cycle-a'),
    ];
    const tree: TraceTreeDto = {
      traceId: 'trace-cycle',
      nodes,
      edges,
      rootEventIds: ['cycle-a'],
      leafEventIds: ['cycle-b'],
      summary: {
        traceId: 'trace-cycle', totalEvents: 2, truncated: false, totalSpanMs: 0,
        participatingNodes: ['node-a'], rootCount: 1, leafCount: 1,
      },
    };

    const start = performance.now();
    const result = layout(tree, DEFAULT_CONFIG);
    const elapsed = performance.now() - start;

    // Must complete quickly (not hang)
    expect(elapsed).toBeLessThan(1000);
    // Both nodes must appear exactly once
    expect(result.nodes.size).toBe(2);
    const keys = [...result.nodes.keys()];
    expect(new Set(keys).size).toBe(keys.length);
  });
});
