<template>
  <header class="app-header">
    <div class="app-header__brand">
      <span class="app-header__title">Tracer</span>
      <span
        v-if="bundleMode.isBundle.value"
        class="app-header__badge app-header__badge--bundle"
      >Bundle Mode</span>
      <span
        v-else-if="bundleMode.isNoBundle.value"
        class="app-header__badge app-header__badge--no-bundle"
      >No Bundle</span>
      <span
        v-if="sessionLabel"
        class="app-header__session"
      >{{ sessionLabel }}</span>
    </div>
    <PersonaSwitcher class="app-header__persona" />
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import PersonaSwitcher from '@/components/PersonaSwitcher.vue';
import { useSessionStore } from '@/stores/sessionStore';
import { useBundleMode } from '@/composables/useBundleMode';

const sessionStore = useSessionStore();
const bundleMode = useBundleMode();

const sessionLabel = computed(() =>
  sessionStore.current?.sessionId ?? null
);
</script>

<style>
.app-header {
  display: flex;
  align-items: center;
  padding: 0 1.5rem;
  height: 3.5rem;
  background: var(--c-bg-surface);
  border-bottom: 1px solid var(--c-bg-subtle);
}

.app-header__brand {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.app-header__title {
  font-size: 1.125rem;
  font-weight: 600;
  color: var(--c-text);
  letter-spacing: 0.02em;
}

.app-header__persona {
  margin-left: auto;
}

.app-header__badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.15rem 0.5rem;
  border-radius: 99px;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.app-header__badge--bundle {
  background: var(--c-accent, #4a9eff);
  color: white;
}

.app-header__badge--no-bundle {
  background: var(--c-bg-subtle, #eee);
  color: var(--c-text-muted, #666);
}

.app-header__session {
  font-size: 0.8rem;
  color: var(--c-text-muted, #888);
  font-family: monospace;
}
</style>

