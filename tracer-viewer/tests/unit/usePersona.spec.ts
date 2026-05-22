import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

describe('usePersona', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
    vi.resetModules();
  });

  afterEach(() => localStorage.clear());

  it('usePersona_Persona_MatchesStore', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const { usePersona } = await import('../../src/composables/usePersona');
    const store = usePersonaStore();
    store.set('operator');
    const { persona } = usePersona();
    expect(persona.value).toBe('operator');
  });

  it('usePersona_AllPersonas_HasThreeItems', async () => {
    const { usePersona } = await import('../../src/composables/usePersona');
    const { allPersonas } = usePersona();
    expect(allPersonas).toHaveLength(3);
    expect(allPersonas).toContain('engineer');
    expect(allPersonas).toContain('scenario-author');
    expect(allPersonas).toContain('operator');
  });
});
