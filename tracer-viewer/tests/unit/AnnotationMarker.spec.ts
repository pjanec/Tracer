import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AnnotationMarker from '../../src/components/AnnotationMarker.vue';
import { useAnnotationStore } from '../../src/stores/annotationStore';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(override?: Partial<AnnotationDto>): AnnotationDto {
  return {
    annotationId: 'ann-1',
    sessionId: 'sess-1',
    kind: 'Event',
    eventId: 'AAAA',
    body: 'Default body',
    tags: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...override,
  };
}

describe('AnnotationMarker', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('Marker_RendersWhenAnnotationExists', () => {
    const store = useAnnotationStore();
    store.upsert(makeAnnotation({ eventId: 'AAAA' }));

    const wrapper = mount(AnnotationMarker, { props: { eventId: 'AAAA' } });
    expect(wrapper.find('.annotation-marker').exists()).toBe(true);
  });

  it('Marker_HiddenWhenNoAnnotation', () => {
    const wrapper = mount(AnnotationMarker, { props: { eventId: 'BBBB' } });
    expect(wrapper.find('.annotation-marker').exists()).toBe(false);
  });

  it('Marker_Tooltip_ShowsAnnotationTitle', () => {
    const store = useAnnotationStore();
    store.upsert(makeAnnotation({ eventId: 'T1', title: 'Suspicious spike', body: 'some body' }));

    const wrapper = mount(AnnotationMarker, { props: { eventId: 'T1' } });
    const btn = wrapper.find('.annotation-marker');
    expect(btn.attributes('title')).toContain('Suspicious spike');
  });

  it('Marker_Tooltip_FallsBackToBodyFirstLine', () => {
    const store = useAnnotationStore();
    store.upsert(makeAnnotation({ eventId: 'T2', title: undefined, body: 'First line\nSecond line' }));

    const wrapper = mount(AnnotationMarker, { props: { eventId: 'T2' } });
    const btn = wrapper.find('.annotation-marker');
    expect(btn.attributes('title')).toBe('First line');
  });

  it('Marker_Click_EmitsEditEvent', async () => {
    const store = useAnnotationStore();
    const ann = makeAnnotation({ annotationId: 'ann-click', eventId: 'CCCC' });
    store.upsert(ann);

    const wrapper = mount(AnnotationMarker, { props: { eventId: 'CCCC' } });
    await wrapper.find('.annotation-marker').trigger('click');

    expect(wrapper.emitted('edit')).toBeTruthy();
    const emitted = wrapper.emitted('edit')![0][0] as AnnotationDto;
    expect(emitted.annotationId).toBe('ann-click');
  });
});
