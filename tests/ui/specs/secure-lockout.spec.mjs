// Secure scenario: failed-attempt lockout (MaxFailedAttempts 5). Uses a fresh user so no other test is affected.
import { test, expect } from '@playwright/test';
import { SECURE_BASE_URL, logIn, registerUser, uniqueTag } from './helpers.mjs';

test.use({ baseURL: SECURE_BASE_URL });

test.describe.configure({ mode: 'serial' });

test('five wrong attempts lock the user out, even with the right password', async ({ page }) => {
  const user = `lock${uniqueTag()}`;
  const password = 'valid-pass-1';
  await registerUser(page, user, password);

  for (let attempt = 1; attempt <= 5; attempt++) {
    await logIn(page, user, 'wrong-password');
    await expect(page.getByText('Invalid username or password.', { exact: true })).toBeVisible();
  }

  await logIn(page, user, password);
  await expect(page.getByText('Too many failed attempts. Try again later.', { exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/login$/);
});
