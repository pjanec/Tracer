import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

describe('personaStore', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.resetModules();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('personaStore_DefaultIsEngineer_WhenLocalStorageEmpty', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    setActivePinia(createPinia());
    const store = usePersonaStore();
    expect(store.current).toBe('engineer');
  });

  it('personaStore_RestoresFromLocalStorage', async () => {
    localStorage.setItem('tracer:persona', 'operator');
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    setActivePinia(createPinia());
    const store = usePersonaStore();
    expect(store.current).toBe('operator');
  });

  it('personaStore_Set_PersistsToLocalStorage', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    setActivePinia(createPinia());
    const store = usePersonaStore();
    store.set('scenario-author');
    expect(localStorage.getItem('tracer:persona')).toBe('scenario-author');
  });

  it('personaStore_Set_UpdatesCurrentReactively', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    setActivePinia(createPinia());
    const store = usePersonaStore();
    const initial = store.current;
    store.set('operator');
    expect(store.current).toBe('operator');
    expect(store.current).not.toBe(initial);
  });
});
