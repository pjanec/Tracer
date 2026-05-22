import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AnnotationEditor from '../../src/components/AnnotationEditor.vue';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(override?: Partial<AnnotationDto>): AnnotationDto {
  return {
    annotationId: 'ann-1',
    sessionId: 'sess-1',
    kind: 'Event',
    eventId: 'evt-1',
    body: 'test body',
    tags: ['foo'],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...override,
  };
}

describe('AnnotationEditor', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('Editor_SaveDisabled_WhenBodyBlank', () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    const saveBtn = wrapper.find('.annotation-editor__save');
    expect(saveBtn.attributes('disabled')).toBeDefined();
  });

  it('Editor_SaveEnabled_WhenBodyFilled', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    await wrapper.find('.annotation-editor__body').setValue('some text');
    const saveBtn = wrapper.find('.annotation-editor__save');
    expect(saveBtn.attributes('disabled')).toBeUndefined();
  });

  it('Editor_PopulatesFromInitialProp', () => {
    const ann = makeAnnotation({ body: 'hello', title: 'world', tags: ['foo'] });
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: ann } });
    expect((wrapper.find('.annotation-editor__body').element as HTMLTextAreaElement).value).toBe('hello');
    expect((wrapper.find('.annotation-editor__title-input').element as HTMLInputElement).value).toBe('world');
    expect(wrapper.text()).toContain('foo');
  });

  it('Editor_DeleteButton_HiddenInCreateMode', () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    expect(wrapper.find('.annotation-editor__delete').exists()).toBe(false);
  });

  it('Editor_DeleteButton_VisibleInEditMode', () => {
    const ann = makeAnnotation();
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: ann } });
    expect(wrapper.find('.annotation-editor__delete').exists()).toBe(true);
  });

  it('Editor_EmitsSaveWithCorrectData', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    await wrapper.find('.annotation-editor__body').setValue('test body');
    await wrapper.find('.annotation-editor__title-input').setValue('test title');
    await wrapper.find('.annotation-editor__save').trigger('click');
    expect(wrapper.emitted('save')).toBeTruthy();
    const [payload] = wrapper.emitted('save')![0] as [{ body: string; title?: string; tags: string[] }];
    expect(payload.body).toBe('test body');
    expect(payload.title).toBe('test title');
    expect(payload.tags).toEqual([]);
  });

  it('Editor_TagManagement_AddAndRemove', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    const tagInput = wrapper.find('.annotation-editor__tag-input');
    await tagInput.setValue('foo');
    await tagInput.trigger('keydown', { key: 'Enter' });
    expect(wrapper.text()).toContain('foo');
    await wrapper.find('.annotation-editor__tag-remove').trigger('click');
    expect(wrapper.findAll('.annotation-editor__tag')).toHaveLength(0);
  });

  it('Editor_CancelEmitsCancel', async () => {
    const wrapper = mount(AnnotationEditor, { props: { visible: true, initial: null } });
    await wrapper.find('.annotation-editor__cancel').trigger('click');
    expect(wrapper.emitted('cancel')).toBeTruthy();
    expect(wrapper.emitted('save')).toBeFalsy();
  });
});
