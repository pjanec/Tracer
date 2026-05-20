import { defineStore } from 'pinia';

export interface LiveConnectionState {
  connected: boolean;
  lastEventAt: Date | null;
  reconnectAttempts: number;
}

export const useLiveStore = defineStore('live', {
  state: () => ({
    connection: {
      connected: false,
      lastEventAt: null,
      reconnectAttempts: 0,
    } as LiveConnectionState,
  }),
  actions: {
    setConnected(connected: boolean) {
      this.connection.connected = connected;
      if (connected) this.connection.reconnectAttempts = 0;
    },
    onEvent() {
      this.connection.lastEventAt = new Date();
    },
    onReconnect() {
      this.connection.reconnectAttempts += 1;
    },
  },
});
