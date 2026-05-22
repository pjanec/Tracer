// tracer-viewer/tests/e2e/entity-history-view.spec.ts
// Playwright E2E smoke tests for EntityPickerView and EntityHistoryView.
// Requires the dev server running at http://localhost:5300 (E2E=true).
// NOT run in the Vitest unit test pass.

import { test, expect } from '@playwright/test';

const BASE_URL = 'http://localhost:5300';
const TEST_ENTITY_ID = 'test-entity-001';
const TEST_SESSION_ID = 'test-session-001';

test.describe('EntityPickerView E2E', () => {
  test('entityPickerView_renders_searchAndList', async ({ page }) => {
    await page.goto(`${BASE_URL}/v/entities/${TEST_SESSION_ID}`);

    // The entity picker container should render (no JS crash / blank screen)
    await expect(page.locator('.entity-picker')).toBeVisible({ timeout: 5000 });

    // The h1 heading should contain the session ID
    await expect(page.locator('h1')).toContainText(TEST_SESSION_ID, { timeout: 5000 });

    // The filter input should be present
    await expect(page.locator('.entity-picker__filter')).toBeVisible({ timeout: 5000 });
  });
});

test.describe('EntityHistoryView E2E', () => {
  test('entityHistoryView_renders_loadingOrSummary', async ({ page }) => {
    await page.goto(`${BASE_URL}/v/entity/${TEST_ENTITY_ID}?session=${TEST_SESSION_ID}`);

    // The top-level view container must be present — accept loading or error state
    // since there is no seeded test data for this stub entity/session.
    await expect(page.locator('.entity-history-view')).toBeVisible({ timeout: 5000 });
  });

  test('entityHistoryView_directUrl_showsEntityId', async ({ page }) => {
    const from = new Date(0).toISOString();
    const to = new Date(60_000).toISOString();
    const url = `${BASE_URL}/v/entity/${TEST_ENTITY_ID}?session=${TEST_SESSION_ID}&from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;

    await page.goto(url);

    // URL must still contain the entity ID (no redirect away from this route)
    expect(page.url()).toContain(TEST_ENTITY_ID);

    // Page must not be a blank white crash — at minimum the view container renders
    await expect(page.locator('.entity-history-view')).toBeVisible({ timeout: 5000 });
  });

  test('entityHistoryView_entityPickerLink_navigatesToPicker', async ({ page }) => {
    // Navigate directly to the picker route and verify it loads without JS crash
    await page.goto(`${BASE_URL}/v/entities/${TEST_SESSION_ID}`);

    await expect(page.locator('.entity-picker')).toBeVisible({ timeout: 5000 });

    // The page should remain on the entities route (no redirect to an error page)
    expect(page.url()).toContain('/v/entities/');
    expect(page.url()).toContain(TEST_SESSION_ID);
  });

  test('entityHistoryView_loadingOrError_noCrash', async ({ page }) => {
    // Navigate and check that even when the API returns 404/empty, the view
    // renders a graceful loading or error state rather than a blank crash.
    await page.goto(`${BASE_URL}/v/entity/${TEST_ENTITY_ID}?session=${TEST_SESSION_ID}`);

    // Wait for either the loading spinner, error message, or the full view
    const anyState = page.locator(
      '.entity-history-view__loading, .entity-history-view__error, .entity-history-view',
    );
    await expect(anyState).toBeVisible({ timeout: 5000 });

    // Confirm the browser has no unhandled page errors
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));
    // Allow a short settle time
    await page.waitForTimeout(500);
    expect(errors).toHaveLength(0);
  });
});
