<script setup lang="ts">
import { ref, computed } from 'vue';
import type { SessionDto } from '@/api/tracerApiClient';
import { api } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

const props = defineProps<{ session?: SessionDto; sessionId?: string }>();

// Effective session ID — from explicit prop or from the full session object
const effectiveSessionId = computed(() => props.sessionId ?? props.session?.sessionId ?? null);

// Build bundle state
type BuildStatus = 'idle' | 'building' | 'done' | 'error';
const buildStatus = ref<BuildStatus>('idle');
const builtBundleId = ref<string | null>(null);
const buildError  = ref<string | null>(null);

async function onBuildBundle() {
  if (!effectiveSessionId.value) return;
  buildStatus.value = 'building';
  buildError.value  = null;
  try {
    const result = await api.buildBundle(effectiveSessionId.value);
    builtBundleId.value = result.bundleId;
    buildStatus.value   = 'done';
  } catch (err: unknown) {
    buildError.value  = err instanceof Error ? err.message : 'Build failed';
    buildStatus.value = 'error';
  }
}
</script>

<template>
  <article class="session-card">
    <!-- Session info shown only when a full session object is provided -->
    <template v-if="session">
      <header class="session-card__header">
        <span class="session-card__scenario">{{ session.scenarioId }}</span>
        <span
          class="session-card__status"
          :class="`session-card__status--${session.status.toLowerCase()}`"
        >
          {{ session.status }}
        </span>
      </header>
      <div class="session-card__meta">
        <span
          v-if="session.label"
          class="session-card__label"
        >{{ session.label }}</span>
        <span class="session-card__time">{{ formatTime(session.startUtc) }}</span>
      </div>
      <footer class="session-card__footer">
        <span>{{ session.eventCount.toLocaleString() }} events</span>
        <span>{{ session.participatingNodes.length }} node(s)</span>
      </footer>
    </template>

    <!-- Build bundle controls (available in both modes) -->
    <div v-if="effectiveSessionId">
      <div v-if="buildStatus === 'idle'" class="session-card__build">
        <button class="session-card__build-btn" @click="onBuildBundle">Build bundle</button>
      </div>
      <div v-else-if="buildStatus === 'building'" class="session-card__build">
        <span class="session-card__progress">Building bundle…</span>
      </div>
      <div v-else-if="buildStatus === 'done'" class="session-card__build">
        <a :href="`/api/bundles/${builtBundleId}/download`" class="session-card__download">
          Download bundle
        </a>
      </div>
      <div v-else-if="buildStatus === 'error'" class="session-card__build session-card__build--error">
        {{ buildError }}
      </div>
    </div>
  </article>
</template>

<style lang="scss">
.session-card {
  background: var(--c-bg-surface);
  border-radius: 8px;
  padding: 1.25rem;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  border: 1px solid transparent;
  transition: border-color 150ms ease;

  &:hover {
    border-color: var(--c-accent);
  }

  &__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  &__scenario {
    font-weight: 600;
    font-size: 1rem;
  }

  &__status {
    font-size: 0.75rem;
    padding: 0.125rem 0.5rem;
    border-radius: 999px;
    background: var(--c-bg-subtle);

    &--active { color: var(--c-success); }
    &--completed { color: var(--c-text-muted); }
  }

  &__meta {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    font-size: 0.875rem;
    color: var(--c-text-muted);
  }

  &__footer {
    display: flex;
    gap: 1rem;
    font-size: 0.8125rem;
    color: var(--c-text-muted);
  }
}
</style>
