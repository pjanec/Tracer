import { computed } from 'vue';
import { usePersonaStore, type Persona, ALL_PERSONAS } from '@/stores/personaStore';

export function usePersona() {
  const store = usePersonaStore();
  const persona = computed(() => store.current);

  function setPersona(p: Persona) {
    store.set(p);
  }

  return { persona, setPersona, allPersonas: ALL_PERSONAS };
}
