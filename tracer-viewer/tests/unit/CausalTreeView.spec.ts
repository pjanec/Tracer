import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createRouter, createMemoryHistory } from 'vue-router';
import CausalTreeView from '@/views/CausalTreeView.vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import type { TraceTreeDto } from '@/types/causalTree';

// Mock useCausalTreeQuery so it doesn't fire API calls in view tests
vi.mock('@/composables/useCausalTreeQuery', () => ({
  useCausalTreeQuery: vi.fn(),
}));

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div/>' } }],
  });
}

function makeTree(): TraceTreeDto {
  return {
    traceId: 'aabbccddeeff0011',
    nodes: [
      { eventId: 'evt-1', traceId: 'aabbccddeeff0011', publishWallclock: '2026-01-01T10:00:00.000Z', publisherNode: 'node-a', topic: 'test' },
    ],
    edges: [],
    rootEventIds: ['evt-1'],
    leafEventIds: ['evt-1'],
    summary: {
      traceId: 'aabbccddeeff0011', totalEvents: 1, truncated: false, totalSpanMs: 100,
      participatingNodes: ['node-a'], rootCount: 1, leafCount: 1,
    },
  };
}

describe('CausalTreeView', () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof makeRouter>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = makeRouter();
  });

  function mountView() {
    return mount(CausalTreeView, {
      global: {
        plugins: [pinia, router],
        stubs: {
          CausalTreeCanvas: true,
          TraceSummaryPanel: true,
          CausalNodeInspector: true,
          TraceSearchInput: true,
          LoadingSpinner: { template: '<div class="loading-spinner"/>' },
          ErrorMessage: { template: '<div><slot/><button @click="$emit(\'retry\')">Retry</button></div>', emits: ['retry'] },
        },
      },
    });
  }

  it('renders_LoadingSpinner_WhenStoreIsLoadingAndNoTree', async () => {
    const store = useCausalTreeStore();
    store.loading = true;
    store.tree = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.loading-spinner').exists()).toBe(true);
    expect(wrapper.findComponent({ name: 'CausalTreeCanvas' }).exists()).toBe(false);
  });

  it('renders_ErrorMessage_WithRetryButton_WhenStoreHasError', async () => {
    const store = useCausalTreeStore();
    store.error = 'timeout';
    store.loading = false;
    store.tree = null;

    const retrySpy = vi.spyOn(store, 'retry');
    const wrapper = mountView();
    await flushPromises();

    const errorDiv = wrapper.find('[data-testid="error-message"]');
    expect(errorDiv.exists()).toBe(true);

    const retryBtn = errorDiv.find('button');
    expect(retryBtn.exists()).toBe(true);

    await retryBtn.trigger('click');
    expect(retrySpy).toHaveBeenCalled();
  });

  it('renders_ThreeColumnGrid_WhenTreeLoadedAndNodeSelected', async () => {
    const store = useCausalTreeStore();
    store.tree = makeTree();
    store.selectedEventId = 'evt-1';
    store.loading = false;
    store.error = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.causal-tree-view__summary').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__canvas').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__inspector').exists()).toBe(true);
  });

  it('renders_TwoColumnGrid_WhenTreeLoadedAndNoNodeSelected', async () => {
    const store = useCausalTreeStore();
    store.tree = makeTree();
    store.selectedEventId = null;
    store.loading = false;
    store.error = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.causal-tree-view__summary').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__canvas').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__inspector').exists()).toBe(false);
  });

  it('renders_EmptyPrompt_WhenNoTreeAndNotLoading', async () => {
    const store = useCausalTreeStore();
    store.tree = null;
    store.loading = false;
    store.error = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.causal-tree-view__empty').exists()).toBe(true);
  });
});
