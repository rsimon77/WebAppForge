import { expect } from '@playwright/test';

export const SECURE_BASE_URL = process.env.UI_SECURE_BASE_URL;
if (!SECURE_BASE_URL) {
  throw new Error('UI_SECURE_BASE_URL is not set. Start the secure site with tools/generate-site.ps1 and set this variable to its base URL.');
}

// Every seeded account uses this password (see tools/harness/Program.cs).
export const SEEDED_PASSWORD = 'secret-pass-123';

export function uniqueTag() {
  return `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;
}

export async function logIn(page, userName, password) {
  await page.goto('/login');
  await page.getByLabel('User name', { exact: true }).fill(userName);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Log in', exact: true }).click();
}

export async function logOut(page) {
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
}

// Signs up through /register and waits for the notice on the login page.
export async function registerUser(page, userName, password) {
  await page.goto('/register');
  await page.getByLabel('User name', { exact: true }).fill(userName);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByLabel('Confirm password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Register', exact: true }).click();
  await expect(page).toHaveURL(/\/login\?registered=1$/);
}
