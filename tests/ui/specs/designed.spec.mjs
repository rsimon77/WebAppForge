// Designed scenario: per-page designs (cards, modal add/edit/view, offcanvas delete), lookup, formats, row styles,
// pattern validation and en/hu localization. Uses UI_DESIGNED_BASE_URL. Seed: Product 01..30, price 10 + n,
// categories rotate Tools/Toys/Books, every 7th product discontinued.
import { test, expect } from '@playwright/test';
import { uniqueTag } from './helpers.mjs';

const DESIGNED_BASE_URL = process.env.UI_DESIGNED_BASE_URL;
if (!DESIGNED_BASE_URL) {
  throw new Error('UI_DESIGNED_BASE_URL is not set. Start the designed site with tools/generate-site.ps1 -Scenario designed and set this variable to its base URL.');
}

test.use({ baseURL: DESIGNED_BASE_URL });

const cards = (page) => page.locator('.card');
const cardFor = (page, text) => page.locator('.card').filter({ hasText: text });
const modal = (page) => page.locator('#pk-modal');
const offcanvas = (page) => page.locator('#pk-offcanvas');
// The server formats currency with its own culture, so accept both separators.
const money = (whole, cents = '00') => new RegExp(`${whole}[.,]${cents}`);

async function addProduct(page, name, price = '12.5', category = 'Toys') {
  await page.goto('/products?pageSize=50');
  await page.getByRole('link', { name: 'Add', exact: true }).click();
  await expect(modal(page)).toBeVisible();
  await modal(page).getByLabel('name', { exact: true }).fill(name);
  await modal(page).getByLabel('price', { exact: true }).fill(price);
  await modal(page).getByLabel('category_id', { exact: true }).selectOption({ label: category });
  await modal(page).getByRole('button', { name: 'Save product', exact: true }).click();
  await expect(modal(page)).toBeHidden();
  await expect(page.getByText('Product saved.')).toBeVisible();
  await page.goto('/products?pageSize=50');
  await expect(cardFor(page, name)).toBeVisible();
}

test.describe('designed list', () => {
  test('renders cards with title, subtitle and footer', async ({ page }) => {
    await page.goto('/products');
    await expect(page).toHaveTitle('Product catalog');
    await expect(page.getByRole('heading', { name: 'Product catalog', level: 1 })).toBeVisible();
    await expect(page.getByText('All products', { exact: true })).toBeVisible();
    await expect(page.locator('#design-footer')).toHaveText('Footer text');
    await expect(page.locator('table')).toHaveCount(0);
    await expect(cards(page)).toHaveCount(20);
    // Columns in the configured order: name, price, category_id; the id and other fields are not listed.
    const first = cardFor(page, 'Product 01');
    await expect(first.locator('dt')).toHaveText(['name', 'price', 'category_id']);
    await expect(first.locator('dd').nth(1)).toHaveText(money('11'));
    await expect(first.locator('dd').nth(2)).toHaveText('Tools');
  });

  test('page size selector changes the count and a hand-edited size falls back', async ({ page }) => {
    await page.goto('/products');
    const select = page.getByLabel('Rows per page');
    await expect(select.locator('option')).toHaveText(['10', '20', '50']);
    await expect(select).toHaveValue('/products?pageSize=20');

    await select.selectOption({ label: '10' });
    await expect(page).toHaveURL(/pageSize=10/);
    await expect(cards(page)).toHaveCount(10);

    await page.getByLabel('Rows per page').selectOption({ label: '50' });
    await expect(page).toHaveURL(/pageSize=50/);
    await expect(cards(page)).toHaveCount(30);

    await page.getByLabel('Rows per page').selectOption({ label: '20' });
    await expect(page).toHaveURL(/pageSize=20/);
    await expect(cards(page)).toHaveCount(20);

    await page.goto('/products?pageSize=7');
    await expect(cards(page)).toHaveCount(20);
    await expect(page.getByLabel('Rows per page')).toHaveValue('/products?pageSize=20');
  });

  test('sorting by query string orders the cards', async ({ page }) => {
    await page.goto('/products?sort=price&dir=desc');
    await expect(cards(page).first()).toContainText('Product 30');
    await page.goto('/products?sort=price&dir=asc');
    await expect(cards(page).first()).toContainText('Product 01');
    await page.goto('/products?sort=name&dir=desc&pageSize=10');
    await expect(cards(page).first()).toContainText('Product 30');
    // A column that is not sortable or does not exist is ignored instead of failing.
    const response = await page.goto('/products?sort=nope&dir=asc');
    expect(response.status()).toBe(200);
    await expect(cards(page)).toHaveCount(20);
  });

  test('card layout offers sort links', async ({ page }) => {
    // The card layout has no column headers, so a sort bar above the cards offers the sortable columns.
    await page.goto('/products');
    await expect(page.locator('a[href*="sort="]')).not.toHaveCount(0);
  });

  test('lookup: list shows the category name and the form lists the names', async ({ page }) => {
    await page.goto('/products');
    await expect(cardFor(page, 'Product 02')).toContainText('Toys');
    await expect(cardFor(page, 'Product 03')).toContainText('Books');
    await expect(page.locator('.card dd', { hasText: /^[123]$/ })).toHaveCount(0);

    await page.getByRole('link', { name: 'Add', exact: true }).click();
    const select = modal(page).getByLabel('category_id', { exact: true });
    await expect(select).toBeVisible();
    await expect(select.locator('option')).toHaveText(['', 'Books', 'Tools', 'Toys']);
  });

  test('discontinued rows are styled as danger', async ({ page }) => {
    // Product 07 is discontinued. The RowStyle rule gives the card border-danger bg-danger-subtle.
    await page.goto('/products');
    await expect(cardFor(page, 'Product 07')).toHaveClass(/danger/);
    await expect(cardFor(page, 'Product 01')).not.toHaveClass(/danger/);
  });

  test('discontinued shows as a badge on the view page', async ({ page }) => {
    await page.goto('/products');
    await cardFor(page, 'Product 07').getByRole('link', { name: 'View', exact: true }).click();
    await expect(modal(page).locator('.badge.text-bg-danger')).toHaveText('Discontinued');
    await modal(page).getByRole('button', { name: 'Close' }).click();
    await cardFor(page, 'Product 01').getByRole('link', { name: 'View', exact: true }).click();
    await expect(modal(page)).toContainText('Product 01');
    await expect(modal(page).locator('.badge')).toHaveCount(0);
  });
});

