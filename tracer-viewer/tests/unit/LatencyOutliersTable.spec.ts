import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import type { LatencyOutlierDto } from '../../src/api/tracerApiClient';

const mockRouterPush = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeOutlier(override?: Partial<LatencyOutlierDto>): LatencyOutlierDto {
  return {
    eventId: 'e1',
    topic: 'weapons.fire',
    publisherNode: 'node-A',
    subscriberNode: 'node-B',
    publishWallclockUtc: '2026-01-01T10:00:01.000Z',
    receiveWallclockUtc: '2026-01-01T10:00:01.150Z',
    latencyMs: 150.12,
    thresholdMs: 100.0,
    budgetSource: 'budget',
    ...override,
  };
}

describe('LatencyOutliersTable', () => {
  it('LatencyOutliersTable_RendersAllRows', async () => {
    const { default: LatencyOutliersTable } = await import('../../src/components/LatencyOutliersTable.vue');
    const outliers = [makeOutlier({ eventId: '1' }), makeOutlier({ eventId: '2' }), makeOutlier({ eventId: '3' })];
    const wrapper = mount(LatencyOutliersTable, { props: { outliers, sessionId: 's1' } });
    expect(wrapper.find('tbody').findAll('tr').length).toBe(3);
    wrapper.unmount();
  });

  it('LatencyOutliersTable_EmptyState_ShowsMessage', async () => {
    const { default: LatencyOutliersTable } = await import('../../src/components/LatencyOutliersTable.vue');
    const wrapper = mount(LatencyOutliersTable, { props: { outliers: [], sessionId: 's1' } });
    expect(wrapper.find('.latency-outliers-table__empty').exists()).toBe(true);
    expect(wrapper.find('tbody').exists()).toBe(false);
    wrapper.unmount();
  });

  it('LatencyOutliersTable_ShowInTimeline_NavigatesCorrectly', async () => {
    mockRouterPush.mockReset();
    const { default: LatencyOutliersTable } = await import('../../src/components/LatencyOutliersTable.vue');
    const T = '2026-01-01T10:00:01.000Z';
    const outlier = makeOutlier({ publishWallclockUtc: T, topic: 'T1', subscriberNode: 'node-B' });
    const wrapper = mount(LatencyOutliersTable, { props: { outliers: [outlier], sessionId: 's1' } });

    await wrapper.find('.latency-outliers-table__pivot').trigger('click');

    expect(mockRouterPush).toHaveBeenCalledOnce();
    const arg = mockRouterPush.mock.calls[0][0];
    expect(arg.name).toBe('timeline');
    expect(arg.params.sessionId).toBe('s1');
    expect(arg.query.topic).toBe('T1');
    expect(arg.query.node).toBe('node-B');
    const tMs = new Date(T).getTime();
    expect(arg.query.from).toBe(new Date(tMs - 1000).toISOString());
    expect(arg.query.to).toBe(new Date(tMs + 1000).toISOString());
    wrapper.unmount();
  });

  it('LatencyOutliersTable_BudgetSource_Displayed', async () => {
    const { default: LatencyOutliersTable } = await import('../../src/components/LatencyOutliersTable.vue');
    const outliers = [
      makeOutlier({ eventId: '1', budgetSource: 'budget' }),
      makeOutlier({ eventId: '2', budgetSource: 'top-0.1%' }),
    ];
    const wrapper = mount(LatencyOutliersTable, { props: { outliers, sessionId: 's1' } });
    const text = wrapper.text();
    expect(text).toContain('budget');
    expect(text).toContain('top-0.1%');
    wrapper.unmount();
  });
});
