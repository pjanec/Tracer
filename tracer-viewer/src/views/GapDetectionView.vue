<template>
  <div class="gap-detection-view">
    <h1 class="gap-detection-view__title">Gap detection</h1>

    <BundleModeRequiredBanner v-if="bundleModeRequired" />

    <template v-if="!bundleModeRequired">
      <div v-if="loading" class="gap-detection-view__loading">Loading…</div>
      <template v-else>
        <!-- Tuple summary -->
        <section v-if="tupleSummary.length > 0" class="gap-detection-view__summary">
          <h3>By (topic, publisher, subscriber)</h3>
          <ul class="gap-detection-view__tuple-list">
            <li
              v-for="(t, i) in tupleSummary"
              :key="i"
              class="gap-detection-view__tuple-row"
            >
              <span class="gap-detection-view__tuple-path">
                {{ t.topic }} · {{ t.publisherNode }} → {{ t.subscriberNode }}
              </span>
              <span class="gap-detection-view__tuple-count">{{ t.missingTotal }} missing</span>
            </li>
          </ul>
        </section>

        <!-- Gap list -->
        <GapList
          :gaps="gapResult?.gaps ?? []"
          :session-id="sessionId"
        />
      </template>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { GapDto } from '@/api/tracerApiClient';
import BundleModeRequiredBanner from '@/components/BundleModeRequiredBanner.vue';
import GapList from '@/components/GapList.vue';

const props = defineProps<{ sessionId: string }>();

const loading = ref(false);
const bundleModeRequired = ref(false);
const gapResult = ref<{ gaps: GapDto[]; totalGaps: number } | null>(null);

interface TupleSummary {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  missingTotal: number;
}

const tupleSummary = computed<TupleSummary[]>(() => {
  if (!gapResult.value) return [];
  const map = new Map<string, TupleSummary>();
  for (const gap of gapResult.value.gaps) {
    const key = `${gap.topic}|${gap.publisherNode}|${gap.subscriberNode}`;
    const existing = map.get(key);
    if (existing) {
      existing.missingTotal += gap.missingCount;
    } else {
      map.set(key, {
        topic: gap.topic,
        publisherNode: gap.publisherNode,
        subscriberNode: gap.subscriberNode,
        missingTotal: gap.missingCount,
      });
    }
  }
  return Array.from(map.values()).sort((a, b) => b.missingTotal - a.missingTotal);
});

onMounted(async () => {
  loading.value = true;
  try {
    const session = await api.getSession(props.sessionId);
    if (!session) return;
    const from = session.startUtc;
    const to = session.endUtc ?? new Date().toISOString();
    gapResult.value = await api.getGaps({ from, to });
  } catch (e: unknown) {
    const status = (e as { status?: number }).status ?? 0;
    if (status === 409) bundleModeRequired.value = true;
  } finally {
    loading.value = false;
  }
});
</script>

<style lang="scss">
.gap-detection-view {
  padding: 1rem;

  &__title {
    margin-bottom: 1rem;
  }

  &__loading {
    color: var(--c-text-muted, #666);
  }

  &__summary {
    margin-bottom: 1rem;

    h3 {
      font-size: 0.95rem;
      font-weight: 600;
      margin-bottom: 0.5rem;
    }
  }

  &__tuple-list {
    list-style: none;
    padding: 0;
    margin: 0;
  }

  &__tuple-row {
    display: flex;
    justify-content: space-between;
    padding: 0.3rem 0;
    border-bottom: 1px solid var(--c-bg-subtle, #333);
    font-size: 0.88rem;
  }

  &__tuple-path {
    font-family: monospace;
  }

  &__tuple-count {
    font-weight: 600;
    color: #e85c5c;
  }
}
</style>
