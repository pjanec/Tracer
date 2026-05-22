import { describe, it, expect, vi } from 'vitest';
import { renderTree } from '../../src/rendering/causalTreeRenderer';
import type { CausalTreeRenderInput } from '../../src/rendering/causalTreeRenderer';
import { layout } from '../../src/rendering/causalTreeLayout';
import type { LayoutConfig } from '../../src/rendering/causalTreeLayout';
import { buildNodeColorMap } from '../../src/rendering/colorScheme';
import type { TraceTreeDto, TraceNodeDto, TraceEdgeDto } from '../../src/types/causalTree';

const CONFIG: LayoutConfig = { nodeRadiusPx: 14, hSpacingPx: 40, vSpacingPx: 80, paddingPx: 40 };

function makeCtxMock() {
  const arcCalls: { radius: number; fillStyle: string }[] = [];
  let currentFillStyle = '';

  const ctx = {
    get fillStyle() { return currentFillStyle; },
    set fillStyle(v: string) { currentFillStyle = v; },
    strokeStyle: '',
    lineWidth: 0,
    font: '',
    textAlign: '',
    textBaseline: '',
    arc: vi.fn((_x, _y, radius) => {
      arcCalls.push({ radius, fillStyle: currentFillStyle });
    }),
    fill: vi.fn(),
    stroke: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    bezierCurveTo: vi.fn(),
    fillRect: vi.fn(),
    fillText: vi.fn(),
    measureText: vi.fn(() => ({ width: 30 })),
    save: vi.fn(),
    restore: vi.fn(),
    arcCallsLog: arcCalls,
  } as unknown as CanvasRenderingContext2D & { arcCallsLog: typeof arcCalls };
  return ctx;
}

function makeTree(nodeOverrides: Partial<TraceNodeDto>[] = [], edges: TraceEdgeDto[] = []): TraceTreeDto {
  const nodes: TraceNodeDto[] = nodeOverrides.map((o, i) => ({
    eventId: `evt-${i}`,
    traceId: 'trace-1',
    publishWallclock: `2026-01-01T10:0${i}:00.000Z`,
    publisherNode: 'node-a',
    topic: 'test.topic',
    ...o,
  }));
  const rootEventIds = nodes.filter(n => !edges.some(e => e.childEventId === n.eventId)).map(n => n.eventId);
  const leafEventIds = nodes.filter(n => !edges.some(e => e.parentEventId === n.eventId)).map(n => n.eventId);
  return {
    traceId: 'trace-1',
    sessionId: '',
    nodes,
    edges,
    rootEventIds,
    leafEventIds,
    summary: {
      traceId: 'trace-1', totalEvents: nodes.length, truncated: false, totalSpanMs: 0,
      participatingNodes: [...new Set(nodes.map(n => n.publisherNode))],
      rootCount: rootEventIds.length, leafCount: leafEventIds.length,
    },
  };
}

