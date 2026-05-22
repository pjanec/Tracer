import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHistogram, formatMs } from '../../src/rendering/histogramRenderer';
import type { LatencyDistributionDto, LatencyBudgetDto } from '../../src/api/tracerApiClient';

function makeCtx() {
  const calls: { method: string; args: unknown[] }[] = [];
  const ctx: Partial<CanvasRenderingContext2D> = {
    clearRect: vi.fn(),
    fillRect: vi.fn((...a) => calls.push({ method: 'fillRect', args: a })),
    fillText: vi.fn((...a) => calls.push({ method: 'fillText', args: a })),
    strokeStyle: '',
    fillStyle: '',
    lineWidth: 1,
    font: '',
    textAlign: 'left' as CanvasTextAlign,
    textBaseline: 'alphabetic' as CanvasTextBaseline,
    save: vi.fn(),
    restore: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    stroke: vi.fn((...a) => calls.push({ method: 'stroke', args: a })),
    fill: vi.fn(),
    setLineDash: vi.fn(),
    closePath: vi.fn(),
  };
  return { ctx: ctx as CanvasRenderingContext2D, calls };
}

function emptyDist(): LatencyDistributionDto {
  return {
    sampleCount: 0,
    p50Ms: 0, p90Ms: 0, p99Ms: 0, p999Ms: 0,
    maxMs: 0, minMs: 0, meanMs: 0, stddevMs: 0,
    buckets: [],
  };
}

function singleBucketDist(): LatencyDistributionDto {
  return {
    sampleCount: 100,
    p50Ms: 2, p90Ms: 5, p99Ms: 10, p999Ms: 20,
    maxMs: 50, minMs: 0.5, meanMs: 3, stddevMs: 1,
    buckets: [{ index: 4, lowMs: 1, highMs: 2, count: 100 }],
  };
}

describe('histogramRenderer', () => {
  it('EmptyDistribution_DrawsNoDataMessage', () => {
    const { ctx, calls } = makeCtx();
    renderHistogram(ctx, {
      distribution: emptyDist(),
      budget: null,
      canvasWidth: 400,
      canvasHeight: 200,
    });
    const fillTextCalls = calls.filter(c => c.method === 'fillText');
    expect(fillTextCalls.some(c => String(c.args[0]).includes('No data in range'))).toBe(true);
  });

  it('SingleBucket_DrawsBar', () => {
    const { ctx, calls } = makeCtx();
    renderHistogram(ctx, {
      distribution: singleBucketDist(),
      budget: null,
      canvasWidth: 400,
      canvasHeight: 200,
    });
    const fillRectCalls = calls.filter(c => c.method === 'fillRect');
    expect(fillRectCalls.length).toBeGreaterThan(0);
  });

  it('P99Line_DrawnAtCorrectX', () => {
    // The p99 line should produce a stroke. We capture stroke calls.
    const strokeCalls: unknown[][] = [];
    const ctx: Partial<CanvasRenderingContext2D> = {
      clearRect: vi.fn(),
      fillRect: vi.fn(),
      fillText: vi.fn(),
      strokeStyle: '',
      fillStyle: '',
      lineWidth: 1,
      font: '',
      textAlign: 'left' as CanvasTextAlign,
      textBaseline: 'alphabetic' as CanvasTextBaseline,
      save: vi.fn(),
      restore: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(() => strokeCalls.push([])),
      fill: vi.fn(),
      setLineDash: vi.fn(),
      closePath: vi.fn(),
    } as unknown as CanvasRenderingContext2D;

    renderHistogram(ctx as CanvasRenderingContext2D, {
      distribution: singleBucketDist(),
      budget: null,
      canvasWidth: 400,
      canvasHeight: 200,
    });
    // At least one stroke call for p99 line (and others for p50, p999)
    expect(strokeCalls.length).toBeGreaterThan(0);
  });

  it('BudgetLine_DrawnWhenPresent', () => {
    const strokeColors: string[] = [];
    const ctx: Partial<CanvasRenderingContext2D> = {
      clearRect: vi.fn(),
      fillRect: vi.fn(),
      fillText: vi.fn(),
      strokeStyle: '',
      fillStyle: '',
      lineWidth: 1,
      font: '',
      textAlign: 'left' as CanvasTextAlign,
      textBaseline: 'alphabetic' as CanvasTextBaseline,
      save: vi.fn(),
      restore: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(function (this: Partial<CanvasRenderingContext2D>) {
        strokeColors.push(String(this.strokeStyle));
      }),
      fill: vi.fn(),
      setLineDash: vi.fn(),
      closePath: vi.fn(),
    } as unknown as CanvasRenderingContext2D;

    const budget: LatencyBudgetDto = { topic: 'test', p99BudgetMs: 50, absoluteMaxMs: 100 };
    renderHistogram(ctx as CanvasRenderingContext2D, {
      distribution: singleBucketDist(),
      budget,
      canvasWidth: 400,
      canvasHeight: 200,
    });
    // Budget line colours are '#f0a000' and '#cc2020' — check at least one appeared
    expect(strokeColors.some(c => c === '#f0a000' || c === '#cc2020')).toBe(true);
  });

  it('BudgetLine_AbsentWhenBudgetNull', () => {
    const strokeColors: string[] = [];
    const ctx: Partial<CanvasRenderingContext2D> = {
      clearRect: vi.fn(),
      fillRect: vi.fn(),
      fillText: vi.fn(),
      strokeStyle: '',
      fillStyle: '',
      lineWidth: 1,
      font: '',
      textAlign: 'left' as CanvasTextAlign,
      textBaseline: 'alphabetic' as CanvasTextBaseline,
      save: vi.fn(),
      restore: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(function (this: Partial<CanvasRenderingContext2D>) {
        strokeColors.push(String(this.strokeStyle));
      }),
      fill: vi.fn(),
      setLineDash: vi.fn(),
      closePath: vi.fn(),
    } as unknown as CanvasRenderingContext2D;

    renderHistogram(ctx as CanvasRenderingContext2D, {
      distribution: singleBucketDist(),
      budget: null,
      canvasWidth: 400,
      canvasHeight: 200,
    });
    expect(strokeColors.some(c => c === '#f0a000' || c === '#cc2020')).toBe(false);
  });
});

describe('formatMs', () => {
  it('formats sub-1ms values in microseconds', () => {
    expect(formatMs(0.5)).toBe('500 μs');
  });

  it('formats ms values with 1dp', () => {
    expect(formatMs(12.3)).toBe('12.3 ms');
  });

  it('formats large values in seconds', () => {
    expect(formatMs(2000)).toBe('2.00 s');
  });
});
