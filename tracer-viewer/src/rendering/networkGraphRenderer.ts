// tracer-viewer/src/rendering/networkGraphRenderer.ts
import type { LaidOutGraph } from './networkGraphLayout';

export interface GraphRenderInput {
  layout: LaidOutGraph;
  nodes: string[];
  edges: { from: string; to: string; weight: number }[];
  selectedEdge: { from: string; to: string } | null;
  hoveredNode: string | null;
}

const NODE_RADIUS = 14;
const NODE_RADIUS_HOVERED = 18;
const ARROWHEAD_SIZE = 8;

function clamp(val: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, val));
}

function edgeLineWidth(weight: number): number {
  return clamp(Math.log10(weight + 1) * 1.5, 1, 8);
}

function isSelectedEdge(
  e: { from: string; to: string },
  sel: { from: string; to: string } | null,
): boolean {
  return sel !== null && e.from === sel.from && e.to === sel.to;
}

export function renderGraph(ctx: CanvasRenderingContext2D, input: GraphRenderInput): void {
  const { layout, nodes, edges, selectedEdge, hoveredNode } = input;

  ctx.clearRect(0, 0, ctx.canvas.width, ctx.canvas.height);

  // Draw edges
  for (const edge of edges) {
    const from = layout.nodes.get(edge.from);
    const to = layout.nodes.get(edge.to);
    if (!from || !to) continue;

    const selected = isSelectedEdge(edge, selectedEdge);
    const lw = edgeLineWidth(edge.weight);

    ctx.save();
    ctx.lineWidth = lw;
    ctx.strokeStyle = selected ? '#5b9dff' : 'rgba(150, 180, 220, 0.6)';
    ctx.setLineDash([]);

    // Bezier with slight curve
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const len = Math.sqrt(dx * dx + dy * dy) || 1;
    const perpX = -dy / len;
    const perpY = dx / len;
    const curvature = 0.15;
    const cx1 = from.x + dx * 0.5 + perpX * len * curvature;
    const cy1 = from.y + dy * 0.5 + perpY * len * curvature;

    // Shorten endpoint to node radius
    const toR = hoveredNode === edge.to ? NODE_RADIUS_HOVERED : NODE_RADIUS;
    const angle = Math.atan2(to.y - cy1, to.x - cx1);
    const endX = to.x - Math.cos(angle) * (toR + ARROWHEAD_SIZE);
    const endY = to.y - Math.sin(angle) * (toR + ARROWHEAD_SIZE);

    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.quadraticCurveTo(cx1, cy1, endX, endY);
    ctx.stroke();

    // Arrowhead
    ctx.fillStyle = selected ? '#5b9dff' : 'rgba(150, 180, 220, 0.6)';
    ctx.beginPath();
    ctx.moveTo(endX, endY);
    ctx.lineTo(
      endX - ARROWHEAD_SIZE * Math.cos(angle - Math.PI / 6),
      endY - ARROWHEAD_SIZE * Math.sin(angle - Math.PI / 6),
    );
    ctx.lineTo(
      endX - ARROWHEAD_SIZE * Math.cos(angle + Math.PI / 6),
      endY - ARROWHEAD_SIZE * Math.sin(angle + Math.PI / 6),
    );
    ctx.closePath();
    ctx.fill();
    ctx.restore();
  }

  // Draw nodes
  for (const node of nodes) {
    const pos = layout.nodes.get(node);
    if (!pos) continue;
    const isHovered = hoveredNode === node;
    const r = isHovered ? NODE_RADIUS_HOVERED : NODE_RADIUS;

    ctx.save();
    ctx.beginPath();
    ctx.arc(pos.x, pos.y, r, 0, 2 * Math.PI);
    ctx.fillStyle = isHovered ? '#7ab8ff' : '#4a7fc1';
    ctx.fill();
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 1.5;
    ctx.stroke();

    // Label below
    ctx.font = '11px sans-serif';
    ctx.fillStyle = '#ddd';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillText(node, pos.x, pos.y + r + 3);
    ctx.restore();
  }
}
