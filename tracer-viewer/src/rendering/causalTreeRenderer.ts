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
