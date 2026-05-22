import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

const mockCreateSavedView = vi.fn();

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    createSavedView: mockCreateSavedView,
  },
}));

const mockRouteQuery = vi.fn(() => ({}));
const mockRouterPush = vi.fn();

vi.mock('vue-router', () => ({
  useRoute: () => ({
    fullPath: '/v/timeline/sess-1',
    get query() { return mockRouteQuery(); },
  }),
  useRouter: () => ({ push: mockRouterPush }),
}));

describe('SaveViewButton', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockCreateSavedView.mockReset();
    mockCreateSavedView.mockResolvedValue({ savedViewId: 'sv-1', label: 'Test', kind: 'Bookmark' });
    mockRouteQuery.mockReturnValue({});
    mockRouterPush.mockReset();
  });

  it('SaveViewButton_BookmarkClick_CallsAPI', async () => {
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    await wrapper.find('.save-view-button__bookmark').trigger('click');
    await flushPromises();
    expect(mockCreateSavedView).toHaveBeenCalledOnce();
    expect(mockCreateSavedView).toHaveBeenCalledWith(expect.objectContaining({ kind: 'Bookmark' }));
  });

  it('SaveViewButton_AutoLabel_NotEmpty', async () => {
    mockRouteQuery.mockReturnValue({});
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    await wrapper.find('.save-view-button__bookmark').trigger('click');
    await flushPromises();
    const callArg = mockCreateSavedView.mock.calls[0][0];
    expect(callArg.label).toBeTruthy();
    expect(callArg.label.length).toBeGreaterThan(0);
  });

  it('SaveViewButton_AutoLabel_IncludesTopic', async () => {
    mockRouteQuery.mockReturnValue({ topic: 'weapons.fire' });
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    await wrapper.find('.save-view-button__bookmark').trigger('click');
    await flushPromises();
    const callArg = mockCreateSavedView.mock.calls[0][0];
    expect(callArg.label).toContain('weapons.fire');
  });

  it('SaveViewButton_SaveDialog_OpenOnClick', async () => {
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    expect(wrapper.find('.save-view-dialog').exists()).toBe(false);
    await wrapper.find('.save-view-button__open-dialog').trigger('click');
    expect(wrapper.find('.save-view-dialog').exists()).toBe(true);
  });

  it('SaveViewButton_SaveDisabled_WhenLabelBlank', async () => {
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    await wrapper.find('.save-view-button__open-dialog').trigger('click');
    const saveBtn = wrapper.find('.save-view-dialog__save');
    expect(saveBtn.attributes('disabled')).toBeDefined();
  });

  it('SaveViewButton_SaveExplicit_CallsAPI', async () => {
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    await wrapper.find('.save-view-button__open-dialog').trigger('click');
    await wrapper.find('.save-view-dialog__label-input').setValue('Test view');
    await wrapper.find('.save-view-dialog__save').trigger('click');
    await flushPromises();
    expect(mockCreateSavedView).toHaveBeenCalledOnce();
    expect(mockCreateSavedView).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'SavedView', label: 'Test view' }),
    );
  });

  it('SaveViewButton_SaveDialog_ClosesAfterSave', async () => {
    const { default: SaveViewButton } = await import('../../src/components/SaveViewButton.vue');
    const wrapper = mount(SaveViewButton, { props: { sessionId: 's1', viewType: 'timeline' } });
    await wrapper.find('.save-view-button__open-dialog').trigger('click');
    await wrapper.find('.save-view-dialog__label-input').setValue('My View');
    await wrapper.find('.save-view-dialog__save').trigger('click');
    await flushPromises();
    expect(wrapper.find('.save-view-dialog').exists()).toBe(false);
  });
});
