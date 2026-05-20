import { ref, computed, onMounted } from 'vue';
import { useApi } from '@/api/useApi';

interface AppMode {
  kind: 'live' | 'bundle' | 'no-bundle';
  bundleId?: string;
  bundleLabel?: string;
}

export function useBundleMode() {
  const mode = ref<AppMode>({ kind: 'live' });

  const detect = async () => {
    const api = useApi();
    try {
      const current = await api.getCurrentBundle();
      if (current) {
        mode.value = {
          kind: 'bundle',
          bundleId: current.bundleId,
          bundleLabel: current.label,
        };
      } else {
        mode.value = { kind: 'no-bundle' };
      }
    } catch {
      // Endpoint not found (404/network error) → live observer mode
      mode.value = { kind: 'live' };
    }
  };

  onMounted(detect);

  return {
    mode: computed(() => mode.value),
    isLive: computed(() => mode.value.kind === 'live'),
    isBundle: computed(() => mode.value.kind === 'bundle'),
    isNoBundle: computed(() => mode.value.kind === 'no-bundle'),
    refresh: detect,
  };
}
