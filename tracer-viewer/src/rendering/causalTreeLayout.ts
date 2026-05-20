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
