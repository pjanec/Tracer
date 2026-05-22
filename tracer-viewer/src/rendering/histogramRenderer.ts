// tracer-viewer/src/rendering/histogramRenderer.ts
import type { LatencyDistributionDto, LatencyBudgetDto } from '@/api/tracerApiClient';

export interface HistogramRenderInput {
  distribution: LatencyDistributionDto;
  budget?: LatencyBudgetDto | null;
  canvasWidth: number;
  canvasHeight: number;
}

/** Format a millisecond value: μs if < 1 ms, ms if < 1000 ms, s otherwise. */
export function formatMs(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)} μs`;
  if (ms < 1000) return `${ms.toFixed(1)} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

const LOG10_RANGE_LOW = -2;  // 0.01 ms
const LOG10_RANGE_HIGH = 5;  // 100 000 ms

function msToX(ms: number, canvasWidth: number): number {
  const msClamp = Math.max(ms, 0.001);
  const logVal = Math.log10(msClamp);
  const fraction = (logVal - LOG10_RANGE_LOW) / (LOG10_RANGE_HIGH - LOG10_RANGE_LOW);
  return Math.round(fraction * canvasWidth);
}

export function renderHistogram(ctx: CanvasRenderingContext2D, input: HistogramRenderInput): void {
  const { distribution, budget, canvasWidth, canvasHeight } = input;
  const padTop = 20;
  const padBottom = 28;
  const padLeft = 10;
  const padRight = 10;
  const chartWidth = canvasWidth - padLeft - padRight;
  const chartHeight = canvasHeight - padTop - padBottom;

  // Clear
  ctx.clearRect(0, 0, canvasWidth, canvasHeight);
  ctx.fillStyle = 'transparent';
  ctx.fillRect(0, 0, canvasWidth, canvasHeight);

  // No data
  if (distribution.sampleCount === 0 || distribution.buckets.length === 0) {
    ctx.save();
    ctx.fillStyle = '#888';
    ctx.font = '13px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('No data in range', canvasWidth / 2, canvasHeight / 2);
    ctx.restore();
    return;
  }

  const maxCount = Math.max(...distribution.buckets.map(b => b.count), 1);

  // Draw bars
  ctx.save();
  ctx.fillStyle = '#4a90d9';
  for (const b of distribution.buckets) {
    const x1 = padLeft + msToX(b.lowMs, chartWidth);
    const x2 = padLeft + msToX(b.highMs, chartWidth);
    const barWidth = Math.max(x2 - x1, 1);
    const barHeight = Math.round((b.count / maxCount) * chartHeight);
    ctx.fillRect(x1, padTop + chartHeight - barHeight, barWidth, barHeight);
  }
  ctx.restore();

  // Percentile lines
  const percentileLines = [
    { val: distribution.p50Ms, color: '#4ec97a', label: 'p50' },
    { val: distribution.p99Ms, color: '#e8b048', label: 'p99' },
    { val: distribution.p999Ms, color: '#e85c5c', label: 'p99.9' },
  ];
  for (const { val, color, label } of percentileLines) {
    if (val <= 0) continue;
    const x = padLeft + msToX(val, chartWidth);
    ctx.save();
    ctx.setLineDash([4, 3]);
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(x, padTop);
    ctx.lineTo(x, padTop + chartHeight);
    ctx.stroke();
    ctx.fillStyle = color;
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(label, x, padTop - 5);
    ctx.restore();
  }

  // Budget lines (solid, thicker)
  if (budget) {
    const budgetLines = [
      { val: budget.p99BudgetMs, color: '#f0a000', label: 'p99 budget' },
      { val: budget.absoluteMaxMs, color: '#cc2020', label: 'max budget' },
    ];
    for (const { val, color, label } of budgetLines) {
      if (val == null || val <= 0) continue;
      const x = padLeft + msToX(val, chartWidth);
      ctx.save();
      ctx.setLineDash([]);
      ctx.strokeStyle = color;
      ctx.lineWidth = 2.5;
      ctx.beginPath();
      ctx.moveTo(x, padTop);
      ctx.lineTo(x, padTop + chartHeight);
      ctx.stroke();
      ctx.fillStyle = color;
      ctx.font = '10px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText(label, x, padTop + chartHeight + 12);
      ctx.restore();
    }
  }

  // Summary text (upper right)
  ctx.save();
  ctx.font = '11px monospace';
  ctx.fillStyle = '#ccc';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'top';
  ctx.fillText(`n=${distribution.sampleCount}`, canvasWidth - padRight, 2);
  ctx.fillText(`p50=${formatMs(distribution.p50Ms)}`, canvasWidth - padRight, 14);
  ctx.fillText(`p99=${formatMs(distribution.p99Ms)}`, canvasWidth - padRight, 26);
  ctx.fillText(`max=${formatMs(distribution.maxMs)}`, canvasWidth - padRight, 38);
  ctx.restore();
}
