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
    {
      path: '/v/trace/:traceId',
      name: 'causal-by-trace',
      component: () => import('@/views/CausalTreeView.vue'),
    },
    {
      path: '/v/causal/:eventId',
      name: 'causal-by-event',
      component: () => import('@/views/CausalTreeView.vue'),
    },
    {
      path: '/v/entity/:entityId',
      name: 'entity-history',
      component: () => import('@/views/EntityHistoryView.vue'),
    },
    {
      path: '/v/entities/:sessionId',
      name: 'entity-picker',
      component: () => import('@/views/EntityPickerView.vue'),
      props: true,
    },
    {
      path: '/v/saved-views/:sessionId',
      name: 'saved-views',
      component: () => import('@/views/SavedViewsView.vue'),
      props: true,
    },
    {
      path: '/v/triggers/:sessionId',
      name: 'triggers',
      component: () => import('@/views/TriggerEvalView.vue'),
      props: true,
    },
  ],
});

export default router;
