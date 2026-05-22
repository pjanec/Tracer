import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import FastStateColumnPicker from '../../src/components/FastStateColumnPicker.vue';
import type { FastStateColumnDto } from '../../src/api/tracerApiClient';

const COLUMNS: FastStateColumnDto[] = [
  { name: 'x', isNumeric: true },
  { name: 'y', isNumeric: true },
  { name: 'label', isNumeric: false },
];

describe('fastStateColumnPicker', () => {
  // SC-7: Only numeric columns rendered (non-numeric absent)
  it('onlyNumericColumnsRendered', () => {
    const wrapper = mount(FastStateColumnPicker, {
      props: { columns: COLUMNS, selected: [] },
    });

    const labels = wrapper.findAll('label.fast-state-column-picker__chip');
    const names = labels.map(l => l.text().trim());
    expect(names).toContain('x');
    expect(names).toContain('y');
    expect(names).not.toContain('label');
    // Hint for hidden non-numeric columns
    expect(wrapper.text()).toContain('non-numeric columns hidden');
  });

  // SC-8: Toggle unchecked → emit update:selected with column added
  it('toggleAddsToSelected_EmitsUpdateSelected', async () => {
    const wrapper = mount(FastStateColumnPicker, {
      props: { columns: COLUMNS, selected: ['x'] },
    });

    const checkboxes = wrapper.findAll('input[type="checkbox"]');
    // y is unchecked; click it to add
    await checkboxes[1].trigger('change');

    const emitted = wrapper.emitted('update:selected') as string[][];
    expect(emitted).toBeTruthy();
    expect(emitted[0][0]).toContain('x');
    expect(emitted[0][0]).toContain('y');
  });

  // SC-9: Toggle checked → emit update:selected without it
  it('toggleRemovesFromSelected_EmitsUpdateSelected', async () => {
    const wrapper = mount(FastStateColumnPicker, {
      props: { columns: COLUMNS, selected: ['x', 'y'] },
    });

    const checkboxes = wrapper.findAll('input[type="checkbox"]');
    // x is checked; click it to remove
    await checkboxes[0].trigger('change');

    const emitted = wrapper.emitted('update:selected') as string[][];
    expect(emitted).toBeTruthy();
    expect(emitted[0][0]).not.toContain('x');
    expect(emitted[0][0]).toContain('y');
  });
});
