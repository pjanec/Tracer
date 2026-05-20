# BATCH-31 Instructions — TRC-P6-005 & TRC-P6-006

## Context

Frontend tasks in `d:\Work\Tracer\tracer-viewer`. All TypeScript/Vitest.

- **TRC-P6-005**: `causalTreeLayout.ts` + 7 Vitest tests
- **TRC-P6-006**: `causalTreeRenderer.ts` + `causalTreeHitTest.ts` + 10 Vitest tests

Also need to:
- Add `buildNodeColorMap` to `colorScheme.ts`
- Create `src/types/causalTree.ts` (TypeScript types mirroring the backend DTOs)

Run tests: `cd d:\Work\Tracer\tracer-viewer ; npx vitest run tests/unit/causalTreeLayout.spec.ts tests/unit/causalTreeRenderer.spec.ts tests/unit/causalTreeHitTest.spec.ts`
Run all frontend tests: `cd d:\Work\Tracer\tracer-viewer ; npx vitest run`

---

## TASK 0: Prerequisites

### 0a. Add `buildNodeColorMap` to `src/rendering/colorScheme.ts`

The file already has `getNodeColor(nodeName: string): string` and `SEVERITY_COLORS`. Add at the end:

```typescript
/**
 * Build a map from node name to color for a list of node names.
 * Uses getNodeColor for each, ensuring deterministic colors.
 */
export function buildNodeColorMap(nodes: readonly string[]): Map<string, string> {
  const map = new Map<string, string>();
  for (const name of nodes) {
    map.set(name, getNodeColor(name));
  }
  return map;
}
```

### 0b. Create `src/types/causalTree.ts`

These mirror the backend DTOs from `Tracer.WebApi.Contracts.Dto`. IDs are 16-char uppercase hex strings.

```typescript
// tracer-viewer/src/types/causalTree.ts

export interface TraceTreeDto {
  traceId: string;
  nodes: TraceNodeDto[];
  edges: TraceEdgeDto[];
  rootEventIds: string[];
  leafEventIds: string[];
  summary: TraceSummaryDto;
}

export interface TraceNodeDto {
  eventId: string;
  traceId: string;
  parentEventId?: string | null;
  publishWallclock: string;     // ISO 8601 date-time string
  publisherNode: string;
  topic: string;
  entityId?: string | null;
  severity?: string | null;     // 'info' | 'warning' | 'error' | null
  notableLabel?: string | null;
  payloadJson?: string | null;
}

export interface TraceEdgeDto {
  parentEventId: string;
  childEventId: string;
  latencyMs: number;
}

export interface TraceSummaryDto {
  traceId: string;
  totalEvents: number;
  totalEventsAvailable?: number | null;
  truncated: boolean;
  totalSpanMs: number;
  participatingNodes: string[];
  rootCount: number;
  leafCount: number;
  firstEventUtc?: string | null;
  lastEventUtc?: string | null;
}
```

---

## TASK 1: TRC-P6-005 — DAG Layout Algorithm

### 1a. Create `src/rendering/causalTreeLayout.ts`

Implement exactly as shown in `docs/tracer_phase6_design.md §6.2`. Copy the full implementation from the design doc. Key points:
- `layout(tree: TraceTreeDto, config: LayoutConfig): LayoutResult`
- Layer assignment: longest-path-from-roots (recursive with cycle defense)
- Layer 0 sorted by `publishWallclock` (chronologically)
- Later layers sorted by median of parents' x positions, with `publishWallclock` tiebreaker
- Nodes centered per-layer within total canvas width
- Edge endpoints: `fromY = parent.y + config.nodeRadiusPx`, `toY = child.y - config.nodeRadiusPx`
- Empty tree returns `{ nodes: new Map(), edges: [], widthPx: 0, heightPx: 0 }`

