<script setup lang="ts">
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { useEntityHistoryQuery } from '@/composables/useEntityHistoryQuery';
import { useEntityHistoryUrl } from '@/composables/useEntityHistoryUrl';
// Stub imports — these components will be implemented in BATCH-38
import EntitySummaryStrip from '@/components/EntitySummaryStrip.vue';
import EntityLifecycleRibbon from '@/components/EntityLifecycleRibbon.vue';
import SlowStateChart from '@/components/SlowStateChart.vue';
import EntityEventStrip from '@/components/EntityEventStrip.vue';
import FastStateDrillDown from '@/components/FastStateDrillDown.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const store = useEntityHistoryStore();
useEntityHistoryUrl(); // URL ↔ store sync
useEntityHistoryQuery(); // drives fetches
</script>

<template>
  <div class="entity-history-view">
    <div v-if="store.loading && !store.summary" class="entity-history-view__loading">
      <LoadingSpinner />
    </div>
    <div v-else-if="store.error && !store.summary" class="entity-history-view__error">
      <ErrorMessage :message="store.error" />
      <button class="entity-history-view__retry" @click="store.retry()">Retry</button>
    </div>
    <template v-else>
      <EntitySummaryStrip v-if="store.summary" :summary="store.summary" />
      <EntityLifecycleRibbon
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
      />
      <SlowStateChart
        v-for="(samples, topic) in store.slowStateByTopic"
        :key="topic"
        :topic="topic"
        :samples="samples"
        :time-range="store.timeRange"
        @select-event="store.selectedEventId = $event"
      />
      <EntityEventStrip
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
        :selected-event-id="store.selectedEventId"
        @select="store.selectedEventId = $event"
      />
      <FastStateDrillDown
        :entity-id="store.entityId ?? ''"
        :session-id="store.sessionId ?? ''"
        :available-topics="store.fastStateTopics"
        :time-range="store.timeRange"
      />
    </template>
  </div>
</template>
