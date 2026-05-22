<template>
  <div
    v-if="visibleToUser"
    class="event-inspector"
  >
    <div
      v-if="loading && !isPropMode"
      class="event-inspector__loading"
    >
      Loading�
    </div>
    <template v-else-if="displayEvent">
      <div class="event-inspector__header">
        <span class="event-inspector__topic">{{ displayEvent.topic }}</span>
        <span class="event-inspector__node">{{ displayEvent.publisherNode }}</span>
      </div>

      <pre class="event-inspector__payload">{{ prettyPayload }}</pre>

      <div class="event-inspector__actions">
        <button
          class="event-inspector__action"
          @click="onFilterToTrace"
        >
          Filter to this trace
        </button>
        <button
          class="event-inspector__action"
          @click="onShowInScenario"
        >
          Show in scenario
        </button>
        <button
          v-if="showCausalButton"
          class="event-inspector__action"
          @click="pivotToCausalTree"
        >
          Show causal tree
        </button>
        <button
          v-if="showTimelineButton"
          class="event-inspector__action"
          @click="pivotToTimeline"
        >
          Show in timeline
        </button>
        <button
          v-if="showEntityHistoryButton"
          class="event-inspector__action"
          @click="pivotToEntityHistory"
        >
          Show entity history
        </button>
        <button
          class="event-inspector__action"
          @click="onCopyEventId"
        >
          Copy event ID
        </button>
      </div>
    </template>
    <div
      v-else
      class="event-inspector__not-found"
    >
      Event not found
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';
import { api } from '@/api/tracerApiClient';
import type { EventDto as ApiEventDto } from '@/api/tracerApiClient';
import type { TraceNodeDto } from '@/types/causalTree';

const props = withDefaults(defineProps<{
  event?: TraceNodeDto | null;
  sessionId?: string | null;
  showCausalTreePivot?: boolean;
  showTimelinePivot?: boolean;
  showEntityHistoryPivot?: boolean;
}>(), {
  event: null,
  sessionId: null,
  showCausalTreePivot: false,
  showTimelinePivot: false,
  showEntityHistoryPivot: false,
});

const store  = useTimelineStore();
const router = useRouter();

// Store mode: fetched event (used when no event prop)
const fetchedEvent = ref<ApiEventDto | null>(null);
const loading = ref(false);

// Detect which mode we're in: prop mode when event is explicitly set (not null)
const isPropMode = computed(() => props.event !== null && props.event !== undefined);

// Resolved values: prefer prop, fall back to store
const resolvedTraceId = computed<string | null>(() => {
  if (isPropMode.value) return props.event!.traceId;
  return fetchedEvent.value?.traceId ?? null;
});

const resolvedSessionId = computed<string | null>(() => {
  if (props.sessionId) return props.sessionId;
  return store.sessionId ?? null;
});

// In store mode: watch selectedEventId and fetch event
watch(
  () => store.selectedEventId,
  async (id) => {
    if (isPropMode.value) return;
    if (!id) { fetchedEvent.value = null; return; }
    loading.value = true;
    try {
      fetchedEvent.value = await api.getEvent(id);
    } finally {
      loading.value = false;
    }
  },
  { immediate: true },
);

const displayEvent = computed(() => {
  if (isPropMode.value) return props.event;
  return fetchedEvent.value;
});

const visibleToUser = computed(() => {
  if (isPropMode.value) return true;
  return !!store.selectedEventId;
});

const prettyPayload = computed(() => {
  const payload = displayEvent.value?.payloadJson;
  if (!payload) return '';
  try { return JSON.stringify(JSON.parse(payload), null, 2); }
  catch { return payload; }
});

// Button visibility
const showCausalButton = computed(() =>
  props.showCausalTreePivot &&
  resolvedTraceId.value !== null &&
  resolvedTraceId.value !== '0000000000000000',
);

const showTimelineButton = computed(() =>
  props.showTimelinePivot && !!resolvedSessionId.value,
);

function getEntityId(event: unknown): string | null {
  if (typeof event === 'object' && event !== null && 'entityId' in event) {
    const v = (event as Record<string, unknown>)['entityId'];
    return typeof v === 'string' && v ? v : null;
  }
  return null;
}

const showEntityHistoryButton = computed(() =>
  props.showEntityHistoryPivot &&
  !!getEntityId(displayEvent.value) &&
  !!resolvedSessionId.value,
);

// Navigation handlers
function pivotToCausalTree() {
  const eventId = isPropMode.value
    ? props.event!.eventId
    : (store.selectedEventId ?? null);
  if (eventId) {
    void router.push({ name: 'causal-by-event', params: { eventId } });
  }
}

function pivotToTimeline() {
  if (!resolvedSessionId.value || !isPropMode.value || !props.event) return;
  const t = new Date(props.event.publishWallclock).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: resolvedSessionId.value },
    query: {
      from: new Date(t - 2000).toISOString(),
      to:   new Date(t + 2000).toISOString(),
      select: props.event.eventId,
    },
  });
}

function pivotToEntityHistory() {
  const entityId = getEntityId(displayEvent.value);
  if (!entityId || !resolvedSessionId.value) return;
  void router.push({
    name: 'entity-history',
    params: { entityId },
    query: { session: resolvedSessionId.value },
  });
}

function onFilterToTrace() {
  if (!resolvedTraceId.value) return;
  store.applyFilter({ traceId: resolvedTraceId.value });
}

function onShowInScenario() {
  const sId = resolvedSessionId.value;
  if (!sId) return;
  void router.push(`/scenario/${sId}`);
}

async function onCopyEventId() {
  const eventId = isPropMode.value
    ? props.event!.eventId
    : (fetchedEvent.value?.eventId ?? null);
  if (!eventId) return;
  await navigator.clipboard.writeText(eventId);
}
</script>