```typescript
// tracer-viewer/src/rendering/causalTreeLayout.ts

import type { TraceTreeDto, TraceNodeDto, TraceEdgeDto } from '@/types/causalTree';

export interface LaidOutNode {
  eventId: string;
  layer: number;
  layerIndex: number;
  x: number;
  y: number;
  node: TraceNodeDto;
}

export interface LaidOutEdge {
  parentId: string;
  childId: string;
  fromX: number;
  fromY: number;
  toX: number;
  toY: number;
  latencyMs: number;
}

export interface LayoutResult {
  nodes: Map<string, LaidOutNode>;
  edges: LaidOutEdge[];
  widthPx: number;
  heightPx: number;
}

export interface LayoutConfig {
  nodeRadiusPx: number;
  hSpacingPx: number;
  vSpacingPx: number;
  paddingPx: number;
}

export function layout(tree: TraceTreeDto, config: LayoutConfig): LayoutResult {
  // Handle empty tree
  if (tree.nodes.length === 0) {
    return { nodes: new Map(), edges: [], widthPx: 0, heightPx: 0 };
  }

  // 1. Build adjacency maps
  const childrenOf = new Map<string, string[]>();
  const parentsOf  = new Map<string, string[]>();
  for (const e of tree.edges) {
    if (!childrenOf.has(e.parentEventId)) childrenOf.set(e.parentEventId, []);
    childrenOf.get(e.parentEventId)!.push(e.childEventId);
    if (!parentsOf.has(e.childEventId)) parentsOf.set(e.childEventId, []);
    parentsOf.get(e.childEventId)!.push(e.parentEventId);
  }

  const nodeById = new Map<string, TraceNodeDto>();
  for (const n of tree.nodes) nodeById.set(n.eventId, n);

  // 2. Assign layers via longest-path-from-roots
  const layerOf = new Map<string, number>();
  const visiting = new Set<string>();

  function computeLayer(id: string): number {
    if (layerOf.has(id)) return layerOf.get(id)!;
    if (visiting.has(id)) return 0; // cycle defense
    visiting.add(id);

    const parents = parentsOf.get(id) ?? [];
    const layer = parents.length === 0
      ? 0
      : Math.max(...parents.map(p => computeLayer(p))) + 1;

    layerOf.set(id, layer);
    visiting.delete(id);
    return layer;
  }

  for (const id of nodeById.keys()) computeLayer(id);

  // 3. Bucket nodes by layer
  const layers: string[][] = [];
  for (const [id, layer] of layerOf) {
    while (layers.length <= layer) layers.push([]);
    layers[layer].push(id);
  }

  // 4. Within-layer ordering
  // Layer 0: sort by publishWallclock (chronological)
  layers[0].sort((a, b) => {
    const ta = new Date(nodeById.get(a)!.publishWallclock).getTime();
    const tb = new Date(nodeById.get(b)!.publishWallclock).getTime();
    return ta - tb;
  });

  // Subsequent layers: sort by median parent index in previous layer
  for (let l = 1; l < layers.length; l++) {
    const prev = layers[l - 1];
    const prevIndex = new Map(prev.map((id, i) => [id, i]));
    layers[l].sort((a, b) => {
      const pa = (parentsOf.get(a) ?? []).map(p => prevIndex.get(p) ?? 0);
      const pb = (parentsOf.get(b) ?? []).map(p => prevIndex.get(p) ?? 0);
      const ma = median(pa);
      const mb = median(pb);
      if (ma !== mb) return ma - mb;
      const ta = new Date(nodeById.get(a)!.publishWallclock).getTime();
      const tb = new Date(nodeById.get(b)!.publishWallclock).getTime();
      return ta - tb;
    });
  }

  // 5. Assign coordinates
  const cellW = config.nodeRadiusPx * 2 + config.hSpacingPx;
  const cellH = config.nodeRadiusPx * 2 + config.vSpacingPx;
  const maxLayerWidth = Math.max(...layers.map(l => l.length));
  const totalWidth  = maxLayerWidth * cellW + config.paddingPx * 2;
  const totalHeight = layers.length * cellH + config.paddingPx * 2;

  const laidOutNodes = new Map<string, LaidOutNode>();
  for (let l = 0; l < layers.length; l++) {
    const layer = layers[l];
    const layerWidth = layer.length * cellW;
    const offsetX = (totalWidth - layerWidth) / 2;

    for (let i = 0; i < layer.length; i++) {
      const id = layer[i];
      const x = offsetX + i * cellW + cellW / 2;
      const y = config.paddingPx + l * cellH + cellH / 2;
      laidOutNodes.set(id, {
        eventId: id,
        layer: l,
        layerIndex: i,
        x, y,
        node: nodeById.get(id)!,
      });
    }
  }

  // 6. Compute edge endpoints
  const laidOutEdges: LaidOutEdge[] = tree.edges.map(e => {
    const parent = laidOutNodes.get(e.parentEventId)!;
    const child  = laidOutNodes.get(e.childEventId)!;
    return {
      parentId: e.parentEventId,
      childId:  e.childEventId,
      fromX: parent.x, fromY: parent.y + config.nodeRadiusPx,
      toX:   child.x,  toY:   child.y - config.nodeRadiusPx,
      latencyMs: e.latencyMs,
    };
  });

  return {
    nodes: laidOutNodes,
    edges: laidOutEdges,
    widthPx:  totalWidth,
    heightPx: totalHeight,
  };
}

function median(xs: number[]): number {
  if (xs.length === 0) return 0;
  const sorted = [...xs].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0
    ? (sorted[mid - 1] + sorted[mid]) / 2
    : sorted[mid];
}
```

