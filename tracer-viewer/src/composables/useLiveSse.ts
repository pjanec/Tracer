import { onMounted, onUnmounted, ref } from 'vue';
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { useLiveStore } from '@/stores/liveStore';
import type { NotableEventDto } from '@/api/tracerApiClient';

export function useLiveNotables(sessionId: string) {
  const liveStore = useLiveStore();
  const events = ref<NotableEventDto[]>([]);
  let abortCtrl: AbortController | null = null;

  const connect = async () => {
    abortCtrl = new AbortController();
    const url = `/api/live/notables?sessionId=${encodeURIComponent(sessionId)}`;

    try {
      await fetchEventSource(url, {
        signal: abortCtrl.signal,
        openWhenHidden: true,
        onopen: async (response) => {
          if (response.ok) liveStore.setConnected(true);
          else throw new Error(`SSE open failed: ${response.status}`);
        },
        onmessage: (ev) => {
          if (!ev.data) return;
          try {
            const dto = JSON.parse(ev.data) as NotableEventDto;
            events.value = [dto, ...events.value].slice(0, 200);
            liveStore.onEvent();
          } catch (err) {
            console.error('Failed to parse SSE event:', err);
          }
        },
        onclose: () => liveStore.setConnected(false),
        onerror: () => {
          liveStore.setConnected(false);
          liveStore.onReconnect();
          // Let fetchEventSource handle backoff — do not rethrow
        },
      });
    } catch (err) {
      console.error('SSE connection error:', err);
    }
  };

  onMounted(connect);
  onUnmounted(() => abortCtrl?.abort());

  return { events };
}
