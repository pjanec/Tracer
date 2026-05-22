// tracer-viewer/src/rendering/latencyTimeSeriesRenderer.ts
import type { LatencyTimeSeriesDto, LatencyTimeSeriesPointDto } from '@/api/tracerApiClient';

export interface TimeSeriesRenderInput {
  timeseries: LatencyTimeSeriesDto;
  canvasWidth: number;
  canvasHeight: number;
}

export function renderTimeSeries(ctx: CanvasRenderingContext2D, input: TimeSeriesRenderInput): void {
  const { timeseries, canvasWidth, canvasHeight } = input;
  const padTop = 16;
  const padBottom = 24;
  const padLeft = 8;
  const padRight = 8;
  const chartWidth = canvasWidth - padLeft - padRight;
  const chartHeight = canvasHeight - padTop - padBottom;

  ctx.clearRect(0, 0, canvasWidth, canvasHeight);

  const points = timeseries.points;
  if (points.length === 0) {
    ctx.save();
    ctx.fillStyle = '#888';
    ctx.font = '13px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('No data', canvasWidth / 2, canvasHeight / 2);
    ctx.restore();
    return;
  }

  const maxP99 = Math.max(...points.map(p => p.p99Ms), 1);
  const yMax = maxP99 * 1.1;
  const yMin = 0;

  function toX(i: number): number {
    return padLeft + (i / (points.length - 1 || 1)) * chartWidth;
  }
  function toY(ms: number): number {
    const fraction = (ms - yMin) / (yMax - yMin);
    return padTop + chartHeight - fraction * chartHeight;
  }

  // p50 line (dim dashed thin)
  ctx.save();
  ctx.setLineDash([4, 4]);
  ctx.strokeStyle = 'rgba(100, 180, 255, 0.55)';
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  points.forEach((p, i) => {
    const x = toX(i);
    const y = toY(p.p50Ms);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  });
  ctx.stroke();
  ctx.restore();

  // p99 line (bright solid thick)
  ctx.save();
  ctx.setLineDash([]);
  ctx.strokeStyle = '#e8b048';
  ctx.lineWidth = 2.5;
  ctx.beginPath();
  points.forEach((p, i) => {
    const x = toX(i);
    const y = toY(p.p99Ms);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  });
  ctx.stroke();
  ctx.restore();
}

/**
 * Returns the index of the nearest data point to mouseX.
 * Returns -1 if points is empty.
 */
export function hitTestTimeSeries(
  points: LatencyTimeSeriesPointDto[],
  mouseX: number,
  canvasWidthPx: number,
): number {
  if (points.length === 0) return -1;
  const padLeft = 8;
  const padRight = 8;
  const chartWidth = canvasWidthPx - padLeft - padRight;
  const relX = mouseX - padLeft;
  const fraction = relX / chartWidth;
  const idx = Math.round(fraction * (points.length - 1));
  return Math.max(0, Math.min(points.length - 1, idx));
}
