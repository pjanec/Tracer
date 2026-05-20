<template>
  <div class="swimlane" :style="{ top: `${topPx}px` }">
    <span
      class="swimlane__color-swatch"
      :style="{ background: color }"
    />
    <span class="swimlane__label">{{ nodeName }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{
  nodeName: string;
  color: string;
  index: number;
  swimlaneHeightPx?: number;
}>();

const topPx = computed(() =>
  props.index * (props.swimlaneHeightPx ?? 80),
);
</script>

<style scoped>
.swimlane {
  position: absolute;
  left: 0;
  display: flex;
  align-items: center;
  gap: 6px;
  height: 80px;
  padding: 0 8px;
  background: #1a1a2a;
  border-bottom: 1px solid #313244;
  width: 100%;
}

.swimlane__color-swatch {
  width: 10px;
  height: 10px;
  border-radius: 2px;
  flex-shrink: 0;
}

.swimlane__label {
  font-size: 0.75rem;
  color: #cdd6f4;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
