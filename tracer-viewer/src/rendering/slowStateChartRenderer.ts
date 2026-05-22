// tracer-viewer/src/rendering/slowStateChartRenderer.ts
// Pure canvas rendering for SlowStateChart — no Vue dependency.

export type FieldKind = 'numeric' | 'categorical';

export interface DetectedField {
  name: string;
  kind: FieldKind;
}

export interface SlowStateSample {
  /** Milliseconds since epoch. */
  t: number;
  /** The selected field's value (unknown type; numeric or string). */
  value: unknown;
}

export interface SlowStateRenderInput {
  ctx: CanvasRenderingContext2D;
  width: number;
  height: number;
  fromMs: number;
  toMs: number;
  samples: SlowStateSample[];
  kind: FieldKind;
}

const NUMERIC_PREFERRED = ['value', 'level', 'health', 'count', 'speed', 'amount'];
const CATEGORICAL_PREFERRED = ['state', 'status', 'phase', 'kind', 'mode'];

/** Colour palette for categorical bands — up to 15 distinct values before collapsing to grey. */
const CATEGORICAL_PALETTE = [
  '#4a9eff', '#22c55e', '#ef4444', '#f59e0b', '#8b5cf6',
  '#ec4899', '#14b8a6', '#f97316', '#6366f1', '#84cc16',
  '#0ea5e9', '#d946ef', '#a855f7', '#10b981', '#f43f5e',
];

/**
 * Inspect up to `maxSamples` payloadJson strings and classify each JSON field as
 * 'numeric' (all observed values are typeof number) or 'categorical'.
 * Returns fields sorted: preferred-numeric first, other-numeric, preferred-categorical, other-categorical.
 */
export function detectFields(payloadJsonSamples: string[], maxSamples?: number): DetectedField[] {
  const limit = maxSamples ?? 20;
  const samples = payloadJsonSamples.slice(0, limit);

  // field name → list of all observed values
  const fieldValues = new Map<string, unknown[]>();

  for (const json of samples) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(json);
    } catch {
      continue;
    }
    if (typeof parsed !== 'object' || parsed === null) continue;
    for (const [key, val] of Object.entries(parsed as Record<string, unknown>)) {
      if (!fieldValues.has(key)) fieldValues.set(key, []);
      fieldValues.get(key)!.push(val);
    }
  }

  const numericFields: DetectedField[] = [];
  const categoricalFields: DetectedField[] = [];

  for (const [name, values] of fieldValues) {
    const isNumeric = values.length > 0 && values.every(v => typeof v === 'number');
    if (isNumeric) {
      numericFields.push({ name, kind: 'numeric' });
    } else {
      categoricalFields.push({ name, kind: 'categorical' });
    }
  }

  const sortGroup = (fields: DetectedField[], preferred: string[]): DetectedField[] => {
    const pref = fields.filter(f => preferred.includes(f.name));
    const other = fields.filter(f => !preferred.includes(f.name));
    pref.sort((a, b) => preferred.indexOf(a.name) - preferred.indexOf(b.name));
    return [...pref, ...other];
  };

  return [
    ...sortGroup(numericFields, NUMERIC_PREFERRED),
    ...sortGroup(categoricalFields, CATEGORICAL_PREFERRED),
  ];
}

/**
 * Renders a stepped (last-value-held) numeric line chart.
 */
export function renderNumericLine(input: SlowStateRenderInput): void {
  const { ctx, width, height, fromMs, toMs, samples } = input;
  ctx.clearRect(0, 0, width, height);
  if (samples.length === 0) return;

  const range = toMs - fromMs;

  const numericSamples = samples
    .filter((s): s is SlowStateSample & { value: number } => typeof s.value === 'number');

  if (numericSamples.length === 0) return;

  let minVal = numericSamples[0].value;
  let maxVal = numericSamples[0].value;
  for (const s of numericSamples) {
    if (s.value < minVal) minVal = s.value;
    if (s.value > maxVal) maxVal = s.value;
  }

  let valRange = maxVal - minVal;
  if (valRange < 1e-9) {
    valRange = 1e-9;
    minVal -= valRange / 2;
    maxVal += valRange / 2;
  }

  function xPos(t: number): number {
    return range === 0 ? 0 : ((t - fromMs) / range) * width;
  }

  function yPos(v: number): number {
    const normalized = (v - minVal) / (maxVal - minVal);
    return Math.max(1, Math.min(height - 1, height - normalized * height));
  }

  ctx.strokeStyle = '#4a9eff';
  ctx.lineWidth = 1.5;
  ctx.beginPath();

  for (let i = 0; i < numericSamples.length; i++) {
    const s = numericSamples[i];
    const x = xPos(s.t);
    const y = yPos(s.value);

    if (i === 0) {
      ctx.moveTo(x, y);
    } else {
      // Stepped line: horizontal first, then vertical
      const prevY = yPos(numericSamples[i - 1].value);
      ctx.lineTo(x, prevY);
      ctx.lineTo(x, y);
    }

    // Extend the last segment to the right edge
    if (i === numericSamples.length - 1) {
      ctx.lineTo(width, y);
    }
  }

  ctx.stroke();
}

/**
 * Renders categorical bands — one filled rectangle per sample value, extending to the next sample.
 * Adds a text label when the band is wide enough (> 30px).
 */
export function renderCategoricalBands(input: SlowStateRenderInput): void {
  const { ctx, width, height, fromMs, toMs, samples } = input;
  ctx.clearRect(0, 0, width, height);
  if (samples.length === 0) return;

  const range = toMs - fromMs;

  // Collect distinct values in order of first appearance
  const distinctValues: string[] = [];
  for (const s of samples) {
    const label = String(s.value);
    if (!distinctValues.includes(label)) {
      distinctValues.push(label);
    }
  }

  function getColor(label: string): string {
    const idx = distinctValues.indexOf(label);
    return idx < CATEGORICAL_PALETTE.length ? CATEGORICAL_PALETTE[idx] : '#888888';
  }

  function getDisplayLabel(label: string): string {
    const idx = distinctValues.indexOf(label);
    return idx >= CATEGORICAL_PALETTE.length ? 'other' : label;
  }

  function xPos(t: number): number {
    return range === 0 ? 0 : ((t - fromMs) / range) * width;
  }

  for (let i = 0; i < samples.length; i++) {
    const s = samples[i];
    const label = String(s.value);
    const xStart = xPos(s.t);
    const xEnd = i + 1 < samples.length ? xPos(samples[i + 1].t) : width;
    const bandWidth = xEnd - xStart;

    ctx.fillStyle = getColor(label);
    ctx.fillRect(xStart, 0, bandWidth, height);

    if (bandWidth > 30) {
      const centreX = xStart + bandWidth / 2;
      const centreY = height / 2;
      ctx.fillStyle = '#fff';
      ctx.font = '10px sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(getDisplayLabel(label), centreX, centreY);
    }
  }
}
