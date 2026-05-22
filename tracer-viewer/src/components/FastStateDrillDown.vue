<script setup lang="ts">
import { ref, computed } from 'vue';
import { useEntityHistoryUrl } from '@/composables/useEntityHistoryUrl';
import { useFastStateChart } from '@/composables/useFastStateChart';
import FastStateColumnPicker from '@/components/FastStateColumnPicker.vue';
import FastStateChart from '@/components/FastStateChart.vue';

const props = defineProps<{
  entityId: string;
  sessionId: string;
  availableTopics: string[];
  timeRange: { from: Date; to: Date };
}>();

const expanded = ref(false);

// fastStateTopic and fastStateColumns are URL-synced local refs
const { fastStateTopic, fastStateColumns } = useEntityHistoryUrl();

// Use fastStateTopic / fastStateColumns as the selected state directly
const { schema, data, schemaLoading, dataLoading, error } = useFastStateChart(
  computed(() => props.entityId),
  computed(() => props.sessionId),
  fastStateTopic,
  fastStateColumns,
  computed(() => props.timeRange),
);

function onToggle(): void {
  if (props.availableTopics.length > 0) {
    expanded.value = !expanded.value;
  }
}
</script>

<template>
  <div class="fast-state-drill-down">
    <button class="fast-state-drill-down__toggle" @click="onToggle">
      <template v-if="availableTopics.length === 0">
        Fast State (no fast-state data)
      </template>
      <template v-else>
        Fast State {{ expanded ? '▲' : '▼' }}
      </template>
    </button>

    <div v-show="expanded && availableTopics.length > 0" class="fast-state-drill-down__body">
      <select v-model="fastStateTopic" class="fast-state-drill-down__topic-select">
        <option value="">— select topic —</option>
        <option v-for="t in availableTopics" :key="t" :value="t">{{ t }}</option>
      </select>

      <div v-if="schemaLoading || dataLoading" class="fast-state-drill-down__loading">Loading…</div>
      <div v-else-if="error" class="fast-state-drill-down__error">{{ error }}</div>
      <template v-else-if="schema && fastStateTopic">
        <FastStateColumnPicker
          :columns="schema.columns"
          :selected="fastStateColumns"
          @update:selected="fastStateColumns = $event"
        />
        <div v-if="data?.downsampled" class="fast-state-drill-down__downsampled-notice">
          Showing {{ data.samples.length.toLocaleString() }} of
          {{ data.totalSamples.toLocaleString() }} samples (downsampled)
        </div>
        <FastStateChart
          v-if="data && fastStateColumns.length > 0"
          :data="data"
          :selected-columns="fastStateColumns"
          :time-range="timeRange"
        />
      </template>
    </div>
  </div>
</template>

