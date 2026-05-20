<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from 'vue';
import { useSessionStore } from '@/stores/sessionStore';
import { useLiveNotables } from '@/composables/useLiveSse';
import ScenarioStatePanel from '@/components/ScenarioStatePanel.vue';
import ScenarioPhaseBanner from '@/components/ScenarioPhaseBanner.vue';
import NotableEventsFeed from '@/components/NotableEventsFeed.vue';
import LiveIndicator from '@/components/LiveIndicator.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';

const props = defineProps<{ sessionId: string }>();
const sessionStore = useSessionStore();

onMounted(() => sessionStore.load(props.sessionId));

watch(() => props.sessionId, (sid) => sessionStore.load(sid));

let refreshTimer: number | null = null;
onMounted(() => {
  refreshTimer = window.setInterval(() => sessionStore.refreshState(), 5000);
});
onUnmounted(() => {
  if (refreshTimer) window.clearInterval(refreshTimer);
});

const { events: liveEvents } = useLiveNotables(props.sessionId);

const headerTitle = computed(() => {
  const s = sessionStore.current;
  if (!s) return 'Loading session\u2026';
  return `Session ${s.sessionId.slice(0, 8)}`;
});
</script>

<template>
  <div class="scenario-view">
    <header class="scenario-view__header">
      <div>
        <h1>{{ headerTitle }}</h1>
        <p
          v-if="sessionStore.current"
          class="scenario-view__subtitle"
        >
          {{ sessionStore.current.scenarioId }}
        </p>
      </div>
      <LiveIndicator />
    </header>

    <LoadingSpinner v-if="sessionStore.loading && !sessionStore.current" />

    <div
      v-else-if="sessionStore.current"
      class="scenario-view__grid"
    >
      <ScenarioStatePanel
        class="scenario-view__state"
        :session="sessionStore.current"
        :state="sessionStore.state"
      />

      <ScenarioPhaseBanner
        class="scenario-view__phases"
        :session="sessionStore.current"
      />

      <NotableEventsFeed
        class="scenario-view__notables"
        :session-id="sessionStore.current.sessionId"
        :live-events="liveEvents"
      />
    </div>
  </div>
</template>

<style lang="scss">
.scenario-view {
  max-width: 1400px;
  margin: 0 auto;
  padding: 2rem;

  &__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 1.5rem;
  }

  &__subtitle {
    color: var(--c-text-muted);
    margin: 0.25rem 0 0;
  }

  &__grid {
    display: grid;
    grid-template-columns: 1fr 2fr;
    grid-template-rows: auto 1fr;
    gap: 1.5rem;
    grid-template-areas:
      "state  phases"
      "state  notables";
  }

  &__state    { grid-area: state; }
  &__phases   { grid-area: phases; }
  &__notables { grid-area: notables; }
}
</style>
