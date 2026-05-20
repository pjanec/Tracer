// src/composables/useCausalTreeLayout.ts
import { ref, watchEffect } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { layout, type LayoutResult, type LayoutConfig } from '@/rendering/causalTreeLayout';

const DEFAULT_CONFIG: LayoutConfig = {
  nodeRadiusPx: 14,
  hSpacingPx: 40,
  vSpacingPx: 80,
  paddingPx: 40,
};

export function useCausalTreeLayout(config: LayoutConfig = DEFAULT_CONFIG) {
  const store = useCausalTreeStore();
  const layoutResult = ref<LayoutResult | null>(null);

  watchEffect(() => {
    if (store.tree) {
      layoutResult.value = layout(store.tree, config);
    } else {
      layoutResult.value = null;
    }
  });

  return { layoutResult };
}
