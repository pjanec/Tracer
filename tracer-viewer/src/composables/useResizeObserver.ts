// src/composables/useResizeObserver.ts
// Generic ResizeObserver composable.

import { onMounted, onUnmounted, type Ref } from 'vue';

export function useResizeObserver(
  target: Ref<Element | null>,
  callback: (entry: ResizeObserverEntry) => void,
) {
  let observer: ResizeObserver | null = null;

  onMounted(() => {
    if (!target.value) return;
    observer = new ResizeObserver((entries) => {
      for (const entry of entries) callback(entry);
    });
    observer.observe(target.value);
  });

  onUnmounted(() => {
    observer?.disconnect();
    observer = null;
  });
}
