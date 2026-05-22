// src/composables/useEntityHistoryQuery.ts
import { watch, onUnmounted } from 'vue';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { useApi } from '@/api/useApi';

export function useEntityHistoryQuery() {
  const store = useEntityHistoryStore();
  const api = useApi();

  let abortController: AbortController | null = null;

  function cancel() {
    if (abortController) {
      abortController.abort();
      abortController = null;
    }
  }

  async function fetchEntity(entityId: string, sessionId: string) {
    cancel();
    abortController = new AbortController();
    const signal = abortController.signal;

    store.loading = true;
    store.error = null;

    try {
      // Step 1: fetch summary (sequential — provides from/to for subsequent queries)
      const summary = await api.getEntitySummary(entityId, sessionId, { signal });
      if (!summary) {
        store.error = `Entity '${entityId}' not found in session '${sessionId}'`;
        return;
      }
      store.setSummary(summary);

      // Step 2: parallel fetch of events, slow-state, and fast-state topics
      const from = store.timeRange.from;
      const to = store.timeRange.to;

      const [events, slowState, fastStateTopics] = await Promise.all([
        api.getEntityEvents(entityId, sessionId, from, to, { signal }),
        api.getEntitySlowState(entityId, sessionId, from, to, { signal }),
        api.getEntityFastStateTopics(entityId, sessionId, { signal }),
      ]);

      store.setResults(events, slowState, fastStateTopics);
    } catch (err: unknown) {
      if (typeof err === 'object' && err !== null && (err as { name?: unknown }).name === 'AbortError') return; // silently swallow
      store.error = err instanceof Error ? err.message : String(err);
    } finally {
      store.loading = false;
    }
  }

  const stopWatch = watch(
    () => [store.entityId, store.sessionId] as const,
    ([entityId, sessionId]) => {
      if (entityId && sessionId) fetchEntity(entityId, sessionId);
    },
    { immediate: true },
  );

  onUnmounted(() => {
    cancel();
    stopWatch();
  });
}
