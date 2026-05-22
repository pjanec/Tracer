<script setup lang="ts">
import { useRouter } from 'vue-router';

const props = defineProps<{ sql: string; sessionId: string }>();
const router = useRouter();

function open() {
  void router.push({
    name: 'sql-console',
    params: { sessionId: props.sessionId },
    query: { sql: props.sql },
  });
}
</script>

<template>
  <button class="show-sql-btn" title="Open the current filter as SQL in the SQL Console" @click="open">
    Show SQL
  </button>
</template>

<style lang="scss">
.show-sql-btn {
  padding: 0.25rem 0.6rem;
  font-size: 0.75rem;
  background: var(--c-bg-subtle);
  border: 1px solid var(--c-border);
  border-radius: 4px;
  color: var(--c-text-muted);
  cursor: pointer;
  &:hover {
    color: var(--c-text);
    background: var(--c-bg-surface);
  }
}
</style>
