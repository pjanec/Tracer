// tests/setup.ts — global test setup for Vitest
// Stub browser APIs not available in jsdom/happy-dom

// ResizeObserver is not implemented in jsdom
if (typeof ResizeObserver === 'undefined') {
  (globalThis as Record<string, unknown>).ResizeObserver = class ResizeObserver {
    observe()   { /* noop */ }
    unobserve() { /* noop */ }
    disconnect() { /* noop */ }
  };
}
