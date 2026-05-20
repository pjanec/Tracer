// tracer-viewer/src/stores/timelineStore.ts
// Minimal Pinia store supporting TimelineView components (full version in TRC-P5-006).

import { defineStore } from 'pinia';
import type { TimelineFilter } from '@/types/timeline';

export const useTimelineStore = defineStore('timeline', {
  state: () => ({
    sessionId: null as string | null,
    viewport: {
      from: new Date(),
      to: new Date(),
      followLive: false,
    },
    filter: {} as TimelineFilter,
    queryMode: 'list' as 'list' | 'aggregate',
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
    isLiveSession: false,
    /** Total matching events (list mode). */
    totalMatching: 0,
    /** Events returned in current list query. */
    returned: 0,
    /** Whether the current list result was truncated. */
    truncated: false,
    /** Current bucket duration (aggregate mode). */
    bucketDuration: '1s',
  }),

  actions: {
    setSession(id: string) {
      this.sessionId = id;
    },

    panBy(ms: number) {
      this.viewport = {
        from: new Date(this.viewport.from.getTime() + ms),
        to:   new Date(this.viewport.to.getTime() + ms),
        followLive: false,
      };
    },

    zoomBy(factor: number, centerMs: number) {
      const span = this.viewport.to.getTime() - this.viewport.from.getTime();
      const newSpan = span * factor;
      this.viewport = {
        from: new Date(centerMs - newSpan / 2),
        to:   new Date(centerMs + newSpan / 2),
        followLive: false,
      };
    },

    setFollowLive(v: boolean) {
      this.viewport = { ...this.viewport, followLive: v };
    },
  },

  getters: {
    viewportSpanMs: (state) =>
      state.viewport.to.getTime() - state.viewport.from.getTime(),
  },
});
