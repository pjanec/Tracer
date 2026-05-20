import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  use: {
    baseURL: 'http://localhost:5300',
  },
  webServer:
    process.env['E2E'] === 'true'
      ? {
          command: 'echo "Server expected to be already running on :5300"',
          url: 'http://localhost:5300/api/health',
          reuseExistingServer: true,
          timeout: 30_000,
        }
      : undefined,
});
