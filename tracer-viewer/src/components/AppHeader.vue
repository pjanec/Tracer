<template>
  <header class="app-header">
    <div class="app-header__brand">
      <span class="app-header__title">Tracer</span>
    </div>
    <PersonaSwitcher class="app-header__persona" />
  </header>
</template>

<script setup lang="ts">
import PersonaSwitcher from '@/components/PersonaSwitcher.vue';
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
</style>

