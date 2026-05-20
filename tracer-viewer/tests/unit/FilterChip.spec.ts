import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import FilterChip from '../../src/components/FilterChip.vue';

describe('FilterChip', () => {
  it('filterChip_rendersLabelAndValue', () => {
    const wrapper = mount(FilterChip, { props: { label: 'topic', value: 'weapons.fire' } });
    expect(wrapper.find('.filter-chip__label').text()).toBe('topic');
    expect(wrapper.find('.filter-chip__value').text()).toBe('weapons.fire');
  });

  it('filterChip_removeButton_emitsRemoveEvent', async () => {
    const wrapper = mount(FilterChip, { props: { label: 'topic', value: 'weapons.fire' } });
    await wrapper.find('.filter-chip__remove').trigger('click');
    expect(wrapper.emitted('remove')).toBeTruthy();
  });
});
