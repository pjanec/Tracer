import { describe, it, expect } from 'vitest';
import { classifyLifecycleEvent } from '../../src/utils/lifecycleClassifier';

describe('lifecycleClassifier', () => {
  it('classifySpawnSuffixes', () => {
    expect(classifyLifecycleEvent('entity.spawned')).toBe('spawn');
    expect(classifyLifecycleEvent('sim.created')).toBe('spawn');
    expect(classifyLifecycleEvent('player.spawn')).toBe('spawn');
  });

  it('classifyOwnershipSuffixes', () => {
    expect(classifyLifecycleEvent('obj.ownership_changed')).toBe('ownership');
    expect(classifyLifecycleEvent('unit.owner_transferred')).toBe('ownership');
  });

  it('classifyDestructionSuffixes', () => {
    expect(classifyLifecycleEvent('unit.destroyed')).toBe('destruction');
    expect(classifyLifecycleEvent('obj.killed')).toBe('destruction');
    expect(classifyLifecycleEvent('entity.died')).toBe('destruction');
  });

  it('unrelatedTopicReturnsNull', () => {
    expect(classifyLifecycleEvent('vehicle_health')).toBeNull();
    expect(classifyLifecycleEvent('transforms')).toBeNull();
  });
});
