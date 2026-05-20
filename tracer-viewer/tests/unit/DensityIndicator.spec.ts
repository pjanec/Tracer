import { describe, it, expect, beforeEach } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import DensityIndicator from '../../src/components/DensityIndicator.vue';

describe('DensityIndicator', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('listMode_showsReturnedAndTotalCounts', () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.queryMode = 'list';
    store.returned = 500;
    store.totalMatching = 1200;
    store.truncated = true;

    const wrapper = mount(DensityIndicator, { global: { plugins: [pinia] } });
    expect(wrapper.text()).toContain('500');
    expect(wrapper.text()).toContain('1200');
  });

  it('aggregateMode_showsBucketDuration', () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.queryMode = 'aggregate';
    store.bucketDuration = '5s';

    const wrapper = mount(DensityIndicator, { global: { plugins: [pinia] } });
    expect(wrapper.text()).toContain('Buckets of 5s');
  });
});
