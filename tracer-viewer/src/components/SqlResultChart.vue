<script setup lang="ts">
import { computed } from 'vue';
import type { SqlExecuteResultDto } from '@/types/sql';

const props = defineProps<{ result: SqlExecuteResultDto }>();

function isNumeric(t: string): boolean {
  return /double|float|int|decimal|bigint|hugeint/i.test(t);
}

const chartData = computed(() => {
  if (!props.result.columns || !props.result.rows || props.result.columns.length < 2) return null;
  const labelCol = 0;
  const valueCol = props.result.columns.findIndex((c, i) => i > 0 && isNumeric(c.duckType));
  if (valueCol < 0) return null;

  const items = (props.result.rows ?? []).slice(0, 30).map(r => ({
    label: String(r[labelCol] ?? ''),
    value: Number(r[valueCol] ?? 0),
  }));
  const maxVal = Math.max(...items.map(i => i.value), 1);
  return { items, maxVal, valueLabel: props.result.columns[valueCol].name };
});
</script>

<template>
  <div class="sql-chart">
    <div v-if="!chartData" class="sql-chart__empty">
      Cannot chart this result shape. Try a query with a label column and a numeric column.
    </div>
    <div v-else class="sql-chart__bars">
      <div v-for="item in chartData.items" :key="item.label" class="sql-chart__row">
        <span class="sql-chart__label" :title="item.label">{{ item.label }}</span>
        <div class="sql-chart__bar-wrap">
          <div
            class="sql-chart__bar"
            :style="{ width: `${(item.value / chartData.maxVal) * 100}%` }"
          />
        </div>
        <span class="sql-chart__value">{{ item.value.toLocaleString() }}</span>
      </div>
    </div>
  </div>
</template>

<style lang="scss">
.sql-chart {
  padding: 1rem;

  &__empty {
    color: var(--c-text-muted);
    padding: 2rem;
    text-align: center;
  }

  &__bars {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }

  &__row {
    display: grid;
    grid-template-columns: 180px 1fr 80px;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.8rem;
  }

  &__label {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: var(--c-text-muted);
  }

  &__bar-wrap {
    background: var(--c-bg-subtle);
    border-radius: 2px;
    height: 16px;
    overflow: hidden;
  }

  &__bar {
    height: 100%;
    background: var(--c-accent);
    border-radius: 2px;
    transition: width 0.2s;
  }

  &__value {
    font-family: var(--font-mono);
    text-align: right;
  }
}
</style>
