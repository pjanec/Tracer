<template>
  <div class="bundles-view">
    <h1>Bundle Library</h1>

    <div v-if="store.loading" class="bundles__loading">Loading…</div>

    <div v-else-if="store.error" class="bundles__error">{{ store.error }}</div>

    <template v-else>
      <p v-if="!isLive.value" class="bundles__offline-hint">
        To open a different bundle, return to the Open Bundle screen.
      </p>

      <div v-if="store.bundles.length === 0" class="bundles__empty">
        No bundles built yet
      </div>

      <ul v-else class="bundles__list">
        <li
          v-for="bundle in store.bundles"
          :key="bundle.bundleId"
          class="bundles__item"
        >
          <div class="bundles__item-info">
            <span class="bundles__item-label">{{ bundle.label ?? bundle.bundleId }}</span>
            <span v-if="bundle.sizeBytes" class="bundles__item-size">
              {{ formatSize(bundle.sizeBytes) }}
            </span>
          </div>
          <a
            v-if="isLive.value"
            :href="`/api/bundles/${bundle.bundleId}/download`"
            class="bundles__item-download"
          >
            Download
          </a>
        </li>
      </ul>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useBundleStore } from '@/stores/bundleStore';
import { useBundleMode } from '@/composables/useBundleMode';

const store = useBundleStore();
const { isLive } = useBundleMode();

onMounted(() => {
  void store.load();
});

function formatSize(bytes: number): string {
  if (bytes < 1024)              return `${bytes} B`;
  if (bytes < 1024 * 1024)       return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
</script>

<style scoped>
.bundles-view { padding: 24px; }
.bundles__empty { color: #888; margin-top: 16px; }
.bundles__offline-hint { color: #666; font-style: italic; margin-bottom: 16px; }
.bundles__list { list-style: none; padding: 0; margin: 0; }
.bundles__item { display: flex; align-items: center; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #eee; }
.bundles__item-info { display: flex; gap: 8px; align-items: center; }
.bundles__item-size { color: #888; font-size: 0.8rem; }
.bundles__item-download { text-decoration: none; color: #1976d2; }
</style>
