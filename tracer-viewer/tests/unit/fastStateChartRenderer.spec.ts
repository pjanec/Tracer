import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderFastStateChart, FAST_STATE_COLORS } from '../../src/rendering/fastStateChartRenderer';
import type { FastStateRenderInput } from '../../src/rendering/fastStateChartRenderer';

function makeCtx() {
  return {
    clearRect: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    stroke: vi.fn(),
    fillRect: vi.fn(),
    fillText: vi.fn(),
    strokeStyle: '' as string | CanvasGradient | CanvasPattern,
    fillStyle: '' as string | CanvasGradient | CanvasPattern,
    lineWidth: 0,
    font: '',
    textBaseline: '' as CanvasTextBaseline,
  } as unknown as CanvasRenderingContext2D;
}

const BASE_INPUT: FastStateRenderInput = {
  ctx: makeCtx(),
  width: 200,
  height: 100,
  fromMs: 1000,
  toMs: 2000,
  samples: [
    { ts: new Date(1000).toISOString(), values: { x: 1, y: 10 } },
    { ts: new Date(1500).toISOString(), values: { x: 2, y: 20 } },
    { ts: new Date(2000).toISOString(), values: { x: 3, y: 30 } },
  ],
  columns: ['x', 'y'],
  colors: FAST_STATE_COLORS,
};

describe('fastStateChartRenderer', () => {
  // SC-10: Two columns → strokeStyle set to two distinct colours
  it('twoColumns_StrokeStyleSetToTwoDistinctColors', () => {
    const ctx = makeCtx();
    const strokeStyles: string[] = [];

    // Override stroke to capture strokeStyle at each call
    const originalStroke = ctx.stroke as ReturnType<typeof vi.fn>;
    (ctx as unknown as Record<string, unknown>)['stroke'] = vi.fn(() => {
      strokeStyles.push(ctx.strokeStyle as string);
    });

    renderFastStateChart({ ...BASE_INPUT, ctx });

    expect(strokeStyles).toHaveLength(2);
    expect(strokeStyles[0]).toBe(FAST_STATE_COLORS[0]);
    expect(strokeStyles[1]).toBe(FAST_STATE_COLORS[1]);
    expect(strokeStyles[0]).not.toBe(strokeStyles[1]);
    void originalStroke; // suppress unused warning
  });

  // SC-11: Null values → at least two moveTo calls (line lifted at null)
  it('nullValues_PenLiftedAtNull_MultipleMoveToCalls', () => {
    const ctx = makeCtx();
    const moveToSpy = vi.spyOn(ctx, 'moveTo');

    renderFastStateChart({
      ...BASE_INPUT,
      ctx,
      columns: ['x'],
      samples: [
        { ts: new Date(1000).toISOString(), values: { x: 1 } },
        { ts: new Date(1250).toISOString(), values: { x: null } },
        { ts: new Date(1500).toISOString(), values: { x: 2 } },
        { ts: new Date(1750).toISOString(), values: { x: null } },
        { ts: new Date(2000).toISOString(), values: { x: 3 } },
      ],
    });

    // After null gap: pen is lifted, next non-null calls moveTo again
    expect(moveToSpy.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  // SC-12: Zero samples → no exception thrown
  it('zeroSamples_NoException', () => {
    const ctx = makeCtx();
    expect(() =>
      renderFastStateChart({ ...BASE_INPUT, ctx, samples: [] }),
    ).not.toThrow();
  });
});
