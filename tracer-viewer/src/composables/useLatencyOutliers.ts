// tracer-viewer/src/composables/useLatencyOutliers.ts
import { ref, watch, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { LatencyOutlierListDto } from '@/api/tracerApiClient';
import type { LatencyFilter } from './useLatencyDistribution';

export function useLatencyOutliers(filter: Ref<LatencyFilter | null>) {
  const outlierList = ref<LatencyOutlierListDto | null>(null);
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
      outlierList.value = await api.getLatencyOutliers(
        { ...filter.value, from: filter.value.from, to: filter.value.to },
        controller.signal,
      );
    } catch (e: unknown) {
      if (e instanceof Error && e.name === 'AbortError') return;
      const status = (e as { status?: number }).status ?? 0;
      error.value = { status };
      outlierList.value = null;
    } finally {
      loading.value = false;
    }
  }

  watch(filter, fetchFn, { immediate: true, deep: true });
  onUnmounted(() => controller?.abort());

  return { outlierList, loading, error };
}
