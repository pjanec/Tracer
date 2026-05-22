// tracer-viewer/src/composables/useSavedQueries.ts
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { SavedQueryDto, CreateSavedQueryDto } from '@/types/savedQuery';

export function useSavedQueries() {
  const queries = ref<SavedQueryDto[]>([]);
  const loading = ref(false);

  async function load(opts?: { tag?: string; favorite?: boolean; builtIn?: boolean }) {
    loading.value = true;
    try {
      queries.value = await api.listSavedQueries(opts);
    } finally {
      loading.value = false;
    }
  }

  async function create(dto: CreateSavedQueryDto): Promise<SavedQueryDto> {
    const q = await api.createSavedQuery(dto);
    queries.value = [...queries.value, q];
    return q;
  }

  async function remove(id: string) {
    await api.deleteSavedQuery(id);
    queries.value = queries.value.filter(q => q.savedQueryId !== id);
  }

  async function toggleFavorite(id: string) {
    const updated = await api.toggleSavedQueryFavorite(id);
    if (updated) {
      queries.value = queries.value.map(q => q.savedQueryId === id ? updated : q);
    }
  }

  async function clone(id: string, label: string): Promise<SavedQueryDto> {
    const cloned = await api.cloneSavedQuery(id, label);
    queries.value = [...queries.value, cloned];
    return cloned;
  }

  return { queries, loading, load, create, remove, toggleFavorite, clone };
}
