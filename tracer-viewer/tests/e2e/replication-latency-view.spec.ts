import { test, expect } from '@playwright/test';

test.describe('Replication Latency View', () => {
  test('bundle session shows pair matrix', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live bundle + SPA; set E2E=true to run');
    const sessionId = process.env['E2E_SESSION_ID'] ?? 'test-session';
    await page.goto(`http://localhost:5300/v/latency/${sessionId}`);
    await expect(page.locator('.pair-matrix__row').first()).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('h1')).toHaveText('Replication latency');
  });

  test('live mode shows bundle required banner', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    const sessionId = process.env['E2E_SESSION_ID'] ?? 'test-session';
    await page.goto(`http://localhost:5300/v/latency/${sessionId}`);
    await expect(page.locator('.bundle-mode-required-banner')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('.bundle-mode-required-banner')).toContainText('requires bundle mode');
  });
});
