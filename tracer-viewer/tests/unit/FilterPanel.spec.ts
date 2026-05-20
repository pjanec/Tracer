import { describe, it, expect, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import FilterPanel from '../../src/components/FilterPanel.vue';

describe('FilterPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('filterPanel_showsActiveFiltersAsChips', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.filter = { topics: ['weapons.fire'] };

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });
    await flushPromises();

    const chips = wrapper.findAll('.filter-chip');
    expect(chips.length).toBeGreaterThanOrEqual(1);
    expect(wrapper.text()).toContain('weapons.fire');
  });

  it('filterPanel_removeChip_removesFilterFromStore', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.filter = { topics: ['weapons.fire'] };

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });
    await flushPromises();

    const removeBtn = wrapper.find('.filter-chip__remove');
    await removeBtn.trigger('click');

    expect(store.filter.topics ?? []).not.toContain('weapons.fire');
  });

  it('filterPanel_addTopic_updatesStore', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });

    // Open the topic section
    await wrapper.find('.filter-panel__section-header').trigger('click');

    // Type a topic and click Add
    const input = wrapper.find('.filter-panel__input');
    await input.setValue('player.spawned');
    await wrapper.find('.filter-panel__add-btn').trigger('click');

    expect(store.filter.topics).toContain('player.spawned');
  });

  it('filterPanel_notablesToggle_setsNotablesOnly', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });

    const checkbox = wrapper.find('.filter-panel__notables-checkbox');
    await checkbox.setValue(true);
    await checkbox.trigger('change');

    expect(store.filter.notablesOnly).toBe(true);
  });
});
