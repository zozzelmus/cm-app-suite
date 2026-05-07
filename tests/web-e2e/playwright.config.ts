import { defineConfig, devices } from '@playwright/test'

// Smoke flow runs against the live Aspire stack at the BFF entry URL.
// Override BASE_URL via env if Aspire allocates a different port.
const BASE_URL = process.env.E2E_BASE_URL ?? 'http://localhost:5010'

export default defineConfig({
  testDir: './specs',
  outputDir: './artifacts/test-output',
  reporter: [
    ['list'],
    ['html', { outputFolder: './artifacts/html-report', open: 'never' }],
  ],
  // Per user request: record video for every run (not just on failure) so the morning
  // review has visual proof of the smoke flow.
  use: {
    baseURL: BASE_URL,
    trace: 'on',
    video: 'on',
    screenshot: 'only-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  // No webServer config — tests assume the Aspire stack is already running externally.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
})
