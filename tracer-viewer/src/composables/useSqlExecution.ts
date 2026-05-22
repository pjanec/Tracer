// tracer-viewer/src/composables/useSqlExecution.ts
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { SqlExecuteResultDto } from '@/types/sql';

export function useSqlExecution() {
  const result = ref<SqlExecuteResultDto | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);
  let abortController: AbortController | null = null;

  async function run(sql: string, opts?: { timeoutSeconds?: number; maxRows?: number; parameters?: Record<string, unknown> }) {
    abortController?.abort();
    abortController = new AbortController();
    loading.value = true;
    error.value = null;
    result.value = null;
    try {
      result.value = await api.executeSql(
        { sql, timeoutSeconds: opts?.timeoutSeconds, maxRows: opts?.maxRows, parameters: opts?.parameters },
        abortController.signal,
      );
    } catch (e: unknown) {
      if (e instanceof Error && e.name === 'AbortError') return;
      error.value = e instanceof Error ? e.message : String(e);
    } finally {
      loading.value = false;
    }
  }

  function cancel() {
    abortController?.abort();
    abortController = null;
    loading.value = false;
  }

  return { result, loading, error, run, cancel };
}
