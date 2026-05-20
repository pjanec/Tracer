// src/composables/useTimelineQuery.ts
// Watches viewport + filter; calls listEvents or aggregateEvents with 100ms debounce
// and AbortController cancellation. In aggregate+live mode, re-polls every 5s.

import { watch, onUnmounted } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { api } from '@/api/tracerApiClient';
import { chooseBucketDuration } from '@/rendering/timelineLayout';

const DEBOUNCE_MS = 100;
const AGGREGATE_LIVE_POLL_MS = 5000;
// Viewport span threshold above which aggregate mode is used (strictly > 4 hours)
const AGGREGATE_THRESHOLD_MS = 4 * 60 * 60 * 1000;

export function useTimelineQuery() {
  const store = useTimelineStore();
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;
  let abortController: AbortController | null = null;
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  function cancelPendingFetch() {
    if (abortController) {
      abortController.abort();
      abortController = null;
    }
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
  }

  function stopPollTimer() {
    if (pollTimer !== null) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  }

  async function fetchNow() {
    if (!store.sessionId) return;

    // Cancel any in-flight fetch
    if (abortController) {
      abortController.abort();
    }
    abortController = new AbortController();
    const signal = abortController.signal;

    const spanMs = store.viewportSpanMs;
    const useAggregate = spanMs > AGGREGATE_THRESHOLD_MS;

    store.loading = true;
    store.error = null;

    try {
      if (useAggregate) {
        const bucketDuration = chooseBucketDuration(spanMs);
        const result = await api.aggregateEvents(
          {
            sessionId:      store.sessionId,
            from:           store.viewport.from,
            to:             store.viewport.to,
            bucketDuration: bucketDuration === 'raw' ? '1s' : bucketDuration,
            groupBy:        'node',
            ...store.filter,
          },
          { signal },
        );
        store.setAggregateResult(result);
      } else {
        const result = await api.listEvents(
          {
            sessionId: store.sessionId,
            from:      store.viewport.from,
            to:        store.viewport.to,
            limit:     2000,
            ...store.filter,
          },
          { signal },
        );
        store.setQueryResult(result);
      }
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') {
        // Aborted — not an error
        return;
      }
      store.error = err instanceof Error ? err.message : 'Query failed';
    } finally {
      store.loading = false;
    }
  }

  function fetchDebounced() {
    cancelPendingFetch();
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      void fetchNow();
    }, DEBOUNCE_MS);
  }

  // Watch viewport and filter; any change triggers a debounced fetch
  watch(
    () => [
      store.viewport.from,
      store.viewport.to,
      store.filter,
    ],
    () => {
      fetchDebounced();
      // Manage aggregate live-poll timer
      stopPollTimer();
      if (store.queryMode === 'aggregate' && store.viewport.followLive) {
        pollTimer = setInterval(() => void fetchNow(), AGGREGATE_LIVE_POLL_MS);
      }
    },
    { deep: true },
  );

  onUnmounted(() => {
    cancelPendingFetch();
    stopPollTimer();
  });

  return { fetchNow, fetchDebounced };
}
