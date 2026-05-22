import { describe, it, expect } from 'vitest';
import { layoutGraph } from '../../src/rendering/networkGraphLayout';

describe('networkGraphLayout', () => {
  it('EmptyGraph_ReturnsEmptyNodes', () => {
    const result = layoutGraph({ nodes: [], edges: [], canvasWidth: 400, canvasHeight: 400 });
    expect(result.nodes.size).toBe(0);
  });

  it('SingleNode_PositionedNearCanvasCenter', () => {
    const result = layoutGraph({
      nodes: ['A'],
      edges: [],
      canvasWidth: 400,
      canvasHeight: 400,
    });
    expect(result.nodes.size).toBe(1);
    const pos = result.nodes.get('A')!;
    const distFromCenter = Math.sqrt((pos.x - 200) ** 2 + (pos.y - 200) ** 2);
    expect(distFromCenter).toBeLessThan(80);
  });

  it('ConnectedNodes_CloserThanDisconnected', () => {
    // A-B edge weight 100; A and C are not connected
    const result = layoutGraph({
      nodes: ['A', 'B', 'C'],
      edges: [{ from: 'A', to: 'B', weight: 100 }],
      canvasWidth: 400,
      canvasHeight: 400,
    });
    const A = result.nodes.get('A')!;
    const B = result.nodes.get('B')!;
    const C = result.nodes.get('C')!;
    const distAB = Math.sqrt((A.x - B.x) ** 2 + (A.y - B.y) ** 2);
    const distAC = Math.sqrt((A.x - C.x) ** 2 + (A.y - C.y) ** 2);
    expect(distAB).toBeLessThan(distAC);
  });

  it('Layout_IsDeterministic', () => {
    const input = {
      nodes: ['A', 'B', 'C', 'D'],
      edges: [
        { from: 'A', to: 'B', weight: 10 },
        { from: 'B', to: 'C', weight: 5 },
        { from: 'C', to: 'D', weight: 20 },
      ],
      canvasWidth: 600,
      canvasHeight: 400,
    };
    const run1 = layoutGraph(input);
    const run2 = layoutGraph(input);
    for (const node of input.nodes) {
      const p1 = run1.nodes.get(node)!;
      const p2 = run2.nodes.get(node)!;
      expect(p1.x).toBe(p2.x);
      expect(p1.y).toBe(p2.y);
    }
  });
});
