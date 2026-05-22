import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import type { SqlExecuteResultDto } from '../../src/types/sql';

const mockExecuteSql = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: { executeSql: mockExecuteSql },
}));

function makeSuccessResult(): SqlExecuteResultDto {
  return {
    state: 'Succeeded',
    columns: [{ name: 'id', duckType: 'INTEGER' }],
    rows: [[1], [2]],
    elapsedMs: 42,
    truncated: false,
  };
}

describe('useSqlExecution', () => {
  beforeEach(() => {
    mockExecuteSql.mockReset();
  });

  it('run_SetsLoadingTrueThenFalse', async () => {
    let resolvePromise!: (v: SqlExecuteResultDto) => void;
    mockExecuteSql.mockReturnValue(new Promise(r => { resolvePromise = r; }));

    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { loading, run } = useSqlExecution();

    const runPromise = run('SELECT 1');
    expect(loading.value).toBe(true);
    resolvePromise(makeSuccessResult());
    await runPromise;
    expect(loading.value).toBe(false);
  });

  it('run_SuccessfulResult_SetsResultValue', async () => {
    mockExecuteSql.mockResolvedValue(makeSuccessResult());
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { result, run } = useSqlExecution();

    await run('SELECT 1');
    await flushPromises();
    expect(result.value?.state).toBe('Succeeded');
  });

  it('run_Error_SetsErrorValue', async () => {
    mockExecuteSql.mockRejectedValue(new Error('network error'));
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { error, run } = useSqlExecution();

    await run('SELECT 1');
    await flushPromises();
    expect(error.value).toBe('network error');
  });

  it('run_ClearsPreviousResultBeforeNewFetch', async () => {
    mockExecuteSql.mockResolvedValue(makeSuccessResult());
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { result, run } = useSqlExecution();

    await run('SELECT 1');
    await flushPromises();
    expect(result.value).not.toBeNull();

    mockExecuteSql.mockReturnValue(new Promise(() => {})); // never resolves
    void run('SELECT 2');
    expect(result.value).toBeNull();
  });

  it('run_RejectedState_IsSetWithoutThrow', async () => {
    const rejected: SqlExecuteResultDto = {
      state: 'Rejected',
      errorMessage: 'Not allowed',
      elapsedMs: 5,
      truncated: false,
    };
    mockExecuteSql.mockResolvedValue(rejected);
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { result, run } = useSqlExecution();

    await run('DROP TABLE events');
    await flushPromises();
    expect(result.value?.state).toBe('Rejected');
    expect(result.value?.errorMessage).toBe('Not allowed');
  });

  it('run_TimeoutState_IsSet', async () => {
    const timeout: SqlExecuteResultDto = {
      state: 'Timeout',
      elapsedMs: 30000,
      truncated: false,
    };
    mockExecuteSql.mockResolvedValue(timeout);
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { result, run } = useSqlExecution();

    await run('SELECT slow_query()');
    await flushPromises();
    expect(result.value?.state).toBe('Timeout');
  });

  it('loading_IsFalseAfterError', async () => {
    mockExecuteSql.mockRejectedValue(new Error('fail'));
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { loading, run } = useSqlExecution();

    await run('SELECT 1');
    await flushPromises();
    expect(loading.value).toBe(false);
  });

  it('cancel_AbortsInFlightRequest', async () => {
    let capturedSignal: AbortSignal | undefined;
    mockExecuteSql.mockImplementation((_req: unknown, signal?: AbortSignal) => {
      capturedSignal = signal;
      return new Promise(() => {}); // never resolves
    });
    const { useSqlExecution } = await import('../../src/composables/useSqlExecution');
    const { loading, run, cancel } = useSqlExecution();

    void run('SELECT 1');
    expect(loading.value).toBe(true);
    cancel();
    expect(capturedSignal?.aborted).toBe(true);
    expect(loading.value).toBe(false);
  });
});
