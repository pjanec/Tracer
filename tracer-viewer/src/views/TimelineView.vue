<template>
  <div class="timeline-view">
    <TimelineToolbar />
    <div class="timeline-view__layout">
      <!-- FilterPanel placeholder (full wiring in TRC-P5-006) -->
      <div class="filter-panel-placeholder" />
      <div class="timeline-view__main">
        <TimelineCanvas
          class="timeline-canvas"
          @marker-click="onMarkerClick"
        />
        <TimelineAxis />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { useTimelineUrl } from '@/composables/useTimelineUrl';
import TimelineToolbar from '@/components/TimelineToolbar.vue';
import TimelineCanvas  from '@/components/TimelineCanvas.vue';
import TimelineAxis    from '@/components/TimelineAxis.vue';

const props = defineProps<{
  sessionId: string;
}>();

const store = useTimelineStore();
useTimelineUrl();

onMounted(() => {
  store.setSession(props.sessionId);
});

function onMarkerClick(eventId: string) {
  store.selectedEventId = eventId;
}
</script>

<style scoped>
.timeline-view {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.timeline-view__layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  flex: 1;
  overflow: hidden;
}

.filter-panel-placeholder {
  background: #1a1a2a;
  border-right: 1px solid #313244;
}

.timeline-view__main {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.timeline-canvas {
  flex: 1;
}
</style>
