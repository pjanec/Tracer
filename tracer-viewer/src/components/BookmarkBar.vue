<template>
  <nav v-if="bookmarks.length > 0" class="bookmark-bar">
    <button
      v-for="b in bookmarks"
      :key="b.savedViewId"
      class="bookmark-bar__chip"
      :title="b.label"
      @click="onChipClick(b)"
    >
      {{ b.label }}
    </button>
  </nav>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import type { SavedViewDto } from '@/api/tracerApiClient';
import { useBookmarks } from '@/composables/useBookmarks';
import { usePersonaStore } from '@/stores/personaStore';

const props = defineProps<{ sessionId: string; viewType: string }>();
const router = useRouter();
const { listBookmarks } = useBookmarks();
const personaStore = usePersonaStore();
const bookmarks = ref<SavedViewDto[]>([]);

async function loadBookmarks() {
  bookmarks.value = await listBookmarks(props.sessionId, props.viewType);
}

onMounted(loadBookmarks);

// Reload when persona changes
watch(() => personaStore.current, loadBookmarks);

async function onChipClick(b: SavedViewDto) {
  void api.recordSavedViewOpened(b.savedViewId);
  await router.push(b.url);
}
</script>

<style lang="scss">
.bookmark-bar {
  display: flex;
  gap: 0.5rem;
  padding: 0.25rem 1rem;
  overflow-x: auto;

  &__chip {
    max-width: 16rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    padding: 0.25rem 0.75rem;
    border-radius: 999px;
    border: 1px solid var(--c-bg-subtle);
    background: var(--c-bg-surface);
    color: var(--c-text);
    font-size: 0.8rem;
    cursor: pointer;

    &:hover {
      background: var(--c-bg-subtle);
    }
  }
}
</style>
