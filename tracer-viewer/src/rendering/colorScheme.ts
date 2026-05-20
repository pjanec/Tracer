// tracer-viewer/src/rendering/colorScheme.ts
// Deterministic per-node color from node name hash

/**
 * djb2 hash — returns a non-negative 32-bit integer.
 */
function djb2(str: string): number {
  let hash = 5381;
  for (let i = 0; i < str.length; i++) {
    hash = ((hash << 5) + hash) ^ str.charCodeAt(i);
  }
  return hash >>> 0; // unsigned
}

/**
 * Returns a stable hex color for the given node name.
 * Same name always produces the same color.
 * Uses HSL with fixed saturation (65%) and lightness (55%) for readability.
 */
export function getNodeColor(nodeName: string): string {
  const hash = djb2(nodeName);
  const hue = hash % 360;
  // Convert HSL to hex
  return hslToHex(hue, 65, 55);
}

function hslToHex(h: number, s: number, l: number): string {
  const sn = s / 100;
  const ln = l / 100;
  const a = sn * Math.min(ln, 1 - ln);
  const f = (n: number): string => {
    const k = (n + h / 30) % 12;
    const color = ln - a * Math.max(Math.min(k - 3, 9 - k, 1), -1);
    return Math.round(255 * color)
      .toString(16)
      .padStart(2, '0');
  };
  return `#${f(0)}${f(8)}${f(4)}`;
}

export const SEVERITY_COLORS = {
  info:    '#5b9dff',
  warning: '#e8b048',
  error:   '#e85c5c',
} as const;
