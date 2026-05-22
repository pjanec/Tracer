import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import type { SqlExecuteResultDto } from '../../src/types/sql';

function makeResult(overrides: Partial<SqlExecuteResultDto> = {}): SqlExecuteResultDto {
  return {
    state: 'Succeeded',
    columns: [
      { name: 'topic', duckType: 'VARCHAR' },
      { name: 'count', duckType: 'BIGINT' },
    ],
    rows: [
      ['weapons.fire', 42],
      ['player.move', 100],
    ],
    elapsedMs: 10,
    truncated: false,
    ...overrides,
  };
}

describe('SqlResultChart', () => {
  it('noData_ShowsEmptyMessage', async () => {
    const { default: SqlResultChart } = await import('../../src/components/SqlResultChart.vue');
    const result: SqlExecuteResultDto = {
      state: 'Succeeded',
      columns: [{ name: 'value', duckType: 'BIGINT' }],
      rows: [[42]],
      elapsedMs: 5,
      truncated: false,
    };
    const wrapper = mount(SqlResultChart, { props: { result } });
    expect(wrapper.find('.sql-chart__empty').exists()).toBe(true);
    wrapper.unmount();
  });

  it('stringPlusNumeric_RendersChart', async () => {
    const { default: SqlResultChart } = await import('../../src/components/SqlResultChart.vue');
    const wrapper = mount(SqlResultChart, { props: { result: makeResult() } });
    expect(wrapper.find('.sql-chart__bars').exists()).toBe(true);
    wrapper.unmount();
  });

  it('chart_ShowsCorrectNumberOfBars', async () => {
    const { default: SqlResultChart } = await import('../../src/components/SqlResultChart.vue');
    const wrapper = mount(SqlResultChart, { props: { result: makeResult() } });
    const bars = wrapper.findAll('.sql-chart__row');
    expect(bars.length).toBe(2);
    wrapper.unmount();
  });

  it('onlyNumericColumn_ShowsEmptyMessage', async () => {
    const { default: SqlResultChart } = await import('../../src/components/SqlResultChart.vue');
    const result: SqlExecuteResultDto = {
      state: 'Succeeded',
      columns: [{ name: 'count', duckType: 'BIGINT' }],
      rows: [[100], [200]],
      elapsedMs: 5,
      truncated: false,
    };
    const wrapper = mount(SqlResultChart, { props: { result } });
    expect(wrapper.find('.sql-chart__empty').exists()).toBe(true);
    wrapper.unmount();
  });

  it('top30Limit_Applied', async () => {
    const { default: SqlResultChart } = await import('../../src/components/SqlResultChart.vue');
    const rows: (string | number)[][] = [];
    for (let i = 0; i < 50; i++) {
      rows.push([`item_${i}`, i]);
    }
    const result: SqlExecuteResultDto = {
      state: 'Succeeded',
      columns: [
        { name: 'label', duckType: 'VARCHAR' },
        { name: 'val', duckType: 'BIGINT' },
      ],
      rows,
      elapsedMs: 5,
      truncated: false,
    };
    const wrapper = mount(SqlResultChart, { props: { result } });
    const bars = wrapper.findAll('.sql-chart__row');
    expect(bars.length).toBe(30);
    wrapper.unmount();
  });
});
