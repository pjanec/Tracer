import { ref, watch } from 'vue';
import type { Ref } from 'vue';
import { useApi } from '@/api/useApi';
import type { NotableEventDto, ScenarioPhaseDto, ScenarioStateDto } from '@/api/tracerApiClient';

export function useScenarioQuery(sessionId: Ref<string>) {
  const notables = ref<NotableEventDto[]>([]);
  const phases = ref<ScenarioPhaseDto[]>([]);
  const state = ref<ScenarioStateDto | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const load = async () => {
    loading.value = true;
    error.value = null;
    try {
      const api = useApi();
      const [n, p, s] = await Promise.all([
        api.getScenarioNotables(sessionId.value, 100),
        api.getScenarioPhases(sessionId.value),
        api.getScenarioState(sessionId.value),
      ]);
      notables.value = n;
      phases.value = p;
      state.value = s;
    } catch (err: unknown) {
      error.value = err instanceof Error ? err.message : 'Failed to load scenario';
    } finally {
      loading.value = false;
    }
  };

  watch(sessionId, load, { immediate: true });

  return { notables, phases, state, loading, error, load };
}
