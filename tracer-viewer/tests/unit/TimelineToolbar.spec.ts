import { describe, it, expect, beforeEach } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import TimelineToolbar from '../../src/components/TimelineToolbar.vue';

describe('TimelineToolbar', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('followToggle_disabledWhenSessionNotLive', () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.isLiveSession = false;

    const wrapper = mount(TimelineToolbar, { global: { plugins: [pinia] } });
    expect(wrapper.find('.toolbar__follow').attributes('disabled')).toBeDefined();
  });

  it('zoomPreset_5m_setsViewportTo5MinuteSpan', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T12:00:00Z'); // 2h span initially

    const wrapper = mount(TimelineToolbar, { global: { plugins: [pinia] } });
    // Click the first button (5m zoom)
    await wrapper.find('button[data-zoom="5m"]').trigger('click');

    const spanMs = store.viewport.to.getTime() - store.viewport.from.getTime();
    expect(spanMs).toBeLessThanOrEqual(5 * 60 * 1000 + 1); // ≤5min (+1ms rounding)
  });

  it('followToggle_EnablesFollowAndSnapsToLiveEdge', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.isLiveSession = true;
    store.viewport.followLive = false;
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:10:00Z'); // 10 min span

    const wrapper = mount(TimelineToolbar, { global: { plugins: [pinia] } });

    // The button should currently say "Follow" (not following)
    expect(wrapper.find('.toolbar__follow').text()).toBe('Follow');

    const beforeClick = Date.now();
    await wrapper.find('.toolbar__follow').trigger('click');

    // Follow mode should be enabled
    expect(store.viewport.followLive).toBe(true);

    // Viewport.to should be within 5s of now
    expect(store.viewport.to.getTime()).toBeGreaterThanOrEqual(beforeClick - 100); // allow 100ms margin
    expect(store.viewport.to.getTime()).toBeLessThanOrEqual(Date.now() + 5_000);

    // Span should be preserved (10 min = 600_000 ms)
    const span = store.viewport.to.getTime() - store.viewport.from.getTime();
    expect(Math.abs(span - 10 * 60 * 1000)).toBeLessThan(1_000); // within 1s

    // Button label should change to "Following live"
    await wrapper.vm.$nextTick();
    expect(wrapper.find('.toolbar__follow').text()).toBe('Following live');
  });
});
