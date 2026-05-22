import { test, expect } from '@playwright/test';

test.describe('Network Topology View', () => {
  test('topology view renders canvas', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live bundle + SPA; set E2E=true to run');
    const sessionId = process.env['E2E_SESSION_ID'] ?? 'test-session';
    await page.goto(`http://localhost:5300/v/topology/${sessionId}`);
    await expect(page.locator('canvas').first()).toBeVisible({ timeout: 10_000 });
  });
});
