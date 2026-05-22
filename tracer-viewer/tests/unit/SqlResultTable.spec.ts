import { describe, it, expect, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import type { SqlExecuteResultDto } from '../../src/types/sql';

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

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
      [null, 5],
    ],
    elapsedMs: 10,
    truncated: false,
    ...overrides,
  };
}

describe('SqlResultTable', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('renders_ColumnHeaders', async () => {
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: makeResult(), sessionId: 'session1' },
    });
    const headers = wrapper.findAll('.sql-result-table__th');
    expect(headers.length).toBeGreaterThanOrEqual(2);
    expect(headers[0].text()).toContain('topic');
    expect(headers[1].text()).toContain('count');
    wrapper.unmount();
  });

  it('renders_CorrectRowCount', async () => {
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: makeResult(), sessionId: 'session1' },
    });
    const count = wrapper.find('.sql-result-table__count');
    expect(count.text()).toContain('3 rows');
    wrapper.unmount();
  });

  it('null_ValuesShowNullSymbol', async () => {
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: makeResult(), sessionId: 'session1' },
    });
    const nullSpan = wrapper.find('.sql-result-table__null');
    expect(nullSpan.exists()).toBe(true);
    expect(nullSpan.text()).toBe('∅');
    wrapper.unmount();
  });

  it('clicking_ColumnHeader_SortsAscending', async () => {
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: makeResult(), sessionId: 'session1' },
    });
    const header = wrapper.findAll('.sql-result-table__th')[1]; // 'count' column
    await header.trigger('click');
    const cells = wrapper.findAll('.sql-result-table__row');
    // Row with count=5 should come first when sorted asc
    expect(cells[0].text()).toContain('5');
    wrapper.unmount();
  });

  it('clicking_SameHeaderTwice_SortsDescending', async () => {
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: makeResult(), sessionId: 'session1' },
    });
    const header = wrapper.findAll('.sql-result-table__th')[1];
    await header.trigger('click'); // asc
    await header.trigger('click'); // desc
    const cells = wrapper.findAll('.sql-result-table__row');
    expect(cells[0].text()).toContain('100');
    wrapper.unmount();
  });

  it('pivot_Column_ShownWhenResultHasEventId', async () => {
    const resultWithEventId = makeResult({
      columns: [
        { name: 'event_id', duckType: 'VARCHAR' },
        { name: 'topic', duckType: 'VARCHAR' },
      ],
      rows: [['evt-1', 'weapons.fire']],
    });
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: resultWithEventId, sessionId: 'session1' },
    });
    // There should be an Actions header
    const headers = wrapper.findAll('.sql-result-table__th');
    const hasActions = headers.some(h => h.text().includes('Actions'));
    expect(hasActions).toBe(true);
    wrapper.unmount();
  });

  it('export_CsvButton_PresentWhenResultHasRows', async () => {
    const { default: SqlResultTable } = await import('../../src/components/SqlResultTable.vue');
    const wrapper = mount(SqlResultTable, {
      props: { result: makeResult(), sessionId: 'session1' },
    });
    const btn = wrapper.find('.sql-result-table__export-btn');
    expect(btn.exists()).toBe(true);
    wrapper.unmount();
  });
});
