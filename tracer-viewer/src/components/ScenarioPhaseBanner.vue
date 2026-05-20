<script setup lang="ts">
import { ref, watch } from 'vue';
import { useApi } from '@/api/useApi';
import type { SessionDto, ScenarioPhaseDto } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

const props = defineProps<{ session: SessionDto }>();

const phases = ref<ScenarioPhaseDto[]>([]);

const loadPhases = async () => {
  const api = useApi();
  phases.value = await api.getScenarioPhases(props.session.sessionId);
};
watch(() => props.session.sessionId, loadPhases, { immediate: true });
</script>

<template>
  <section class="scenario-phase-banner">
    <div
      v-for="phase in phases"
      :key="phase.phaseName"
      class="scenario-phase-banner__row"
      :class="{ 'scenario-phase-banner__row--active': phase.status === 'Active' }"
    >
      <span class="scenario-phase-banner__name">{{ phase.phaseName }}</span>
      <span class="scenario-phase-banner__status">{{ phase.status }}</span>
      <span
        v-if="phase.endedAtUtc"
        class="scenario-phase-banner__end"
      >{{ formatTime(phase.endedAtUtc) }}</span>
    </div>
  </section>
</template>

<style lang="scss">
.scenario-phase-banner {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;

  &__row {
    display: flex;
    align-items: center;
    gap: 1rem;
    padding: 0.5rem 0.75rem;
    border-radius: 6px;
    background: var(--c-bg-subtle);

    &--active {
      border-left: 3px solid var(--c-accent);
    }
  }

  &__name {
    flex: 1;
    font-weight: 500;
  }

  &__status {
    font-size: 0.8125rem;
    color: var(--c-text-muted);
  }

  &__end {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    font-family: var(--font-mono, monospace);
  }
}
</style>
