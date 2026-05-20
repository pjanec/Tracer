// src/composables/useCanvasRenderer.ts
// Full canvas renderer: watches store + DPI + ResizeObserver → re-renders on RAF.

import { ref, watchEffect, onUnmounted, type Ref } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { render } from '@/rendering/timelineRenderer';
import { useResizeObserver } from './useResizeObserver';
import type { HitIndex } from '@/rendering/timelineHitTest';

export function useCanvasRenderer(canvasRef: Ref<HTMLCanvasElement | null>) {
  const store   = useTimelineStore();
  const hitIndex = ref<HitIndex | null>(null);
  let rafId: number | null = null;

  function scheduleRender() {
    if (rafId !== null) cancelAnimationFrame(rafId);
    rafId = requestAnimationFrame(() => {
      rafId = null;
      const canvas = canvasRef.value;
      if (!canvas) return;

      const ctx = canvas.getContext('2d');
      if (!ctx) return;

      // DPI-correct sizing
      const dpr    = window.devicePixelRatio || 1;
      const width  = canvas.clientWidth;
      const height = canvas.clientHeight;
      if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
        canvas.width  = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        ctx.scale(dpr, dpr);
      }

      const nodes = store.queryResult?.events
        .map((e) => e.publisherNode)
        .filter((v, i, a) => a.indexOf(v) === i) ?? [];

      const output = render(ctx, {
        width,
        height,
        fromMs:         store.viewport.from.getTime(),
        toMs:           store.viewport.to.getTime(),
        nodes,
        swimlaneHeightPx: 80,
        markerRadiusPx:    4,
        events:      store.queryMode === 'list'      ? store.queryResult?.events ?? [] : null,
        aggregate:   store.queryMode === 'aggregate' ? store.aggregateResult          : null,
        groupBy:     'node',
      });

      hitIndex.value = output.hitIndex;
    });
  }

  // Re-render on any reactive state change
  watchEffect(() => {
    // Access reactive dependencies
    void store.viewport.from;
    void store.viewport.to;
    void store.queryResult;
    void store.aggregateResult;
    void store.selectedEventId;
    scheduleRender();
  });

  // Re-render on canvas resize
  useResizeObserver(canvasRef, () => scheduleRender());

  onUnmounted(() => {
    if (rafId !== null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
  });

  return { hitIndex };
}
