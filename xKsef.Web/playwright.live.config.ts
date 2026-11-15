import { defineConfig, devices } from '@playwright/test';
const origin = process.env.XKSEF_WEB_LIVE_ORIGIN;
if (!origin) throw new Error('Set XKSEF_WEB_LIVE_ORIGIN explicitly to a controlled local HTTPS API.');
const url = new URL(origin);
if (url.protocol !== 'https:' || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)) throw new Error('Live tests are restricted to local HTTPS.');
export default defineConfig({
  testDir: './tests/live', testMatch: '**/*.spec.ts', workers: 1, retries: 0, forbidOnly: true, timeout: 90_000,
  reporter: [['list']], outputDir: 'test-results-live',
  use: { ...devices['Desktop Chrome'], channel: process.env.PLAYWRIGHT_CHANNEL, baseURL: origin,
    locale: 'pl-PL', timezoneId: 'Europe/Warsaw', viewport: { width: 1600, height: 1000 },
    ignoreHTTPSErrors: false, trace: 'off', screenshot: 'off' },
});
