import { defineStore } from 'pinia';
import { api } from '@/api/tracerApiClient';
import type { TopologyDto } from '@/api/tracerApiClient';

export const useTopologyStore = defineStore('topology', {
  state: () => ({
    topology: null as TopologyDto | null,
    loading: false,
  }),
  actions: {
    async load() {
      this.loading = true;
      try {
        this.topology = await api.getTopology();
      } finally {
        this.loading = false;
      }
    },
  },
});
