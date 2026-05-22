<template>
  <div class="latency-outliers-table">
    <p v-if="outliers.length === 0" class="latency-outliers-table__empty">No outliers detected</p>
    <table v-else class="latency-outliers-table__table">
      <thead>
        <tr>
          <th>Timestamp</th>
          <th>Topic</th>
          <th>Path</th>
          <th>Latency</th>
          <th>Threshold</th>
          <th>Source</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(o, i) in outliers" :key="i">
          <td>{{ formatTime(o.publishWallclockUtc) }}</td>
          <td class="latency-outliers-table__topic">{{ o.topic }}</td>
          <td>{{ o.publisherNode }} → {{ o.subscriberNode }}</td>
          <td>{{ o.latencyMs.toFixed(2) }} ms</td>
          <td>{{ o.thresholdMs.toFixed(2) }} ms</td>
          <td>{{ o.budgetSource }}</td>
          <td>
            <button
              class="latency-outliers-table__pivot"
              @click="showInTimeline(o)"
            >
              Timeline →
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router';
import type { LatencyOutlierDto } from '@/api/tracerApiClient';

const props = defineProps<{
  outliers: LatencyOutlierDto[];
  sessionId: string;
}>();

const router = useRouter();

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour12: false });
}

function showInTimeline(o: LatencyOutlierDto) {
  const T = new Date(o.publishWallclockUtc).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: props.sessionId },
    query: {
      from: new Date(T - 1000).toISOString(),
      to: new Date(T + 1000).toISOString(),
      topic: o.topic,
      node: o.subscriberNode,
    },
  });
}
</script>

<style lang="scss">
.latency-outliers-table {
  &__empty {
    color: var(--c-text-muted, #666);
  }

  &__table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.88rem;

    th, td {
      padding: 0.35rem 0.5rem;
      text-align: left;
      border-bottom: 1px solid var(--c-bg-subtle, #333);
    }

    th {
      font-weight: 600;
    }
  }

  &__topic {
    font-family: monospace;
  }

  &__pivot {
    font-size: 0.8rem;
    padding: 0.15rem 0.5rem;
    cursor: pointer;
    background: var(--c-accent-bg, #2a4a70);
    color: var(--c-accent-text, #9ac5ff);
    border: 1px solid var(--c-accent-border, #4a7fc1);
    border-radius: 4px;
  }
}
</style>
