<script setup lang="ts">
import type { EntitySummaryDto } from '@/api/tracerApiClient';

defineProps<{
  summary: EntitySummaryDto;
}>();
</script>

<template>
  <div class="entity-summary-strip">{{ summary.entityId }}</div>
</template>
