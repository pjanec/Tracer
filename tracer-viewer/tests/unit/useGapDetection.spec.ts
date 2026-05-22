import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ref, defineComponent } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import type { GapResultDto } from '../../src/api/tracerApiClient';

const mockGetGaps = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: { getGaps: mockGetGaps },
}));

function makeGapResult(): GapResultDto {
  return { gaps: [], totalGaps: 0 };
}

describe('useGapDetection', () => {
  beforeEach(() => {
    mockGetGaps.mockReset();
  });

  it('Loading_TrueWhileFetching_FalseAfter', async () => {
    let resolveApi!: (v: GapResultDto) => void;
    mockGetGaps.mockReturnValue(new Promise<GapResultDto>(r => { resolveApi = r; }));

    const { useGapDetection } = await import('../../src/composables/useGapDetection');

    let loadingRef: ReturnType<typeof ref<boolean>>;
    const wrapper = mount(
      defineComponent({
        setup() {
          const filter = ref({ from: '2026-01-01T00:00:00Z', to: '2026-01-01T01:00:00Z' });
          const { loading } = useGapDetection(filter);
          loadingRef = loading;
          return {};
        },
        template: '<div/>',
      }),
    );

    await Promise.resolve(); // let the watch fire
    expect(loadingRef!.value).toBe(true);

    resolveApi(makeGapResult());
    await flushPromises();
    expect(loadingRef!.value).toBe(false);
    wrapper.unmount();
  });
});
