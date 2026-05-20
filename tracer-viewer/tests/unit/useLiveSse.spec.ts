import { describe, it, expect, vi, beforeEach } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';
import { createApp } from 'vue';
import { flushPromises } from '@vue/test-utils';
import { useLiveNotables } from '@/composables/useLiveSse';
import { useLiveStore } from '@/stores/liveStore';

vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: vi.fn(),
}));

import { fetchEventSource } from '@microsoft/fetch-event-source';

type SseHandlers = {
  onopen?: (r: { ok: boolean; status: number }) => Promise<void>;
  onmessage?: (e: { data: string }) => void;
  onclose?: () => void;
  onerror?: (err: Error) => void;
};

let capturedHandlers: SseHandlers = {};

function withSetup<T>(composable: () => T): [T, () => void] {
  let result!: T;
  const pinia = createPinia();
  setActivePinia(pinia);
  const app = createApp({
    setup() {
      result = composable();
      return () => null;
    },
  });
  app.use(pinia);
  app.mount(document.createElement('div'));
  return [result, () => app.unmount()];
}

describe('useLiveNotables', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    capturedHandlers = {};
    (fetchEventSource as ReturnType<typeof vi.fn>).mockImplementation(
      (_url: string, opts: SseHandlers) => {
        capturedHandlers = opts;
        return new Promise(() => {});
      },
    );
  });

  it('Connect_SetsLiveStoreConnected', async () => {
    const [result, unmount] = withSetup(() => useLiveNotables('session-1'));
    await flushPromises();

    const liveStore = useLiveStore();
    await capturedHandlers.onopen!({ ok: true, status: 200 });

    expect(liveStore.connection.connected).toBe(true);
    unmount();
    void result;
  });

  it('Message_PrependsEventToList', async () => {
    const [result, unmount] = withSetup(() => useLiveNotables('session-1'));
    await flushPromises();

    const event = {
      eventId: 'abc-123',
      traceId: 'trace-1',
      occurredAtUtc: '2025-01-01T00:00:00Z',
      topic: 'combat.event',
      notableLabel: 'TestHit',
    };
    capturedHandlers.onmessage!({ data: JSON.stringify(event) });

    expect(result.events.value[0].eventId).toBe('abc-123');
    unmount();
  });

  it('Message_CapsListAt200Events', async () => {
    const [result, unmount] = withSetup(() => useLiveNotables('session-1'));
    await flushPromises();

    for (let i = 0; i < 201; i++) {
      capturedHandlers.onmessage!({
        data: JSON.stringify({
          eventId: `id-${i}`,
          traceId: `trace-${i}`,
          occurredAtUtc: '2025-01-01T00:00:00Z',
          topic: 'combat.event',
          notableLabel: 'Hit',
        }),
      });
    }

    expect(result.events.value.length).toBe(200);
    unmount();
  });

  it('Close_SetsDisconnected', async () => {
    const [result, unmount] = withSetup(() => useLiveNotables('session-1'));
    await flushPromises();

    const liveStore = useLiveStore();
    liveStore.setConnected(true);
    capturedHandlers.onclose!();

    expect(liveStore.connection.connected).toBe(false);
    unmount();
    void result;
  });

  it('Error_IncrementsReconnectAttempts', async () => {
    const [result, unmount] = withSetup(() => useLiveNotables('session-1'));
    await flushPromises();

    const liveStore = useLiveStore();
    const before = liveStore.connection.reconnectAttempts;
    capturedHandlers.onerror!(new Error('test'));

    expect(liveStore.connection.reconnectAttempts).toBeGreaterThan(before);
    unmount();
    void result;
  });
});
