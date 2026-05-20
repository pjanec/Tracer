import { defineStore } from 'pinia';
import { api } from '@/api/tracerApiClient';
import type { SessionDto, ScenarioStateDto } from '@/api/tracerApiClient';

export const useSessionStore = defineStore('session', {
  state: () => ({
    current: null as SessionDto | null,
    state: null as ScenarioStateDto | null,
    loading: false,
    error: null as string | null,
  }),
  actions: {
    async load(sessionId: string) {
      this.loading = true;
      this.error = null;
      try {
        const [session, scenarioState] = await Promise.all([
          api.getSession(sessionId),
          api.getScenarioState(sessionId),
        ]);
        this.current = session;
        this.state = scenarioState;
      } catch (err: unknown) {
        this.error = err instanceof Error ? err.message : 'Failed to load session';
      } finally {
        this.loading = false;
      }
    },
    async refreshState() {
      if (!this.current) return;
      this.state = await api.getScenarioState(this.current.sessionId);
    },
    clear() {
      this.current = null;
      this.state = null;
      this.error = null;
    },
  },
});
