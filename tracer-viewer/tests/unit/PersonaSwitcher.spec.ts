import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import PersonaSwitcher from '../../src/components/PersonaSwitcher.vue';

describe('PersonaSwitcher', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    localStorage.clear();
    vi.resetModules();
    pinia = createPinia();
    setActivePinia(pinia);
  });

  afterEach(() => localStorage.clear());

  it('PersonaSwitcher_AllThreeButtons_Render', () => {
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    const buttons = wrapper.findAll('.persona-switcher__btn');
    expect(buttons).toHaveLength(3);
    const texts = buttons.map(b => b.text());
    expect(texts).toContain('Engineer');
    expect(texts).toContain('Scenario Author');
    expect(texts).toContain('Operator');
  });

  it('PersonaSwitcher_ActiveButton_MatchesCurrent', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('engineer');
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    const active = wrapper.find('.persona-switcher__btn--active');
    expect(active.exists()).toBe(true);
    expect(active.text()).toBe('Engineer');
  });

  it('PersonaSwitcher_Click_SetsPersona', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    const scenarioBtn = wrapper.findAll('.persona-switcher__btn').find(b => b.text() === 'Scenario Author');
    await scenarioBtn!.trigger('click');
    expect(store.current).toBe('scenario-author');
  });

  it('PersonaSwitcher_ActiveButtonChanges_OnStoreUpdate', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('engineer');
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    store.set('operator');
    await wrapper.vm.$nextTick();
    const active = wrapper.find('.persona-switcher__btn--active');
    expect(active.text()).toBe('Operator');
  });
});
