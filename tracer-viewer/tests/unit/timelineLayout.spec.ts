import { describe, it, expect } from 'vitest';
import {
  chooseBucketDuration,
  pixelToMs,
  msToPixel,
  swimlaneY,
} from '../../src/rendering/timelineLayout';

describe('timelineLayout', () => {
  it('chooseBucketDuration_SubOneMinute_ReturnsRaw', () => {
    expect(chooseBucketDuration(59_999)).toBe('raw');
    expect(chooseBucketDuration(0)).toBe('raw');
    expect(chooseBucketDuration(1)).toBe('raw');
  });

  it('chooseBucketDuration_FiveMinutes_Returns100ms', () => {
    // Exactly 5 minutes: the '1s' threshold is STRICTLY > 5min, so 5min exact → '100ms'
    const fiveMinMs = 5 * 60 * 1000; // 300000
    expect(chooseBucketDuration(fiveMinMs)).toBe('100ms');
  });

  it('chooseBucketDuration_ThirtyMinutes_Returns5s', () => {
    expect(chooseBucketDuration(30 * 60 * 1000)).toBe('5s');
  });

  it('chooseBucketDuration_OneHour_Returns30s', () => {
    expect(chooseBucketDuration(60 * 60 * 1000)).toBe('30s');
  });

  it('chooseBucketDuration_FourHoursOrMore_Returns5m', () => {
    expect(chooseBucketDuration(4 * 60 * 60 * 1000)).toBe('5m');
    expect(chooseBucketDuration(8 * 60 * 60 * 1000)).toBe('5m');
  });

  it('chooseBucketDuration_BoundaryValues_CorrectThresholdBehavior', () => {
    // At exactly 1min → '100ms'
    expect(chooseBucketDuration(60_000)).toBe('100ms');
    // Just below 1min → 'raw'
    expect(chooseBucketDuration(59_999)).toBe('raw');

    // At exactly 5min → '100ms' (threshold for 1s is strictly > 5min)
    expect(chooseBucketDuration(300_000)).toBe('100ms');
    // Just above 5min → '1s'
    expect(chooseBucketDuration(300_001)).toBe('1s');

    // At exactly 30min → '5s'
    expect(chooseBucketDuration(1_800_000)).toBe('5s');
    // Just below 30min → '1s'
    expect(chooseBucketDuration(1_799_999)).toBe('1s');

    // At exactly 1h → '30s'
    expect(chooseBucketDuration(3_600_000)).toBe('30s');
    // Just below 1h → '5s'
    expect(chooseBucketDuration(3_599_999)).toBe('5s');

    // At exactly 4h → '5m'
    expect(chooseBucketDuration(14_400_000)).toBe('5m');
    // Just below 4h → '30s'
    expect(chooseBucketDuration(14_399_999)).toBe('30s');
  });
});

describe('timelineLayout helpers', () => {
  it('msToPixel converts timestamp to pixel', () => {
    expect(msToPixel(1000, 1000, 0, 2000)).toBe(500);
    expect(msToPixel(0, 1000, 0, 2000)).toBe(0);
    expect(msToPixel(2000, 1000, 0, 2000)).toBe(1000);
  });

  it('pixelToMs converts pixel to timestamp', () => {
    expect(pixelToMs(500, 1000, 0, 2000)).toBe(1000);
    expect(pixelToMs(0, 1000, 0, 2000)).toBe(0);
    expect(pixelToMs(1000, 1000, 0, 2000)).toBe(2000);
  });

  it('swimlaneY returns center of correct lane', () => {
    expect(swimlaneY(0, 80)).toBe(40);
    expect(swimlaneY(1, 80)).toBe(120);
    expect(swimlaneY(2, 80)).toBe(200);
  });
});
