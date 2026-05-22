<script setup lang="ts">
import { computed } from 'vue';
import type { FastStateColumnDto } from '@/api/tracerApiClient';

const props = defineProps<{
  columns: FastStateColumnDto[];
  selected: string[];
}>();

const emit = defineEmits<{
  'update:selected': [columns: string[]];
}>();

const numericColumns = computed(() => props.columns.filter(c => c.isNumeric));
const hasNonNumeric = computed(() => props.columns.some(c => !c.isNumeric));

function toggle(name: string): void {
  if (props.selected.includes(name)) {
    emit('update:selected', props.selected.filter(c => c !== name));
  } else {
    emit('update:selected', [...props.selected, name]);
  }
}
</script>

<template>
  <div class="fast-state-column-picker">
    <label
      v-for="col in numericColumns"
      :key="col.name"
      class="fast-state-column-picker__chip"
    >
      <input
        type="checkbox"
        :checked="selected.includes(col.name)"
        @change="toggle(col.name)"
      />
      {{ col.name }}
    </label>
    <span v-if="hasNonNumeric" class="fast-state-column-picker__hint">
      (non-numeric columns hidden)
    </span>
  </div>
</template>
