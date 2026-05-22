<script setup lang="ts">
defineProps<{
  entityId: string;
  sessionId: string;
  availableTopics: string[];
  timeRange: { from: Date; to: Date };
}>();
</script>

<template>
  <div class="fast-state-drill-down"></div>
</template>
