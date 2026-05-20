<script setup lang="ts">
import { ref } from 'vue';
import { useApi } from '@/api/useApi';
import { useRouter } from 'vue-router';
import { useBundleMode } from '@/composables/useBundleMode';

const api = useApi();
const router = useRouter();
const { refresh } = useBundleMode();

const filePath = ref('');
const loading = ref(false);
const error = ref<string | null>(null);

async function openBundle() {
  if (!filePath.value) return;
  loading.value = true;
  error.value = null;
  try {
    await api.openBundle({ path: filePath.value });
    await refresh();
    router.push({ name: 'sessions' });
  } catch (err: unknown) {
    error.value = (err instanceof Error ? err.message : null) ?? 'Failed to open bundle';
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <div class="bundle-open">
    <h1>Open a Tracer bundle</h1>
    <p class="bundle-open__hint">
      Paste the absolute path to a <code>.tracerbundle</code> directory
      or <code>.tracerbundle.zip</code> file.
    </p>
    <input
      v-model="filePath"
      type="text"
      placeholder="C:\bundles\training_run.tracerbundle"
      class="bundle-open__input"
      @keyup.enter="openBundle"
    />
    <button class="bundle-open__btn" :disabled="!filePath || loading" @click="openBundle">
      {{ loading ? 'Opening…' : 'Open' }}
    </button>
    <div v-if="error" class="bundle-open__error">{{ error }}</div>
  </div>
</template>

<style scoped lang="scss">
.bundle-open {
  max-width: 600px;
  margin: 4rem auto;
  padding: 2rem;

  &__hint { color: var(--c-text-muted); margin-bottom: 1.5rem; }
  &__input {
    width: 100%;
    padding: 0.75rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-bg-subtle);
    border-radius: 6px;
    color: var(--c-text);
    font-family: var(--font-mono);
  }
  &__btn {
    margin-top: 1rem;
    padding: 0.75rem 1.5rem;
    background: var(--c-accent);
    color: white;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    &:disabled { opacity: 0.5; cursor: not-allowed; }
  }
  &__error {
    margin-top: 1rem;
    padding: 0.75rem;
    background: rgba(232, 92, 92, 0.1);
    border: 1px solid var(--c-danger);
    border-radius: 6px;
    color: var(--c-danger);
  }
}
</style>
