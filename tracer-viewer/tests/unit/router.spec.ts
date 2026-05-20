import { describe, it, expect } from 'vitest';
import router from '../../src/router/index';

describe('router', () => {
  it('causalByEventRoute_IsLazyLoaded', () => {
    const route = router.getRoutes().find(r => r.name === 'causal-by-event');
    expect(route).toBeDefined();
    // Component should be a function (dynamic import) not a static component object
    expect(typeof route!.components?.default).toBe('function');
  });
});
