import { describe, it, expect, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import type { LatencyPairSummaryDto, LatencyBudgetDto } from '../../src/api/tracerApiClient';

function makePair(override?: Partial<LatencyPairSummaryDto>): LatencyPairSummaryDto {
  return {
    topic: 'weapons.fire',
    publisherNode: 'node-A',
    subscriberNode: 'node-B',
    sampleCount: 200,
    p50Ms: 3,
    p99Ms: 30,
    maxMs: 200,
    ...override,
  };
}

function makeBudget(topic: string, p99BudgetMs: number): LatencyBudgetDto {
  return { topic, p99BudgetMs };
}

describe('PublisherSubscriberMatrix', () => {
  it('PublisherSubscriberMatrix_RendersAllPairs', async () => {
    const { default: PublisherSubscriberMatrix } = await import('../../src/components/PublisherSubscriberMatrix.vue');
    const pairs = Array.from({ length: 5 }, (_, i) =>
      makePair({ subscriberNode: `node-${i}` }),
    );
    const wrapper = mount(PublisherSubscriberMatrix, {
      props: { pairs, budgets: [], selectedPair: null },
    });
    expect(wrapper.findAll('li.pair-matrix__row').length).toBe(5);
    wrapper.unmount();
  });

  it('PublisherSubscriberMatrix_OverBudget_AppliesClass', async () => {
    const { default: PublisherSubscriberMatrix } = await import('../../src/components/PublisherSubscriberMatrix.vue');
    const pair = makePair({ p99Ms: 100 });
    const budget = makeBudget('weapons.fire', 50);
    const wrapper = mount(PublisherSubscriberMatrix, {
      props: { pairs: [pair], budgets: [budget], selectedPair: null },
    });
    expect(wrapper.find('li.pair-matrix__row--over-budget').exists()).toBe(true);
    wrapper.unmount();
  });

  it('PublisherSubscriberMatrix_NoBudget_NoOverBudgetClass', async () => {
    const { default: PublisherSubscriberMatrix } = await import('../../src/components/PublisherSubscriberMatrix.vue');
    const pair = makePair({ p99Ms: 100 });
    const wrapper = mount(PublisherSubscriberMatrix, {
      props: { pairs: [pair], budgets: [], selectedPair: null },
    });
    expect(wrapper.find('li.pair-matrix__row--over-budget').exists()).toBe(false);
    wrapper.unmount();
  });

  it('PublisherSubscriberMatrix_ClickRow_EmitsSelect', async () => {
    const { default: PublisherSubscriberMatrix } = await import('../../src/components/PublisherSubscriberMatrix.vue');
    const pairs = Array.from({ length: 3 }, (_, i) =>
      makePair({ subscriberNode: `node-${i}` }),
    );
    const wrapper = mount(PublisherSubscriberMatrix, {
      props: { pairs, budgets: [], selectedPair: null },
    });
    const rows = wrapper.findAll('li.pair-matrix__row');
    await rows[1].trigger('click');
    const emitted = wrapper.emitted('select') as LatencyPairSummaryDto[][];
    expect(emitted).toBeTruthy();
    expect(emitted[0][0]).toEqual(pairs[1]);
    wrapper.unmount();
  });

  it('PublisherSubscriberMatrix_SelectedPair_AppliesSelectedClass', async () => {
    const { default: PublisherSubscriberMatrix } = await import('../../src/components/PublisherSubscriberMatrix.vue');
    const pairs = Array.from({ length: 4 }, (_, i) =>
      makePair({ subscriberNode: `node-${i}` }),
    );
    const wrapper = mount(PublisherSubscriberMatrix, {
      props: { pairs, budgets: [], selectedPair: pairs[2] },
    });
    const rows = wrapper.findAll('li.pair-matrix__row');
    expect(rows[2].classes()).toContain('pair-matrix__row--selected');
    expect(rows[0].classes()).not.toContain('pair-matrix__row--selected');
    expect(rows[1].classes()).not.toContain('pair-matrix__row--selected');
    expect(rows[3].classes()).not.toContain('pair-matrix__row--selected');
    wrapper.unmount();
  });
});
