import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AnnotationList from '../../src/components/AnnotationList.vue';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(id: string): AnnotationDto {
  return {
    annotationId: id,
    sessionId: 'sess-1',
    kind: 'Event',
    body: `Body of ${id}`,
    tags: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
  };
}

describe('AnnotationList', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('List_RendersAnnotations', () => {
    const wrapper = mount(AnnotationList, {
      props: { annotations: [makeAnnotation('a1'), makeAnnotation('a2')] },
    });
    expect(wrapper.findAll('.annotation-list__item')).toHaveLength(2);
  });

  it('List_ClickRowEmitsSelect', async () => {
    const ann = makeAnnotation('a1');
    const wrapper = mount(AnnotationList, { props: { annotations: [ann] } });
    await wrapper.find('.annotation-list__item').trigger('click');
    expect(wrapper.emitted('select')).toBeTruthy();
    expect(wrapper.emitted('select')![0][0]).toMatchObject({ annotationId: 'a1' });
  });

  it('List_EditButtonEmitsEdit', async () => {
    const ann = makeAnnotation('a1');
    const wrapper = mount(AnnotationList, { props: { annotations: [ann] } });
    await wrapper.find('.annotation-list__edit-btn').trigger('click');
    expect(wrapper.emitted('edit')).toBeTruthy();
    expect(wrapper.emitted('edit')![0][0]).toMatchObject({ annotationId: 'a1' });
  });
});
