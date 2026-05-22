// tracer-viewer/src/rendering/fastStateChartRenderer.ts
// Pure canvas rendering for FastStateChart — no Vue dependency.

/** Deterministic colour palette by column index. */
export const FAST_STATE_COLORS = [
  '#4a9eff', '#22c55e', '#ef4444', '#f59e0b', '#8b5cf6',
  '#ec4899', '#14b8a6', '#f97316', '#6366f1', '#84cc16',
];

export interface FastStateRenderInput {
  ctx: CanvasRenderingContext2D;
  width: number;
  height: number;
  fromMs: number;
  toMs: number;
  samples: Array<{ ts: string; values: Record<string, number | null> }>;
  columns: string[];   // which columns to render (in order)
  colors: string[];    // colors[i] maps to columns[i % colors.length]
}

export function renderFastStateChart(input: FastStateRenderInput): void {
  const { ctx, width, height, fromMs, toMs, samples, columns, colors } = input;

  ctx.clearRect(0, 0, width, height);

  if (samples.length === 0 || columns.length === 0) return;

  const timeRange = toMs - fromMs;

  for (let i = 0; i < columns.length; i++) {
    const column = columns[i];
    const color = colors[i % colors.length];

    // Compute per-column min/max (ignoring nulls)
    let minVal: number | null = null;
    let maxVal: number | null = null;
    for (const sample of samples) {
      const v = sample.values[column];
      if (v === null || v === undefined) continue;
      if (minVal === null || v < minVal) minVal = v;
      if (maxVal === null || v > maxVal) maxVal = v;
    }

    // Skip column if all values are null
    if (minVal === null || maxVal === null) continue;

    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.beginPath();

    let pathStarted = false;
    for (const sample of samples) {
      const v = sample.values[column];
      const x = timeRange === 0
        ? 0
        : (new Date(sample.ts).getTime() - fromMs) / timeRange * width;

      if (v === null || v === undefined) {
        // Lift pen on null values
        pathStarted = false;
        continue;
      }

      let y: number;
      if (maxVal === minVal) {
        y = height / 2;
      } else {
        y = height - ((v - minVal) / (maxVal - minVal)) * (height - 4) - 2;
      }

      if (!pathStarted) {
        ctx.moveTo(x, y);
        pathStarted = true;
      } else {
        ctx.lineTo(x, y);
      }
    }

    ctx.stroke();
  }

  // Draw legend in top-left: 8×8 px coloured rect + column name
  const LEGEND_X = 4;
  const RECT_SIZE = 8;
  const TEXT_OFFSET_X = 10;
  let legendY = 4;

  ctx.font = '10px sans-serif';
  ctx.textBaseline = 'top';

  for (let i = 0; i < columns.length; i++) {
    const color = colors[i % colors.length];
    ctx.fillStyle = color;
    ctx.fillRect(LEGEND_X, legendY, RECT_SIZE, RECT_SIZE);
    ctx.fillStyle = '#ffffff';
    ctx.fillText(columns[i], LEGEND_X + RECT_SIZE + TEXT_OFFSET_X, legendY);
    legendY += RECT_SIZE + 4;
  }
}
