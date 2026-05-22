import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { SavedViewDto } from '../../src/api/tracerApiClient';

const mockListSavedViews = vi.fn();
const mockDeleteSavedView = vi.fn();
const mockRecordSavedViewOpened = vi.fn();
const mockRouterPush = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listSavedViews: mockListSavedViews,
    deleteSavedView: mockDeleteSavedView,
    recordSavedViewOpened: mockRecordSavedViewOpened,
  },
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { sessionId: 'sess-1' } }),
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeSavedView(override?: Partial<SavedViewDto>): SavedViewDto {
  return {
    savedViewId: `sv-${Math.random()}`,
    sessionId: 'sess-1',
    kind: 'SavedView',
    viewType: 'timeline',
    url: '/v/timeline/sess-1',
    label: 'Test View',
    persona: 'engineer',
    createdAtUtc: '2026-01-01T10:00:00Z',
    openCount: 0,
    ...override,
  };
}

describe('SavedViewsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockListSavedViews.mockReset();
    mockDeleteSavedView.mockReset();
    mockRecordSavedViewOpened.mockReset();
    mockRouterPush.mockReset();
    mockDeleteSavedView.mockResolvedValue(undefined);
    mockRecordSavedViewOpened.mockResolvedValue(undefined);
  });

  it('SavedViewsView_RendersViewsGroupedByType', async () => {
    const views = [
      makeSavedView({ savedViewId: 'sv-1', viewType: 'timeline', label: 'TL View 1' }),
      makeSavedView({ savedViewId: 'sv-2', viewType: 'timeline', label: 'TL View 2' }),
      makeSavedView({ savedViewId: 'sv-3', viewType: 'scenario', label: 'Sc View 1' }),
    ];
    mockListSavedViews.mockResolvedValue(views);
    const { default: SavedViewsView } = await import('../../src/views/SavedViewsView.vue');
    const wrapper = mount(SavedViewsView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    const headings = wrapper.findAll('.saved-views-view__group-heading');
    expect(headings.length).toBe(2);
    const headingTexts = headings.map(h => h.text());
    expect(headingTexts).toContain('timeline');
    expect(headingTexts).toContain('scenario');
  });

  it('SavedViewsView_EmptyState_Shown', async () => {
    mockListSavedViews.mockResolvedValue([]);
    const { default: SavedViewsView } = await import('../../src/views/SavedViewsView.vue');
    const wrapper = mount(SavedViewsView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    expect(wrapper.find('.saved-views-view__empty').exists()).toBe(true);
  });

  it('SavedViewsView_DeleteView_CallsAPIAndReloads', async () => {
    const view = makeSavedView({ savedViewId: 'sv-del' });
    mockListSavedViews.mockResolvedValue([view]);
    vi.stubGlobal('confirm', () => true);
    const { default: SavedViewsView } = await import('../../src/views/SavedViewsView.vue');
    const wrapper = mount(SavedViewsView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    await wrapper.find('.saved-views-view__delete').trigger('click');
    await flushPromises();

    expect(mockDeleteSavedView).toHaveBeenCalledWith('sv-del');
    expect(mockListSavedViews.mock.calls.length).toBeGreaterThanOrEqual(2);
    vi.unstubAllGlobals();
  });

  it('SavedViewsView_PersonaFilterChange_Reloads', async () => {
    mockListSavedViews.mockResolvedValue([]);
    const { default: SavedViewsView } = await import('../../src/views/SavedViewsView.vue');
    const wrapper = mount(SavedViewsView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    const callsBefore = mockListSavedViews.mock.calls.length;
    await wrapper.find('.saved-views-view__persona-select').setValue('engineer');
    await flushPromises();

    expect(mockListSavedViews.mock.calls.length).toBeGreaterThan(callsBefore);
    const lastCall = mockListSavedViews.mock.calls[mockListSavedViews.mock.calls.length - 1][0];
    expect(lastCall.persona).toBe('engineer');
  });

  it('SavedViewsView_OpenView_NavigatesAndRecordsOpen', async () => {
    const view = makeSavedView({ savedViewId: 'sv-open', url: '/v/timeline/s1' });
    mockListSavedViews.mockResolvedValue([view]);
    const { default: SavedViewsView } = await import('../../src/views/SavedViewsView.vue');
    const wrapper = mount(SavedViewsView, { props: { sessionId: 'sess-1' } });
    await flushPromises();

    await wrapper.find('.saved-views-view__open').trigger('click');
    await flushPromises();

    expect(mockRecordSavedViewOpened).toHaveBeenCalledWith('sv-open');
  });
});
