import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { computed } from 'vue';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AppHeader from '../../src/components/AppHeader.vue';
import PersonaSwitcher from '../../src/components/PersonaSwitcher.vue';

describe('AppHeader', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
  });
  afterEach(() => localStorage.clear());

  it('AppHeader_ContainsPersonaSwitcher', () => {
    const wrapper = mount(AppHeader, {
      global: { plugins: [createPinia()] },
    });
    expect(wrapper.findComponent(PersonaSwitcher).exists()).toBe(true);
  });

  it('AppHeader_ShowsBundleBadge_WhenInBundleMode', async () => {
    vi.resetModules();
    vi.doMock('@/composables/useBundleMode', () => ({
      useBundleMode: () => ({
        isBundle: computed(() => true),
        isNoBundle: computed(() => false),
        isLive: computed(() => false),
        mode: computed(() => ({ kind: 'bundle' })),
        refresh: vi.fn(),
      }),
    }));
    vi.doMock('@/stores/sessionStore', () => ({
      useSessionStore: () => ({ current: null }),
    }));
    const { default: AppHeaderFresh } = await import('../../src/components/AppHeader.vue');
    const wrapper = mount(AppHeaderFresh, {
      global: { plugins: [createPinia()] },
    });
    const badge = wrapper.find('.app-header__badge--bundle');
    expect(badge.exists()).toBe(true);
    expect(badge.text()).toBe('Bundle Mode');
  });

  it('AppHeader_ShowsSessionId_WhenSessionLoaded', async () => {
    vi.resetModules();
    vi.doMock('@/stores/sessionStore', () => ({
      useSessionStore: () => ({ current: { sessionId: 'sess-abc' } }),
    }));
    vi.doMock('@/composables/useBundleMode', () => ({
      useBundleMode: () => ({
        isBundle: computed(() => false),
        isNoBundle: computed(() => false),
        isLive: computed(() => true),
        mode: computed(() => ({ kind: 'live' })),
        refresh: vi.fn(),
      }),
    }));
    const { default: AppHeaderFresh } = await import('../../src/components/AppHeader.vue');
    const wrapper = mount(AppHeaderFresh, {
      global: { plugins: [createPinia()] },
    });
    expect(wrapper.find('.app-header__session').text()).toContain('sess-abc');
  });

  it('AppHeader_HidesSessionId_WhenNoSession', async () => {
    vi.resetModules();
    vi.doMock('@/stores/sessionStore', () => ({
      useSessionStore: () => ({ current: null }),
    }));
    vi.doMock('@/composables/useBundleMode', () => ({
      useBundleMode: () => ({
        isBundle: computed(() => false),
        isNoBundle: computed(() => false),
        isLive: computed(() => true),
        mode: computed(() => ({ kind: 'live' })),
        refresh: vi.fn(),
      }),
    }));
    const { default: AppHeaderFresh } = await import('../../src/components/AppHeader.vue');
    const wrapper = mount(AppHeaderFresh, {
      global: { plugins: [createPinia()] },
    });
    expect(wrapper.find('.app-header__session').exists()).toBe(false);
  });
});
