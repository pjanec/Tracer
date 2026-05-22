import { test, expect } from '@playwright/test';

test.describe('Annotations Flow', () => {
  test('E2E_CreateAnnotation_PersistsAfterReload', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    // Open event inspector for a known event
    await page.goto('http://localhost:5300/v/timeline/test-session');
    // Open inspector for first event
    await page.locator('.timeline-event').first().click();
    // Click "Add note"
    await page.locator('button:has-text("Add note")').click();
    // Fill body
    await page.locator('.annotation-editor__body').fill('Integration test annotation');
    // Save
    await page.locator('button:has-text("Save")').click();
    // Reload
    await page.reload();
    // Marker should be visible
    await expect(page.locator('.annotation-marker')).toBeVisible({ timeout: 5_000 });
  });
});
