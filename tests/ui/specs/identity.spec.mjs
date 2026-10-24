// Identity scenario: ASP.NET Core Identity user store. The site created its AspNet* tables and the administrator "admin"
// (password Admin-pass-42, a test value passed by generate-site.ps1). notes are owned, secrets need level 5, and the project
// table "users" keeps its own /users pages beside /admin/users. Uses UI_IDENTITY_BASE_URL.
// The tests run in file order and add users: generate the site again before every full run.
import { test, expect } from '@playwright/test';

// helpers.mjs insists on UI_SECURE_BASE_URL, which this scenario does not use.
const uniqueTag = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;

const IDENTITY_BASE_URL = process.env.UI_IDENTITY_BASE_URL;
if (!IDENTITY_BASE_URL) {
  throw new Error('UI_IDENTITY_BASE_URL is not set. Start the identity site with tools/generate-site.ps1 -Scenario identity and set this variable to its base URL.');
}

test.use({ baseURL: IDENTITY_BASE_URL });
test.describe.configure({ mode: 'serial' });

const ADMIN_PASSWORD = 'Admin-pass-42';
const PASSWORD = 'Some-pass-42';

async function logIn(page, userName, password) {
  await page.goto('/login');
  await page.getByLabel('User name', { exact: true }).fill(userName);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Log in', exact: true }).click();
}

async function logInAsAdmin(page) {
  await logIn(page, 'admin', ADMIN_PASSWORD);
  await expect(page).toHaveURL(/\/$/);
}

async function logOut(page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
}

const userRow = (page, name) => page.getByRole('row').filter({ has: page.getByRole('cell', { name, exact: true }) });

async function addUser(page, name, level, roles = []) {
  await page.goto('/admin/users/add');
  await page.getByLabel('User name', { exact: true }).fill(name);
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
  await page.getByLabel('Confirm password', { exact: true }).fill(PASSWORD);
  await page.getByLabel('User level', { exact: true }).fill(String(level));
  for (const role of roles) await page.getByLabel(role, { exact: true }).check();
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  // A new user opens on their edit page.
  await expect(page.getByRole('heading', { name: `Edit user ${name}` })).toBeVisible();
  await expect(page.getByText('The record has been created.')).toBeVisible();
}

async function openUser(page, name) {
  await page.goto(`/admin/users?q=${encodeURIComponent(name)}`);
  await userRow(page, name).getByRole('link', { name: 'Edit', exact: true }).click();
  await expect(page.getByRole('heading', { name: `Edit user ${name}` })).toBeVisible();
}

