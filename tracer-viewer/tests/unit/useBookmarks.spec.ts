import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import type { SavedViewDto } from '../../src/api/tracerApiClient';

const mockCreateSavedView = vi.fn();
const mockListSavedViews = vi.fn();
const mockDeleteSavedView = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    createSavedView: mockCreateSavedView,
    listSavedViews: mockListSavedViews,
    deleteSavedView: mockDeleteSavedView,
  },
}));

vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/v/timeline/s1', query: {} }),
  useRouter: () => ({ push: vi.fn() }),
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

describe('useBookmarks', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockCreateSavedView.mockReset();
    mockListSavedViews.mockReset();
    mockDeleteSavedView.mockReset();
    mockCreateSavedView.mockResolvedValue(makeBookmark());
    mockListSavedViews.mockResolvedValue([makeBookmark()]);
    mockDeleteSavedView.mockResolvedValue(undefined);
  });

  it('useBookmarks_BookmarkCurrentUrl_CallsAPI', async () => {
    const { useBookmarks } = await import('../../src/composables/useBookmarks');
    const { bookmarkCurrentUrl } = useBookmarks();
    await bookmarkCurrentUrl('s1', 'timeline');

    expect(mockCreateSavedView).toHaveBeenCalledOnce();
    const arg = mockCreateSavedView.mock.calls[0][0];
    expect(arg.kind).toBe('Bookmark');
    expect(arg.viewType).toBe('timeline');
    expect(arg.label).toBeTruthy();
    expect(arg.label.length).toBeGreaterThan(0);
  });

  it('useBookmarks_ListBookmarks_ReturnsOnlyBookmarks', async () => {
    const { useBookmarks } = await import('../../src/composables/useBookmarks');
    const { listBookmarks } = useBookmarks();
    await listBookmarks('s1');

    expect(mockListSavedViews).toHaveBeenCalledOnce();
    const arg = mockListSavedViews.mock.calls[0][0];
    expect(arg.kind).toBe('Bookmark');
  });

  it('useBookmarks_RemoveBookmark_CallsDelete', async () => {
    const { useBookmarks } = await import('../../src/composables/useBookmarks');
    const { removeBookmark } = useBookmarks();
    await removeBookmark('id-1');

    expect(mockDeleteSavedView).toHaveBeenCalledOnce();
    expect(mockDeleteSavedView).toHaveBeenCalledWith('id-1');
  });

  it('useBookmarks_LimitTen', async () => {
    const { useBookmarks } = await import('../../src/composables/useBookmarks');
    const { listBookmarks } = useBookmarks();
    await listBookmarks('s1');

    const arg = mockListSavedViews.mock.calls[0][0];
    expect(arg.limit).toBe(10);
  });
});
