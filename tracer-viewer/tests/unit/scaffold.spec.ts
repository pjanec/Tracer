import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import ErrorMessage from '../../src/components/ErrorMessage.vue';

describe('Scaffold smoke test', () => {
  it('imports App without error', async () => {
    const { default: App } = await import('../../src/App.vue');
    expect(App).toBeDefined();
  });

  it('ErrorMessage renders the message prop', () => {
    const wrapper = mount(ErrorMessage, {
      global: { plugins: [createPinia()] },
      props: { message: 'Something went wrong' },
    });
    expect(wrapper.text()).toContain('Something went wrong');
  });

  it('ErrorMessage emits retry when button clicked', async () => {
    const wrapper = mount(ErrorMessage, {
      global: { plugins: [createPinia()] },
      props: { message: 'Error' },
    });
    await wrapper.find('button').trigger('click');
    expect(wrapper.emitted('retry')).toBeTruthy();
  });
});
