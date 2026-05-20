import { test, expect } from '@playwright/test';

const skip = process.env['E2E'] !== 'true';

test.describe('Scenario View E2E', () => {
  test('NavigatesToSessionBrowser_OnRootLoad', async ({ page }) => {
    test.skip(skip, 'E2E tests require a live server (set E2E=true)');
    await page.goto('http://localhost:5300/');
    await page.waitForURL(/\/sessions/, { timeout: 3000 });
  });

  test('SessionCard_Visible_Within10s', async ({ page }) => {
    test.skip(skip, 'E2E tests require a live server (set E2E=true)');
    await page.goto('http://localhost:5300/sessions');
    await expect(page.locator('.session-card').first()).toBeVisible({ timeout: 10_000 });
  });

  test('ClickSessionCard_OpensScenarioView', async ({ page }) => {
    test.skip(skip, 'E2E tests require a live server (set E2E=true)');
    await page.goto('http://localhost:5300/sessions');
    await page.locator('.session-card').first().click();
    await page.waitForURL(/\/scenario\//, { timeout: 3000 });
    await expect(page.locator('.scenario-state-panel')).toBeVisible({ timeout: 3000 });
  });

  test('LiveIndicator_TurnsGreen_Within5s', async ({ page }) => {
    test.skip(skip, 'E2E tests require a live server (set E2E=true)');
    await page.goto('http://localhost:5300/sessions');
    await page.locator('.session-card').first().click();
    await page.waitForURL(/\/scenario\//, { timeout: 3000 });
    await expect(page.locator('.live-indicator--live')).toBeVisible({ timeout: 5000 });
  });

  test('NotableEvents_AppearWithin500ms_OfLiveIndicator', async ({ page }) => {
    test.skip(skip, 'E2E tests require a live server (set E2E=true)');
    await page.goto('http://localhost:5300/sessions');
    await page.locator('.session-card').first().click();
    await page.waitForURL(/\/scenario\//, { timeout: 3000 });
    await page.locator('.live-indicator--live').waitFor({ timeout: 5000 });
    await expect(page.locator('.notable-event-card').first()).toBeVisible({ timeout: 500 });
  });

  test('PageLoad_Cold_Under2s', async ({ page }) => {
    test.skip(skip, 'E2E tests require a live server (set E2E=true)');
    await page.context().clearCookies();
    await page.goto('http://localhost:5300/sessions');
    const timing = await page.evaluate(() => {
      const t = performance.timing;
      return t.domContentLoadedEventEnd - t.navigationStart;
    });
    expect(timing).toBeLessThan(2000);
  });
});
