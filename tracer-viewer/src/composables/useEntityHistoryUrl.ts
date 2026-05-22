// src/composables/useEntityHistoryUrl.ts
// Bidirectional binding between entityHistoryStore state and Vue Router query/path params.
// Store → URL: 250ms debounced router.replace (never push).
// URL → Store: immediate on mount via route watch.
import { watch, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';

const URL_DEBOUNCE_MS = 250;

export function useEntityHistoryUrl() {
  const store = useEntityHistoryStore();
  const route = useRoute();
  const router = useRouter();
  let urlDebounceTimer: ReturnType<typeof setTimeout> | null = null;

  // URL → Store
  function applyUrlToStore() {
    const entityId = route.params['entityId'] as string | undefined;
    const sessionId = route.query['session'] as string | undefined;
    const fromStr = route.query['from'] as string | undefined;
    const toStr = route.query['to'] as string | undefined;
    const select = route.query['select'] as string | undefined;

    if (entityId && sessionId) {
      store.setEntity(entityId, sessionId);
    }

    if (fromStr && toStr) {
      store.setTimeRange(new Date(fromStr), new Date(toStr));
    }

    if (select) {
      store.selectedEventId = select;
    }
  }

  applyUrlToStore(); // immediate on mount

  const stopRouteWatch = watch(() => route.query, applyUrlToStore);

  // Store → URL (debounced)
  function scheduleUrlUpdate() {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    urlDebounceTimer = setTimeout(() => {
      const query: Record<string, string> = {};
      if (store.sessionId) query['session'] = store.sessionId;
      query['from'] = store.timeRange.from.toISOString();
      query['to'] = store.timeRange.to.toISOString();
      if (store.selectedEventId) query['select'] = store.selectedEventId;
      void router.replace({ query });
    }, URL_DEBOUNCE_MS);
  }

  const stopStoreWatch = watch(
    () => [
      store.timeRange.from.toISOString(),
      store.timeRange.to.toISOString(),
      store.selectedEventId,
    ],
    scheduleUrlUpdate,
  );

  onUnmounted(() => {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    stopRouteWatch();
    stopStoreWatch();
  });
}
