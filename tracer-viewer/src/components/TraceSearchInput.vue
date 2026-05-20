<!-- src/components/TraceSearchInput.vue -->
<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';

const router = useRouter();
const input = ref('');
const kind = ref<'event' | 'trace'>('event');
const error = ref<string | null>(null);

function submit() {
  error.value = null;
  const value = input.value.trim();
  if (!value) return;

  if (!/^[0-9a-fA-F]{16}$/.test(value)) {
    error.value = 'Expected a 16-character hex ID';
    return;
  }

  if (kind.value === 'trace') {
    void router.push({ name: 'causal-by-trace', params: { traceId: value.toLowerCase() } });
  } else {
    void router.push({ name: 'causal-by-event', params: { eventId: value.toLowerCase() } });
  }
  input.value = '';
}
</script>

<template>
  <form
    class="trace-search"
    @submit.prevent="submit"
  >
    <select
      v-model="kind"
      class="trace-search__kind"
    >
      <option value="event">
        Event
      </option>
      <option value="trace">
        Trace
      </option>
    </select>
    <input
      v-model="input"
      type="text"
      placeholder="Paste 16-char hex ID"
      class="trace-search__input"
      :class="{ 'trace-search__input--error': error }"
    />
    <button
      type="submit"
      class="trace-search__btn"
      :disabled="!input"
    >
      Open
    </button>
    <div
      v-if="error"
      class="trace-search__error"
    >
      {{ error }}
    </div>
  </form>
</template>

<style scoped>
.trace-search {
  display: flex;
  gap: 0.5rem;
  flex: 1;
  position: relative;
  align-items: center;
}
.trace-search__kind {
  padding: 0.5rem;
  background: var(--c-bg-subtle, #252538);
  border: 1px solid var(--c-bg-subtle, #252538);
  border-radius: 6px;
  color: var(--c-text, #cdd6f4);
  font-size: 0.875rem;
}
.trace-search__input {
  flex: 1;
  padding: 0.5rem 0.75rem;
  background: var(--c-bg-subtle, #252538);
  border: 1px solid var(--c-bg-subtle, #252538);
  border-radius: 6px;
  color: var(--c-text, #cdd6f4);
  font-family: var(--font-mono, monospace);
  font-size: 0.875rem;
}
.trace-search__input--error {
  border-color: var(--c-danger, #e85c5c);
}
.trace-search__btn {
  padding: 0.5rem 1rem;
  background: var(--c-accent, #1976d2);
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}
.trace-search__btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.trace-search__error {
  position: absolute;
  top: 100%;
  left: 0;
  margin-top: 0.25rem;
  color: var(--c-danger, #e85c5c);
  font-size: 0.75rem;
}
</style>
