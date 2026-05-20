// src/stores/bundleStore.ts
import { defineStore } from 'pinia';
import { api } from '@/api/tracerApiClient';

export interface BundleListEntryDto {
  bundleId:    string;
  label?:      string;
  sizeBytes?:  number;
  createdAtUtc: string;
}

export const useBundleStore = defineStore('bundles', {
  state: () => ({
    bundles: [] as BundleListEntryDto[],
    loading: false,
    error:   null as string | null,
  }),

  actions: {
    async load() {
      this.loading = true;
      this.error   = null;
      try {
        const raw = await api.listBundles();
        this.bundles = raw as BundleListEntryDto[];
      } catch (err: unknown) {
        this.error = err instanceof Error ? err.message : 'Failed to load bundles';
        this.bundles = [];
      } finally {
        this.loading = false;
      }
    },
  },
});
