import { defineStore } from 'pinia';

export type Persona = 'engineer' | 'scenario-author' | 'operator';
const STORAGE_KEY = 'tracer:persona';
const ALL_PERSONAS: Persona[] = ['engineer', 'scenario-author', 'operator'];

export const usePersonaStore = defineStore('persona', {
  state: (): { current: Persona } => ({
    current: (localStorage.getItem(STORAGE_KEY) as Persona | null) ?? 'engineer',
  }),
  actions: {
    set(persona: Persona) {
      this.current = persona;
      localStorage.setItem(STORAGE_KEY, persona);
    },
  },
});

export { ALL_PERSONAS };
