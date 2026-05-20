// tracer-viewer/tests/e2e/causal-tree-view.spec.ts
// Playwright E2E tests for CausalTreeView.
// Requires the dev server running at http://localhost:5300 (E2E=true).
// NOT run in the Vitest unit test pass.

import { test, expect } from '@playwright/test';

const BASE_URL = 'http://localhost:5300';
const TEST_EVENT_ID = '0000000000000001';  // known seeded test event
const CAUSAL_URL = `${BASE_URL}/v/causal/${TEST_EVENT_ID}`;

test.describe('CausalTreeView E2E', () => {
  test('causalTreeView_renders_canvasAfterEventLoad', async ({ page }) => {
    await page.goto(CAUSAL_URL);
    // The CausalTreeCanvas canvas element should be rendered
    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible({ timeout: 5000 });
    // Summary panel should be visible
    await expect(page.locator('.trace-summary')).toBeVisible({ timeout: 5000 });
  });

  test('causalTreeView_searchInput_acceptsHexId', async ({ page }) => {
    await page.goto(BASE_URL + '/v/causal/0000000000000001');
    // TraceSearchInput should be visible
    const searchInput = page.locator('input[placeholder*="event ID"]');
    await expect(searchInput).toBeVisible({ timeout: 5000 });

    // Type a valid hex ID and submit
    await searchInput.fill('0000000000000002');
    await page.locator('.trace-search__btn').click();

    // URL should change to the new event ID
    await page.waitForURL(/\/v\/causal\/0{14}02/, { timeout: 3000 });
    expect(page.url()).toContain('0000000000000002');
  });

  test('causalTreeView_invalidHexInput_showsError', async ({ page }) => {
    await page.goto(CAUSAL_URL);

    const searchInput = page.locator('input[placeholder*="event ID"]');
    await expect(searchInput).toBeVisible({ timeout: 5000 });

    // Type an invalid (non-hex) ID and submit
    await searchInput.fill('not-a-hex-id!');
    await page.locator('.trace-search__btn').click();

    // Error message should appear
    await expect(page.locator('.trace-search__error')).toBeVisible({ timeout: 2000 });
  });
});
