<template>
  <div class="trigger-eval-view">
    <div class="trigger-eval-view__filters">
      <select v-model="triggerFilter" @change="reload">
        <option value="">All triggers</option>
        <option v-for="id in distinctTriggerIds" :key="id" :value="id">{{ id }}</option>
      </select>
      <select v-model="resultFilter" @change="reload">
        <option value="">All results</option>
        <option value="fired">Fired</option>
        <option value="not-fired">Not fired</option>
      </select>
    </div>

    <div v-if="loading" class="trigger-eval-view__loading">Loading…</div>
    <p v-else-if="evaluations.length === 0" class="trigger-eval-view__empty">
      No trigger evaluations found.
    </p>
    <table v-else class="trigger-eval-view__table">
      <thead>
        <tr>
          <th>Time</th><th>Trigger</th><th>Publisher</th><th>Result</th><th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <TriggerEvalRow
          v-for="ev in evaluations"
          :key="ev.eventId"
          :evaluation="ev"
          :sessionId="sessionId"
        />
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { TriggerEvaluationDto } from '@/api/tracerApiClient';
import TriggerEvalRow from '@/components/TriggerEvalRow.vue';

const props = defineProps<{ sessionId: string }>();

const evaluations = ref<TriggerEvaluationDto[]>([]);
const loading = ref(false);
const triggerFilter = ref('');
const resultFilter = ref('');

const distinctTriggerIds = computed(() => {
  const ids = new Set(evaluations.value.map(e => e.triggerId));
  return Array.from(ids).sort();
});

async function reload() {
  loading.value = true;
  try {
    evaluations.value = await api.listTriggerEvaluations({
      sessionId: props.sessionId,
      triggerId: triggerFilter.value || undefined,
      result: resultFilter.value || undefined,
    });
  } finally {
    loading.value = false;
  }
}

onMounted(reload);
</script>

<style lang="scss">
.trigger-eval-view {
  padding: 1rem;

  &__filters {
    display: flex;
    gap: 0.75rem;
    margin-bottom: 1rem;
  }

  &__loading,
  &__empty {
    color: var(--c-text-muted, #666);
  }

  &__table {
    width: 100%;
    border-collapse: collapse;

    th, td {
      padding: 0.5rem;
      text-align: left;
      border-bottom: 1px solid var(--c-bg-subtle, #eee);
    }

    th {
      font-weight: 600;
    }
  }

  &__pill {
    display: inline-block;
    padding: 0.15rem 0.5rem;
    border-radius: 999px;
    font-size: 0.8rem;
    font-weight: 600;

    &--Fired {
      background: var(--c-success-bg, #d4edda);
      color: var(--c-success-text, #155724);
    }

    &--NotFired {
      background: var(--c-warning-bg, #fff3cd);
      color: var(--c-warning-text, #856404);
    }
  }
}
</style>
