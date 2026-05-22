import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import type { SqlSchemaDto } from '../../src/types/sql';

function makeSchema(): SqlSchemaDto {
  return {
    tables: [
      {
        name: 'events',
        columns: [
          { name: 'event_id', duckType: 'VARCHAR' },
          { name: 'topic', duckType: 'VARCHAR' },
        ],
      },
    ],
    refreshedAtUtc: '2026-01-01T00:00:00Z',
    dialectNotes: ['Hint 1'],
  };
}

describe('SchemaPanel', () => {
  it('showsLoadingWhenSchemaNIsNull', async () => {
    const { default: SchemaPanel } = await import('../../src/components/SchemaPanel.vue');
    const wrapper = mount(SchemaPanel, { props: { schema: null } });
    expect(wrapper.find('.schema-panel__empty').exists()).toBe(true);
    expect(wrapper.find('.schema-panel__empty').text()).toContain('Loading');
    wrapper.unmount();
  });

  it('showsTableNamesWhenSchemaProvided', async () => {
    const { default: SchemaPanel } = await import('../../src/components/SchemaPanel.vue');
    const wrapper = mount(SchemaPanel, { props: { schema: makeSchema() } });
    expect(wrapper.find('.schema-panel__table-name').text()).toBe('events');
    wrapper.unmount();
  });

  it('clickOnTableName_EmitsInsert', async () => {
    const { default: SchemaPanel } = await import('../../src/components/SchemaPanel.vue');
    const wrapper = mount(SchemaPanel, { props: { schema: makeSchema() } });
    await wrapper.find('.schema-panel__table-name').trigger('click');
    expect(wrapper.emitted('insert')).toBeTruthy();
    expect(wrapper.emitted('insert')![0]).toEqual(['events']);
    wrapper.unmount();
  });

  it('toggle_ExpandsColumns', async () => {
    const { default: SchemaPanel } = await import('../../src/components/SchemaPanel.vue');
    const wrapper = mount(SchemaPanel, { props: { schema: makeSchema() } });
    // Columns should not be shown initially
    expect(wrapper.find('.schema-panel__columns').exists()).toBe(false);
    // Click the expand icon
    await wrapper.find('.schema-panel__table-row').trigger('click');
    // Now columns should appear
    expect(wrapper.find('.schema-panel__columns').exists()).toBe(true);
    expect(wrapper.findAll('.schema-panel__column').length).toBe(2);
    wrapper.unmount();
  });
});