test.describe('identity users and roles', () => {
  test('the administrator signs in and sees the user and role pages beside the users table', async ({ page }) => {
    await logInAsAdmin(page);
    await expect(page.getByRole('link', { name: 'users', exact: true })).toHaveAttribute('href', '/users');
    await page.getByRole('link', { name: 'Users', exact: true }).click();
    await expect(page).toHaveURL(/\/admin\/users$/);
    await expect(userRow(page, 'admin')).toContainText('100');

    // The project table named users still has its own list.
    await page.goto('/users');
    await expect(page.getByText('a plain row')).toBeVisible();
  });

  test('a role with level 5 opens the secrets for its members only', async ({ page }) => {
    const tag = uniqueTag();
    await logInAsAdmin(page);
    await page.goto('/admin/roles');
    await page.getByLabel('Role name', { exact: true }).fill(`Managers${tag}`);
    await page.getByLabel('User level', { exact: true }).fill('5');
    await page.getByRole('button', { name: 'Add role', exact: true }).click();
    await expect(page.getByRole('row').filter({ hasText: `Managers${tag}` })).toContainText('5');

    await addUser(page, `carol${tag}`, 1, [`Managers${tag}`]);
    await addUser(page, `dave${tag}`, 1);
    await page.goto(`/admin/users?q=carol${tag}`);
    await expect(userRow(page, `carol${tag}`)).toContainText(`Managers${tag}`);
    await page.goto('/');
    await logOut(page);

    await logIn(page, `carol${tag}`, PASSWORD);
    await page.goto('/secrets');
    await expect(page.getByText('The launch plan')).toBeVisible();
    await logOut(page);

    await logIn(page, `dave${tag}`, PASSWORD);
    await expect(page.getByRole('link', { name: 'Users', exact: true })).toHaveCount(0);
    const secrets = await page.goto('/secrets');
    expect(secrets.status()).toBe(403);
    const admin = await page.goto('/admin/users');
    expect(admin.status()).toBe(403);
  });

  test('the administrator edits, locks, unlocks, sets the password of and deletes a user', async ({ page, browser }) => {
    const tag = uniqueTag();
    const name = `erin${tag}`;
    await logInAsAdmin(page);
    await addUser(page, name, 0);

    await openUser(page, name);
    await page.getByLabel('E-mail', { exact: true }).fill(`${name}@example.test`);
    await page.getByLabel('User level', { exact: true }).fill('2');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await page.goto(`/admin/users?q=${name}`);
    await expect(userRow(page, name)).toContainText(`${name}@example.test`);
    await expect(userRow(page, name)).toContainText('2');

    await openUser(page, name);
    await page.getByRole('button', { name: 'Lock', exact: true }).click();
    await page.goto(`/admin/users?q=${name}`);
    await expect(userRow(page, name)).toContainText('Yes');

    // A locked user cannot sign in, even with the right password.
    const other = await browser.newPage({ baseURL: IDENTITY_BASE_URL });
    await logIn(other, name, PASSWORD);
    await expect(other.getByText('Too many failed attempts. Try again later.')).toBeVisible();

    await openUser(page, name);
    await page.getByRole('button', { name: 'Unlock', exact: true }).click();
    await openUser(page, name);
    await page.getByLabel('New password', { exact: true }).fill('Fresh-pass-44');
    await page.getByLabel('Confirm new password', { exact: true }).fill('Fresh-pass-44');
    await page.getByRole('button', { name: 'Set password', exact: true }).click();
    await expect(page.getByText('The password has been set.')).toBeVisible();

    await logIn(other, name, 'Fresh-pass-44');
    await expect(other).toHaveURL(/\/$/);
    await other.close();

    await openUser(page, name);
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await page.goto(`/admin/users?q=${name}`);
    await expect(userRow(page, name)).toHaveCount(0);
  });

  test('the administrator cannot delete their own account', async ({ page }) => {
    await logInAsAdmin(page);
    await openUser(page, 'admin');
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page.getByText('You cannot do this to your own account.')).toBeVisible();
  });

  test('a visitor registers, owns their notes, changes the password and logs out', async ({ page }) => {
    const tag = uniqueTag();
    const name = `fay${tag}`;
    await page.goto('/register');
    await page.getByLabel('User name', { exact: true }).fill(name);
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password', { exact: true }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Register', exact: true }).click();
    await expect(page).toHaveURL(/\/login\?registered=1$/);
    await expect(page.getByText('Registration complete. You can now log in.')).toBeVisible();

    await logIn(page, name, PASSWORD);
    await expect(page).toHaveURL(/\/$/);
    await page.goto('/notes/add');
    await page.getByLabel('title', { exact: true }).fill(`Note ${tag}`);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/notes$/);
    // The owner column is set by the site and not shown; the owner check is the other user's list below.
    await expect(page.getByRole('row').filter({ hasText: `Note ${tag}` })).toBeVisible();

    await page.goto('/changepassword');
    await page.getByLabel('Current password', { exact: true }).fill('wrong-current');
    await page.getByLabel('New password', { exact: true }).fill('Other-pass-43');
    await page.getByLabel('Confirm new password', { exact: true }).fill('Other-pass-43');
    await page.getByRole('button', { name: 'Change password', exact: true }).click();
    await expect(page.getByText('The current password is not correct.')).toBeVisible();
    await page.getByLabel('Current password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('New password', { exact: true }).fill('Other-pass-43');
    await page.getByLabel('Confirm new password', { exact: true }).fill('Other-pass-43');
    await page.getByRole('button', { name: 'Change password', exact: true }).click();
    await expect(page.getByText('Your password has been changed.')).toBeVisible();

    await page.goto('/');
    await logOut(page);
    await logIn(page, name, PASSWORD);
    await expect(page.getByText('Invalid username or password.')).toBeVisible();
    await logIn(page, name, 'Other-pass-43');
    await expect(page).toHaveURL(/\/$/);

    // Another user does not see the note.
    await logOut(page);
    await logInAsAdmin(page);
    await page.goto('/notes');
    await expect(page.getByText(`Note ${tag}`)).toHaveCount(0);
  });

  test('three wrong passwords lock the account', async ({ page }) => {
    const tag = uniqueTag();
    await logInAsAdmin(page);
    await addUser(page, `gus${tag}`, 0);
    await logOut(page);
    for (let i = 0; i < 2; i++) {
      await logIn(page, `gus${tag}`, 'wrong-password');
      await expect(page.getByText('Invalid username or password.')).toBeVisible();
    }

    await logIn(page, `gus${tag}`, 'wrong-password');
    await expect(page.getByText('Too many failed attempts. Try again later.')).toBeVisible();
    await logIn(page, `gus${tag}`, PASSWORD);
    await expect(page.getByText('Too many failed attempts. Try again later.')).toBeVisible();
  });
});
