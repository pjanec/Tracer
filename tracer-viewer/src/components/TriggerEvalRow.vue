<template>
  <tr class="trigger-eval-row" @click="expanded = !expanded">
    <td>{{ formatTime(evaluation.evaluatedAtUtc) }}</td>
    <td>
      <span class="trigger-eval-row__trigger-id">{{ evaluation.triggerId }}</span>
      <span v-if="evaluation.triggerLabel" class="trigger-eval-row__trigger-label"> — {{ evaluation.triggerLabel }}</span>
    </td>
    <td>{{ evaluation.publisherNode }}</td>
    <td>
      <span
        class="trigger-eval-view__pill"
        :class="`trigger-eval-view__pill--${evaluation.result}`"
      >{{ evaluation.result }}</span>
    </td>
    <td class="trigger-eval-row__actions" @click.stop>
      <button @click="goToTimeline">Timeline</button>
      <button @click="goToTree">Tree</button>
    </td>
  </tr>
  <tr v-if="expanded" class="trigger-eval-row__expansion">
    <td colspan="5">
      <pre class="trigger-eval-row__inputs">{{ evaluation.inputs }}</pre>
    </td>
  </tr>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import type { TriggerEvaluationDto } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

const props = defineProps<{
  evaluation: TriggerEvaluationDto;
  sessionId: string;
}>();

const router = useRouter();
const expanded = ref(false);

function goToTimeline() {
  const evalTime = new Date(props.evaluation.evaluatedAtUtc);
  const from = new Date(evalTime.getTime() - 5_000).toISOString();
  const to = new Date(evalTime.getTime() + 5_000).toISOString();
  void router.push({
    name: 'timeline',
    params: { sessionId: props.sessionId },
    query: { from, to, select: props.evaluation.eventId },
  });
}

function goToTree() {
  void router.push({ name: 'causal-by-event', params: { eventId: props.evaluation.eventId } });
}
</script>
