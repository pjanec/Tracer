import { test, expect } from '@playwright/test';

test.describe('Saved Views Flow', () => {
  test('E2E_SavedView_RestoresFilterState', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    // Navigate to timeline with a filter
    await page.goto('http://localhost:5300/v/timeline/test-session?topic=weapons.fire');
    // Click "Save view"
    await page.locator('button:has-text("Save view")').click();
    // Accept the dialog
    await page.locator('button:has-text("Save")').click();
    // Navigate to saved views
    await page.goto('http://localhost:5300/v/saved-views/test-session');
    await expect(page.locator('.saved-views-view__item').first()).toBeVisible({ timeout: 5_000 });
    // Click first saved view
    await page.locator('.saved-views-view__item').first().click();
    // URL must contain the filter param
    await expect(page).toHaveURL(/topic=weapons\.fire/);
  });
});
