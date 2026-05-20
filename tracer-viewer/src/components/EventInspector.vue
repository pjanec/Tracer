<template>
  <div v-if="store.selectedEventId" class="event-inspector">
    <div v-if="loading" class="event-inspector__loading">Loading…</div>
    <template v-else-if="event">
      <div class="event-inspector__header">
        <span class="event-inspector__topic">{{ event.topic }}</span>
        <span class="event-inspector__node">{{ event.publisherNode }}</span>
      </div>

      <pre class="event-inspector__payload">{{ prettyPayload }}</pre>

      <div class="event-inspector__actions">
        <button class="event-inspector__action" @click="onFilterToTrace">
          Filter to this trace
        </button>
        <button class="event-inspector__action" @click="onShowInScenario">
          Show in scenario
        </button>
        <button class="event-inspector__action event-inspector__action--disabled" disabled>
          Show causal tree
          <!-- TODO Phase 6: enable causal tree navigation -->
        </button>
        <button class="event-inspector__action event-inspector__action--disabled" disabled>
          Show entity history
          <!-- TODO Phase 7: enable entity history navigation -->
        </button>
        <button class="event-inspector__action" @click="onCopyEventId">
          Copy event ID
        </button>
      </div>
    </template>
    <div v-else class="event-inspector__not-found">Event not found</div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';
import { api } from '@/api/tracerApiClient';
import type { EventDto as ApiEventDto } from '@/api/tracerApiClient';

const store  = useTimelineStore();
const router = useRouter();

const event   = ref<ApiEventDto | null>(null);
const loading = ref(false);

const prettyPayload = computed(() => {
  if (!event.value?.payloadJson) return '';
  try {
    return JSON.stringify(JSON.parse(event.value.payloadJson), null, 2);
  } catch {
    return event.value.payloadJson;
  }
});

watch(
  () => store.selectedEventId,
  async (id) => {
    if (!id) { event.value = null; return; }
    loading.value = true;
    try {
      event.value = await api.getEvent(id);
    } finally {
      loading.value = false;
    }
  },
  { immediate: true },
);

function onFilterToTrace() {
  if (!event.value) return;
  store.applyFilter({ traceId: event.value.traceId });
}

function onShowInScenario() {
  if (!store.sessionId) return;
  void router.push(`/scenario/${store.sessionId}`);
}

async function onCopyEventId() {
  if (!event.value) return;
  await navigator.clipboard.writeText(event.value.eventId);
}
</script>

<style scoped>
.event-inspector { border-left: 2px solid #1976d2; padding: 8px 12px; background: #fafafa; }
.event-inspector__header { display: flex; gap: 8px; margin-bottom: 8px; font-weight: 600; }
.event-inspector__payload { background: #f5f5f5; padding: 8px; border-radius: 4px; font-size: 0.75rem; overflow: auto; max-height: 300px; }
.event-inspector__actions { display: flex; flex-direction: column; gap: 4px; margin-top: 8px; }
.event-inspector__action { text-align: left; background: none; border: 1px solid #ccc; border-radius: 4px; padding: 4px 8px; cursor: pointer; }
.event-inspector__action:hover:not(:disabled) { background: #e3f2fd; }
.event-inspector__action--disabled { opacity: 0.5; cursor: not-allowed; }
</style>
