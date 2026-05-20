import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createApp, ref } from 'vue';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getCurrentBundle: vi.fn(),
    openBundle: vi.fn(),
    closeBundle: vi.fn(),
  },
}));

import { api } from '@/api/tracerApiClient';

function withSetup<T>(composable: () => T): [T, () => void] {
  let result!: T;
  const app = createApp({
    setup() {
      result = composable();
      return () => null;
    },
  });
  const div = document.createElement('div');
  app.mount(div);
  return [result, () => app.unmount()];
}

describe('useBundleMode', () => {
  beforeEach(() => {
    vi.mocked(api.getCurrentBundle).mockReset();
  });

  it('reports live mode when getCurrentBundle throws', async () => {
    vi.mocked(api.getCurrentBundle).mockRejectedValue(new Error('404'));
    const { useBundleMode } = await import('@/composables/useBundleMode');
    const [result, unmount] = withSetup(() => useBundleMode());
    await flushPromises();
    expect(result.mode.value.kind).toBe('live');
    expect(result.isLive.value).toBe(true);
    unmount();
  });

  it('reports bundle mode when current bundle is present', async () => {
    vi.mocked(api.getCurrentBundle).mockResolvedValue({
      bundleId: 'abc',
      label: 'Test bundle',
      timeRange: { startUtc: '2026-01-01T00:00:00Z', endUtc: '2026-01-01T01:00:00Z' },
    });
    const { useBundleMode } = await import('@/composables/useBundleMode');
    const [result, unmount] = withSetup(() => useBundleMode());
    await flushPromises();
    expect(result.mode.value.kind).toBe('bundle');
    expect(result.mode.value.bundleId).toBe('abc');
    expect(result.isBundle.value).toBe(true);
    unmount();
  });

  it('reports no-bundle mode when current bundle is null', async () => {
    vi.mocked(api.getCurrentBundle).mockResolvedValue(null);
    const { useBundleMode } = await import('@/composables/useBundleMode');
    const [result, unmount] = withSetup(() => useBundleMode());
    await flushPromises();
    expect(result.mode.value.kind).toBe('no-bundle');
    expect(result.isNoBundle.value).toBe(true);
    unmount();
  });
});
