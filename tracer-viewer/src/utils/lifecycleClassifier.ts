// tracer-viewer/src/utils/lifecycleClassifier.ts
// Pure utility: classifies an entity-lifecycle event topic by its last dotted segment.

export type LifecycleKind = 'spawn' | 'ownership' | 'destruction';

const SPAWN_SUFFIXES = new Set([
  'spawned', 'spawn', 'created', 'create', 'born', 'birth', 'instantiated',
]);

const OWNERSHIP_SUFFIXES = new Set([
  'ownership_changed', 'owner_changed', 'owner_transferred', 'ownership_transferred',
]);

const DESTRUCTION_SUFFIXES = new Set([
  'destroyed', 'killed', 'despawned', 'removed', 'deleted', 'died', 'death',
]);

/**
 * Classifies an entity-lifecycle event topic by examining its last dotted segment.
 * Returns the lifecycle kind, or null if the topic does not match any known lifecycle pattern.
 */
export function classifyLifecycleEvent(topic: string): LifecycleKind | null {
  const suffix = topic.split('.').pop()?.toLowerCase();
  if (!suffix) return null;
  if (SPAWN_SUFFIXES.has(suffix)) return 'spawn';
  if (OWNERSHIP_SUFFIXES.has(suffix)) return 'ownership';
  if (DESTRUCTION_SUFFIXES.has(suffix)) return 'destruction';
  return null;
}
