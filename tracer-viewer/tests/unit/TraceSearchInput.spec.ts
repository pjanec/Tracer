import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createRouter, createMemoryHistory } from 'vue-router';
import TraceSearchInput from '@/components/TraceSearchInput.vue';

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div/>' } }],
  });
}

describe('TraceSearchInput', () => {
  let router: ReturnType<typeof makeRouter>;

  beforeEach(() => {
    router = makeRouter();
  });

  it('submit_WithValidEventHex_NavigatesToCausalByEventRoute', async () => {
    const pushSpy = vi.spyOn(router, 'push').mockResolvedValue(undefined as never);

    const wrapper = mount(TraceSearchInput, {
      global: { plugins: [router] },
    });

    // Set kind to 'event' (it's the default, but be explicit)
    await wrapper.find('select').setValue('event');
    await wrapper.find('input').setValue('aabbccddeeff0011');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(pushSpy).toHaveBeenCalledWith({
      name: 'causal-by-event',
      params: { eventId: 'aabbccddeeff0011' },
    });
  });

  it('submit_WithNonHexValue_DisplaysValidationError', async () => {
    const pushSpy = vi.spyOn(router, 'push').mockResolvedValue(undefined as never);

    const wrapper = mount(TraceSearchInput, {
      global: { plugins: [router] },
    });

    await wrapper.find('input').setValue('zzzzzzzzzzzzzzzz');
    await wrapper.find('form').trigger('submit');

    expect(wrapper.find('.trace-search__error').exists()).toBe(true);
    expect(pushSpy).not.toHaveBeenCalled();
  });
});