test.describe('designed categories (compact list)', () => {
  test('compact table with sticky header and row numbers', async ({ page }) => {
    await page.goto('/categories');
    const table = page.locator('table');
    await expect(table).toHaveClass(/table-sm/);
    await expect(table).toHaveClass(/table-sticky/);
    const css = await page.evaluate(() => [...document.querySelectorAll('style')].map((e) => e.textContent).join(' '));
    expect(css).toContain('position:sticky');
    await expect(table.locator('thead th').first()).toHaveText('#');
    await expect(table.locator('tbody tr')).toHaveCount(3);
    await expect(table.locator('tbody tr').nth(0).locator('td').first()).toHaveText('1');
    await expect(table.locator('tbody tr').nth(2).locator('td').first()).toHaveText('3');
    await expect(page.locator('.card')).toHaveCount(0);
    // A table whose fields are not customized has plain headers (sort links only come with field customization).
    // Plain links, no modal opening, on a page with default add/edit designs.
    await expect(page.getByRole('link', { name: 'Add', exact: true })).toHaveAttribute('href', '/categories/add');
    await expect(page.locator('[data-open]')).toHaveCount(0);
  });
});

test.describe('designed modals', () => {
  test('Add opens a modal without navigating and shows a two-column form', async ({ page }) => {
    await page.goto('/products');
    await page.getByRole('link', { name: 'Add', exact: true }).click();
    await expect(modal(page)).toBeVisible();
    await expect(page).toHaveURL(/\/products$/);
    await expect(modal(page).getByRole('heading', { name: 'Add products' })).toBeVisible();
    // TwoColumns: each field sits in a col-md-6.
    await expect(modal(page).locator('form .row > .col-md-6')).toHaveCount(5);
    await expect(modal(page).getByLabel('name', { exact: true })).toBeVisible();
    await expect(modal(page).getByLabel('price', { exact: true })).toBeVisible();
    await expect(modal(page).getByLabel('discontinued', { exact: true })).toBeVisible();
    await expect(modal(page).getByRole('button', { name: 'Save product', exact: true })).toBeVisible();
    await expect(modal(page).getByRole('link', { name: 'Cancel', exact: true })).toBeVisible();
    await modal(page).getByRole('link', { name: 'Cancel', exact: true }).click();
    await expect(modal(page)).toBeHidden();
    await expect(page).toHaveURL(/\/products$/);
  });

  test('empty name: the form is invalid and the server error shows inside the modal', async ({ page }) => {
    await page.goto('/products');
    await page.getByRole('link', { name: 'Add', exact: true }).click();
    const form = modal(page).locator('form');
    await modal(page).getByRole('button', { name: 'Save product', exact: true }).click();
    // The browser blocks the submit first (required attribute); the modal stays open.
    await expect(modal(page).getByLabel('name', { exact: true })).toHaveJSProperty('validity.valueMissing', true);
    await expect(modal(page)).toBeVisible();
    // Skip browser validation to reach the server-side check.
    await form.evaluate((f) => { f.noValidate = true; });
    await modal(page).getByRole('button', { name: 'Save product', exact: true }).click();
    await expect(modal(page).getByText('name is required.')).toBeVisible();
    await expect(modal(page)).toBeVisible();
    await expect(page).toHaveURL(/\/products$/);
  });

  test('bad character: the pattern message shows inside the modal', async ({ page }) => {
    await page.goto('/products');
    await page.getByRole('link', { name: 'Add', exact: true }).click();
    await modal(page).getByLabel('name', { exact: true }).fill('Bad@Name');
    await modal(page).locator('form').evaluate((f) => { f.noValidate = true; });
    await modal(page).getByRole('button', { name: 'Save product', exact: true }).click();
    await expect(modal(page).getByText('Name has invalid characters.')).toBeVisible();
    await expect(modal(page).getByLabel('name', { exact: true })).toHaveValue('Bad@Name');
    await expect(page).toHaveURL(/\/products$/);
    // Nothing was saved.
    await page.goto('/products?pageSize=50');
    await expect(cards(page)).toHaveCount(30);
  });

  test('valid product closes the modal and flashes "Product saved." once', async ({ page }) => {
    const name = `Gadget ${uniqueTag()}`;
    await page.goto('/products?pageSize=50');
    await page.getByRole('link', { name: 'Add', exact: true }).click();
    await modal(page).getByLabel('name', { exact: true }).fill(name);
    await modal(page).getByLabel('price', { exact: true }).fill('99.5');
    await modal(page).getByLabel('category_id', { exact: true }).selectOption({ label: 'Books' });
    await modal(page).getByRole('button', { name: 'Save product', exact: true }).click();

    await expect(modal(page)).toBeHidden();
    await expect(page).toHaveURL(/\/products/);
    await expect(page.getByText('Product saved.')).toHaveCount(1);
    await expect(page.getByText('Product saved.')).toBeVisible();
    await expect(cardFor(page, name)).toContainText('Books');
    await expect(cardFor(page, name)).toContainText(money('99', '50'));

    // The flash is consumed: a reload no longer shows it.
    await page.reload();
    await expect(page.getByText('Product saved.')).toHaveCount(0);
    await expect(cardFor(page, name)).toBeVisible();
  });

  test('Edit modal changes the price and the card shows the new currency value', async ({ page }) => {
    const name = `Editable ${uniqueTag()}`;
    await addProduct(page, name, '20');
    await expect(cardFor(page, name)).toContainText(money('20'));
    await cardFor(page, name).getByRole('link', { name: 'Edit', exact: true }).click();
    await expect(modal(page).getByRole('heading', { name: 'Edit products' })).toBeVisible();
    await expect(modal(page).getByLabel('name', { exact: true })).toHaveValue(name);
    await expect(modal(page).getByLabel('category_id', { exact: true })).toHaveValue(/\d/);
    await expect(modal(page).locator('form .row > .col-md-6')).toHaveCount(5);
    await modal(page).getByLabel('price', { exact: true }).fill('1234.5');
    await modal(page).getByRole('button', { name: 'Save product', exact: true }).click();
    await expect(modal(page)).toBeHidden();
    await expect(page.getByText('Product saved.')).toHaveCount(1);
    await page.goto('/products?pageSize=50');
    // 1,234.50 or 1 234,50 depending on the server culture; digits and the cents are what matters.
    await expect(cardFor(page, name)).toContainText(/1\D?234[.,]50/);
  });

  test('View opens in a modal', async ({ page }) => {
    await page.goto('/products');
    await cardFor(page, 'Product 02').getByRole('link', { name: 'View', exact: true }).click();
    await expect(modal(page)).toBeVisible();
    await expect(page).toHaveURL(/\/products$/);
    await expect(modal(page)).toContainText('Product 02');
    await expect(modal(page)).toContainText('Toys');
    await expect(modal(page)).toContainText(money('12'));
    await expect(modal(page).getByRole('link', { name: 'Back to list' })).toBeVisible();
    await modal(page).getByRole('button', { name: 'Close' }).click();
    await expect(modal(page)).toBeHidden();
  });

  test('Delete opens an offcanvas; Cancel closes it; Delete removes the record', async ({ page }) => {
    const name = `Doomed ${uniqueTag()}`;
    await addProduct(page, name);
    const before = await cards(page).count();

    await cardFor(page, name).getByRole('link', { name: 'Delete', exact: true }).click();
    await expect(offcanvas(page)).toBeVisible();
    await expect(page).toHaveURL(/\/products/);
    await expect(offcanvas(page)).toContainText('Delete this record? This cannot be undone.');
    await offcanvas(page).getByRole('link', { name: 'Cancel', exact: true }).click();
    await expect(offcanvas(page)).toBeHidden();
    await expect(cardFor(page, name)).toBeVisible();

    await cardFor(page, name).getByRole('link', { name: 'Delete', exact: true }).click();
    await expect(offcanvas(page)).toBeVisible();
    await offcanvas(page).getByRole('button', { name: 'Delete', exact: true }).click();
    // The partial post answers {"ok":true} and the page reloads itself (same URL, pageSize=50).
    await expect(cardFor(page, name)).toHaveCount(0);
    await expect(offcanvas(page)).toBeHidden();
    await expect(cards(page)).toHaveCount(before - 1);
  });
});

