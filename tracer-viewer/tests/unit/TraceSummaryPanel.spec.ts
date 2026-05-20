import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import TraceSummaryPanel from '@/components/TraceSummaryPanel.vue';
import type { TraceSummaryDto } from '@/types/causalTree';
import { buildNodeColorMap } from '@/rendering/colorScheme';

function hexToRgb(hex: string): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgb(${r}, ${g}, ${b})`;
}

function makeSummary(overrides: Partial<TraceSummaryDto> = {}): TraceSummaryDto {
  return {
    traceId: 'aabbccddeeff0011',
    totalEvents: 10,
    truncated: false,
    totalSpanMs: 500,
    participatingNodes: ['node-alpha'],
    rootCount: 1,
    leafCount: 3,
    ...overrides,
  };
}

describe('TraceSummaryPanel', () => {
  it('renders_TruncationNotice_WhenSummaryTruncatedIsTrue', () => {
    const summary = makeSummary({
      truncated: true,
      totalEventsAvailable: 6000,
      totalEvents: 1000,
    });

    const wrapper = mount(TraceSummaryPanel, { props: { summary } });

    const notice = wrapper.find('.trace-summary__truncation-notice');
    expect(notice.exists()).toBe(true);
    // toLocaleString() may use commas, dots, or narrow no-break spaces depending on locale
    expect(notice.text().replace(/[^0-9]/g, '')).toContain('6000');
  });

  it('renders_NodeList_WithBorderColorMatchingNodeColorMap', () => {
    const nodes = ['node-alpha', 'node-beta'];
    const summary = makeSummary({ participatingNodes: nodes });
    const colorMap = buildNodeColorMap(nodes);

    const wrapper = mount(TraceSummaryPanel, { props: { summary } });

    const nodeEls = wrapper.findAll('.trace-summary__node');
    expect(nodeEls).toHaveLength(2);

    nodeEls.forEach((el, i) => {
      const expectedHex = colorMap.get(nodes[i]) ?? '';
      const style = el.attributes('style') ?? '';
      // jsdom may convert hex to rgb; check either hex or computed rgb
      const rgb = hexToRgb(expectedHex);
      expect(style.includes(expectedHex) || style.includes(rgb)).toBe(true);
    });
  });
});
