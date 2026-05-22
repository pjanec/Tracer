import { describe, it, expect, vi } from 'vitest';
import {
  renderNumericLine,
  renderCategoricalBands,
} from '../../src/rendering/slowStateChartRenderer';
import type { SlowStateRenderInput } from '../../src/rendering/slowStateChartRenderer';

function makeCanvasMock() {
  return {
    clearRect:    vi.fn(),
    beginPath:    vi.fn(),
    moveTo:       vi.fn(),
    lineTo:       vi.fn(),
    stroke:       vi.fn(),
    fillRect:     vi.fn(),
    fillText:     vi.fn(),
    strokeStyle:  '' as string,
    fillStyle:    '' as string,
    lineWidth:    0,
    font:         '' as string,
    textAlign:    '' as CanvasTextAlign,
    textBaseline: '' as CanvasTextBaseline,
  } as unknown as CanvasRenderingContext2D;
}

const FROM_MS = 0;
const TO_MS   = 3;   // 3 ms range for numeric tests

describe('slowStateChartRenderer', () => {
  describe('renderNumericLine', () => {
    it('pathHasCorrectYCoordinatesForThreeSamples', () => {
      // 3 samples: t=[0,1,2]ms, v=[10,20,15]; canvas 300×60
      // min=10, max=20, valRange=10
      // yPos(10) = max(1, min(59, 60 - (0/10)*60)) = max(1,59) = 59
      // yPos(20) = max(1, min(59, 60 - (10/10)*60)) = max(1,1) = 1
      // yPos(15) = max(1, min(59, 60 - (5/10)*60)) = 30
      const ctx = makeCanvasMock();
      const input: SlowStateRenderInput = {
        ctx,
        width: 300,
        height: 60,
        fromMs: FROM_MS,
        toMs: TO_MS,
        samples: [
          { t: 0, value: 10 },
          { t: 1, value: 20 },
          { t: 2, value: 15 },
        ],
        kind: 'numeric',
      };

      renderNumericLine(input);

      const moveCalls = (ctx.moveTo as ReturnType<typeof vi.fn>).mock.calls;
      const lineCalls = (ctx.lineTo as ReturnType<typeof vi.fn>).mock.calls;

      // First sample: moveTo(x=0, y≈59)
      expect(moveCalls.length).toBeGreaterThan(0);
      expect((moveCalls[0] as [number, number])[1]).toBeCloseTo(59, 0);

      // Second sample introduces two lineTo calls (step): (100, prevY=59) then (100, 1)
      const lineYValues = (lineCalls as [number, number][]).map(c => c[1]);
      expect(lineYValues).toContain(1); // yPos(20)

      // Third sample: (200, prevY=1) then (200, 30)
      expect(lineYValues).toContain(30); // yPos(15)
    });

    it('singleSampleExtendsLineToRightEdge', () => {
      // 1 sample at t=0ms, canvas 300px wide, range 0–100ms → x=0
      const ctx = makeCanvasMock();
      renderNumericLine({
        ctx,
        width: 300,
        height: 60,
        fromMs: 0,
        toMs: 100,
        samples: [{ t: 0, value: 5 }],
        kind: 'numeric',
      });

      const lineCalls = (ctx.lineTo as ReturnType<typeof vi.fn>).mock.calls as [number, number][];
      // The final lineTo must reach x=width=300 (right edge)
      const xValues = lineCalls.map(c => c[0]);
      expect(xValues).toContain(300);
    });

    it('allSameValueNoException', () => {
      // All values identical → valRange collapses, must not throw
      const ctx = makeCanvasMock();
      expect(() => {
        renderNumericLine({
          ctx,
          width: 300,
          height: 60,
          fromMs: 0,
          toMs: 100,
          samples: [
            { t: 0,  value: 7 },
            { t: 50, value: 7 },
            { t: 99, value: 7 },
          ],
          kind: 'numeric',
        });
      }).not.toThrow();
    });
  });

  describe('renderCategoricalBands', () => {
    it('firstBandWidthMatchesTimeRange', () => {
      // 2 samples: 'idle' at t=0, 'attack' at t=500ms; canvas 1000px, range 0–1000ms
      // first band: fillRect(0, 0, 500, height)
      const ctx = makeCanvasMock();
      renderCategoricalBands({
        ctx,
        width: 1000,
        height: 60,
        fromMs: 0,
        toMs: 1000,
        samples: [
          { t: 0,   value: 'idle' },
          { t: 500, value: 'attack' },
        ],
        kind: 'categorical',
      });

      const fillCalls = (ctx.fillRect as ReturnType<typeof vi.fn>).mock.calls as [number, number, number, number][];
      expect(fillCalls.length).toBeGreaterThanOrEqual(2);
      // First fillRect: x=0, y=0, width=500, height=60
      expect(fillCalls[0][0]).toBeCloseTo(0, 0);   // x
      expect(fillCalls[0][2]).toBeCloseTo(500, 0); // width
    });

    it('emptySamplesDoesNotThrow', () => {
      const ctx = makeCanvasMock();
      expect(() => {
        renderCategoricalBands({
          ctx,
          width: 1000,
          height: 60,
          fromMs: 0,
          toMs: 1000,
          samples: [],
          kind: 'categorical',
        });
      }).not.toThrow();

      expect((ctx.clearRect as ReturnType<typeof vi.fn>).mock.calls.length).toBe(1);
    });
  });
});
