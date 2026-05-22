// src/composables/useCausalTreeUrl.ts
// Bidirectional URL ↔ causalTreeStore binding.
// URL → Store: route params parsed immediately on mount.
// Store → URL: selectedEventId written as ?select= with 250ms debounce via router.replace.

import { watch, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useCausalTreeStore } from '@/stores/causalTreeStore';

const URL_DEBOUNCE_MS = 250;

export function useCausalTreeUrl() {
  const store  = useCausalTreeStore();
  const route  = useRoute();
  const router = useRouter();

  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  // --- URL → Store (immediate on mount + on route change) ---
  function applyRouteToStore(
    name: string | symbol | null | undefined,
    params: Record<string, string | string[]>,
    query: Record<string, string | string[]>,
  ) {
    const get = (k: string): string | undefined => {
      const v = query[k];
      return Array.isArray(v) ? v[0] : v;
    };
    const num = (k: string): number | undefined => {
      const v = get(k);
      if (!v) return undefined;
      const n = parseInt(v, 10);
      return isNaN(n) ? undefined : n;
    };

    if (name === 'causal-by-event') {
      const eventId = Array.isArray(params['eventId']) ? params['eventId'][0] : params['eventId'];
      if (!eventId) return;
      const mode = get('mode');
      if (mode === 'ancestors') {
        store.openAncestors(eventId, num('maxDepth'));
      } else if (mode === 'descendants') {
        store.openDescendants(eventId, num('maxDepth'), num('maxNodes'));
      } else {
        store.openByEvent(eventId, num('maxEvents'));
      }
    } else if (name === 'causal-by-trace') {
      const traceId = Array.isArray(params['traceId']) ? params['traceId'][0] : params['traceId'];
      if (!traceId) return;
      store.openTrace(traceId, num('maxEvents'));
      const select = get('select');
      if (select) {
        store.selectedEventId = select;
      }
    }
  }

  // Apply immediately on mount
  applyRouteToStore(
    route.name,
    route.params as Record<string, string | string[]>,
    route.query as Record<string, string | string[]>,
  );

  // Watch for route navigation (back/forward, router.push)
  const stopRouteWatch = watch(
    () => ({ name: route.name, params: { ...route.params }, query: { ...route.query } }),
    ({ name, params, query }) => applyRouteToStore(name, params as Record<string, string | string[]>, query as Record<string, string | string[]>),
  );

  // --- Store → URL (debounced replace of ?select param) ---
  function scheduleSelectWrite() {
    if (debounceTimer !== null) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      if (!store.selectedEventId) return;
      void router.replace({
        query: { ...route.query, select: store.selectedEventId },
      });
    }, URL_DEBOUNCE_MS);
  }

  const stopSelectWatch = watch(
    () => store.selectedEventId,
    (id) => {
      if (id) scheduleSelectWrite();
    },
  );

  onUnmounted(() => {
    stopRouteWatch();
    stopSelectWatch();
    if (debounceTimer !== null) clearTimeout(debounceTimer);
  });
}