test.describe('designed without JavaScript', () => {
  test('the plain add page works as a normal form post', async ({ browser }) => {
    const context = await browser.newContext({ baseURL: DESIGNED_BASE_URL, javaScriptEnabled: false });
    try {
      const page = await context.newPage();
      const name = `NoScript ${uniqueTag()}`;
      await page.goto('/products');
      // The link carries data-open but without scripts it is a plain link to the full page.
      await page.getByRole('link', { name: 'Add', exact: true }).click();
      await expect(page).toHaveURL(/\/products\/add$/);
      await expect(page.getByRole('heading', { name: 'Add products' })).toBeVisible();
      await expect(page.locator('#pk-modal')).toHaveCount(0);
      await page.getByLabel('name', { exact: true }).fill(name);
      await page.getByLabel('price', { exact: true }).fill('7.25');
      await page.getByLabel('category_id', { exact: true }).selectOption({ label: 'Tools' });
      await page.getByRole('button', { name: 'Save product', exact: true }).click();
      await expect(page).toHaveURL(/\/products$/);
      await page.goto('/products?pageSize=50');
      await expect(page.locator('.card').filter({ hasText: name })).toContainText('Tools');
    } finally {
      await context.close();
    }
  });
});

test.describe('designed language switcher', () => {
  test('hu shows Hungarian captions and buttons, persists, and en switches back', async ({ page, context }) => {
    await page.goto('/products');
    await expect(page.getByRole('link', { name: 'Add', exact: true })).toBeVisible();
    await expect(cardFor(page, 'Product 01').locator('dt').first()).toHaveText('name');

    await page.getByRole('link', { name: 'HU', exact: true }).click();
    await expect(page).toHaveURL(/\/products$/);
    await expect(page.locator('html')).toHaveAttribute('lang', 'hu');
    await expect(page.getByRole('link', { name: 'Hozzáadás', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Keresés', exact: true })).toBeVisible();
    await expect(cardFor(page, 'Product 01').locator('dt')).toHaveText(['Név', 'Ár', 'Kategória']);
    await expect(cardFor(page, 'Product 01').getByRole('link', { name: 'Szerkesztés', exact: true })).toBeVisible();
    await expect(cardFor(page, 'Product 01').getByRole('link', { name: 'Törlés', exact: true })).toBeVisible();

    // The cookie persists across a reload and a new navigation.
    const cookies = await context.cookies();
    expect(cookies.find((c) => c.name === 'lang')?.value).toBe('hu');
    await page.reload();
    await expect(page.getByRole('link', { name: 'Hozzáadás', exact: true })).toBeVisible();
    await page.goto('/categories');
    await expect(page.getByRole('link', { name: 'Hozzáadás', exact: true })).toBeVisible();

    // Hungarian also applies in the modal opened from the list.
    await page.goto('/products');
    await page.getByRole('link', { name: 'Hozzáadás', exact: true }).click();
    await expect(modal(page).getByLabel('Név', { exact: true })).toBeVisible();
    await expect(modal(page).getByLabel('Kategória', { exact: true })).toBeVisible();

    await page.goto('/products');
    await page.getByRole('link', { name: 'EN', exact: true }).click();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('link', { name: 'Add', exact: true })).toBeVisible();
    await expect(cardFor(page, 'Product 01').locator('dt').first()).toHaveText('name');
    await page.reload();
    await expect(page.getByRole('link', { name: 'Add', exact: true })).toBeVisible();
  });
});
