import { test, expect } from '@playwright/test';

test.describe('Persona Switcher', () => {
  test('E2E_PersonaSwitcher_EngineerLandsOnTimeline', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    await page.goto('http://localhost:5300/sessions');
    // Set Engineer persona
    await page.locator('.persona-switcher__btn:has-text("Engineer")').click();
    // Click first session card
    await page.locator('.session-card').first().click();
    // Should navigate to /v/timeline/
    await expect(page).toHaveURL(/\/v\/timeline\//);
  });

  test('E2E_PersonaSwitcher_ScenarioAuthorLandsOnScenario', async ({ page }) => {
    test.skip(process.env['E2E'] !== 'true', 'Requires live Observer + SPA; set E2E=true to run');
    await page.goto('http://localhost:5300/sessions');
    // Set Scenario Author persona
    await page.locator('.persona-switcher__btn:has-text("Scenario Author")').click();
    // Click first session card
    await page.locator('.session-card').first().click();
    // Should navigate to /v/scenario/
    await expect(page).toHaveURL(/\/v\/scenario\//);
  });
});
