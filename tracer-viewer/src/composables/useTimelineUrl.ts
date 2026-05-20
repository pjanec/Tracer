// src/composables/useTimelineUrl.ts
// Bidirectional binding between timelineStore state and Vue Router query params.
// Store → URL: 250ms debounced router.replace (never push).
// URL → Store: immediate on mount via watcher.

import { watch, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';

const URL_DEBOUNCE_MS = 250;

export function useTimelineUrl() {
  const store = useTimelineStore();
  const route  = useRoute();
  const router = useRouter();

  let urlDebounceTimer: ReturnType<typeof setTimeout> | null = null;

  // --- URL → Store (immediate) ---
  function applyUrlToStore(query: Record<string, string | string[]>) {
    const get = (k: string): string | undefined => {
      const v = query[k];
      return Array.isArray(v) ? v[0] : v;
    };
    const getAll = (k: string): string[] => {
      const v = query[k];
      if (!v) return [];
      return Array.isArray(v) ? v : [v];
    };

    const from = get('from');
    const to   = get('to');
    if (from && to) {
      store.viewport.from = new Date(from);
      store.viewport.to   = new Date(to);
    }

    const topics    = getAll('topic');
    const nodes     = getAll('node');
    const severities = getAll('severity');
    const entityIds = getAll('entityId');
    const playerIds = getAll('playerId');
    const traceId   = get('traceId');
    const notablesOnly = get('notablesOnly') === 'true';
    const select    = get('select');
    const follow    = get('follow') === 'true';

    if (topics.length || nodes.length || severities.length || entityIds.length ||
        playerIds.length || traceId || notablesOnly) {
      store.filter = {
        topics:      topics.length   ? topics    : undefined,
        nodes:       nodes.length    ? nodes     : undefined,
        severities:  severities.length ? severities : undefined,
        entityIds:   entityIds.length ? entityIds  : undefined,
        playerIds:   playerIds.length ? playerIds  : undefined,
        traceId:     traceId         || undefined,
        notablesOnly: notablesOnly   || undefined,
      };
    }

    if (select) {
      store.selectedEventId = select;
    }

    if (follow) {
      store.viewport.followLive = true;
    }
  }

  // Apply immediately on mount
  applyUrlToStore(route.query as Record<string, string | string[]>);

  // Watch route.query for back/forward navigation
  const stopRouteWatch = watch(
    () => route.query,
    (q) => applyUrlToStore(q as Record<string, string | string[]>),
  );

  // --- Store → URL (debounced replace) ---
  function scheduleUrlUpdate() {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    urlDebounceTimer = setTimeout(() => {
      urlDebounceTimer = null;

      const q: Record<string, string | string[]> = {
        from: store.viewport.from.toISOString(),
        to:   store.viewport.to.toISOString(),
      };

      if (store.filter.topics?.length)    q['topic']        = store.filter.topics;
      if (store.filter.nodes?.length)     q['node']         = store.filter.nodes;
      if (store.filter.severities?.length) q['severity']    = store.filter.severities;
      if (store.filter.entityIds?.length)  q['entityId']    = store.filter.entityIds;
      if (store.filter.playerIds?.length)  q['playerId']    = store.filter.playerIds;
      if (store.filter.traceId)           q['traceId']      = store.filter.traceId;
      if (store.filter.notablesOnly)      q['notablesOnly'] = 'true';
      if (store.selectedEventId)          q['select']       = store.selectedEventId;
      if (store.viewport.followLive)      q['follow']       = 'true';

      void router.replace({ query: q });
    }, URL_DEBOUNCE_MS);
  }

  const stopStoreWatch = watch(
    () => ({
      from:           store.viewport.from,
      to:             store.viewport.to,
      followLive:     store.viewport.followLive,
      filter:         store.filter,
      selectedEventId: store.selectedEventId,
    }),
    scheduleUrlUpdate,
    { deep: true, immediate: true },
  );

  onUnmounted(() => {
    stopRouteWatch();
    stopStoreWatch();
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
  });
}
