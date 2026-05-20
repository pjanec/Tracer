<template>
  <div class="density-indicator">
    <template v-if="store.queryMode === 'list'">
      Showing {{ store.returned }} of {{ store.totalMatching }} events
    </template>
    <template v-else>
      Buckets of {{ store.bucketDuration }}
    </template>
  </div>
</template>

<script setup lang="ts">
import { useTimelineStore } from '@/stores/timelineStore';

const store = useTimelineStore();
</script>

<style scoped>
.density-indicator {
  font-size: 0.8rem;
  color: #888;
  margin-left: auto;
  padding: 0 8px;
}
</style>
