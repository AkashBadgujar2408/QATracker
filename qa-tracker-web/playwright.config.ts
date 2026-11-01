import { randomBytes } from 'node:crypto';
import { defineConfig } from '@playwright/test';

/**
 * E2E for the three critical flows (plan 15.1): tester logs an issue with a pasted screenshot, developer picks and
 * fixes it, tester retests. Runs the real API on a fresh QaTracker_E2E database (e2e/start-api.ps1, port 5129) and
 * `ng serve` on port 4300 – the normal dev servers (5029 / 4200) can keep running.
 * Uses the installed Chrome (channel "chrome"), so no browser download is needed.
 * Admin password and signing key are random per run (workers inherit them through the environment).
 */
process.env['E2E_ADMIN_PASSWORD'] ??= `E2e-${randomBytes(9).toString('base64url')}1`;
process.env['E2E_SIGNING_KEY'] ??= randomBytes(48).toString('base64url');

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://localhost:4300',
    channel: 'chrome',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    timezoneId: 'Asia/Kolkata',
    locale: 'en-IN',
    viewport: { width: 1440, height: 900 },
  },
  projects: [
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    { name: 'chrome', testMatch: /.*\.e2e\.ts/, dependencies: ['setup'] },
  ],
  webServer: [
    {
      command: 'powershell -NoProfile -ExecutionPolicy Bypass -File e2e/start-api.ps1',
      url: 'http://localhost:5129/health',
      timeout: 240_000,
      reuseExistingServer: false,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: 'npx ng serve --port 4300 --proxy-config e2e/proxy.e2e.json',
      url: 'http://localhost:4300',
      timeout: 240_000,
      reuseExistingServer: !process.env['CI'],
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
