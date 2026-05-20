import { test, expect } from '@playwright/test';

// This test requires a live Observer + FakeNode instance.
// It is a stub that will be run as part of TRC-P3-013 (Playwright E2E Smoke Tests).
// The test body is intentionally complete — it will be skipped in CI until TRC-P3-013.

test.describe('Session Browser', () => {
  test('loads_and_shows_session_card', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer; set E2E=true to run');
    await page.goto('http://localhost:5300/sessions');
    await expect(page.locator('.session-card').first()).toBeVisible({ timeout: 10_000 });
  });
});
