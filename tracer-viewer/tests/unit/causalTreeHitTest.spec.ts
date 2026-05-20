import { describe, it, expect } from 'vitest';
import { findNodeAt } from '../../src/rendering/causalTreeHitTest';
import { layout } from '../../src/rendering/causalTreeLayout';
import type { LayoutConfig } from '../../src/rendering/causalTreeLayout';
import type { TraceTreeDto, TraceNodeDto } from '../../src/types/causalTree';

const CONFIG: LayoutConfig = { nodeRadiusPx: 14, hSpacingPx: 40, vSpacingPx: 80, paddingPx: 40 };

function makeSimpleTree(nodeIds: string[]): TraceTreeDto {
  const nodes: TraceNodeDto[] = nodeIds.map(id => ({
    eventId: id, traceId: 'trace-1',
    publishWallclock: '2026-01-01T10:00:00.000Z',
    publisherNode: 'node-a', topic: 'test',
  }));
  return {
    traceId: 'trace-1', nodes, edges: [],
    rootEventIds: nodeIds, leafEventIds: nodeIds,
    summary: { traceId: 'trace-1', totalEvents: nodeIds.length, truncated: false, totalSpanMs: 0,
      participatingNodes: ['node-a'], rootCount: nodeIds.length, leafCount: nodeIds.length },
  };
}

describe('causalTreeHitTest', () => {
  it('findNodeAt_QueryAtNodeCenter_ReturnsNode', () => {
    const tree = makeSimpleTree(['node-a']);
    const layoutResult = layout(tree, CONFIG);
    const node = [...layoutResult.nodes.values()][0];

    const hit = findNodeAt(layoutResult, node.x, node.y, 20);
    expect(hit).not.toBeNull();
    expect(hit!.eventId).toBe('node-a');
  });

  it('findNodeAt_QueryBeyondRadius_ReturnsNull', () => {
    const tree = makeSimpleTree(['node-a']);
    const layoutResult = layout(tree, CONFIG);
    const node = [...layoutResult.nodes.values()][0];

    // Query point exactly radius+1 away
    const hit = findNodeAt(layoutResult, node.x + 21, node.y, 20);
    expect(hit).toBeNull();
  });

  it('findNodeAt_TwoNodesWithinRadius_ReturnsCloserNode', () => {
    // Two nodes: place them manually via a tree that lays them at known positions
    // Use two separate single-node trees to get known x positions, then combine
    const tree = makeSimpleTree(['node-far', 'node-near']);
    const layoutResult = layout(tree, CONFIG);

    const nodeFar  = layoutResult.nodes.get('node-far')!;
    const nodeNear = layoutResult.nodes.get('node-near')!;

    // Query halfway between them, but closer to nodeNear
    // Since nodes are laid out in the same layer, their y is equal
    // and they are separated horizontally
    const midX = (nodeFar.x + nodeNear.x) / 2;
    // Shift slightly toward nodeNear
    const queryX = midX + (nodeNear.x - nodeFar.x) * 0.1;
    const queryY = nodeFar.y; // same y

    const hit = findNodeAt(layoutResult, queryX, queryY, 1000); // large radius ensures both are within
    expect(hit).not.toBeNull();
    expect(hit!.eventId).toBe(nodeNear.x > nodeFar.x ? 'node-near' : 'node-far');
  });

  it('findNodeAt_ClickAtRadiusMinusOne_StillReturnsNode', () => {
    // A click at exactly (radius - 1) pixels from the node center must still hit
    const tree = makeSimpleTree(['target-node']);
    const layoutResult = layout(tree, CONFIG);
    const node = [...layoutResult.nodes.values()][0];

    const radius = CONFIG.nodeRadiusPx; // 14
    // Click at offset (radius - 1, 0) from center → still inside the node circle
    const hit = findNodeAt(layoutResult, node.x + radius - 1, node.y, radius);

    expect(hit).not.toBeNull();
    expect(hit!.eventId).toBe('target-node');
  });
});
