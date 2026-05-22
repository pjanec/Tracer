// tracer-viewer/src/rendering/networkGraphLayout.ts

export interface GraphLayoutInput {
  nodes: string[];
  edges: { from: string; to: string; weight: number }[];
  canvasWidth: number;
  canvasHeight: number;
}

export interface NodePosition {
  x: number;
  y: number;
}

export interface LaidOutGraph {
  nodes: Map<string, NodePosition>;
}

const MARGIN = 40;
const ITERATIONS = 200;

/**
 * Fruchterman-Reingold-ish layout.
 * Deterministic: same input → same output.
 * Initial positions: nodes on a circle in index order.
 */
export function layoutGraph(input: GraphLayoutInput): LaidOutGraph {
  const { nodes, edges, canvasWidth, canvasHeight } = input;
  const result = new Map<string, NodePosition>();

  if (nodes.length === 0) return { nodes: result };

  const cx = canvasWidth / 2;
  const cy = canvasHeight / 2;
  const radius = Math.min(canvasWidth, canvasHeight) / 2 - MARGIN;

  // Initial circle placement (deterministic)
  const positions = new Map<string, { x: number; y: number }>();
  nodes.forEach((node, i) => {
    const angle = (2 * Math.PI * i) / nodes.length - Math.PI / 2;
    positions.set(node, {
      x: cx + radius * Math.cos(angle),
      y: cy + radius * Math.sin(angle),
    });
  });

  if (nodes.length === 1) {
    result.set(nodes[0], { x: Math.round(cx), y: Math.round(cy) });
    return { nodes: result };
  }

  const k = Math.sqrt((canvasWidth * canvasHeight) / nodes.length);

  // Repulsion force
  function repulse(dist: number): number {
    return (k * k) / (dist || 0.0001);
  }

  // Attraction force scaled by log10(weight + 1)
  function attract(dist: number, weight: number): number {
    return ((dist * dist) / k) / Math.log10(weight + 1 + 1);
  }

  let temperature = 0.1 * (canvasWidth + canvasHeight) / 2;

  for (let iter = 0; iter < ITERATIONS; iter++) {
    // Repulsive forces
    const disp = new Map<string, { dx: number; dy: number }>();
    nodes.forEach(n => disp.set(n, { dx: 0, dy: 0 }));

    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const ni = nodes[i];
        const nj = nodes[j];
        const pi = positions.get(ni)!;
        const pj = positions.get(nj)!;
        const dx = pi.x - pj.x;
        const dy = pi.y - pj.y;
        const dist = Math.sqrt(dx * dx + dy * dy) || 0.0001;
        const force = repulse(dist);
        const fx = (dx / dist) * force;
        const fy = (dy / dist) * force;
        disp.get(ni)!.dx += fx;
        disp.get(ni)!.dy += fy;
        disp.get(nj)!.dx -= fx;
        disp.get(nj)!.dy -= fy;
      }
    }

    // Attractive forces
    for (const e of edges) {
      const pf = positions.get(e.from);
      const pt = positions.get(e.to);
      if (!pf || !pt) continue;
      const dx = pf.x - pt.x;
      const dy = pf.y - pt.y;
      const dist = Math.sqrt(dx * dx + dy * dy) || 0.0001;
      const force = attract(dist, e.weight);
      const fx = (dx / dist) * force;
      const fy = (dy / dist) * force;
      disp.get(e.from)!.dx -= fx;
      disp.get(e.from)!.dy -= fy;
      disp.get(e.to)!.dx += fx;
      disp.get(e.to)!.dy += fy;
    }

    // Apply displacement with temperature cap
    for (const n of nodes) {
      const d = disp.get(n)!;
      const p = positions.get(n)!;
      const dlen = Math.sqrt(d.dx * d.dx + d.dy * d.dy) || 0.0001;
      const moved = Math.min(dlen, temperature);
      p.x += (d.dx / dlen) * moved;
      p.y += (d.dy / dlen) * moved;
      // Clamp to canvas
      p.x = Math.max(MARGIN, Math.min(canvasWidth - MARGIN, p.x));
      p.y = Math.max(MARGIN, Math.min(canvasHeight - MARGIN, p.y));
    }

    temperature *= 0.95;
  }

  for (const [node, pos] of positions) {
    result.set(node, { x: Math.round(pos.x), y: Math.round(pos.y) });
  }

  return { nodes: result };
}
