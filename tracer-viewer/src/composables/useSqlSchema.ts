// tracer-viewer/src/composables/useSqlSchema.ts
import { ref, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { SqlSchemaDto } from '@/types/sql';

export function useSqlSchema() {
  const schema = ref<SqlSchemaDto | null>(null);
  const loading = ref(false);

  async function refresh() {
    loading.value = true;
    try {
      schema.value = await api.getSqlSchema();
    } catch {
      // schema stays null
    } finally {
      loading.value = false;
    }
  }

  onMounted(refresh);

  return { schema, loading, refresh };
}
