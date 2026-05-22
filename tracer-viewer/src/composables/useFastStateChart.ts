// src/composables/useFastStateChart.ts
// Manages fast-state schema + data fetching for a single entity's fast-state panel.
import { ref, watch, onUnmounted } from 'vue';
import type { Ref } from 'vue';
import { useApi } from '@/api/useApi';
import type { FastStateTopicSchemaDto, EntityFastStateDto } from '@/api/tracerApiClient';

const MAX_SAMPLES = 5000;

export function useFastStateChart(
  entityId: Ref<string | null>,
  sessionId: Ref<string | null>,
  selectedTopic: Ref<string | null>,
  selectedColumns: Ref<string[]>,
  timeRange: Ref<{ from: Date; to: Date }>,
): {
  schema: Ref<FastStateTopicSchemaDto | null>;
  data: Ref<EntityFastStateDto | null>;
  schemaLoading: Ref<boolean>;
  dataLoading: Ref<boolean>;
  error: Ref<string | null>;
} {
  const api = useApi();

  const schema = ref<FastStateTopicSchemaDto | null>(null);
  const data = ref<EntityFastStateDto | null>(null);
  const schemaLoading = ref(false);
  const dataLoading = ref(false);
  const error = ref<string | null>(null);

  let schemaAbortController: AbortController | null = null;
  let dataAbortController: AbortController | null = null;

  function cancelSchema(): void {
    if (schemaAbortController) {
      schemaAbortController.abort();
      schemaAbortController = null;
    }
  }

  function cancelData(): void {
    if (dataAbortController) {
      dataAbortController.abort();
      dataAbortController = null;
    }
  }

  async function fetchSchema(eId: string, sId: string, topic: string): Promise<void> {
    cancelSchema();
    cancelData();
    schema.value = null;
    data.value = null;
    selectedColumns.value = [];
    error.value = null;
    schemaLoading.value = true;

    schemaAbortController = new AbortController();
    const signal = schemaAbortController.signal;

    try {
      const result = await api.getEntityFastStateSchema(eId, topic, sId, { signal });
      schema.value = result;

      // Auto-select first numeric column if none are currently selected
      if (selectedColumns.value.length === 0 && schema.value) {
        const firstNumeric = schema.value.columns.find(c => c.isNumeric);
        if (firstNumeric) selectedColumns.value = [firstNumeric.name];
      }
    } catch (err: unknown) {
      if (
        typeof err === 'object' &&
        err !== null &&
        (err as { name?: unknown }).name === 'AbortError'
      ) return;
      error.value = err instanceof Error ? err.message : String(err);
    } finally {
      schemaLoading.value = false;
    }
  }

  async function fetchData(
    eId: string,
    sId: string,
    topic: string,
    columns: string[],
    range: { from: Date; to: Date },
  ): Promise<void> {
    if (columns.length === 0) return;
    cancelData();
    dataLoading.value = true;
    error.value = null;

    dataAbortController = new AbortController();
    const signal = dataAbortController.signal;

    try {
      const result = await api.getEntityFastState(
        eId,
        topic,
        sId,
        range.from,
        range.to,
        columns,
        { maxSamples: MAX_SAMPLES, signal },
      );
      data.value = result;
    } catch (err: unknown) {
      if (
        typeof err === 'object' &&
        err !== null &&
        (err as { name?: unknown }).name === 'AbortError'
      ) return;
      error.value = err instanceof Error ? err.message : String(err);
    } finally {
      dataLoading.value = false;
    }
  }

  // Topic change → fetch schema + clear data/columns
  const stopTopicWatch = watch(selectedTopic, (topic) => {
    if (!topic || !entityId.value || !sessionId.value) {
      cancelSchema();
      cancelData();
      return;
    }
    void fetchSchema(entityId.value, sessionId.value, topic);
  }, { immediate: true });

  // Columns or timeRange change → fetch data (schema watch already handled above)
  const stopDataWatch = watch(
    [selectedColumns, timeRange] as const,
    ([columns, range]) => {
      const topic = selectedTopic.value;
      const eId = entityId.value;
      const sId = sessionId.value;
      if (!topic || !eId || !sId || columns.length === 0) return;
      void fetchData(eId, sId, topic, [...columns], range);
    },
    { deep: true },
  );

  onUnmounted(() => {
    cancelSchema();
    cancelData();
    stopTopicWatch();
    stopDataWatch();
  });

  return { schema, data, schemaLoading, dataLoading, error };
}
