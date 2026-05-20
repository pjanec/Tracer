// src/composables/useTimelineLiveStream.ts
// Opens GET /api/live/events SSE via @microsoft/fetch-event-source.
// Calls store.appendLiveEvent on each message.
// Re-connects when store.filter changes.

import { watch, onUnmounted } from 'vue';
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { useTimelineStore } from '@/stores/timelineStore';
import type { EventDto } from '@/types/timeline';

export function useTimelineLiveStream() {
  const store = useTimelineStore();
  let abortCtrl: AbortController | null = null;

  function buildUrl(): string {
    const params = new URLSearchParams();
    if (store.sessionId) params.set('sessionId', store.sessionId);
    store.filter.topics?.forEach((t) => params.append('topic', t));
    store.filter.nodes?.forEach((n) => params.append('node', n));
    store.filter.severities?.forEach((s) => params.append('severity', s));
    store.filter.entityIds?.forEach((e) => params.append('entityId', e));
    store.filter.playerIds?.forEach((p) => params.append('playerId', p));
    if (store.filter.traceId)     params.set('traceId',     store.filter.traceId);
    if (store.filter.notablesOnly) params.set('notablesOnly', 'true');
    return `/api/live/events?${params.toString()}`;
  }

  async function connect() {
    abortCtrl?.abort();
    abortCtrl = new AbortController();

    try {
      await fetchEventSource(buildUrl(), {
        signal: abortCtrl.signal,
        openWhenHidden: true,
        onmessage(ev) {
          if (!ev.data) return;
          try {
            const dto = JSON.parse(ev.data) as EventDto;
            store.appendLiveEvent(dto);
          } catch {
            // ignore malformed messages
          }
        },
        onerror() {
          // Let fetchEventSource handle back-off; don't rethrow
        },
      });
    } catch {
      // Swallow — abort throws DOMException
    }
  }

  // Re-connect when filter changes
  const stopFilterWatch = watch(
    () => store.filter,
    () => void connect(),
    { deep: true },
  );

  // Initial connect on mount
  void connect();

  onUnmounted(() => {
    stopFilterWatch();
    abortCtrl?.abort();
  });
}