describe('causalTreeRenderer', () => {
  it('renderTree_SingleEdge_CallsBezierCurveToAndFillText', () => {
    const tree = makeTree(
      [{ eventId: 'parent' }, { eventId: 'child' }],
      [{ parentEventId: 'parent', childEventId: 'child', latencyMs: 5 }]
    );
    // Fix the event IDs since makeTree generates evt-0 etc.
    tree.nodes[0].eventId = 'parent';
    tree.nodes[1].eventId = 'child';
    tree.edges[0].parentEventId = 'parent';
    tree.edges[0].childEventId = 'child';
    tree.rootEventIds = ['parent'];
    tree.leafEventIds = ['child'];

    const layoutResult = layout(tree, CONFIG);
    const ctx = makeCtxMock();
    const input: CausalTreeRenderInput = {
      selectedEventId: null,
      nodeColors: buildNodeColorMap(['node-a']),
    };

    renderTree(ctx, layoutResult, input);

    expect((ctx.bezierCurveTo as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThanOrEqual(1);
    expect((ctx.fillText as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThanOrEqual(1);
  });

  it('renderTree_ErrorSeverityNode_InnerDotUsesErrorColor', () => {
    const tree = makeTree([{ severity: 'error' }]);
    const layoutResult = layout(tree, CONFIG);
    const ctx = makeCtxMock();

    // Track fill style during arc calls
    const arcLog = (ctx as any).arcCallsLog as { radius: number; fillStyle: string }[];

    const input: CausalTreeRenderInput = {
      selectedEventId: null,
      nodeColors: buildNodeColorMap(['node-a']),
    };

    renderTree(ctx, layoutResult, input);

    // There should be an arc call where fillStyle was '#e85c5c' at the time of the arc
    // The inner dot is arc(x, y, 5, ...) with fillStyle = '#e85c5c'
    const dotCall = arcLog.find(c => c.radius === 5 && c.fillStyle === '#e85c5c');
    expect(dotCall).toBeTruthy();
  });

  it('renderTree_NotableNode_FillRectCalledAtCornerOffset', () => {
    const tree = makeTree([{ notableLabel: 'notable' }]);
    const layoutResult = layout(tree, CONFIG);
    const ctx = makeCtxMock();
    const input: CausalTreeRenderInput = {
      selectedEventId: null,
      nodeColors: buildNodeColorMap(['node-a']),
    };

    renderTree(ctx, layoutResult, input);

    const fillRectCalls = (ctx.fillRect as ReturnType<typeof vi.fn>).mock.calls;
    // The notable square: fillRect(x + 8, y - 16, 8, 8)
    const node = [...layoutResult.nodes.values()][0];
    const hasNotableRect = fillRectCalls.some(
      (args: number[]) => args[0] === node.x + 8 && args[1] === node.y - 16
    );
    expect(hasNotableRect).toBe(true);
  });

  it('renderTree_SelectedNode_OuterRingArcPrecedesFillArc', () => {
    const tree = makeTree([{ eventId: 'selected-node' }]);
    tree.nodes[0].eventId = 'selected-node';
    tree.rootEventIds = ['selected-node'];
    tree.leafEventIds = ['selected-node'];

    const layoutResult = layout(tree, CONFIG);
    const ctx = makeCtxMock();
    const arcLog = (ctx as any).arcCallsLog as { radius: number; fillStyle: string }[];

    const input: CausalTreeRenderInput = {
      selectedEventId: 'selected-node',
      nodeColors: buildNodeColorMap(['node-a']),
    };

    renderTree(ctx, layoutResult, input);

    // Find the ring arc (radius 18) and fill arc (radius 14)
    const ringIdx  = arcLog.findIndex(c => c.radius === 18);
    const fillIdx  = arcLog.findIndex(c => c.radius === 14);

    expect(ringIdx).toBeGreaterThanOrEqual(0);
    expect(fillIdx).toBeGreaterThan(ringIdx); // ring precedes fill
  });

  it('renderTree_PublisherNodeColor_MatchesBuildNodeColorMapOutput', () => {
    const tree = makeTree([
      { publisherNode: 'node-alpha' },
      { publisherNode: 'node-beta' },
    ]);
    const layoutResult = layout(tree, CONFIG);
    const nodeColors = buildNodeColorMap(['node-alpha', 'node-beta']);
    const ctx = makeCtxMock();

    const arcLog = (ctx as any).arcCallsLog as { radius: number; fillStyle: string }[];

    const input: CausalTreeRenderInput = {
      selectedEventId: null,
      nodeColors,
    };

    renderTree(ctx, layoutResult, input);

    // Each node fill arc (radius 14) should use the color from nodeColors
    const fillArcs = arcLog.filter(c => c.radius === 14);
    expect(fillArcs.length).toBe(2);
    for (const call of fillArcs) {
      const matchesAlpha = call.fillStyle === nodeColors.get('node-alpha');
      const matchesBeta  = call.fillStyle === nodeColors.get('node-beta');
      expect(matchesAlpha || matchesBeta).toBe(true);
    }
  });

  it('renderTree_500NodeTree_CompletesUnder200ms', () => {
    // Build a 500-node linear chain
    const nodes: TraceNodeDto[] = [];
    const edges: TraceEdgeDto[] = [];
    for (let i = 0; i < 500; i++) {
      nodes.push({
        eventId: `node-${i}`, traceId: 'trace-perf',
        publishWallclock: '2026-01-01T10:00:00.000Z',
        publisherNode: 'node-a', topic: 'perf.test',
      });
      if (i > 0) edges.push({ parentEventId: `node-${i-1}`, childEventId: `node-${i}`, latencyMs: 1 });
    }
    const tree: TraceTreeDto = {
      traceId: 'trace-perf', nodes, edges,
      sessionId: '',
      rootEventIds: ['node-0'],
      leafEventIds: ['node-499'],
      summary: { traceId: 'trace-perf', totalEvents: 500, truncated: false, totalSpanMs: 0,
        participatingNodes: ['node-a'], rootCount: 1, leafCount: 1 },
    };
    const layoutResult = layout(tree, CONFIG);
    const ctx = makeCtxMock();
    const input: CausalTreeRenderInput = {
      selectedEventId: null,
      nodeColors: buildNodeColorMap(['node-a']),
    };

    const t0 = performance.now();
    renderTree(ctx, layoutResult, input);
    const elapsed = performance.now() - t0;

    expect(elapsed).toBeLessThan(200);
  });
});
