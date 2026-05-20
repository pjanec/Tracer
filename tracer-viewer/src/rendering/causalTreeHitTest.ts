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
