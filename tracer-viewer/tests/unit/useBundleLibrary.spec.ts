import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import type { BundleLibraryEntryDto } from '../../src/types/bundle';

const mockListBundleLibrary = vi.fn();
const mockUpdateBundleMetadata = vi.fn();
const mockDeleteBundle = vi.fn();
const mockRecordBundleOpened = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listBundleLibrary: mockListBundleLibrary,
    updateBundleMetadata: mockUpdateBundleMetadata,
    deleteBundle: mockDeleteBundle,
    recordBundleOpened: mockRecordBundleOpened,
  },
}));

function makeBundle(id: string, opts: Partial<BundleLibraryEntryDto> = {}): BundleLibraryEntryDto {
  return {
    bundleId: id,
    sessionId: `session-${id}`,
    tags: [],
    isArchived: false,
    sessionStartUtc: '2026-01-01T10:00:00Z',
    sessionEndUtc: '2026-01-01T11:00:00Z',
    builtAtUtc: '2026-01-01T12:00:00Z',
    sizeBytes: 1024 * 1024,
    ...opts,
  };
}

describe('useBundleLibrary', () => {
  beforeEach(() => {
    mockListBundleLibrary.mockReset();
    mockUpdateBundleMetadata.mockReset();
    mockDeleteBundle.mockReset();
    mockRecordBundleOpened.mockReset();
  });

  it('load_PopulatesBundles', async () => {
    mockListBundleLibrary.mockResolvedValue({ entries: [makeBundle('b1'), makeBundle('b2')] });
    const { useBundleLibrary } = await import('../../src/composables/useBundleLibrary');
    const { bundles, load } = useBundleLibrary();

    await load();
    await flushPromises();
    expect(bundles.value.length).toBe(2);
  });

  it('load_PassesShowArchivedParamToApi', async () => {
    mockListBundleLibrary.mockResolvedValue({ entries: [] });
    const { useBundleLibrary } = await import('../../src/composables/useBundleLibrary');
    const { load } = useBundleLibrary();

    await load({ showArchived: false });
    await flushPromises();
    expect(mockListBundleLibrary).toHaveBeenCalledWith({ showArchived: false });
  });

  it('updateMetadata_CallsApiThenReloads', async () => {
    mockListBundleLibrary.mockResolvedValue({ entries: [makeBundle('b1')] });
    mockUpdateBundleMetadata.mockResolvedValue(undefined);

    const { useBundleLibrary } = await import('../../src/composables/useBundleLibrary');
    const { load, updateMetadata } = useBundleLibrary();

    await load();
    await flushPromises();
    await updateMetadata('b1', { label: 'New label' });
    await flushPromises();

    expect(mockUpdateBundleMetadata).toHaveBeenCalledWith('b1', { label: 'New label' });
    expect(mockListBundleLibrary).toHaveBeenCalledTimes(2); // initial load + reload after update
  });

  it('deleteBundle_RemovesFromList', async () => {
    mockListBundleLibrary.mockResolvedValue({ entries: [makeBundle('b1'), makeBundle('b2')] });
    mockDeleteBundle.mockResolvedValue(undefined);

    const { useBundleLibrary } = await import('../../src/composables/useBundleLibrary');
    const { bundles, load, deleteBundle } = useBundleLibrary();

    await load();
    await flushPromises();
    await deleteBundle('b1');
    await flushPromises();

    expect(bundles.value.length).toBe(1);
    expect(bundles.value[0].bundleId).toBe('b2');
  });

  it('recordOpened_CallsApi', async () => {
    mockRecordBundleOpened.mockResolvedValue(undefined);
    const { useBundleLibrary } = await import('../../src/composables/useBundleLibrary');
    const { recordOpened } = useBundleLibrary();

    await recordOpened('b1');
    expect(mockRecordBundleOpened).toHaveBeenCalledWith('b1');
  });

  it('load_Error_SetsErrorValue', async () => {
    mockListBundleLibrary.mockRejectedValue(new Error('fetch failed'));
    const { useBundleLibrary } = await import('../../src/composables/useBundleLibrary');
    const { error, load } = useBundleLibrary();

    await load();
    await flushPromises();
    expect(error.value).toBe('fetch failed');
  });
});
