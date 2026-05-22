<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
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
import ShowSqlButton from '@/components/ShowSqlButton.vue';
import { entityHistoryFilterToSql } from '@/utils/showSqlGenerators';

const store = useEntityHistoryStore();
const router = useRouter();
useEntityHistoryUrl(); // URL ↔ store sync
useEntityHistoryQuery(); // drives fetches

const selectedEvent = computed(() => {
  if (!store.selectedEventId || !store.events) return null;
  return store.events.events.find(e => e.eventId === store.selectedEventId) ?? null;
});

function pivotToTimeline() {
  const ev = selectedEvent.value;
  if (!ev || !store.sessionId) return;
  const t = new Date(ev.occurredAtUtc).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: store.sessionId },
    query: {
      from: new Date(t - 2000).toISOString(),
      to: new Date(t + 2000).toISOString(),
      select: ev.eventId,
    },
  });
}

function pivotToCausalTree() {
  const ev = selectedEvent.value;
  if (!ev || !ev.traceId || ev.traceId === '0') return;
  void router.push({ name: 'causal-by-event', params: { eventId: ev.eventId } });
}

const currentSql = computed(() => {
  if (!store.entityId || !store.sessionId) return '';
  return entityHistoryFilterToSql(
    store.entityId,
    store.timeRange.from.toISOString(),
    store.timeRange.to.toISOString(),
  );
});

const canPivotToCausal = computed(() =>
  !!selectedEvent.value?.traceId && selectedEvent.value.traceId !== '0',
);
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
        @select-event="store.selectedEventId = $event.traceId ?? null"
      />
      <EntityEventStrip
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
        :selected-event-id="store.selectedEventId"
        @select="store.selectedEventId = $event"
      />
      <div v-if="selectedEvent" class="entity-history-view__pivot-actions">
        <button class="entity-history-view__pivot-btn" @click="pivotToTimeline">
          Show in timeline
        </button>
        <button
          class="entity-history-view__pivot-btn"
          :disabled="!canPivotToCausal"
          :class="{ 'entity-history-view__pivot-btn--disabled': !canPivotToCausal }"
          @click="pivotToCausalTree"
        >
          Show causal tree
        </button>
        <ShowSqlButton v-if="currentSql && store.sessionId" :sql="currentSql" :session-id="store.sessionId" />
      </div>
      <FastStateDrillDown
        :entity-id="store.entityId ?? ''"
        :session-id="store.sessionId ?? ''"
        :available-topics="store.fastStateTopics"
        :time-range="store.timeRange"
      />
    </template>
  </div>
</template>
