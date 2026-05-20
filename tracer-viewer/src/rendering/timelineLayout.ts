// tracer-viewer/src/rendering/timelineLayout.ts
// Coordinate math and bucket duration selection

/**
 * Choose the aggregate bucket duration based on the visible time span in ms.
 * Returns 'raw' when the span is small enough that raw events fit in the row budget.
 *
 * Thresholds:
 *   >= 4h       → '5m'
 *   >= 1h       → '30s'
 *   >= 30 min   → '5s'
 *   >= 5 min    → '1s'
 *   >= 1 min    → '100ms'
 *   < 1 min     → 'raw'
 */
export function chooseBucketDuration(spanMs: number): string {
  const MS_4H   = 4 * 60 * 60 * 1000;
  const MS_1H   = 60 * 60 * 1000;
  const MS_30M  = 30 * 60 * 1000;
  const MS_5M   = 5 * 60 * 1000;
  const MS_1M   = 60 * 1000;

  if (spanMs >= MS_4H)  return '5m';
  if (spanMs >= MS_1H)  return '30s';
  if (spanMs >= MS_30M) return '5s';
  if (spanMs > MS_5M)   return '1s';   // strictly > 5min
  if (spanMs >= MS_1M)  return '100ms';
  return 'raw';
}

/**
 * Convert px-coordinate to timestamp (milliseconds) given viewport.
 */
export function pixelToMs(
  px: number,
  widthPx: number,
  fromMs: number,
  toMs: number,
): number {
  const ratio = px / widthPx;
  return fromMs + ratio * (toMs - fromMs);
}

/**
 * Convert timestamp (ms) to px-coordinate.
 */
export function msToPixel(
  ms: number,
  widthPx: number,
  fromMs: number,
  toMs: number,
): number {
  const ratio = (ms - fromMs) / (toMs - fromMs);
  return ratio * widthPx;
}

/**
 * Compute swimlane Y-center for a node index.
 */
export function swimlaneY(nodeIndex: number, swimlaneHeightPx: number): number {
  return nodeIndex * swimlaneHeightPx + swimlaneHeightPx / 2;
}
