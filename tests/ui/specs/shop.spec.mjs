// Shop scenario: customers and orders tables, no login. Uses UI_BASE_URL.
import { test, expect } from '@playwright/test';
import { uniqueTag } from './helpers.mjs';

const tbodyRows = (page) => page.locator('tbody tr');
const rowFor = (page, text) => page.getByRole('row').filter({ hasText: text });

// The list is 20 rows per page in id order, and new rows land on page 2, so tests find a row
// through the quick search on its unique text instead.
async function searchCustomers(page, text) {
  await page.goto(`/customers?q=${encodeURIComponent(text)}`);
  await expect(rowFor(page, text)).toBeVisible();
}

async function addCustomer(page, name, email) {
  await page.goto('/customers/add');
  await page.getByLabel('name', { exact: true }).fill(name);
  await page.getByLabel('email', { exact: true }).fill(email);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page).toHaveURL(/\/customers$/);
  await searchCustomers(page, name);
}

test.describe('shop site', () => {
  test('home lists the tables', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Web Application' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'customers', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'orders', exact: true })).toBeVisible();
  });

  test('customers list shows rows', async ({ page }) => {
    await page.goto('/customers');
    await expect(page.getByRole('heading', { name: 'customers', exact: true })).toBeVisible();
    await expect(tbodyRows(page)).toHaveCount(20);
    await expect(rowFor(page, 'Ann Adams')).toContainText('ann@example.com');
  });

  test('search form filters by field', async ({ page }) => {
    await page.goto('/customers/search');
    await page.getByLabel('name', { exact: true }).fill('Bob Brown');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page).toHaveURL(/f_name=Bob/);
    await expect(tbodyRows(page)).toHaveCount(1);
    await expect(rowFor(page, 'Bob Brown')).toBeVisible();
  });

  test('quick search filters across columns', async ({ page }) => {
    await page.goto('/customers/search');
    await page.getByLabel('Quick search', { exact: true }).fill('Ann Adams');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page).toHaveURL(/q=Ann/);
    await expect(tbodyRows(page)).toHaveCount(1);
    await expect(rowFor(page, 'Ann Adams')).toBeVisible();
  });

  test('paging shows the next page', async ({ page }) => {
    await page.goto('/customers');
    await page.getByRole('navigation').getByRole('link', { name: '2', exact: true }).click();
    await expect(page).toHaveURL(/\?page=2$/);
    // Other tests add customers, so only check rows that are on page 2 and not on page 1.
    await expect(rowFor(page, 'Customer 21')).toBeVisible();
    await expect(rowFor(page, 'Ann Adams')).toHaveCount(0);
  });

  test('add a customer and see it in the list', async ({ page }) => {
    const tag = uniqueTag();
    await addCustomer(page, `Zoe ${tag}`, `zoe${tag}@example.com`);
    await expect(rowFor(page, `Zoe ${tag}`)).toContainText(`zoe${tag}@example.com`);
  });

  test('edit a customer', async ({ page }) => {
    const tag = uniqueTag();
    await addCustomer(page, `Edit ${tag}`, `edit${tag}@example.com`);
    await rowFor(page, `Edit ${tag}`).getByRole('link', { name: 'Edit', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Edit customers' })).toBeVisible();
    await page.getByLabel('name', { exact: true }).fill(`Edited ${tag}`);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
    await searchCustomers(page, `Edited ${tag}`);
    await expect(rowFor(page, `Edit ${tag}`)).toHaveCount(0);
  });

  test('copy a customer', async ({ page }) => {
    const tag = uniqueTag();
    await addCustomer(page, `Copy ${tag}`, `copy${tag}@example.com`);
    await rowFor(page, `Copy ${tag}`).getByRole('link', { name: 'Copy', exact: true }).click();
    await expect(page.getByLabel('name', { exact: true })).toHaveValue(`Copy ${tag}`);
    await page.getByLabel('name', { exact: true }).fill(`Copied ${tag}`);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
    await searchCustomers(page, `Copied ${tag}`);
    await searchCustomers(page, `Copy ${tag}`);
  });

  test('delete a customer through the confirm page', async ({ page }) => {
    const tag = uniqueTag();
    await addCustomer(page, `Gone ${tag}`, `gone${tag}@example.com`);
    await rowFor(page, `Gone ${tag}`).getByRole('link', { name: 'Delete', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Delete customers' })).toBeVisible();
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
    await page.goto(`/customers?q=${encodeURIComponent(`Gone ${tag}`)}`);
    await expect(tbodyRows(page)).toHaveCount(0);
  });

  test('view page shows the stored values', async ({ page }) => {
    const tag = uniqueTag();
    await addCustomer(page, `View ${tag}`, `view${tag}@example.com`);
    await rowFor(page, `View ${tag}`).getByRole('link', { name: 'View', exact: true }).click();
    const details = page.locator('dl');
    await expect(details.locator('dd', { hasText: `View ${tag}` })).toBeVisible();
    await expect(details.locator('dd', { hasText: `view${tag}@example.com` })).toBeVisible();
  });

  test('Cancel on the delete confirm page returns to the list', async ({ page }) => {
    const tag = uniqueTag();
    await addCustomer(page, `Keep ${tag}`, `keep${tag}@example.com`);
    await rowFor(page, `Keep ${tag}`).getByRole('link', { name: 'Delete', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Delete customers' })).toBeVisible();
    await page.getByRole('link', { name: 'Cancel', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
    await searchCustomers(page, `Keep ${tag}`);
  });
});
