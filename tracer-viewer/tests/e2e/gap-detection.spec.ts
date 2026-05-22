import { test, expect } from '@playwright/test';

test.describe('Gap Detection View', () => {
  test('gap detection view loads', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live bundle + SPA; set E2E=true to run');
    const sessionId = process.env['E2E_SESSION_ID'] ?? 'test-session';
    await page.goto(`http://localhost:5300/v/gaps/${sessionId}`);
    await expect(page.locator('h1')).toContainText('Gap detection');
    // No JS errors check via console listener (set before navigation)
  });
});
