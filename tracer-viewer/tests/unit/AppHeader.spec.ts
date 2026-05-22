import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AppHeader from '../../src/components/AppHeader.vue';
import PersonaSwitcher from '../../src/components/PersonaSwitcher.vue';

describe('AppHeader', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
  });
  afterEach(() => localStorage.clear());

  it('AppHeader_ContainsPersonaSwitcher', () => {
    const wrapper = mount(AppHeader, {
      global: { plugins: [createPinia()] },
    });
    expect(wrapper.findComponent(PersonaSwitcher).exists()).toBe(true);
  });
});
