<script setup lang="ts">
import type { EntityEventsDto } from '@/api/tracerApiClient';

defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
  selectedEventId: string | null;
}>();

defineEmits<{
  select: [eventId: string];
}>();
</script>

<template>
  <div class="entity-event-strip"></div>
</template>
