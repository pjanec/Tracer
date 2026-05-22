<script setup lang="ts">
import type { SlowStateSampleDto } from '@/api/tracerApiClient';

defineProps<{
  topic: string;
  samples: SlowStateSampleDto[];
  timeRange: { from: Date; to: Date };
}>();

defineEmits<{
  'select-event': [eventId: string];
}>();
</script>

<template>
  <div class="slow-state-chart"></div>
</template>
