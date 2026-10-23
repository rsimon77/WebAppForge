// Secure scenario: record-level security (notes.owner = signed-in user name). Uses UI_SECURE_BASE_URL.
import { test, expect } from '@playwright/test';
import { SECURE_BASE_URL, SEEDED_PASSWORD, logIn, uniqueTag } from './helpers.mjs';

test.use({ baseURL: SECURE_BASE_URL });

// Seed data (tools/harness/Program.cs): note 1 belongs to alice, note 2 belongs to bob.

test('alice sees only her notes', async ({ page }) => {
  await logIn(page, 'alice', SEEDED_PASSWORD);
  await page.goto('/notes');
  await expect(page.getByRole('row').filter({ hasText: 'Alice secret' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Bob secret' })).toHaveCount(0);
});

test("alice cannot open bob's note", async ({ page }) => {
  await logIn(page, 'alice', SEEDED_PASSWORD);
  const response = await page.goto('/notes/view/2');
  expect(response?.status()).toBe(404);
  await expect(page.getByText('Bob secret')).toHaveCount(0);
});

test('adding a note stores alice as the owner', async ({ page }) => {
  const title = `Alice note ${uniqueTag()}`;
  await logIn(page, 'alice', SEEDED_PASSWORD);
  await page.goto('/notes/add');
  await page.getByLabel('title', { exact: true }).fill(title);
  await page.getByLabel('body', { exact: true }).fill('owned by alice');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(/\/notes$/);
  await expect(page.getByRole('row').filter({ hasText: title })).toBeVisible();

  // The owner column is hidden from the HTML pages, so check it through the JSON API for the same session.
  const api = await page.request.get(`${SECURE_BASE_URL}/api/notes`);
  expect(api.ok()).toBeTruthy();
  const notes = await api.json();
  const added = notes.find((note) => note.title === title);
  expect(added?.owner).toBe('alice');

  // Bob does not see it.
  await page.goto('/');
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await logIn(page, 'bob', SEEDED_PASSWORD);
  await page.goto('/notes');
  await expect(page.getByRole('row').filter({ hasText: title })).toHaveCount(0);
});
