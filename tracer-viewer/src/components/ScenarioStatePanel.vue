<script setup lang="ts">
import { computed } from 'vue';
import type { SessionDto, ScenarioStateDto } from '@/api/tracerApiClient';
import { formatDuration } from '@/utils/time';

const props = defineProps<{
  session: SessionDto;
  state: ScenarioStateDto | null;
}>();

const elapsedDisplay = computed(() => {
  if (!props.state) return '—';
  return formatDuration(props.state.sessionElapsed);
});

const phaseDisplay = computed(() => props.state?.currentPhase ?? '—');
const statusLabel = computed(() => props.session.status);
</script>

<template>
  <section class="scenario-state-panel">
    <div class="scenario-state-panel__row">
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">
          Status
        </div>
        <div
          class="scenario-state-panel__value scenario-state-panel__value--status"
          :class="`scenario-state-panel__value--${statusLabel.toLowerCase()}`"
        >
          {{ statusLabel }}
        </div>
      </div>
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">
          Elapsed
        </div>
        <div class="scenario-state-panel__value">
          {{ elapsedDisplay }}
        </div>
      </div>
    </div>

    <div class="scenario-state-panel__field">
      <div class="scenario-state-panel__label">
        Current phase
      </div>
      <div class="scenario-state-panel__value scenario-state-panel__value--phase">
        {{ phaseDisplay }}
      </div>
    </div>

    <div class="scenario-state-panel__row">
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">
          Events
        </div>
        <div class="scenario-state-panel__value">
          {{ state?.totalEvents?.toLocaleString() ?? '—' }}
        </div>
      </div>
      <div class="scenario-state-panel__field">
        <div class="scenario-state-panel__label">
          Notables
        </div>
        <div class="scenario-state-panel__value">
          {{ state?.totalNotables?.toLocaleString() ?? '—' }}
        </div>
      </div>
    </div>

    <div class="scenario-state-panel__field">
      <div class="scenario-state-panel__label">
        Nodes ({{ session.participatingNodes.length }})
      </div>
      <div class="scenario-state-panel__nodes">
        <span
          v-for="node in session.participatingNodes"
          :key="node"
          class="scenario-state-panel__node"
        >
          {{ node }}
        </span>
      </div>
    </div>
  </section>
</template>

<style lang="scss">
.scenario-state-panel {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;

  &__row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 1rem;
  }

  &__label {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    margin-bottom: 0.25rem;
  }

  &__value {
    font-size: 1.5rem;
    font-weight: 500;

    &--status {
      &.scenario-state-panel__value--active { color: var(--c-success); }
      &.scenario-state-panel__value--completed { color: var(--c-text); }
      &.scenario-state-panel__value--inferred { color: var(--c-text-muted); }
    }

    &--phase {
      color: var(--c-accent);
    }
  }

  &__nodes {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }

  &__node {
    padding: 0.25rem 0.5rem;
    background: var(--c-bg-subtle);
    border-radius: 4px;
    font-size: 0.875rem;
    font-family: var(--font-mono);
  }
}
</style>
