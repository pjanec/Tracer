// src/stores/entityHistoryStore.ts
import { defineStore } from 'pinia';
import type {
  EntitySummaryDto,
  EntityEventsDto,
  EntitySlowStateDto,
  SlowStateSampleDto,
} from '@/api/tracerApiClient';

export const useEntityHistoryStore = defineStore('entityHistory', {
  state: () => ({
    entityId: null as string | null,
    sessionId: null as string | null,
    timeRange: {
      from: new Date(),
      to: new Date(),
    },
    summary: null as EntitySummaryDto | null,
    events: null as EntityEventsDto | null,
    slowStateByTopic: {} as Record<string, SlowStateSampleDto[]>,
    fastStateTopics: [] as string[],
    selectedEventId: null as string | null,
    loading: false,
    error: null as string | null,
  }),

  actions: {
    setEntity(entityId: string, sessionId: string) {
      this.entityId = entityId;
      this.sessionId = sessionId;
      // Clear prior data
      this.summary = null;
      this.events = null;
      this.slowStateByTopic = {};
      this.fastStateTopics = [];
      this.selectedEventId = null;
      this.error = null;
    },

    setSummary(summary: EntitySummaryDto) {
      this.summary = summary;
      // Default timeRange to entity lifespan only if timeRange is not already user-set
      const isDefault = this.timeRange.from.getTime() === this.timeRange.to.getTime();
      if (isDefault) {
        this.timeRange = {
          from: new Date(summary.firstSeenUtc),
          to: new Date(summary.lastSeenUtc),
        };
      }
    },

    setTimeRange(from: Date, to: Date) {
      this.timeRange = { from, to };
    },

    setResults(
      events: EntityEventsDto,
      slowState: EntitySlowStateDto,
      fastStateTopics: string[],
    ) {
      this.events = events;
      this.slowStateByTopic = slowState.byTopic;
      this.fastStateTopics = fastStateTopics;
    },

    retry() {
      // Signals to useEntityHistoryQuery to re-run by clearing error
      this.error = null;
    },
  },
});
