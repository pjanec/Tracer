<template>
  <div class="network-topology-view">
    <h1 class="network-topology-view__title">Network topology</h1>

    <BundleModeRequiredBanner v-if="bundleModeRequired" />

    <template v-if="!bundleModeRequired">
      <div v-if="loading" class="network-topology-view__loading">Loading…</div>
      <div v-else class="network-topology-view__layout">
        <div class="network-topology-view__graph">
          <NetworkGraphCanvas
            :nodes="topology?.nodes ?? []"
            :edges="canvasEdges"
            :selected-edge="selectedEdge"
            @select-edge="onSelectEdge"
          />
        </div>
        <aside v-if="selectedEdge" class="network-topology-view__panel">
          <h3>
            {{ selectedEdge.from }} → {{ selectedEdge.to }}
          </h3>
          <ul class="network-topology-view__topic-list">
            <li
              v-for="row in selectedEdgeTopics"
              :key="row.topic"
              class="network-topology-view__topic-row"
            >
              <span class="network-topology-view__topic-name">{{ row.topic }}</span>
              <span class="network-topology-view__topic-count">{{ row.messageCount }}</span>
              <button
                class="network-topology-view__latency-btn"
                @click="drillLatency(row.topic)"
              >
                Latency →
              </button>
            </li>
          </ul>
        </aside>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import type { NetworkTopologyDto, NetworkTopologyEdgeDto } from '@/api/tracerApiClient';
import BundleModeRequiredBanner from '@/components/BundleModeRequiredBanner.vue';
import NetworkGraphCanvas from '@/components/NetworkGraphCanvas.vue';

const props = defineProps<{ sessionId: string }>();
const router = useRouter();

const loading = ref(false);
const bundleModeRequired = ref(false);
const topology = ref<NetworkTopologyDto | null>(null);
const selectedEdge = ref<{ from: string; to: string } | null>(null);

/** Edges collapsed to (publisher, subscriber) pairs with summed messageCount. */
const canvasEdges = computed(() => {
  if (!topology.value) return [];
  const map = new Map<string, { from: string; to: string; weight: number }>();
  for (const e of topology.value.edges) {
    const key = `${e.publisherNode}|${e.subscriberNode}`;
    const existing = map.get(key);
    if (existing) {
      existing.weight += e.messageCount;
    } else {
      map.set(key, { from: e.publisherNode, to: e.subscriberNode, weight: e.messageCount });
    }
  }
  return Array.from(map.values());
});

/** Per-topic breakdown for the selected (publisher, subscriber) pair. */
const selectedEdgeTopics = computed<NetworkTopologyEdgeDto[]>(() => {
  if (!selectedEdge.value || !topology.value) return [];
  return topology.value.edges.filter(
    e =>
      e.publisherNode === selectedEdge.value!.from &&
      e.subscriberNode === selectedEdge.value!.to,
  );
});

function onSelectEdge(edge: { from: string; to: string }) {
  selectedEdge.value = edge;
}

function drillLatency(topic: string) {
  if (!selectedEdge.value) return;
  void router.push({
    name: 'replication-latency',
    params: { sessionId: props.sessionId },
    query: {
      publisherNode: selectedEdge.value.from,
      subscriberNode: selectedEdge.value.to,
      topic,
    },
  });
}

onMounted(async () => {
  loading.value = true;
  try {
    const session = await api.getSession(props.sessionId);
    if (!session) return;
    const from = session.startUtc;
    const to = session.endUtc ?? new Date().toISOString();
    topology.value = await api.getNetworkTopology({ from, to });
  } catch (e: unknown) {
    const status = (e as { status?: number }).status ?? 0;
    if (status === 409) bundleModeRequired.value = true;
  } finally {
    loading.value = false;
  }
});
</script>

<style lang="scss">
.network-topology-view {
  padding: 1rem;
  height: 100%;

  &__title {
    margin-bottom: 1rem;
  }

  &__loading {
    color: var(--c-text-muted, #666);
  }

  &__layout {
    display: grid;
    grid-template-columns: 1fr 280px;
    gap: 1rem;
    height: calc(100vh - 120px);
  }

  &__graph {
    border: 1px solid var(--c-bg-subtle, #333);
    border-radius: 6px;
    overflow: hidden;
  }

  &__panel {
    h3 {
      font-size: 0.95rem;
      font-weight: 600;
      margin-bottom: 0.75rem;
    }
  }

  &__topic-list {
    list-style: none;
    padding: 0;
    margin: 0;
  }

  &__topic-row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.35rem 0;
    border-bottom: 1px solid var(--c-bg-subtle, #333);
    font-size: 0.88rem;
  }

  &__topic-name {
    font-family: monospace;
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  &__topic-count {
    color: var(--c-text-muted, #888);
    white-space: nowrap;
  }

  &__latency-btn {
    font-size: 0.78rem;
    padding: 0.1rem 0.4rem;
    cursor: pointer;
    background: var(--c-accent-bg, #2a4a70);
    color: var(--c-accent-text, #9ac5ff);
    border: 1px solid var(--c-accent-border, #4a7fc1);
    border-radius: 4px;
    white-space: nowrap;
  }
}
</style>
