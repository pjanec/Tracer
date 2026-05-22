<template>
  <div class="bundle-mode-required-banner">
    <div class="bundle-mode-required-banner__icon">⚠</div>
    <div class="bundle-mode-required-banner__body">
      <strong class="bundle-mode-required-banner__title">This view requires bundle mode.</strong>
      <p class="bundle-mode-required-banner__message">
        Replication latency analysis requires per-node receive times, which are only available
        when a bundle has been opened.
      </p>
      <p v-if="detail" class="bundle-mode-required-banner__detail">{{ detail }}</p>
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{ detail?: string }>();
</script>

<style lang="scss">
.bundle-mode-required-banner {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  border: 1px solid var(--c-warning-border, #d9a520);
  border-radius: 6px;
  background: var(--c-warning-bg, #fff8e1);
  color: var(--c-warning-text, #5a4000);
  margin: 1rem 0;

  &__icon {
    font-size: 1.25rem;
    line-height: 1;
  }

  &__body {
    flex: 1;
  }

  &__title {
    display: block;
    font-weight: 600;
    margin-bottom: 0.25rem;
  }

  &__message,
  &__detail {
    margin: 0;
    font-size: 0.9rem;
    color: var(--c-warning-text, #5a4000);
  }

  &__detail {
    margin-top: 0.35rem;
    font-style: italic;
  }
}
</style>
