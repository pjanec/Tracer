import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import type { SavedQueryDto } from '../../src/types/savedQuery';

const mockListSavedQueries = vi.fn();
const mockCreateSavedQuery = vi.fn();
const mockDeleteSavedQuery = vi.fn();
const mockToggleSavedQueryFavorite = vi.fn();
const mockCloneSavedQuery = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listSavedQueries: mockListSavedQueries,
    createSavedQuery: mockCreateSavedQuery,
    deleteSavedQuery: mockDeleteSavedQuery,
    toggleSavedQueryFavorite: mockToggleSavedQueryFavorite,
    cloneSavedQuery: mockCloneSavedQuery,
  },
}));

function makeQuery(id: string, label: string, opts: Partial<SavedQueryDto> = {}): SavedQueryDto {
  return {
    savedQueryId: id,
    label,
    sql: `SELECT * FROM events WHERE id='${id}'`,
    parameters: [],
    tags: [],
    isBuiltIn: false,
    isFavorite: false,
    createdAtUtc: '2026-01-01T00:00:00Z',
    runCount: 0,
    ...opts,
  };
}

describe('useSavedQueries', () => {
  beforeEach(() => {
    mockListSavedQueries.mockReset();
    mockCreateSavedQuery.mockReset();
    mockDeleteSavedQuery.mockReset();
    mockToggleSavedQueryFavorite.mockReset();
    mockCloneSavedQuery.mockReset();
  });

  it('load_PopulatesQueries', async () => {
    mockListSavedQueries.mockResolvedValue([makeQuery('q1', 'Query 1'), makeQuery('q2', 'Query 2')]);
    const { useSavedQueries } = await import('../../src/composables/useSavedQueries');
    const { queries, load } = useSavedQueries();

    await load();
    await flushPromises();
    expect(queries.value.length).toBe(2);
    expect(queries.value[0].label).toBe('Query 1');
  });

  it('create_AppendsNewQuery', async () => {
    mockListSavedQueries.mockResolvedValue([makeQuery('q1', 'Existing')]);
    const newQuery = makeQuery('q2', 'New Query');
    mockCreateSavedQuery.mockResolvedValue(newQuery);

    const { useSavedQueries } = await import('../../src/composables/useSavedQueries');
    const { queries, load, create } = useSavedQueries();

    await load();
    await flushPromises();
    await create({ label: 'New Query', sql: 'SELECT 1' });
    await flushPromises();

    expect(queries.value.length).toBe(2);
    expect(queries.value[1].savedQueryId).toBe('q2');
  });

  it('remove_RemovesFromList', async () => {
    mockListSavedQueries.mockResolvedValue([makeQuery('q1', 'Q1'), makeQuery('q2', 'Q2')]);
    mockDeleteSavedQuery.mockResolvedValue(undefined);

    const { useSavedQueries } = await import('../../src/composables/useSavedQueries');
    const { queries, load, remove } = useSavedQueries();

    await load();
    await flushPromises();
    await remove('q1');
    await flushPromises();

    expect(queries.value.length).toBe(1);
    expect(queries.value[0].savedQueryId).toBe('q2');
  });

  it('toggleFavorite_UpdatesMatchingQuery', async () => {
    const q = makeQuery('q1', 'Q1', { isFavorite: false });
    mockListSavedQueries.mockResolvedValue([q]);
    const updated = { ...q, isFavorite: true };
    mockToggleSavedQueryFavorite.mockResolvedValue(updated);

    const { useSavedQueries } = await import('../../src/composables/useSavedQueries');
    const { queries, load, toggleFavorite } = useSavedQueries();

    await load();
    await flushPromises();
    await toggleFavorite('q1');
    await flushPromises();

    expect(queries.value[0].isFavorite).toBe(true);
  });

  it('clone_AppendsClonedQuery', async () => {
    mockListSavedQueries.mockResolvedValue([makeQuery('q1', 'Q1')]);
    const cloned = makeQuery('q1-clone', 'Q1 (copy)');
    mockCloneSavedQuery.mockResolvedValue(cloned);

    const { useSavedQueries } = await import('../../src/composables/useSavedQueries');
    const { queries, load, clone } = useSavedQueries();

    await load();
    await flushPromises();
    await clone('q1', 'Q1 (copy)');
    await flushPromises();

    expect(queries.value.length).toBe(2);
    expect(queries.value[1].savedQueryId).toBe('q1-clone');
  });

  it('load_WithBuiltInFilter_CallsApiWithParam', async () => {
    mockListSavedQueries.mockResolvedValue([]);
    const { useSavedQueries } = await import('../../src/composables/useSavedQueries');
    const { load } = useSavedQueries();

    await load({ builtIn: true });
    await flushPromises();

    expect(mockListSavedQueries).toHaveBeenCalledWith({ builtIn: true });
  });
});
