// tracer-viewer/src/composables/useCanvasRenderer.ts
// Stub for TRC-P5-006. Full rendering wired in that batch.

import { ref, type Ref } from 'vue';
import type { HitIndex } from '@/rendering/timelineHitTest';

export function useCanvasRenderer(_canvasRef: Ref<HTMLCanvasElement | null>) {
  const hitIndex = ref<HitIndex | null>(null);
  // Full rendering pipeline wired in TRC-P5-006
  return { hitIndex };
}
