import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';
import type { TraceTreeDto, TraceNodeDto } from '../../src/types/causalTree';

function makeTree(nodes: Partial<TraceNodeDto>[]): TraceTreeDto {
  const fullNodes: TraceNodeDto[] = nodes.map((n, i) => ({
    eventId: `evt-${i}`,
    traceId: 'trace-1',
    publishWallclock: '2026-01-01T10:00:00.000Z',
    publisherNode: 'node-a',
    topic: 'test.topic',
    ...n,
  }));
  return {
    traceId: 'trace-1',
    nodes: fullNodes,
    edges: [],
    rootEventIds: fullNodes.map(n => n.eventId),
    leafEventIds: fullNodes.map(n => n.eventId),
    summary: {
      traceId: 'trace-1',
      totalEvents: fullNodes.length,
      truncated: false,
      totalSpanMs: 0,
      participatingNodes: ['node-a'],
      rootCount: fullNodes.length,
      leafCount: fullNodes.length,
    },
  };
}

describe('causalTreeStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('openTrace_SetsRequestKindTraceAndClearsTree', () => {
    const store = useCausalTreeStore();
    // Seed some state first
    store.tree = makeTree([{ eventId: 'old-evt' }]);

    store.openTrace('abc0123456789def');

    expect(store.request).not.toBeNull();
    expect(store.request!.kind).toBe('trace');
    expect(store.request!.id).toBe('abc0123456789def');
    expect(store.tree).toBeNull();
  });

  it('setResult_WhenSelectedIdNotInTree_SelectsFirstNotableNode', () => {
    const store = useCausalTreeStore();
    store.selectedEventId = 'nonexistent';

    const tree = makeTree([
      { eventId: 'plain-evt' },
      { eventId: 'notable-evt', notableLabel: 'ImportantThing' },
    ]);

    store.setResult(tree);

    expect(store.selectedEventId).toBe('notable-evt');
  });

  it('setResult_WhenNoNotableNodes_SelectsFirstNode', () => {
    const store = useCausalTreeStore();
    store.selectedEventId = null;

    const tree = makeTree([
      { eventId: 'first-evt' },
      { eventId: 'second-evt' },
    ]);

    store.setResult(tree);

    expect(store.selectedEventId).toBe('first-evt');
  });

  it('retry_ReassignsRequest_TriggeringWatcher', () => {
    const store = useCausalTreeStore();
    store.openTrace('abc0123456789def');

    const firstRef = store.request;
    store.retry();
    const secondRef = store.request;

    expect(secondRef).not.toBeNull();
    expect(secondRef).not.toBe(firstRef); // new object reference
    expect(secondRef!.kind).toBe('trace');
    expect(secondRef!.id).toBe('abc0123456789def');
  });
});
