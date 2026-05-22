import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { SavedViewDto } from '../../src/api/tracerApiClient';

const mockListSavedViews = vi.fn();
const mockRecordSavedViewOpened = vi.fn();
const mockRouterPush = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listSavedViews: mockListSavedViews,
    recordSavedViewOpened: mockRecordSavedViewOpened,
  },
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/v/timeline/s1', query: {} }),
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeBookmark(override?: Partial<SavedViewDto>): SavedViewDto {
  return {
    savedViewId: 'bk-1',
    sessionId: 's1',
    kind: 'Bookmark',
    viewType: 'timeline',
    url: '/v/timeline/s1',
    label: 'My bookmark',
    persona: 'engineer',
    createdAtUtc: '2026-01-01T10:00:00Z',
    openCount: 0,
    ...override,
  };
}

describe('BookmarkBar', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockListSavedViews.mockReset();
    mockRecordSavedViewOpened.mockReset();
    mockRouterPush.mockReset();
    mockRecordSavedViewOpened.mockResolvedValue(undefined);
    mockRouterPush.mockResolvedValue(undefined);
  });

  it('BookmarkBar_Hidden_WhenNoBookmarks', async () => {
    mockListSavedViews.mockResolvedValue([]);
    const { default: BookmarkBar } = await import('../../src/components/BookmarkBar.vue');
    const wrapper = mount(BookmarkBar, { props: { sessionId: 's1', viewType: 'timeline' } });
    await flushPromises();

    expect(wrapper.find('.bookmark-bar').exists()).toBe(false);
  });

  it('BookmarkBar_RendersChips', async () => {
    const bookmarks = [
      makeBookmark({ savedViewId: 'bk-1', label: 'BM 1' }),
      makeBookmark({ savedViewId: 'bk-2', label: 'BM 2' }),
      makeBookmark({ savedViewId: 'bk-3', label: 'BM 3' }),
    ];
    mockListSavedViews.mockResolvedValue(bookmarks);
    const { default: BookmarkBar } = await import('../../src/components/BookmarkBar.vue');
    const wrapper = mount(BookmarkBar, { props: { sessionId: 's1', viewType: 'timeline' } });
    await flushPromises();

    expect(wrapper.findAll('.bookmark-bar__chip').length).toBe(3);
  });

  it('BookmarkBar_ChipClick_NavigatesAndRecords', async () => {
    const bookmark = makeBookmark({ savedViewId: 'bk1', url: '/v/timeline/s1' });
    mockListSavedViews.mockResolvedValue([bookmark]);
    const { default: BookmarkBar } = await import('../../src/components/BookmarkBar.vue');
    const wrapper = mount(BookmarkBar, { props: { sessionId: 's1', viewType: 'timeline' } });
    await flushPromises();

    await wrapper.find('.bookmark-bar__chip').trigger('click');
    await flushPromises();

    expect(mockRecordSavedViewOpened).toHaveBeenCalledWith('bk1');
    expect(mockRouterPush).toHaveBeenCalledWith('/v/timeline/s1');
  });

  it('BookmarkBar_ReloadsOnPersonaChange', async () => {
    mockListSavedViews.mockResolvedValue([]);
    const { default: BookmarkBar } = await import('../../src/components/BookmarkBar.vue');
    mount(BookmarkBar, { props: { sessionId: 's1', viewType: 'timeline' } });
    await flushPromises();

    const callsBefore = mockListSavedViews.mock.calls.length;

    // Change persona via the store
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('operator');
    await flushPromises();

    expect(mockListSavedViews.mock.calls.length).toBeGreaterThan(callsBefore);
  });
});
