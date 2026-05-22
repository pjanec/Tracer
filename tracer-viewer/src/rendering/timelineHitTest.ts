// tracer-viewer/src/rendering/timelineHitTest.ts
// Spatial index for hit-testing markers and aggregate buckets on the canvas.
// Uses a 64 × 16 uniform grid. Each cell contains references to overlapping entries.

export interface MarkerHitEntry {
  x: number;
  y: number;
  w: number;
  h: number;
  eventId: string;
}

export interface BucketHitEntry {
  x: number;
  y: number;
  w: number;
  h: number;
  bucketStartUtc: string;
  nodeId: string;
  count: number;
}

const COLS = 64;
const ROWS = 16;

export class HitIndex {
  private readonly cellW: number;
  private readonly cellH: number;

  // Flat arrays: index = row * COLS + col
  private readonly markerCells: MarkerHitEntry[][];
  private readonly bucketCells: BucketHitEntry[][];

  constructor(canvasWidth: number, canvasHeight: number) {
    this.cellW = canvasWidth / COLS;
    this.cellH = canvasHeight / ROWS;
    this.markerCells = Array.from({ length: COLS * ROWS }, () => []);
    this.bucketCells = Array.from({ length: COLS * ROWS }, () => []);
  }

  /** Register a marker in all grid cells it overlaps. */
  add(entry: MarkerHitEntry): void {
    const r0 = this._rowOf(entry.y - entry.h / 2);
    const r1 = this._rowOf(entry.y + entry.h / 2);
    const c0 = this._colOf(entry.x - entry.w / 2);
    const c1 = this._colOf(entry.x + entry.w / 2);
    for (let r = r0; r <= r1; r++) {
      for (let c = c0; c <= c1; c++) {
        this.markerCells[r * COLS + c].push(entry);
      }
    }
  }

  /** Register a bucket rectangle in all grid cells it overlaps. */
  addBucket(entry: BucketHitEntry): void {
    const r0 = this._rowOf(entry.y);
    const r1 = this._rowOf(entry.y + entry.h);
    const c0 = this._colOf(entry.x);
    const c1 = this._colOf(entry.x + entry.w);
    for (let r = r0; r <= r1; r++) {
      for (let c = c0; c <= c1; c++) {
        this.bucketCells[r * COLS + c].push(entry);
      }
    }
  }

  /**
   * Returns the closest marker whose bounding box contains (x, y),
   * or null if none found.
   */
  findMarkerAt(x: number, y: number): MarkerHitEntry | null {
    const col = this._colOf(x);
    const row = this._rowOf(y);
    const candidates = this.markerCells[row * COLS + col];

    let best: MarkerHitEntry | null = null;
    let bestDist = Infinity;

    for (const e of candidates) {
      const halfW = e.w / 2;
      const halfH = e.h / 2;
      if (x < e.x - halfW || x > e.x + halfW) continue;
      if (y < e.y - halfH || y > e.y + halfH) continue;
      const dx = x - e.x;
      const dy = y - e.y;
      const dist = dx * dx + dy * dy;
      if (dist < bestDist) {
        bestDist = dist;
        best = e;
      }
    }
    return best;
  }

  /**
   * Returns the first bucket whose axis-aligned bounding rect contains (x, y), or null.
   */
  findBucketAt(x: number, y: number): BucketHitEntry | null {
    const col = this._colOf(x);
    const row = this._rowOf(y);
    const candidates = this.bucketCells[row * COLS + col];

    for (const e of candidates) {
      if (x >= e.x && x <= e.x + e.w && y >= e.y && y <= e.y + e.h) {
        return e;
      }
    }
    return null;
  }

  private _colOf(x: number): number {
    return Math.max(0, Math.min(COLS - 1, Math.floor(x / this.cellW)));
  }

  private _rowOf(y: number): number {
    return Math.max(0, Math.min(ROWS - 1, Math.floor(y / this.cellH)));
  }
}
