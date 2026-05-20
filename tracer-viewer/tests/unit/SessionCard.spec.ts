import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

const mockBuildBundle = vi.fn();
vi.mock('@/api/tracerApiClient', () => ({
  api: { buildBundle: mockBuildBundle },
}));

describe('SessionCard', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockBuildBundle.mockReset();
  });

  it('buildBundle_showsProgressThenDownloadLink', async () => {
    // First call: simulate in-progress (resolves after we check intermediate state)
    let resolveBuild!: (value: { bundleId: string }) => void;
    mockBuildBundle.mockReturnValue(
      new Promise<{ bundleId: string }>((resolve) => { resolveBuild = resolve; }),
    );

    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const wrapper = mount(SessionCard, {
      props: { sessionId: 'sess-1' },
      global: { plugins: [createPinia()] },
    });

    // Click "Build bundle"
    await wrapper.find('.session-card__build-btn').trigger('click');

    // Should show progress indicator
    expect(wrapper.find('.session-card__progress').exists()).toBe(true);
    expect(wrapper.find('.session-card__download').exists()).toBe(false);

    // Now resolve the build
    resolveBuild({ bundleId: 'new-bundle-abc' });
    await flushPromises();

    // Should now show download link
    expect(wrapper.find('.session-card__download').exists()).toBe(true);
    expect(wrapper.find('.session-card__download').attributes('href'))
      .toContain('new-bundle-abc');
  });
});
