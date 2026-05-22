import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises, RouterLinkStub } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { EntityListDto, EntitySummaryDto, SessionDto } from '../../src/api/tracerApiClient';

const mockListEntities = vi.fn<[], Promise<EntityListDto>>();
const mockRouterPush = vi.fn();
const mockBuildBundle = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listEntities: mockListEntities,
    buildBundle: mockBuildBundle,
  },
}));

vi.mock('vue-router', () => ({
  useRouter: vi.fn(() => ({ push: mockRouterPush })),
}));

function makeEntity(override?: Partial<EntitySummaryDto>): EntitySummaryDto {
  return {
    entityId: 'ent-default',
    firstSeenUtc: '2026-01-01T10:00:00Z',
    lastSeenUtc: '2026-01-01T11:00:00Z',
    eventCount: 100,
    topics: ['topic.a', 'topic.b'],
    ...override,
  };
}

function makeEntityList(entities: EntitySummaryDto[]): EntityListDto {
  return { entities, count: entities.length };
}

describe('EntityPickerView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockListEntities.mockReset();
    mockRouterPush.mockReset();
    mockBuildBundle.mockReset();
  });

  // SC-1: Loads and renders 3 entities
  it('loadsAndRenders_ThreeEntities', async () => {
    const entities = [
      makeEntity({ entityId: 'ent-1' }),
      makeEntity({ entityId: 'ent-2' }),
      makeEntity({ entityId: 'ent-3' }),
    ];
    mockListEntities.mockResolvedValue(makeEntityList(entities));
    const { default: EntityPickerView } = await import('../../src/views/EntityPickerView.vue');
    const wrapper = mount(EntityPickerView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    const items = wrapper.findAll('.entity-picker__item');
    expect(items.length).toBe(3);
    expect(wrapper.text()).toContain('ent-1');
    expect(wrapper.text()).toContain('ent-2');
    expect(wrapper.text()).toContain('ent-3');
  });

  // SC-2: Loading state shown while API pending
  it('loadingState_SpinnerVisible_WhileApiPending', async () => {
    let resolveApi!: (value: EntityListDto) => void;
    mockListEntities.mockReturnValue(
      new Promise<EntityListDto>((r) => { resolveApi = r; }),
    );
    const { default: EntityPickerView } = await import('../../src/views/EntityPickerView.vue');
    const wrapper = mount(EntityPickerView, { props: { sessionId: 'sess-1' } });

    // Wait one tick so onMounted runs and sets loading=true, DOM updates
    await wrapper.vm.$nextTick();

    expect(wrapper.find('.loading-spinner').exists()).toBe(true);
    expect(wrapper.find('.entity-picker__list').exists()).toBe(false);

    // Clean up
    resolveApi(makeEntityList([]));
    await flushPromises();
  });

  // SC-3: Empty result shows empty state
  it('emptyResult_ShowsEmptyState', async () => {
    mockListEntities.mockResolvedValue(makeEntityList([]));
    const { default: EntityPickerView } = await import('../../src/views/EntityPickerView.vue');
    const wrapper = mount(EntityPickerView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    expect(wrapper.find('.entity-picker__empty').exists()).toBe(true);
    expect(wrapper.find('.loading-spinner').exists()).toBe(false);
    expect(wrapper.find('.entity-picker__list').exists()).toBe(false);
  });

  // SC-4: Filter hides non-matching entities
  it('filter_HidesNonMatchingEntities', async () => {
    const entities = [
      makeEntity({ entityId: 'player-alpha' }),
      makeEntity({ entityId: 'tank-beta' }),
      makeEntity({ entityId: 'player-gamma' }),
    ];
    mockListEntities.mockResolvedValue(makeEntityList(entities));
    const { default: EntityPickerView } = await import('../../src/views/EntityPickerView.vue');
    const wrapper = mount(EntityPickerView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    // Before filter: all 3 items
    expect(wrapper.findAll('.entity-picker__item').length).toBe(3);

    // Apply filter
    const filterInput = wrapper.find('.entity-picker__filter');
    await filterInput.setValue('player');
    await wrapper.vm.$nextTick();

    // After filter: only 2 matching items
    expect(wrapper.findAll('.entity-picker__item').length).toBe(2);
  });

  // SC-5: Clicking entity navigates to entity-history
  it('clickEntity_NavigatesToEntityHistory', async () => {
    const entities = [makeEntity({ entityId: 'ent-clicked' })];
    mockListEntities.mockResolvedValue(makeEntityList(entities));
    const { default: EntityPickerView } = await import('../../src/views/EntityPickerView.vue');
    const wrapper = mount(EntityPickerView, { props: { sessionId: 'sess-42' } });
    await flushPromises();

    await wrapper.find('.entity-picker__item').trigger('click');

    expect(mockRouterPush).toHaveBeenCalledWith({
      name: 'entity-history',
      params: { entityId: 'ent-clicked' },
      query: { session: 'sess-42' },
    });
  });

  // SC-6: Topics overflow — 8 topics shows first 5 + "+3 more"
  it('topicsOverflow_ShowsFirstFivePlusMoreCount', async () => {
    const entity = makeEntity({
      entityId: 'ent-many-topics',
      topics: ['t1', 't2', 't3', 't4', 't5', 't6', 't7', 't8'],
    });
    mockListEntities.mockResolvedValue(makeEntityList([entity]));
    const { default: EntityPickerView } = await import('../../src/views/EntityPickerView.vue');
    const wrapper = mount(EntityPickerView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    const topicsSpan = wrapper.find('.entity-picker__topics');
    expect(topicsSpan.text()).toContain('t1');
    expect(topicsSpan.text()).toContain('t5');
    expect(topicsSpan.text()).toContain('+3 more');
    expect(topicsSpan.text()).not.toContain('t6');
  });
});

// SC-7: Entities link on SessionCard
describe('EntityPickerView — SessionCard entities link', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockBuildBundle.mockReset();
  });

  it('sessionCard_HasEntitiesLink_ToEntityPickerRoute', async () => {
    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const session: SessionDto = {
      sessionId: 'sess-xyz',
      scenarioId: 'sc-1',
      startUtc: '2026-01-01T00:00:00Z',
      status: 'active',
      participatingNodes: ['node-a'],
      eventCount: 42,
    };

    const wrapper = mount(SessionCard, {
      props: { session },
      global: {
        plugins: [createPinia()],
        stubs: { RouterLink: RouterLinkStub },
      },
    });

    const link = wrapper.findComponent(RouterLinkStub);
    expect(link.exists()).toBe(true);
    expect(link.props('to')).toEqual({
      name: 'entity-picker',
      params: { sessionId: 'sess-xyz' },
    });
    expect(link.text()).toBe('Entities');
  });
});
