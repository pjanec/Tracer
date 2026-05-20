import { describe, it, expect } from 'vitest';
import { getNodeColor, SEVERITY_COLORS } from '../../src/rendering/colorScheme';

describe('colorScheme', () => {
  it('isDeterministic', () => {
    const name = 'game-server-node-1';
    const first  = getNodeColor(name);
    const second = getNodeColor(name);
    expect(first).toBe(second);
    expect(first).toMatch(/^#[0-9a-f]{6}$/i);
  });

  it('severityColors_areDistinct', () => {
    const { info, warning, error } = SEVERITY_COLORS;
    expect(info).not.toBe(warning);
    expect(info).not.toBe(error);
    expect(warning).not.toBe(error);
    // All should be non-empty strings
    expect(info.length).toBeGreaterThan(0);
    expect(warning.length).toBeGreaterThan(0);
    expect(error.length).toBeGreaterThan(0);
  });
});
