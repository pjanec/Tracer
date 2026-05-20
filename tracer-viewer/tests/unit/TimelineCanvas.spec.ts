import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import TimelineCanvas from '../../src/components/TimelineCanvas.vue';

describe('TimelineCanvas', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('panHandler_capturesPointerOnDown', async () => {
    const setPointerCapture = vi.fn();
    const wrapper = mount(TimelineCanvas, {
      global: { plugins: [createPinia()] },
      attachTo: document.body,
    });

    const canvas = wrapper.find('canvas');
    // jsdom may not have setPointerCapture — mock it directly
    (canvas.element as unknown as Record<string, unknown>).setPointerCapture = setPointerCapture;

    await canvas.trigger('pointerdown', { pointerId: 42, clientX: 100, clientY: 50 });

    expect(setPointerCapture).toHaveBeenCalledWith(42);
    wrapper.unmount();
  });
});
