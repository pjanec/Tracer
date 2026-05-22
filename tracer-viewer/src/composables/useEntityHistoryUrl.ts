// src/composables/useEntityHistoryUrl.ts
// Bidirectional binding between entityHistoryStore state and Vue Router query/path params.
// Store → URL: 250ms debounced router.replace (never push).
// URL → Store: immediate on mount via route watch.
// Also manages fastStateTopic / fastStateColumns as local reactive refs (not store state).
import { ref, watch, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';

const URL_DEBOUNCE_MS = 250;

export function useEntityHistoryUrl(): {
  fastStateTopic: Ref<string | null>;
  fastStateColumns: Ref<string[]>;
} {
  const store = useEntityHistoryStore();
  const route = useRoute();
  const router = useRouter();
  let urlDebounceTimer: ReturnType<typeof setTimeout> | null = null;

  // Fast-state local refs (not stored in entityHistoryStore)
  const fastStateTopic = ref<string | null>(null);
  const fastStateColumns = ref<string[]>([]);

  // URL → Store + local refs
  function applyUrlToStore() {
    const entityId = route.params['entityId'] as string | undefined;
    const sessionId = route.query['session'] as string | undefined;
    const fromStr = route.query['from'] as string | undefined;
    const toStr = route.query['to'] as string | undefined;
    const select = route.query['select'] as string | undefined;
    const fstTopic = route.query['fastStateTopic'] as string | undefined;
    const fstColumns = route.query['fastStateColumns'] as string | undefined;

    if (entityId && sessionId) {
      store.setEntity(entityId, sessionId);
    }

    if (fromStr && toStr) {
      store.setTimeRange(new Date(fromStr), new Date(toStr));
    }

    if (select) {
      store.selectedEventId = select;
    }

    fastStateTopic.value = fstTopic ?? null;
    fastStateColumns.value = fstColumns ? fstColumns.split(',').filter(Boolean) : [];
  }

  applyUrlToStore(); // immediate on mount

  const stopRouteWatch = watch(() => route.query, applyUrlToStore);

  // Store + fast-state refs → URL (debounced)
  function scheduleUrlUpdate() {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    urlDebounceTimer = setTimeout(() => {
      // Start from existing query params so other composable instances don't wipe each other's keys.
      const query: Record<string, string> = {};
      for (const [k, v] of Object.entries(route.query)) {
        if (typeof v === 'string') query[k] = v;
      }
      if (store.sessionId) query['session'] = store.sessionId;
      query['from'] = store.timeRange.from.toISOString();
      query['to'] = store.timeRange.to.toISOString();
      if (store.selectedEventId) query['select'] = store.selectedEventId; else delete query['select'];
      if (fastStateTopic.value) query['fastStateTopic'] = fastStateTopic.value; else delete query['fastStateTopic'];
      if (fastStateColumns.value.length > 0) {
        query['fastStateColumns'] = fastStateColumns.value.join(',');
      } else {
        delete query['fastStateColumns'];
      }
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

  const stopFastStateWatch = watch(
    [fastStateTopic, fastStateColumns],
    scheduleUrlUpdate,
    { deep: true },
  );

  onUnmounted(() => {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    stopRouteWatch();
    stopStoreWatch();
    stopFastStateWatch();
  });

  return { fastStateTopic, fastStateColumns };
}
