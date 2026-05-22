// tracer-viewer/src/composables/useBundleLibrary.ts
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { BundleLibraryEntryDto, UpdateBundleMetadataDto } from '@/types/bundle';

export function useBundleLibrary() {
  const bundles = ref<BundleLibraryEntryDto[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  async function load(opts?: { showArchived?: boolean; tag?: string }) {
    loading.value = true;
    error.value = null;
    try {
      const data = await api.listBundleLibrary(opts);
      bundles.value = data.entries;
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : String(e);
    } finally {
      loading.value = false;
    }
  }

  async function updateMetadata(bundleId: string, dto: UpdateBundleMetadataDto) {
    await api.updateBundleMetadata(bundleId, dto);
    await load();
  }

  async function deleteBundle(bundleId: string) {
    await api.deleteBundle(bundleId);
    bundles.value = bundles.value.filter(b => b.bundleId !== bundleId);
  }

  async function recordOpened(bundleId: string) {
    await api.recordBundleOpened(bundleId);
  }

  return { bundles, loading, error, load, updateMetadata, deleteBundle, recordOpened };
}
