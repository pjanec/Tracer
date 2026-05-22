<template>
  <div class="persona-switcher">
    <button
      v-for="p in allPersonas"
      :key="p"
      class="persona-switcher__btn"
      :class="{ 'persona-switcher__btn--active': persona === p }"
      @click="setPersona(p)"
    >
      {{ label(p) }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { usePersona } from '@/composables/usePersona';
import type { Persona } from '@/stores/personaStore';

const { persona, setPersona, allPersonas } = usePersona();

const LABELS: Record<Persona, string> = {
  'engineer': 'Engineer',
  'scenario-author': 'Scenario Author',
  'operator': 'Operator',
};

function label(p: Persona) {
  return LABELS[p];
}
</script>

<style lang="scss">
.persona-switcher {
  display: flex;
  gap: 0.25rem;

  &__btn {
    padding: 0.25rem 0.75rem;
    border-radius: 4px;
    border: 1px solid var(--c-bg-subtle);
    background: transparent;
    color: var(--c-text-muted);
    cursor: pointer;
    font-size: 0.875rem;

    &:hover {
      background: var(--c-bg-subtle);
    }

    &--active {
      background: var(--c-accent);
      border-color: var(--c-accent);
      color: #fff;
    }
  }
}
</style>
