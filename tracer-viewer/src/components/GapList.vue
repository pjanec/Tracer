<template>
  <div class="gap-list">
    <p v-if="gaps.length === 0" class="gap-list__empty">No gaps detected</p>
    <table v-else class="gap-list__table">
      <thead>
        <tr>
          <th>Resumed At</th>
          <th>Topic</th>
          <th>Path</th>
          <th>Prev Seq</th>
          <th>Last Missing</th>
          <th>Missing</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(gap, i) in gaps" :key="i">
          <td>{{ formatTime(gap.resumedAtWallclockUtc) }}</td>
          <td class="gap-list__topic">{{ gap.topic }}</td>
          <td>{{ gap.publisherNode }} → {{ gap.subscriberNode }}</td>
          <td>{{ gap.previousSequence }}</td>
          <td>{{ gap.resumedAtSequence - 1 }}</td>
          <td class="gap-list__missing">{{ gap.missingCount }}</td>
          <td>
            <button
              class="gap-list__pivot"
              @click="showInTimeline(gap)"
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
import type { GapDto } from '@/api/tracerApiClient';

const props = defineProps<{
  gaps: GapDto[];
  sessionId: string;
}>();

const router = useRouter();

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour12: false });
}

function showInTimeline(gap: GapDto) {
  const T = new Date(gap.resumedAtWallclockUtc).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: props.sessionId },
    query: {
      from: new Date(T - 5000).toISOString(),
      to: new Date(T + 1000).toISOString(),
      topic: gap.topic,
      node: gap.subscriberNode,
    },
  });
}
</script>

<style lang="scss">
.gap-list {
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

  &__missing {
    font-weight: 600;
    color: #e85c5c;
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