### 1b. Create `tests/unit/causalTreeLayout.spec.ts` (7 tests)

```typescript
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
});
```

---

## TASK 2: TRC-P6-006 — Canvas Renderer + Hit Test

### 2a. Create `src/rendering/causalTreeRenderer.ts`

Implement exactly as shown in `docs/tracer_phase6_design.md §7.4`. Key points:
- `renderTree(ctx, layout, input)` — draws edges first, then nodes
- Edges: Bézier curves with control points at 40%/60% of y distance; latency label pill at midpoint
- Nodes: outer ring (radius 18, strokeStyle '#fff') if selected BEFORE fill arc (radius 14); fill with publisher color; inner severity dot (radius 5) for warning/error; notable square at `(x+8, y-16)` size 8×8; topic label below
- Error color: `'#e85c5c'`, Warning color: `'#e8b048'`

```typescript
// tracer-viewer/src/rendering/causalTreeRenderer.ts

import type { LayoutResult, LaidOutNode, LaidOutEdge } from './causalTreeLayout';

export interface CausalTreeRenderInput {
  selectedEventId: string | null;
  nodeColors: Map<string, string>;
}

export function renderTree(
  ctx: CanvasRenderingContext2D,
  layout: LayoutResult,
  input: CausalTreeRenderInput
): void {
  drawEdges(ctx, layout);
  drawNodes(ctx, layout, input);
}

function drawEdges(ctx: CanvasRenderingContext2D, layout: LayoutResult): void {
  ctx.lineWidth = 1.5;
  ctx.strokeStyle = 'rgba(255,255,255,0.25)';
  for (const e of layout.edges) {
    const cp1y = e.fromY + (e.toY - e.fromY) * 0.4;
    const cp2y = e.fromY + (e.toY - e.fromY) * 0.6;
    ctx.beginPath();
    ctx.moveTo(e.fromX, e.fromY);
    ctx.bezierCurveTo(e.fromX, cp1y, e.toX, cp2y, e.toX, e.toY);
    ctx.stroke();
    drawEdgeLatencyLabel(ctx, e);
  }
}

function drawEdgeLatencyLabel(ctx: CanvasRenderingContext2D, e: LaidOutEdge): void {
  const midX = (e.fromX + e.toX) / 2;
  const midY = (e.fromY + e.toY) / 2;
  const label = formatLatency(e.latencyMs);
  ctx.font = '11px var(--font-mono, monospace)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const metrics = ctx.measureText(label);
  const pad = 4;
  ctx.fillStyle = 'rgba(0,0,0,0.5)';
  ctx.fillRect(midX - metrics.width / 2 - pad, midY - 7, metrics.width + pad * 2, 14);
  ctx.fillStyle = 'rgba(255,255,255,0.85)';
  ctx.fillText(label, midX, midY);
}

function drawNodes(
  ctx: CanvasRenderingContext2D,
  layout: LayoutResult,
  input: CausalTreeRenderInput
): void {
  for (const node of layout.nodes.values()) {
    drawNode(ctx, node, input);
  }
}

function drawNode(
  ctx: CanvasRenderingContext2D,
  node: LaidOutNode,
  input: CausalTreeRenderInput
): void {
  const isSelected = node.eventId === input.selectedEventId;
  const color = input.nodeColors.get(node.node.publisherNode) ?? '#888';

  const severityColor =
    node.node.severity === 'error'   ? '#e85c5c' :
    node.node.severity === 'warning' ? '#e8b048' :
    null;

  // Selection ring BEFORE fill (so ring is visually behind but drawn first)
  if (isSelected) {
    ctx.lineWidth = 3;
    ctx.strokeStyle = '#fff';
    ctx.beginPath();
    ctx.arc(node.x, node.y, 18, 0, Math.PI * 2);
    ctx.stroke();
  }

  // Filled circle
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.arc(node.x, node.y, 14, 0, Math.PI * 2);
  ctx.fill();

  // Inner severity dot
  if (severityColor) {
    ctx.fillStyle = severityColor;
    ctx.beginPath();
    ctx.arc(node.x, node.y, 5, 0, Math.PI * 2);
    ctx.fill();
  }

  // Notable square at corner
  if (node.node.notableLabel) {
    ctx.fillStyle = '#fff';
    ctx.fillRect(node.x + 8, node.y - 16, 8, 8);
  }

  // Topic label
  const label = truncate(node.node.topic, 16);
  ctx.font = '10px var(--font-mono, monospace)';
  ctx.fillStyle = 'rgba(255,255,255,0.7)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText(label, node.x, node.y + 18);
}

function formatLatency(ms: number): string {
  if (ms < 1)    return `${(ms * 1000).toFixed(0)}μs`;
  if (ms < 10)   return `${ms.toFixed(1)}ms`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

function truncate(s: string, max: number): string {
  return s.length > max ? s.slice(0, max - 1) + '…' : s;
}
```

