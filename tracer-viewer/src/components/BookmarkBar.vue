<template>
  <nav v-if="bookmarks.length > 0" class="bookmark-bar">
    <button
      v-for="bm in bookmarks"
      :key="bm.savedViewId"
      class="bookmark-bar__chip"
      @click="navigate(bm)"
    >
      {{ bm.label }}
    </button>
  </nav>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import { usePersonaStore } from '@/stores/personaStore';
import type { SavedViewDto } from '@/api/tracerApiClient';

const props = defineProps<{ sessionId: string; viewType: string }>();
const router = useRouter();
const personaStore = usePersonaStore();
const bookmarks = ref<SavedViewDto[]>([]);

async function load() {
  try {
    bookmarks.value = await api.listSavedViews({
      sessionId: props.sessionId,
      kind: 'Bookmark',
      viewType: props.viewType,
      persona: personaStore.current,
    });
  } catch {
    bookmarks.value = [];
  }
}

onMounted(load);
watch(() => personaStore.current, load);

async function navigate(bm: SavedViewDto) {
  await api.recordSavedViewOpened(bm.savedViewId);
  void router.push(bm.url);
}
</script>

<style>
.bookmark-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.375rem 1.5rem;
  background: var(--c-bg-subtle, #f5f5f5);
  border-bottom: 1px solid var(--c-bg-subtle, #e8e8e8);
  overflow-x: auto;
}

.bookmark-bar__chip {
  background: none;
  border: 1px solid var(--c-accent, #4a9eff);
  border-radius: 99px;
  color: var(--c-accent, #4a9eff);
  cursor: pointer;
  font-size: 0.8rem;
  padding: 0.2rem 0.75rem;
  white-space: nowrap;
}

.bookmark-bar__chip:hover {
  background: var(--c-accent, #4a9eff);
  color: white;
}
</style>
