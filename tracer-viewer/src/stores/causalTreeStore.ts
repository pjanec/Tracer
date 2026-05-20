// src/stores/causalTreeStore.ts
import { defineStore } from 'pinia';
import type { TraceTreeDto } from '@/types/causalTree';

export interface CausalTreeRequest {
  kind: 'trace' | 'event' | 'ancestors' | 'descendants';
  id: string;
  maxEvents?: number;
  maxDepth?: number;
  maxNodes?: number;
}

export const useCausalTreeStore = defineStore('causalTree', {
  state: () => ({
    request: null as CausalTreeRequest | null,
    tree: null as TraceTreeDto | null,
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
  }),
  actions: {
    openTrace(traceId: string, maxEvents?: number) {
      this.request = { kind: 'trace', id: traceId, maxEvents };
      this.tree = null;
      this.selectedEventId = null;
    },
    openByEvent(eventId: string, maxEvents?: number) {
      this.request = { kind: 'event', id: eventId, maxEvents };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    openAncestors(eventId: string, maxDepth?: number) {
      this.request = { kind: 'ancestors', id: eventId, maxDepth };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    openDescendants(eventId: string, maxDepth?: number, maxNodes?: number) {
      this.request = { kind: 'descendants', id: eventId, maxDepth, maxNodes };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    selectEvent(eventId: string | null) {
      this.selectedEventId = eventId;
    },
    setResult(tree: TraceTreeDto) {
      this.tree = tree;
      if (
        this.selectedEventId &&
        !tree.nodes.some(n => n.eventId === this.selectedEventId)
      ) {
        this.selectedEventId = pickInitialSelection(tree);
      } else if (!this.selectedEventId) {
        this.selectedEventId = pickInitialSelection(tree);
      }
    },
    setError(message: string) {
      this.error = message;
    },
    clear() {
      this.request = null;
      this.tree = null;
      this.selectedEventId = null;
      this.error = null;
    },
    retry() {
      const r = this.request;
      if (!r) return;
      this.request = null;
      this.request = { ...r }; // new object reference so watch fires again
    },
  },
});

function pickInitialSelection(tree: TraceTreeDto): string | null {
  const notable = tree.nodes.find(n => n.notableLabel);
  if (notable) return notable.eventId;
  return tree.nodes[0]?.eventId ?? null;
}