### 2b. Create `src/rendering/causalTreeHitTest.ts`

```typescript
// tracer-viewer/src/rendering/causalTreeHitTest.ts

import type { LayoutResult, LaidOutNode } from './causalTreeLayout';

/**
 * Returns the nearest laid-out node within `radius` pixels of (x, y),
 * or null if no node is within radius.
 */
export function findNodeAt(
  layout: LayoutResult,
  x: number,
  y: number,
  radius: number
): LaidOutNode | null {
  let best: LaidOutNode | null = null;
  let bestDist = radius * radius;
  for (const node of layout.nodes.values()) {
    const dx = node.x - x;
    const dy = node.y - y;
    const d2 = dx * dx + dy * dy;
    if (d2 < bestDist) {
      bestDist = d2;
      best = node;
    }
  }
  return best;
}
```

### 2c. Create `tests/unit/causalTreeRenderer.spec.ts` (6 tests)

The mock canvas needs to track the ORDER of arc calls (for the selected-node test). Use a calls array.

```typescript
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
    arc: vi.fn((x, y, radius) => {
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

    const fillStylesAtArc: string[] = [];
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
```

### 2d. Create `tests/unit/causalTreeHitTest.spec.ts` (3 tests + 1 extra)

```typescript
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
});
```

**Note on the TwoNodesWithinRadius test**: The layout places two nodes in layer 0, sorted chronologically (both have the same timestamp so order is stable by iteration order). `node-far` will be at index 0 (leftmost) and `node-near` at index 1 (rightmost) based on the order they appear in `nodeIds`. So `nodeNear.x > nodeFar.x`. The test checks that the closer node is returned. Since we query closer to `nodeNear` (the right one), the result should be `'node-near'`.

---

## Verification

```powershell
cd d:\Work\Tracer\tracer-viewer
npx vitest run tests/unit/causalTreeLayout.spec.ts tests/unit/causalTreeRenderer.spec.ts tests/unit/causalTreeHitTest.spec.ts
npx vitest run
```

All existing tests must still pass. The new test classes (causalTreeLayout: 7, causalTreeRenderer: 6, causalTreeHitTest: 3) must all pass. Total expected: ~100+.

---

## Notes on potential issues

1. **`buildNodeColorMap` not yet exported**: Add it to `colorScheme.ts` as described above.
2. **`@/types/causalTree`** import: create the file first before `causalTreeLayout.ts`.
3. **`OffscreenCanvas` for perf test** (`renderTree_500NodeTree_CompletesUnder200ms`): The jsdom environment doesn't support `OffscreenCanvas`. Use the mock ctx (from `makeCtxMock()`) instead — that's what the test above does.
4. **Arc call logging**: The mock ctx above logs arc calls with the current `fillStyle` at the time of the call. This is essential for `renderTree_ErrorSeverityNode_InnerDotUsesErrorColor` and `renderTree_SelectedNode_OuterRingArcPrecedesFillArc` to work.
5. **`renderTree_ErrorSeverityNode_InnerDotUsesErrorColor`**: The renderer sets `ctx.fillStyle = '#e85c5c'` then calls `ctx.arc(node.x, node.y, 5, ...)` then `ctx.fill()`. The arc is logged with the fillStyle at the TIME OF THE ARC CALL — so this captures the right color.
6. **`renderTree_SelectedNode_OuterRingArcPrecedesFillArc`**: The renderer calls `ctx.arc(x, y, 18, ...)` (ring, via stroke) then `ctx.arc(x, y, 14, ...)` (fill). Confirm the ring arc index < fill arc index.

## Return in your report

1. All files created/modified (with paths)
2. Any corrections made
3. Test results for all three new spec files (test name + pass/fail)
4. Total frontend test count (vitest run)
5. Any issues encountered and how resolved
