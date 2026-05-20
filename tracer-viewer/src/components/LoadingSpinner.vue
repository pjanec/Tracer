<template>
  <div
    class="loading-spinner"
    role="status"
    aria-label="Loading"
  >
    <div class="loading-spinner__ring" />
  </div>
</template>

<style>
.loading-spinner {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 2rem;
}

.loading-spinner__ring {
  width: 2.5rem;
  height: 2.5rem;
  border: 3px solid var(--c-bg-subtle);
  border-top-color: var(--c-accent);
  border-radius: 50%;
  animation: spin 0.75s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
