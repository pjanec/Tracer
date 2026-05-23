<script setup lang="ts">
import './styles/base.scss';
import { computed } from 'vue';
import { RouterView, useRoute } from 'vue-router';
import AppHeader from './components/AppHeader.vue';
import BookmarkBar from './components/BookmarkBar.vue';

const route = useRoute();
const sessionId = computed(() => route.params.sessionId as string | undefined);
const viewType = computed(() => (route.name as string | undefined) ?? '');
</script>

<template>
  <div class="app">
    <AppHeader />
    <BookmarkBar
      v-if="sessionId"
      :session-id="sessionId"
      :view-type="viewType"
    />
    <main class="app__main">
      <RouterView v-slot="{ Component }">
        <Transition
          mode="out-in"
          name="fade"
        >
          <component :is="Component" />
        </Transition>
      </RouterView>
    </main>
  </div>
</template>

<style>
.app {
  min-height: 100vh;
}

.app__main {
  padding: 0;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 150ms ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
