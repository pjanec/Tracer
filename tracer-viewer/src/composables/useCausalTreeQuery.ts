// src/composables/useCausalTreeQuery.ts
import { watch } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { api } from '@/api/tracerApiClient';

export function useCausalTreeQuery() {
  const store = useCausalTreeStore();
  let abortCtrl: AbortController | null = null;

  watch(
    () => store.request,
    async (req) => {
      if (!req) return;

      abortCtrl?.abort();
      abortCtrl = new AbortController();
      const signal = abortCtrl.signal;

      store.loading = true;
      store.error = null;

      try {
        let tree;
        switch (req.kind) {
          case 'trace':
            tree = await api.getTraceTree(req.id, req.maxEvents ?? 1000, { signal });
            break;
          case 'event':
            tree = await api.getTraceByEvent(req.id, req.maxEvents ?? 1000, { signal });
            break;
          case 'ancestors':
            tree = await api.getEventAncestors(req.id, req.maxDepth ?? 50, { signal });
            break;
          case 'descendants':
            tree = await api.getEventDescendants(
              req.id,
              req.maxDepth ?? 30,
              req.maxNodes ?? 1000,
              { signal },
            );
            break;
          default:
            return;
        }
        store.setResult(tree);
      } catch (err: unknown) {
        if (err instanceof Error && err.name === 'AbortError') return;
        store.setError(err instanceof Error ? err.message : 'Failed to load causal tree');
      } finally {
        store.loading = false;
      }
    },
    { immediate: true },
  );
}
