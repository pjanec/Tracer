// src/composables/useTimelineSelection.ts
// Manages selectedEventId and pivot navigation actions.

import { useTimelineStore } from '@/stores/timelineStore';
import { useRouter } from 'vue-router';

export function useTimelineSelection() {
  const store  = useTimelineStore();
  const router = useRouter();

  function selectEvent(eventId: string | null) {
    store.selectedEventId = eventId;
  }

  function filterToTrace(traceId: string) {
    store.applyFilter({ traceId });
  }

  function showInScenario() {
    if (!store.sessionId) return;
    void router.push(`/scenario/${store.sessionId}`);
  }

  return { selectEvent, filterToTrace, showInScenario };
}
