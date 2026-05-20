<template>
  <div class="bundles-view">
    <h1>Bundle Library</h1>
    <div v-if="loading" class="bundles__loading">Loading…</div>
    <div v-else-if="error" class="bundles__error">{{ error }}</div>
    <ul v-else class="bundles__list">
      <li
        v-for="bundle in bundles"
        :key="bundle.bundleId"
        class="bundles__item"
      >
        <a :href="`/api/bundle/${bundle.bundleId}/download`">
          {{ bundle.label ?? bundle.bundleId }}
        </a>
        <button @click="onBuildBundle(bundle.bundleId)">Build bundle</button>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';

interface BundleItem {
  bundleId: string;
  label?: string;
  createdAtUtc: string;
}

const bundles = ref<BundleItem[]>([]);
const loading = ref(false);
const error   = ref<string | null>(null);

onMounted(async () => {
  loading.value = true;
  try {
    bundles.value = await api.listBundles();
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to load bundles';
  } finally {
    loading.value = false;
  }
});

async function onBuildBundle(sessionId: string) {
  try {
    await api.buildBundle(sessionId);
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to build bundle';
  }
}
</script>

<style scoped>
.bundles-view {
  padding: 24px;
}

.bundles__list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.bundles__item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  background: #1e1e2e;
  border-radius: 4px;
}

.bundles__item a {
  flex: 1;
  color: #89b4fa;
  text-decoration: none;
}

.bundles__item a:hover {
  text-decoration: underline;
}

.bundles__loading,
.bundles__error {
  padding: 8px 0;
  color: #cdd6f4;
}

.bundles__error {
  color: #f38ba8;
}
</style>
