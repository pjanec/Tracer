import { createRouter, createWebHistory } from 'vue-router';

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/sessions',
    },
    {
      path: '/sessions',
      name: 'sessions',
      component: () => import('@/views/SessionBrowserView.vue'),
    },
    {
      path: '/scenario/:sessionId',
      name: 'scenario',
      component: () => import('@/views/ScenarioView.vue'),
      props: true,
    },
    {
      path: '/v/timeline/:sessionId',
      name: 'timeline',
      component: () => import('@/views/TimelineView.vue'),
      props: true,
    },
    {
      path: '/bundles',
      name: 'bundles',
      component: () => import('@/views/BundlesView.vue'),
    },
  ],
});

export default router;
