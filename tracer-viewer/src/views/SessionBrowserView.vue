<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import type { SessionDto } from '@/api/tracerApiClient';
import SessionCard from '@/components/SessionCard.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const router = useRouter();
const sessions = ref<SessionDto[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

const load = async () => {
  loading.value = true;
  error.value = null;
  try {
    const api = useApi();
    sessions.value = await api.listSessions();
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to load sessions';
  } finally {
    loading.value = false;
  }
};

const openSession = (s: SessionDto) => {
  router.push({ name: 'scenario', params: { sessionId: s.sessionId } });
};

onMounted(load);
</script>

<template>
  <div class="session-browser">
    <h1>Sessions</h1>
    <p class="session-browser__hint">
      Select a session to view its scenario flow and notable events.
    </p>

    <LoadingSpinner v-if="loading" />
    <ErrorMessage
      v-else-if="error"
      :message="error"
      @retry="load"
    />
    <div
      v-else-if="sessions.length === 0"
      class="session-browser__empty"
    >
      No sessions yet. Start FakeNode and refresh.
    </div>
    <div
      v-else
      class="session-browser__list"
    >
      <SessionCard
        v-for="s in sessions"
        :key="s.sessionId"
        :session="s"
        @click="openSession(s)"
      />
    </div>
  </div>
</template>

<style lang="scss">
.session-browser {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;

  &__hint {
    color: var(--c-text-muted);
    margin-bottom: 1.5rem;
  }

  &__list {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
    gap: 1rem;
  }

  &__empty {
    padding: 3rem;
    text-align: center;
    color: var(--c-text-muted);
    background: var(--c-bg-subtle);
    border-radius: 8px;
  }
}
</style>
