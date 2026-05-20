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
  ],
});

export default router;
