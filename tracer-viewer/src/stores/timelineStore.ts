// src/stores/timelineStore.ts
import { defineStore } from 'pinia';
import type { TimelineFilter, EventListDto, EventAggregateDto, EventDto } from '@/types/timeline';

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
    queryResult: null as EventListDto | null,
    aggregateResult: null as EventAggregateDto | null,
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
    isLiveSession: false,
    // Derived from queryResult (mirrored for template convenience)
    totalMatching: 0,
    returned: 0,
    truncated: false,
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
      if (v) {
        // Snap viewport to live edge: preserve span, move to = now, from = now - span
        const spanMs = this.viewportSpanMs;
        const nowMs = Date.now();
        this.viewport = {
          from: new Date(nowMs - spanMs),
          to:   new Date(nowMs),
          followLive: true,
        };
      } else {
        this.viewport = { ...this.viewport, followLive: false };
      }
    },

    applyFilter(patch: Partial<TimelineFilter>) {
      this.filter = { ...this.filter, ...patch };
    },

    setQueryResult(result: EventListDto) {
      this.queryResult = result;
      this.queryMode = 'list';
      this.totalMatching = result.totalMatching;
      this.returned = result.returned;
      this.truncated = result.truncated;
    },

    setAggregateResult(result: EventAggregateDto) {
      this.aggregateResult = result;
      this.queryMode = 'aggregate';
      this.bucketDuration = result.bucketDuration;
    },

    /**
     * Append a live event from SSE.
     * - In aggregate mode: does NOT mutate queryResult.
     * - In list mode: appends the event and increments counters.
     * - If followLive + event is beyond viewport.to: slides the viewport forward.
     */
    appendLiveEvent(event: EventDto) {
      if (this.queryMode === 'aggregate') {
        // Aggregate mode: live events trigger periodic refetch, not append
        return;
      }

      // List mode: append
      if (this.queryResult) {
        this.queryResult = {
          ...this.queryResult,
          events: [...this.queryResult.events, event],
          totalMatching: this.queryResult.totalMatching + 1,
          returned: this.queryResult.returned + 1,
        };
        this.totalMatching = this.queryResult.totalMatching;
        this.returned = this.queryResult.returned;
      }

      // Follow-live: slide viewport if event is beyond current end
      if (this.viewport.followLive) {
        const evMs = new Date(event.publishWallclock).getTime();
        const toMs = this.viewport.to.getTime();
        if (evMs > toMs) {
          const span = this.viewport.to.getTime() - this.viewport.from.getTime();
          // Slide forward: event + 5s headroom becomes the new to
          const newTo = new Date(evMs + 5000);
          const newFrom = new Date(newTo.getTime() - span);
          this.viewport = { from: newFrom, to: newTo, followLive: true };
        }
      }
    },
  },

  getters: {
    viewportSpanMs: (state) =>
      state.viewport.to.getTime() - state.viewport.from.getTime(),
  },
});
