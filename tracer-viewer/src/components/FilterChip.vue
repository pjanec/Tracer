<template>
  <span class="filter-chip">
    <span class="filter-chip__label">{{ label }}</span>:
    <span class="filter-chip__value">{{ value }}</span>
    <button class="filter-chip__remove" aria-label="Remove filter" @click="emit('remove')">×</button>
  </span>
</template>

<script setup lang="ts">
defineProps<{ label: string; value: string }>();
const emit = defineEmits<{ (e: 'remove'): void }>();
</script>

<style scoped>
.filter-chip { display: inline-flex; align-items: center; gap: 2px; background: #e8f4fd; border: 1px solid #90caf9; border-radius: 12px; padding: 2px 8px; font-size: 0.8rem; }
.filter-chip__label { font-weight: 600; color: #1565c0; }
.filter-chip__value { color: #333; }
.filter-chip__remove { background: none; border: none; cursor: pointer; padding: 0 2px; color: #666; font-size: 1rem; line-height: 1; }
</style>
