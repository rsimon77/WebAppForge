import { defineConfig } from '@playwright/test';

// Base URL of the shop site (UI_BASE_URL). The secure site is read per spec file from UI_SECURE_BASE_URL.
// tools/generate-site.ps1 prints both as UI_BASE_URL=... for the site it started.
export default defineConfig({
  testDir: './specs',
  testMatch: '**/*.spec.mjs',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  timeout: 30_000,
  use: {
    baseURL: process.env.UI_BASE_URL,
    headless: true,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
