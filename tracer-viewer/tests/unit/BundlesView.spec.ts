import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import BundlesView from '../../src/views/BundlesView.vue';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listBundles: vi.fn().mockResolvedValue([
      { bundleId: 'b1', label: 'Alpha', createdAtUtc: '2026-01-01T00:00:00Z' },
      { bundleId: 'b2', label: 'Beta',  createdAtUtc: '2026-01-02T00:00:00Z' },
      { bundleId: 'b3', label: null,    createdAtUtc: '2026-01-03T00:00:00Z' },
    ]),
    buildBundle: vi.fn().mockResolvedValue({ bundleId: 'new-bundle' }),
  },
}));

describe('BundlesView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('bundlesView_listsAllBundlesFromApi', async () => {
    const wrapper = mount(BundlesView, {
      global: { plugins: [createPinia()] },
    });
    await flushPromises();

    const items = wrapper.findAll('.bundles__item');
    expect(items.length).toBe(3);
  });

  it('bundlesView_downloadLink_containsBundleId', async () => {
    const wrapper = mount(BundlesView, {
      global: { plugins: [createPinia()] },
    });
    await flushPromises();

    const links = wrapper.findAll('.bundles__item a');
    expect(links[0].attributes('href')).toContain('b1');
    expect(links[1].attributes('href')).toContain('b2');
    expect(links[2].attributes('href')).toContain('b3');
  });

  it('bundlesView_buildBundleButton_callsBuildApi', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const wrapper = mount(BundlesView, {
      global: { plugins: [createPinia()] },
    });
    await flushPromises();

    const buttons = wrapper.findAll('.bundles__item button');
    await buttons[0].trigger('click');

    expect(api.buildBundle).toHaveBeenCalledWith('b1');
  });
});
