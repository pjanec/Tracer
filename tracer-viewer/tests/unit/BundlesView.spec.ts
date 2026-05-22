import { describe, it, expect, beforeEach, vi } from 'vitest';
import { shallowRef } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { BundleListEntryDto } from '../../src/stores/bundleStore';

const mockListBundles = vi.fn().mockResolvedValue([]);

vi.mock('@/composables/useBundleMode', () => ({
  useBundleMode: vi.fn(() => ({
    isLive:     shallowRef(true),
    isBundle:   shallowRef(false),
    isNoBundle: shallowRef(false),
    mode:       shallowRef({ kind: 'live' }),
    refresh:    vi.fn(),
  })),
}));

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listBundles: mockListBundles,
    buildBundle: vi.fn(),
  },
}));

async function mountView(pinia: ReturnType<typeof createPinia>) {
  const { default: BundlesView } = await import('../../src/views/BundlesView.vue');
  const wrapper = mount(BundlesView, { global: { plugins: [pinia] } });
  return wrapper;
}

describe('BundlesView', () => {
  beforeEach(() => {
    mockListBundles.mockReset();
    mockListBundles.mockResolvedValue([]);
    setActivePinia(createPinia());
    vi.clearAllMocks();
    // Restore default mock after clearAllMocks
    mockListBundles.mockResolvedValue([]);
  });

  it('renders_bundle_list_from_store', async () => {
    const entries: BundleListEntryDto[] = [
      { bundleId: 'b1', label: 'Alpha', createdAtUtc: '2026-01-01T00:00:00Z' },
      { bundleId: 'b2', label: 'Beta',  createdAtUtc: '2026-01-02T00:00:00Z' },
    ];
    mockListBundles.mockResolvedValueOnce(entries);

    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = await mountView(pinia);
    await flushPromises();

    const items = wrapper.findAll('.bundles__item');
    expect(items.length).toBe(2);
    expect(wrapper.text()).toContain('Alpha');
    expect(wrapper.text()).toContain('Beta');
  });

  it('shows_empty_state_when_no_bundles', async () => {
    mockListBundles.mockResolvedValueOnce([]);

    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = await mountView(pinia);
    await flushPromises();

    expect(wrapper.text()).toContain('No bundles built yet');
    expect(wrapper.findAll('.bundles__item').length).toBe(0);
  });

  it('shows_error_state_on_fetch_failure', async () => {
    mockListBundles.mockRejectedValueOnce(new Error('Connection refused'));

    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = await mountView(pinia);
    await flushPromises();

    expect(wrapper.text()).toContain('Connection refused');
    expect(wrapper.findAll('.bundles__item').length).toBe(0);
  });

  it('shows_offline_hint_in_bundle_mode', async () => {
    // Re-mock useBundleMode to return isLive = false
    const { useBundleMode } = await import('@/composables/useBundleMode');
    (useBundleMode as ReturnType<typeof vi.fn>).mockReturnValue({
      isLive:     shallowRef(false),
      isBundle:   shallowRef(true),
      isNoBundle: shallowRef(false),
      mode:       shallowRef({ kind: 'bundle', bundleId: 'b1' }),
      refresh:    vi.fn(),
    });

    const bundles: BundleListEntryDto[] = [
      { bundleId: 'b1', label: 'Alpha', createdAtUtc: '2026-01-01Z' },
    ];
    mockListBundles.mockResolvedValueOnce(bundles);

    const pinia = createPinia();
    setActivePinia(pinia);

    const wrapper = await mountView(pinia);
    await flushPromises();

    expect(wrapper.text()).toContain('To open a different bundle, return to the Open Bundle screen.');
    // In offline mode, no download links shown
    expect(wrapper.findAll('.bundles__item-download').length).toBe(0);
  });
});
