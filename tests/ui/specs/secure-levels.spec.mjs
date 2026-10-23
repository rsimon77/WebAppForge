// Secure scenario: user levels. reports has EditMinLevel 2; alice is level 1, bob is level 0. Uses UI_SECURE_BASE_URL.
import { test, expect } from '@playwright/test';
import { SECURE_BASE_URL, SEEDED_PASSWORD, logIn } from './helpers.mjs';

test.use({ baseURL: SECURE_BASE_URL });

test('alice (level 1) can open the reports list', async ({ page }) => {
  await logIn(page, 'alice', SEEDED_PASSWORD);
  const response = await page.goto('/reports');
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'reports', exact: true })).toBeVisible();
});

test('alice (level 1) gets 403 with the message when adding a report', async ({ page }) => {
  await logIn(page, 'alice', SEEDED_PASSWORD);
  const response = await page.goto('/reports/add');
  expect(response?.status()).toBe(403);
  await expect(page.locator('body')).toContainText('You do not have permission to change this table.');
});

test('bob (level 0) can open the reports list', async ({ page }) => {
  await logIn(page, 'bob', SEEDED_PASSWORD);
  const response = await page.goto('/reports');
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'reports', exact: true })).toBeVisible();
});
