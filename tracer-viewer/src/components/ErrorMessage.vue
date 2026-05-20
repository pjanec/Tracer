<script setup lang="ts">
defineProps<{
  message: string;
}>();

const emit = defineEmits<{
  retry: [];
}>();

function onRetry() {
  emit('retry');
}
</script>

<template>
  <div
    class="error-message"
    role="alert"
  >
    <p class="error-message__text">
      {{ message }}
    </p>
    <button
      class="error-message__retry"
      type="button"
      @click="onRetry"
    >
      Retry
    </button>
  </div>
</template>

<style>
.error-message {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 2rem;
  background: var(--c-bg-surface);
  border: 1px solid var(--c-danger);
  border-radius: 0.5rem;
}

.error-message__text {
  margin: 0;
  color: var(--c-danger);
  font-size: 0.9375rem;
}

.error-message__retry {
  padding: 0.5rem 1.25rem;
  background: var(--c-accent);
  color: #fff;
  border: none;
  border-radius: 0.375rem;
  cursor: pointer;
  font-size: 0.875rem;
}
</style>
