import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import EntityLifecycleRibbon from '../../src/components/EntityLifecycleRibbon.vue';
import type { EntityEventsDto, EntityEventDto } from '../../src/api/tracerApiClient';

let _eventCounter = 0;

function makeEvent(overrides: Partial<EntityEventDto> = {}): EntityEventDto {
  return {
    eventId: `evt-${++_eventCounter}`,
    traceId: '0000000000000000',
    occurredAtUtc: '2026-01-01T10:00:00.500Z',
    topic: 'entity.update',
    publisherNode: 'node-A',
    ...overrides,
  };
}

function makeEventsDto(events: EntityEventDto[]): EntityEventsDto {
  return { entityId: 'ent-1', events, truncated: false };
}

const BASE_TIME_RANGE = {
  from: new Date('2026-01-01T10:00:00.000Z'),
  to: new Date('2026-01-01T10:00:01.000Z'),
};

describe('entityLifecycleRibbon', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    _eventCounter = 0;
  });

  it('rendersCorrectNumberOfMarkersByKind', () => {
    const events = makeEventsDto([
      makeEvent({ topic: 'sim.spawned',           occurredAtUtc: '2026-01-01T10:00:00.100Z' }),
      makeEvent({ topic: 'sim.ownership_changed', occurredAtUtc: '2026-01-01T10:00:00.400Z' }),
      makeEvent({ topic: 'sim.ownership_changed', occurredAtUtc: '2026-01-01T10:00:00.600Z' }),
      makeEvent({ topic: 'unit.destroyed',        occurredAtUtc: '2026-01-01T10:00:00.900Z' }),
      makeEvent({ topic: 'entity.update',         occurredAtUtc: '2026-01-01T10:00:00.500Z' }), // non-lifecycle
    ]);

    const wrapper = mount(EntityLifecycleRibbon, {
      props: { events, timeRange: BASE_TIME_RANGE },
    });

    expect(wrapper.findAll('.entity-lifecycle-ribbon__marker--spawn').length).toBe(1);
    expect(wrapper.findAll('.entity-lifecycle-ribbon__marker--ownership').length).toBe(2);
    expect(wrapper.findAll('.entity-lifecycle-ribbon__marker--destruction').length).toBe(1);
  });

  it('markerHorizontalPositionMatchesTime', () => {
    // timeRange 0–1000ms; spawn event at 500ms → left: 50%
    const from = new Date('2026-01-01T10:00:00.000Z');
    const to = new Date('2026-01-01T10:00:01.000Z');
    const events = makeEventsDto([
      makeEvent({
        topic: 'entity.spawned',
        occurredAtUtc: '2026-01-01T10:00:00.500Z', // 500ms into range → 50%
      }),
    ]);

    const wrapper = mount(EntityLifecycleRibbon, {
      props: { events, timeRange: { from, to } },
    });

    const marker = wrapper.find('.entity-lifecycle-ribbon__marker--spawn');
    expect(marker.exists()).toBe(true);
    expect((marker.element as HTMLElement).style.left).toBe('50%');
  });

  it('noMarkersWhenNoLifecycleEvents', () => {
    const events = makeEventsDto([
      makeEvent({ topic: 'vehicle.health', occurredAtUtc: '2026-01-01T10:00:00.500Z' }),
      makeEvent({ topic: 'transforms',     occurredAtUtc: '2026-01-01T10:00:00.600Z' }),
    ]);

    const wrapper = mount(EntityLifecycleRibbon, {
      props: { events, timeRange: BASE_TIME_RANGE },
    });

    // No lifecycle markers, but track element must still render
    expect(wrapper.findAll('.entity-lifecycle-ribbon__marker').length).toBe(0);
    expect(wrapper.find('.entity-lifecycle-ribbon__track').exists()).toBe(true);
  });

  it('twoOwnershipBands_WhenSpawnThenOwnershipChanged', () => {
    // spawn at t=0 (0%), ownership_changed at t=500ms (50%)
    const from = new Date('2026-01-01T10:00:00.000Z');
    const to = new Date('2026-01-01T10:00:01.000Z');

    const events = makeEventsDto([
      makeEvent({
        topic: 'entity.spawned',
        occurredAtUtc: '2026-01-01T10:00:00.000Z', // xPct = 0%
      }),
      makeEvent({
        topic: 'unit.ownership_changed',
        occurredAtUtc: '2026-01-01T10:00:00.500Z', // xPct = 50%
      }),
    ]);

    const wrapper = mount(EntityLifecycleRibbon, {
      props: { events, timeRange: { from, to } },
    });

    const bands = wrapper.findAll('.entity-lifecycle-ribbon__ownership-band');
    expect(bands.length).toBe(2);

    // First band: left=0%, width=50% → ends at 50% position
    const firstBand = bands[0].element as HTMLElement;
    const endPct = parseFloat(firstBand.style.left) + parseFloat(firstBand.style.width);
    expect(endPct).toBeCloseTo(50, 1);
  });
});
