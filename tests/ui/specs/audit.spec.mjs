// Audit scenario: the shop (customers, orders) with an audit trail on both tables, no login. Uses UI_AUDIT_BASE_URL.
// The tests run in file order and add entries: generate the site again before every full run.
import { test, expect } from '@playwright/test';

// helpers.mjs insists on UI_SECURE_BASE_URL, which this scenario does not use.
const uniqueTag = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;

const AUDIT_BASE_URL = process.env.UI_AUDIT_BASE_URL;
if (!AUDIT_BASE_URL) {
  throw new Error('UI_AUDIT_BASE_URL is not set. Start the audit site with tools/generate-site.ps1 -Scenario audit and set this variable to its base URL.');
}

test.use({ baseURL: AUDIT_BASE_URL });
test.describe.configure({ mode: 'serial' });

const entries = (page) => page.locator('table.audit-trail tbody tr').filter({ hasNot: page.locator('td[colspan]') });
const entryFor = (page, text) => entries(page).filter({ hasText: text });
const saveButton = (page) => page.getByRole('button', { name: 'Save', exact: true });

async function applyFilter(page, { table, action, user, record }) {
  await page.goto('/audit-trail');
  if (table !== undefined) await page.getByLabel('Table', { exact: true }).selectOption(table);
  if (action !== undefined) await page.getByLabel('Action', { exact: true }).selectOption(action);
  if (user !== undefined) await page.getByLabel('User', { exact: true }).fill(user);
  if (record !== undefined) await page.getByLabel('Record', { exact: true }).fill(record);
  await page.getByRole('button', { name: 'Apply', exact: true }).click();
}

