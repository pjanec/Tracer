<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { useBundleLibrary } from '@/composables/useBundleLibrary';
import BundleCard from '@/components/BundleCard.vue';
import BundleFilterPanel from '@/components/BundleFilterPanel.vue';
import BundleMetadataEditor from '@/components/BundleMetadataEditor.vue';
import type { BundleLibraryEntryDto, UpdateBundleMetadataDto } from '@/types/bundle';
import { api } from '@/api/tracerApiClient';

const router = useRouter();
const { bundles, loading, error, load, updateMetadata, deleteBundle: deleteBundleApi, recordOpened } = useBundleLibrary();

interface BundleFilter {
  tags: string[];
  showArchived: boolean;
  query: string;
  fromDate: Date | null;
  toDate: Date | null;
}

const filter = ref<BundleFilter>({
  tags: [],
  showArchived: false,
  query: '',
  fromDate: null,
  toDate: null,
});

const sortBy = ref('builtAtUtc');
const sortDesc = ref(true);
const editingBundle = ref<BundleLibraryEntryDto | null>(null);

onMounted(() => load({ showArchived: false }));

const allTags = computed(() => {
  const set = new Set<string>();
  for (const b of bundles.value) b.tags.forEach(t => set.add(t));
  return Array.from(set).sort();
});

const filteredBundles = computed(() => {
  let list = bundles.value;
  if (!filter.value.showArchived) list = list.filter(b => !b.isArchived);
  if (filter.value.tags.length) list = list.filter(b => filter.value.tags.every(t => b.tags.includes(t)));
  if (filter.value.query.trim()) {
    const q = filter.value.query.toLowerCase();
    list = list.filter(b => (b.label ?? '').toLowerCase().includes(q) || (b.description ?? '').toLowerCase().includes(q));
  }
  if (filter.value.fromDate) list = list.filter(b => new Date(b.sessionStartUtc) >= filter.value.fromDate!);
  if (filter.value.toDate) list = list.filter(b => new Date(b.sessionEndUtc) <= filter.value.toDate!);
  return list;
});

async function openBundle(bundle: BundleLibraryEntryDto) {
  await recordOpened(bundle.bundleId);
  void router.push({ name: 'scenario', params: { sessionId: bundle.sessionId } });
}

function exportBundle(bundle: BundleLibraryEntryDto) {
  const url = api.getBundleDownloadUrl(bundle.bundleId);
  window.location.href = url;
}

async function archiveBundle(bundle: BundleLibraryEntryDto) {
  await updateMetadata(bundle.bundleId, { isArchived: !bundle.isArchived });
}

async function handleDeleteBundle(bundle: BundleLibraryEntryDto) {
  if (!confirm(`Delete bundle "${bundle.label ?? bundle.bundleId}"? This cannot be undone.`)) return;
  await deleteBundleApi(bundle.bundleId);
}

async function handleSaveMetadata(dto: UpdateBundleMetadataDto) {
  if (!editingBundle.value) return;
  await updateMetadata(editingBundle.value.bundleId, dto);
  editingBundle.value = null;
}
</script>

<template>
  <div class="bundle-library-view">
    <div class="bundle-library-view__header">
      <h1 class="bundle-library-view__title">Bundle library</h1>
      <div class="bundle-library-view__sort">
        <select v-model="sortBy" class="bundle-library-view__select">
          <option value="builtAtUtc">Built at</option>
          <option value="lastOpenedAtUtc">Last opened</option>
          <option value="sizeBytes">Size</option>
          <option value="label">Label</option>
        </select>
        <button class="bundle-library-view__sort-dir" @click="sortDesc = !sortDesc">
          {{ sortDesc ? '↓' : '↑' }}
        </button>
      </div>
    </div>

    <div v-if="error" class="bundle-library-view__error">{{ error }}</div>

    <div class="bundle-library-view__layout">
      <BundleFilterPanel
        v-model:filter="filter"
        :tags="allTags"
      />

      <main class="bundle-library-view__main">
        <div v-if="loading" class="bundle-library-view__loading">Loading…</div>
        <div v-else-if="bundles.length === 0" class="bundle-library-view__empty">No bundles yet.</div>
        <div v-else-if="filteredBundles.length === 0" class="bundle-library-view__empty">No bundles match the current filter.</div>
        <div v-else class="bundle-library-view__grid">
          <BundleCard
            v-for="bundle in filteredBundles"
            :key="bundle.bundleId"
            :bundle="bundle"
            @open="openBundle(bundle)"
            @edit="editingBundle = bundle"
            @export="exportBundle(bundle)"
            @archive="archiveBundle(bundle)"
            @delete="handleDeleteBundle(bundle)"
          />
        </div>
      </main>
    </div>

    <BundleMetadataEditor
      v-if="editingBundle"
      :bundle="editingBundle"
      @save="handleSaveMetadata"
      @cancel="editingBundle = null"
    />
  </div>
</template>

<style lang="scss">
.bundle-library-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;

  &__header {
    display: flex;
    align-items: center;
    gap: 1rem;
    padding: 0.75rem 1rem;
    border-bottom: 1px solid var(--c-border);
    flex-shrink: 0;
  }

  &__title { margin: 0; }
  &__sort { display: flex; align-items: center; gap: 0.35rem; margin-left: auto; }

  &__select {
    padding: 0.3rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
  }

  &__sort-dir {
    padding: 0.3rem 0.5rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
  }

  &__error { color: var(--c-danger, #f87171); padding: 0.5rem 1rem; }

  &__layout {
    display: grid;
    grid-template-columns: 220px 1fr;
    flex: 1;
    overflow: hidden;
  }

  &__main {
    overflow-y: auto;
    padding: 1rem;
  }

  &__loading, &__empty { color: var(--c-text-muted); padding: 1rem; }

  &__grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
    gap: 1rem;
  }
}
</style>
