import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ref } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { SavedQueryDto } from '../../src/types/savedQuery';

const mockRecordSavedQueryRun = vi.fn();
const mockRouterPush = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    recordSavedQueryRun: mockRecordSavedQueryRun,
    updateSavedQuery: vi.fn(),
  },
}));

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeQuery(override?: Partial<SavedQueryDto>): SavedQueryDto {
  return {
    savedQueryId: 'q-1',
    label: 'Test Query',
    sql: 'SELECT * FROM t LIMIT $limit',
    parameters: [],
    tags: [],
    isBuiltIn: false,
    isFavorite: false,
    createdAtUtc: '2026-01-01T00:00:00Z',
    runCount: 0,
    ...override,
  };
}

function makeMockUseSavedQueries(queries: SavedQueryDto[]) {
  return {
    useSavedQueries: () => ({
      queries: ref(queries),
      loading: ref(false),
      load: vi.fn(),
      create: vi.fn(),
      remove: vi.fn(),
      toggleFavorite: vi.fn(),
      clone: vi.fn(),
    }),
  };
}

describe('SavedQueriesParamValidation', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockRecordSavedQueryRun.mockReset();
    mockRouterPush.mockReset();
    mockRecordSavedQueryRun.mockResolvedValue(undefined);
    mockRouterPush.mockResolvedValue(undefined);
    vi.resetModules();
  });

  it('paramRunDisabled_True_WhenNumericParamIsNaN', async () => {
    const query = makeQuery({
      parameters: [{ name: 'limit', duckType: 'BIGINT', defaultValueText: '10' }],
    });
    vi.doMock('@/composables/useSavedQueries', () => makeMockUseSavedQueries([query]));
    const { default: SavedQueriesView } = await import('../../src/views/SavedQueriesView.vue');
    const wrapper = mount(SavedQueriesView, { global: { plugins: [createPinia()] } });
    await flushPromises();

    // Click Run to open the param form
    await wrapper.find('.saved-queries-view__btn--primary').trigger('click');
    await flushPromises();

    // Set input to non-numeric value
    const input = wrapper.find('.saved-queries-view__form-input');
    await input.setValue('abc');

    const executeBtn = wrapper.find('.saved-queries-view__param-form .saved-queries-view__btn--primary');
    expect((executeBtn.element as HTMLButtonElement).disabled).toBe(true);
  });

  it('paramRunDisabled_False_WhenNumericParamIsValid', async () => {
    const query = makeQuery({
      parameters: [{ name: 'limit', duckType: 'BIGINT', defaultValueText: '10' }],
    });
    vi.doMock('@/composables/useSavedQueries', () => makeMockUseSavedQueries([query]));
    const { default: SavedQueriesView } = await import('../../src/views/SavedQueriesView.vue');
    const wrapper = mount(SavedQueriesView, { global: { plugins: [createPinia()] } });
    await flushPromises();

    // Click Run to open the param form
    await wrapper.find('.saved-queries-view__btn--primary').trigger('click');
    await flushPromises();

    // Set input to valid numeric value
    const input = wrapper.find('.saved-queries-view__form-input');
    await input.setValue('42');

    const executeBtn = wrapper.find('.saved-queries-view__param-form .saved-queries-view__btn--primary');
    expect((executeBtn.element as HTMLButtonElement).disabled).toBe(false);
  });

  it('paramRunDisabled_False_ForTextParam', async () => {
    const query = makeQuery({
      parameters: [{ name: 'topic', duckType: 'VARCHAR', defaultValueText: '' }],
    });
    vi.doMock('@/composables/useSavedQueries', () => makeMockUseSavedQueries([query]));
    const { default: SavedQueriesView } = await import('../../src/views/SavedQueriesView.vue');
    const wrapper = mount(SavedQueriesView, { global: { plugins: [createPinia()] } });
    await flushPromises();

    // Click Run to open the param form
    await wrapper.find('.saved-queries-view__btn--primary').trigger('click');
    await flushPromises();

    // Set input to non-numeric value (allowed for VARCHAR)
    const input = wrapper.find('.saved-queries-view__form-input');
    await input.setValue('not-a-number');

    const executeBtn = wrapper.find('.saved-queries-view__param-form .saved-queries-view__btn--primary');
    expect((executeBtn.element as HTMLButtonElement).disabled).toBe(false);
  });

  it('runQuery_WithParams_ShowsParamForm', async () => {
    const query = makeQuery({
      parameters: [{ name: 'limit', duckType: 'BIGINT', defaultValueText: '10' }],
    });
    vi.doMock('@/composables/useSavedQueries', () => makeMockUseSavedQueries([query]));
    const { default: SavedQueriesView } = await import('../../src/views/SavedQueriesView.vue');
    const wrapper = mount(SavedQueriesView, { global: { plugins: [createPinia()] } });
    await flushPromises();

    // Click Run
    await wrapper.find('.saved-queries-view__btn--primary').trigger('click');
    await flushPromises();

    expect(wrapper.find('.saved-queries-view__param-form').exists()).toBe(true);
  });

  it('runQuery_WithoutParams_NavigatesDirectly', async () => {
    const query = makeQuery({ parameters: [] });
    vi.doMock('@/composables/useSavedQueries', () => makeMockUseSavedQueries([query]));
    const { default: SavedQueriesView } = await import('../../src/views/SavedQueriesView.vue');
    const wrapper = mount(SavedQueriesView, { global: { plugins: [createPinia()] } });
    await flushPromises();

    // Click Run
    await wrapper.find('.saved-queries-view__btn--primary').trigger('click');
    await flushPromises();

    expect(mockRouterPush).toHaveBeenCalled();
    expect(wrapper.find('.saved-queries-view__param-form').exists()).toBe(false);
  });
});
