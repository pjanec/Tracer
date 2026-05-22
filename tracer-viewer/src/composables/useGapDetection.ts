// tracer-viewer/src/composables/useGapDetection.ts
import { ref, watch, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { GapResultDto } from '@/api/tracerApiClient';

export interface GapFilter {
  from: string | null;
  to: string | null;
  topic?: string;
  publisherNode?: string;
  subscriberNode?: string;
}

export function useGapDetection(filter: Ref<GapFilter | null>) {
  const gapResult = ref<GapResultDto | null>(null);
  const loading = ref(false);
  const error = ref<{ status: number } | null>(null);
  let controller: AbortController | null = null;

  async function fetchFn() {
    if (!filter.value?.from || !filter.value?.to) return;
    controller?.abort();
    controller = new AbortController();
    loading.value = true;
    error.value = null;
    try {
      gapResult.value = await api.getGaps(
        { ...filter.value, from: filter.value.from, to: filter.value.to },
        controller.signal,
      );
    } catch (e: unknown) {
      if (e instanceof Error && e.name === 'AbortError') return;
      const status = (e as { status?: number }).status ?? 0;
      error.value = { status };
      gapResult.value = null;
    } finally {
      loading.value = false;
    }
  }

  watch(filter, fetchFn, { immediate: true, deep: true });
  onUnmounted(() => controller?.abort());

  return { gapResult, loading, error };
}
