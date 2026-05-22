<!-- src/views/CausalTreeView.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { useCausalTreeQuery } from '@/composables/useCausalTreeQuery';
import { useCausalTreeUrl } from '@/composables/useCausalTreeUrl';
import CausalTreeCanvas from '@/components/CausalTreeCanvas.vue';
import TraceSummaryPanel from '@/components/TraceSummaryPanel.vue';
import EventInspector from '@/components/EventInspector.vue';
import TraceSearchInput from '@/components/TraceSearchInput.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const store = useCausalTreeStore();
useCausalTreeQuery();
useCausalTreeUrl();

const selectedNode = computed(() => {
  if (!store.selectedEventId || !store.tree) return null;
  return store.tree.nodes.find(n => n.eventId === store.selectedEventId) ?? null;
});
</script>

<template>
  <div class="causal-tree-view">
    <header class="causal-tree-view__header">
      <h1>Causal tree</h1>
      <TraceSearchInput />
    </header>

    <LoadingSpinner v-if="store.loading && !store.tree" />

    <div
      v-else-if="store.error"
      data-testid="error-message"
    >
      <ErrorMessage
        :message="store.error"
        @retry="store.retry"
      />
    </div>

    <div
      v-else-if="store.tree"
      class="causal-tree-view__grid"
      :class="{ 'causal-tree-view__grid--with-inspector': selectedNode !== null }"
    >
      <TraceSummaryPanel
        class="causal-tree-view__summary"
        :summary="store.tree.summary"
      />
      <CausalTreeCanvas
        class="causal-tree-view__canvas"
        :tree="store.tree"
        :selected-event-id="store.selectedEventId"
        @select="store.selectEvent"
      />
      <EventInspector
        v-if="selectedNode"
        class="causal-tree-view__inspector"
        :event="selectedNode"
        :session-id="store.tree?.sessionId ?? null"
        :show-causal-tree-pivot="false"
        :show-timeline-pivot="true"
        :show-entity-history-pivot="true"
      />
    </div>

    <div
      v-else
      class="causal-tree-view__empty"
    >
      Open a causal tree from the timeline, or paste an event ID above.
    </div>
  </div>
</template>

<style scoped>
.causal-tree-view {
  max-width: 1600px;
  margin: 0 auto;
  padding: 1.5rem;
}
.causal-tree-view__header {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  margin-bottom: 1rem;
}
.causal-tree-view__header h1 {
  margin: 0;
}
.causal-tree-view__grid {
  display: grid;
  grid-template-columns: 280px 1fr;
  grid-template-areas: "summary canvas";
  gap: 1.5rem;
}
.causal-tree-view__grid--with-inspector {
  grid-template-columns: 280px 1fr 400px;
  grid-template-areas: "summary canvas inspector";
}
.causal-tree-view__summary  { grid-area: summary; }
.causal-tree-view__canvas   { grid-area: canvas; }
.causal-tree-view__inspector { grid-area: inspector; }
</style>