test.describe('audit trail', () => {
  test('the home menu links to the audit trail, which starts empty', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('link', { name: 'Audit trail', exact: true }).click();
    await expect(page).toHaveURL(/\/audit-trail$/);
    await expect(page.getByRole('heading', { name: 'Audit trail' })).toBeVisible();
    await expect(page.getByText('No entries match.', { exact: true })).toBeVisible();
  });

  test('adding, editing and deleting a customer write one entry each', async ({ page }) => {
    const tag = uniqueTag();
    await page.goto('/customers/add');
    await page.getByLabel('name', { exact: true }).fill(`Audit ${tag}`);
    await page.getByLabel('email', { exact: true }).fill(`audit${tag}@example.com`);
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers$/);

    await page.goto(`/customers?q=${encodeURIComponent(`Audit ${tag}`)}`);
    const row = page.getByRole('row').filter({ hasText: `Audit ${tag}` });
    await row.getByRole('link', { name: 'Edit', exact: true }).click();
    await page.getByLabel('name', { exact: true }).fill(`Audited ${tag}`);
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers$/);

    await page.goto(`/customers?q=${encodeURIComponent(`Audited ${tag}`)}`);
    await page.getByRole('row').filter({ hasText: `Audited ${tag}` }).getByRole('link', { name: 'Delete', exact: true }).click();
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);

    await page.goto('/audit-trail?t=customers');
    // Newest first: delete, update, insert.
    const mine = entries(page).filter({ hasText: tag });
    await expect(mine).toHaveCount(3);
    await expect(mine.nth(0)).toContainText('Deleted');
    await expect(mine.nth(0)).toContainText(`Audited ${tag}`);
    await expect(mine.nth(1)).toContainText('Changed');
    // An update shows the old value struck out and the new one.
    await expect(mine.nth(1).locator('del')).toHaveText(`Audit ${tag}`);
    await expect(mine.nth(1)).toContainText(`Audited ${tag}`);
    await expect(mine.nth(2)).toContainText('Added');
    await expect(mine.nth(2)).toContainText(`audit${tag}@example.com`);
    // The record of the changed row links to its view page; a deleted record has no page.
    await expect(mine.nth(1).getByRole('link')).toHaveAttribute('href', /\/customers\/view\/\d+$/);
    await expect(mine.nth(0).getByRole('link')).toHaveCount(0);
  });

  test('an update that changes nothing writes no entry', async ({ page }) => {
    const tag = uniqueTag();
    const created = await page.request.post('/api/customers', { data: { name: `Same ${tag}`, email: `same${tag}@example.com` } });
    expect(created.status()).toBe(201);
    const id = (await created.json()).id;

    await page.goto(`/customers/edit/${id}`);
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers$/);

    await applyFilter(page, { table: 'customers', record: String(id) });
    await expect(entries(page)).toHaveCount(1);
    await expect(entries(page).first()).toContainText('Added');
  });

  test('the API writes entries too', async ({ page }) => {
    const tag = uniqueTag();
    const created = await page.request.post('/api/orders', { data: { customer_id: 1, total: 7.5, note: `api ${tag}` } });
    expect(created.status()).toBe(201);
    const id = (await created.json()).id;
    const updated = await page.request.put(`/api/orders/${id}`, { data: { id, customer_id: 1, total: 8.5, note: `api ${tag}` } });
    expect(updated.ok()).toBeTruthy();
    expect((await page.request.delete(`/api/orders/${id}`)).ok()).toBeTruthy();

    await applyFilter(page, { table: 'orders', record: String(id) });
    await expect(entries(page)).toHaveCount(3);
    await expect(entries(page).nth(0)).toContainText('Deleted');
    await expect(entries(page).nth(1)).toContainText('Changed');
    await expect(entries(page).nth(1).locator('del')).toHaveText('7.5');
    await expect(entries(page).nth(2)).toContainText('Added');
  });

  test('the table, action, record and user filters narrow the list', async ({ page }) => {
    // Earlier tests left entries for customers and orders, inserts, updates and deletes.
    await applyFilter(page, { table: 'orders' });
    await expect(page.getByLabel('Table', { exact: true })).toHaveValue('orders');
    const total = await entries(page).count();
    expect(total).toBeGreaterThanOrEqual(3);
    for (const text of await entries(page).allInnerTexts()) {
      expect(text).not.toContain('customers');
    }

    await applyFilter(page, { table: 'orders', action: 'delete' });
    await expect(entries(page).first()).toContainText('Deleted');
    expect(await entries(page).count()).toBeLessThan(total);
    for (const text of await entries(page).allInnerTexts()) {
      expect(text).toContain('Deleted');
    }

    // The sites are open (no login), so entries have no user: nobody matches a user name.
    await applyFilter(page, { user: 'nobody-called-this' });
    await expect(page.getByText('No entries match.', { exact: true })).toBeVisible();

    // Clear drops every filter.
    await page.getByRole('link', { name: 'Clear', exact: true }).click();
    await expect(page).toHaveURL(/\/audit-trail$/);
    await expect(page.getByLabel('Table', { exact: true })).toHaveValue('');
    expect(await entries(page).count()).toBeGreaterThanOrEqual(total);
  });

  test('the date filter keeps today and drops other days', async ({ page }) => {
    const today = new Date().toISOString().slice(0, 10);
    await page.goto(`/audit-trail?from=${today}&to=${today}`);
    // The server stores UTC; allow for a run across midnight by only requiring the filter to be applied and valid.
    await expect(page.getByLabel('From', { exact: true })).toHaveValue(today);
    await expect(page.getByLabel('To', { exact: true })).toHaveValue(today);
    expect(await entries(page).count()).toBeGreaterThan(0);

    await page.goto('/audit-trail?from=2001-01-01&to=2001-01-31');
    await expect(page.getByText('No entries match.', { exact: true })).toBeVisible();
  });

  test('the list pages after 50 entries', async ({ page }) => {
    const tag = uniqueTag();
    for (let i = 0; i < 55; i++) {
      const created = await page.request.post('/api/customers', { data: { name: `Bulk ${tag} ${i}`, email: null } });
      expect(created.status()).toBe(201);
    }

    await page.goto('/audit-trail');
    await expect(entries(page)).toHaveCount(50);
    const pager = page.getByRole('navigation').locator('ul.pagination');
    await expect(pager).toBeVisible();
    // Newest first: the last bulk customer is on the first page.
    await expect(entryFor(page, `Bulk ${tag} 54`)).toHaveCount(1);

    await pager.getByRole('link', { name: '2', exact: true }).click();
    await expect(page).toHaveURL(/\/audit-trail\?page=2$/);
    expect(await entries(page).count()).toBeGreaterThan(0);
    await expect(entryFor(page, `Bulk ${tag} 54`)).toHaveCount(0);

    // Paging keeps the filters.
    await page.goto('/audit-trail?t=customers&a=insert');
    await page.getByRole('navigation').locator('ul.pagination').getByRole('link', { name: '2', exact: true }).click();
    await expect(page).toHaveURL(/t=customers/);
    await expect(page).toHaveURL(/a=insert/);
    await expect(page).toHaveURL(/page=2/);
    await expect(page.getByLabel('Action', { exact: true })).toHaveValue('insert');

    // A page past the end shows the last page instead of an error.
    const far = await page.request.get('/audit-trail?page=999');
    expect(far.status()).toBe(200);
  });

  test('values from the database are HTML-encoded on the page', async ({ page }) => {
    const tag = uniqueTag();
    const payload = `<b id="x${tag}">bold</b>`;
    const created = await page.request.post('/api/customers', { data: { name: payload, email: null } });
    expect(created.status()).toBe(201);

    await page.goto('/audit-trail?t=customers&a=insert');
    await expect(page.locator(`#x${tag}`)).toHaveCount(0);
    await expect(entryFor(page, payload).first()).toBeVisible();
  });
});
