<template>
  <div class="replication-latency-view">
    <div class="replication-latency-view__toolbar">
      <h1 class="replication-latency-view__title">Replication latency</h1>
      <ShowSqlButton v-if="currentSql" :sql="currentSql" :session-id="sessionId" />
    </div>

    <BundleModeRequiredBanner v-if="bundleModeRequired" />

    <template v-if="!bundleModeRequired">
      <div v-if="loading" class="replication-latency-view__loading">Loading…</div>
      <div v-else class="replication-latency-view__panels">
        <!-- Left: pair matrix -->
        <aside class="replication-latency-view__aside">
          <PublisherSubscriberMatrix
            :pairs="pairs"
            :budgets="budgets"
            :selected-pair="selectedPair"
            @select="onSelectPair"
          />
          <button
            v-if="selectedPair"
            class="replication-latency-view__clear-btn"
            @click="clearPair"
          >
            × Clear selection
          </button>
        </aside>

        <!-- Centre: distribution + timeseries -->
        <main class="replication-latency-view__centre">
          <LatencyDistributionChart
            :distribution="distribution"
            :budget="selectedBudget"
            :loading="distLoading"
          />
          <LatencyTimeSeriesChart
            :timeseries="timeseries"
            :loading="tsLoading"
          />
        </main>

        <!-- Right: outliers -->
        <section class="replication-latency-view__right">
          <h3>Outliers</h3>
          <LatencyOutliersTable
            :outliers="outlierList?.outliers ?? []"
            :session-id="sessionId"
          />
        </section>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { LatencyPairSummaryDto, LatencyBudgetDto } from '@/api/tracerApiClient';
import BundleModeRequiredBanner from '@/components/BundleModeRequiredBanner.vue';
import PublisherSubscriberMatrix from '@/components/PublisherSubscriberMatrix.vue';
import LatencyDistributionChart from '@/components/LatencyDistributionChart.vue';
import LatencyTimeSeriesChart from '@/components/LatencyTimeSeriesChart.vue';
import LatencyOutliersTable from '@/components/LatencyOutliersTable.vue';
import { useLatencyDistribution } from '@/composables/useLatencyDistribution';
import { useLatencyTimeSeries } from '@/composables/useLatencyTimeSeries';
import { useLatencyOutliers } from '@/composables/useLatencyOutliers';
import type { LatencyFilter } from '@/composables/useLatencyDistribution';
import ShowSqlButton from '@/components/ShowSqlButton.vue';
import { latencyFilterToSql } from '@/utils/showSqlGenerators';

const props = defineProps<{ sessionId: string }>();

const loading = ref(false);
const bundleModeRequired = ref(false);
const pairs = ref<LatencyPairSummaryDto[]>([]);
const budgets = ref<LatencyBudgetDto[]>([]);
const selectedPair = ref<LatencyPairSummaryDto | null>(null);
const sessionFrom = ref<string | null>(null);
const sessionTo = ref<string | null>(null);

const filter = computed<LatencyFilter | null>(() => {
  if (!sessionFrom.value || !sessionTo.value) return null;
  if (!selectedPair.value) return { from: sessionFrom.value, to: sessionTo.value };
  return {
    from: sessionFrom.value,
    to: sessionTo.value,
    topic: selectedPair.value.topic,
    publisherNode: selectedPair.value.publisherNode,
    subscriberNode: selectedPair.value.subscriberNode,
  };
});

const { distribution, loading: distLoading } = useLatencyDistribution(filter);
const { timeseries, loading: tsLoading } = useLatencyTimeSeries(filter);
const { outlierList } = useLatencyOutliers(filter);

const budgetByTopic = computed(() => {
  const map = new Map<string, LatencyBudgetDto>();
  for (const b of budgets.value) map.set(b.topic, b);
  return map;
});

const selectedBudget = computed(() =>
  selectedPair.value ? (budgetByTopic.value.get(selectedPair.value.topic) ?? null) : null,
);

const currentSql = computed(() =>
  sessionFrom.value && sessionTo.value
    ? latencyFilterToSql(sessionFrom.value, sessionTo.value, selectedPair.value?.topic)
    : '',
);

function onSelectPair(pair: LatencyPairSummaryDto) {
  selectedPair.value = pair;
}

function clearPair() {
  selectedPair.value = null;
}

onMounted(async () => {
  loading.value = true;
  try {
    const session = await api.getSession(props.sessionId);
    if (!session) return;
    sessionFrom.value = session.startUtc;
    sessionTo.value = session.endUtc ?? new Date().toISOString();

    const [budgetList, pairList] = await Promise.all([
      api.getLatencyBudgets(props.sessionId),
      api.getLatencyPairs({ from: sessionFrom.value, to: sessionTo.value, minSamples: 50, limit: 200 }),
    ]);
    budgets.value = budgetList.budgets;
    pairs.value = pairList;
  } catch (e: unknown) {
    const status = (e as { status?: number }).status ?? 0;
    if (status === 409) bundleModeRequired.value = true;
  } finally {
    loading.value = false;
  }
});
</script>

<style lang="scss">
.replication-latency-view {
  padding: 1rem;

  &__title {
    margin-bottom: 1rem;
  }

  &__loading {
    color: var(--c-text-muted, #666);
  }

  &__panels {
    display: grid;
    grid-template-columns: 260px 1fr 280px;
    gap: 1rem;
    align-items: start;
  }

  &__aside {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  &__clear-btn {
    align-self: flex-start;
    font-size: 0.8rem;
    cursor: pointer;
    background: none;
    border: 1px solid var(--c-text-muted, #666);
    border-radius: 4px;
    padding: 0.2rem 0.5rem;
    color: var(--c-text-muted, #666);
  }

  &__centre {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }

  &__right {
    h3 {
      font-size: 0.95rem;
      font-weight: 600;
      margin-bottom: 0.5rem;
    }
  }
}
</style>
