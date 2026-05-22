import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { GapDto } from '../../src/api/tracerApiClient';

const mockGetSession = vi.fn();
const mockGetGaps = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getSession: mockGetSession,
    getGaps: mockGetGaps,
  },
}));

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ params: {} }),
}));

vi.mock('@/components/BundleModeRequiredBanner.vue', () => ({
  default: { template: '<div class="bundle-mode-required-banner">Banner</div>', props: ['detail'] },
}));

vi.mock('@/components/GapList.vue', () => ({
  default: { template: '<div class="gap-list"/>', props: ['gaps', 'sessionId'] },
}));

function makeSession() {
  return {
    sessionId: 's1',
    scenarioId: 'sc1',
    startUtc: '2026-01-01T10:00:00Z',
    endUtc: '2026-01-01T11:00:00Z',
    status: 'completed',
    participatingNodes: [],
    eventCount: 1000,
  };
}

function makeGap(topic: string, pub: string, sub: string, missing: number): GapDto {
  return {
    topic,
    publisherNode: pub,
    subscriberNode: sub,
    previousSequence: 99,
    resumedAtSequence: 99 + missing + 1,
    missingCount: missing,
    resumedAtWallclockUtc: '2026-01-01T10:30:00Z',
  };
}

describe('GapDetectionView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockGetSession.mockReset();
    mockGetGaps.mockReset();
  });

  it('GapDetectionView_409_ShowsBanner', async () => {
    const err = Object.assign(new Error('409'), { status: 409 });
    mockGetSession.mockResolvedValue(makeSession());
    mockGetGaps.mockRejectedValue(err);

    const { default: GapDetectionView } = await import('../../src/views/GapDetectionView.vue');
    const wrapper = mount(GapDetectionView, { props: { sessionId: 's1' } });
    await flushPromises();

    expect(wrapper.find('.bundle-mode-required-banner').exists()).toBe(true);
    wrapper.unmount();
  });

  it('GapDetectionView_TupleSummary_SortedByMissingCount', async () => {
    mockGetSession.mockResolvedValue(makeSession());
    const gaps = [
      makeGap('weapons.fire', 'node-A', 'node-C', 10), // tuple A: 10
      makeGap('weapons.fire', 'node-A', 'node-B', 25), // tuple B: 25
    ];
    mockGetGaps.mockResolvedValue({ gaps, totalGaps: 2 });

    const { default: GapDetectionView } = await import('../../src/views/GapDetectionView.vue');
    const wrapper = mount(GapDetectionView, { props: { sessionId: 's1' } });
    await flushPromises();

    const rows = wrapper.findAll('.gap-detection-view__tuple-row');
    expect(rows.length).toBe(2);
    // First row should be tuple B (25 missing) > tuple A (10 missing)
    expect(rows[0].text()).toContain('25');
    expect(rows[1].text()).toContain('10');
    wrapper.unmount();
  });
});
