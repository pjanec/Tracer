import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import type { GapDto } from '../../src/api/tracerApiClient';

const mockRouterPush = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockRouterPush }),
}));

function makeGap(override?: Partial<GapDto>): GapDto {
  return {
    topic: 'weapons.fire',
    publisherNode: 'node-A',
    subscriberNode: 'node-B',
    previousSequence: 99,
    resumedAtSequence: 105,
    missingCount: 5,
    resumedAtWallclockUtc: '2026-01-01T10:05:00.000Z',
    ...override,
  };
}

describe('GapList', () => {
  it('GapList_RendersGaps', async () => {
    const { default: GapList } = await import('../../src/components/GapList.vue');
    const gaps = [makeGap(), makeGap(), makeGap()];
    const wrapper = mount(GapList, { props: { gaps, sessionId: 's1' } });
    expect(wrapper.find('tbody').findAll('tr').length).toBe(3);
    wrapper.unmount();
  });

  it('GapList_EmptyState_ShowsMessage', async () => {
    const { default: GapList } = await import('../../src/components/GapList.vue');
    const wrapper = mount(GapList, { props: { gaps: [], sessionId: 's1' } });
    expect(wrapper.find('.gap-list__empty').exists()).toBe(true);
    expect(wrapper.find('table').exists()).toBe(false);
    wrapper.unmount();
  });

  it('GapList_ShowInTimeline_NavigatesCorrectly', async () => {
    mockRouterPush.mockReset();
    const { default: GapList } = await import('../../src/components/GapList.vue');
    const T = '2026-01-01T10:05:00.000Z';
    const gap = makeGap({ resumedAtWallclockUtc: T, topic: 'T1', subscriberNode: 'node-C' });
    const wrapper = mount(GapList, { props: { gaps: [gap], sessionId: 's1' } });

    await wrapper.find('.gap-list__pivot').trigger('click');

    expect(mockRouterPush).toHaveBeenCalledOnce();
    const arg = mockRouterPush.mock.calls[0][0];
    expect(arg.name).toBe('timeline');
    expect(arg.query.topic).toBe('T1');
    expect(arg.query.node).toBe('node-C');
    const tMs = new Date(T).getTime();
    expect(arg.query.from).toBe(new Date(tMs - 5000).toISOString());
    expect(arg.query.to).toBe(new Date(tMs + 1000).toISOString());
    wrapper.unmount();
  });
});
