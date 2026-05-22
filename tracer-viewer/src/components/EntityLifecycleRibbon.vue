<script setup lang="ts">
import type { EntityEventsDto } from '@/api/tracerApiClient';

defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
}>();
</script>

<template>
  <div class="entity-lifecycle-ribbon"></div>
</template>
