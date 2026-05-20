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
});
