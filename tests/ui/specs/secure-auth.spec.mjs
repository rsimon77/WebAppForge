// Secure scenario: login, registration and change password. Uses UI_SECURE_BASE_URL.
import { test, expect } from '@playwright/test';
import { SECURE_BASE_URL, SEEDED_PASSWORD, logIn, logOut, registerUser, uniqueTag } from './helpers.mjs';

test.use({ baseURL: SECURE_BASE_URL });

test.describe('login and registration', () => {
  test('GET / redirects to the login page', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login\?ReturnUrl=%2F$/);
    await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible();
  });

  test('wrong password shows the invalid message', async ({ page }) => {
    await logIn(page, 'alice', 'not-the-password');
    await expect(page.getByText('Invalid username or password.', { exact: true })).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
  });

  test('right password reaches the home page', async ({ page }) => {
    await logIn(page, 'alice', SEEDED_PASSWORD);
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByRole('heading', { name: 'Web Application' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'notes', exact: true })).toBeVisible();
  });

  test('register a user, see the notice, then log in as that user', async ({ page }) => {
    const user = `new${uniqueTag()}`;
    const password = 'valid-pass-1';
    await page.goto('/login');
    await page.getByRole('link', { name: 'Register', exact: true }).click();
    await expect(page).toHaveURL(/\/register$/);
    await page.getByLabel('User name', { exact: true }).fill(user);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByLabel('Confirm password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Register', exact: true }).click();

    await expect(page).toHaveURL(/\/login\?registered=1$/);
    await expect(page.getByText('Registration complete. You can now log in.', { exact: true })).toBeVisible();

    await logIn(page, user, password);
    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByRole('heading', { name: 'Web Application' })).toBeVisible();
  });

  test('login returns to the page that asked for it', async ({ page }) => {
    // Visiting a secured page while logged out redirects to /login?ReturnUrl=/notes.
    await page.goto('/notes');
    await expect(page).toHaveURL(/ReturnUrl=%2Fnotes$/);
    await page.getByLabel('User name', { exact: true }).fill('bob');
    await page.getByLabel('Password', { exact: true }).fill(SEEDED_PASSWORD);
    await page.getByRole('button', { name: 'Log in', exact: true }).click();
    await expect(page).toHaveURL(/\/notes$/);
  });

  test('registering a taken user name shows an error', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('User name', { exact: true }).fill('alice');
    await page.getByLabel('Password', { exact: true }).fill('valid-pass-1');
    await page.getByLabel('Confirm password', { exact: true }).fill('valid-pass-1');
    await page.getByRole('button', { name: 'Register', exact: true }).click();
    await expect(page.getByText('That user name is already taken.', { exact: true })).toBeVisible();
    await expect(page).toHaveURL(/\/register$/);
  });
});

test.describe('change password', () => {
  test('wrong current password shows an error', async ({ page }) => {
    const user = `pw${uniqueTag()}`;
    await registerUser(page, user, 'valid-pass-1');
    await logIn(page, user, 'valid-pass-1');
    await page.goto('/changepassword');
    await page.getByLabel('Current password', { exact: true }).fill('wrong-current');
    await page.getByLabel('New password', { exact: true }).fill('changed-pass-2');
    await page.getByLabel('Confirm new password', { exact: true }).fill('changed-pass-2');
    await page.getByRole('button', { name: 'Change password', exact: true }).click();
    await expect(page.getByText('The current password is not correct.', { exact: true })).toBeVisible();
    await expect(page.getByText('Your password has been changed.', { exact: true })).toHaveCount(0);
  });

  test('right current password shows success, and the old password fails after log out', async ({ page }) => {
    const user = `pw${uniqueTag()}`;
    await registerUser(page, user, 'valid-pass-1');
    await logIn(page, user, 'valid-pass-1');
    await page.goto('/changepassword');
    await page.getByLabel('Current password', { exact: true }).fill('valid-pass-1');
    await page.getByLabel('New password', { exact: true }).fill('changed-pass-2');
    await page.getByLabel('Confirm new password', { exact: true }).fill('changed-pass-2');
    await page.getByRole('button', { name: 'Change password', exact: true }).click();
    await expect(page.getByText('Your password has been changed.', { exact: true })).toBeVisible();

    await page.goto('/');
    await logOut(page);

    await logIn(page, user, 'valid-pass-1');
    await expect(page.getByText('Invalid username or password.', { exact: true })).toBeVisible();

    await logIn(page, user, 'changed-pass-2');
    await expect(page).toHaveURL(/\/$/);
  });
});
