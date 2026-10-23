// Relations scenario: master/detail links and multi-page forms. Uses UI_RELATIONS_BASE_URL.
// Seed: customers C1..C5 (C5 has no orders), orders 1..30 with customer C((n-1)%4+1) (C1 8, C2 8, C3 7, C4 7 orders),
// order_lines 1..3 for orders 1..10, categories Tools/Toys/Books, products Product 01..12 (category rotates 1,2,3).
// The tests modify data and run in file order: regenerate the site before every full run.
import { test, expect } from '@playwright/test';

// helpers.mjs insists on UI_SECURE_BASE_URL, which this scenario does not use.
const uniqueTag = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1000)}`;

const RELATIONS_BASE_URL = process.env.UI_RELATIONS_BASE_URL;
if (!RELATIONS_BASE_URL) {
  throw new Error('UI_RELATIONS_BASE_URL is not set. Start the relations site with tools/generate-site.ps1 -Scenario relations and set this variable to its base URL.');
}

test.use({ baseURL: RELATIONS_BASE_URL });
test.describe.configure({ mode: 'serial' });

const pane = (page, id) => page.locator(`#pk-pane-${id}`);
const grid = (page) => page.locator('[data-md-grid]');
const tab = (page, name) => page.getByRole('tab', { name, exact: true });
const gridRows = (page) => grid(page).locator('tbody tr');
const saveButton = (page) => page.getByRole('button', { name: 'Save', exact: true });

async function json(page, url) {
  const response = await page.request.get(url);
  return { status: response.status(), body: response.status() === 200 ? await response.json() : null };
}

test.describe('customers: multi-page forms', () => {
  test('view page is an accordion: first page open, others expand on click', async ({ page }) => {
    await page.goto('/customers/view/C1');
    const main = page.getByRole('button', { name: 'Main', exact: true });
    const contact = page.getByRole('button', { name: 'Contact', exact: true });
    const notes = page.getByRole('button', { name: 'Notes', exact: true });
    await expect(main).toHaveAttribute('aria-expanded', 'true');
    await expect(contact).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByText('Customer 1', { exact: true })).toBeVisible();
    await expect(page.getByText('555-0001', { exact: true })).toBeHidden();

    await contact.click();
    await expect(contact).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByText('555-0001', { exact: true })).toBeVisible();
    // This accordion keeps one page open at a time.
    await expect(main).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByText('Customer 1', { exact: true })).toBeHidden();

    await notes.click();
    await expect(page.getByText('Notes 1', { exact: true })).toBeVisible();
    await expect(contact).toHaveAttribute('aria-expanded', 'false');

    // Clicking the open page collapses it.
    await notes.click();
    await expect(page.getByText('Notes 1', { exact: true })).toBeHidden();
  });

  test('add page is tabs: tabs switch panes and a new customer is saved from all of them', async ({ page }) => {
    const tag = uniqueTag();
    await page.goto('/customers/add');
    await expect(tab(page, 'Main')).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByLabel('name', { exact: true })).toBeVisible();
    await expect(page.getByLabel('phone', { exact: true })).toBeHidden();
    await expect(page.getByLabel('notes', { exact: true })).toBeHidden();
    // Tabs, not a wizard: there are no Previous and Next buttons and Save is always there.
    await expect(page.getByRole('button', { name: 'Next', exact: true })).toHaveCount(0);
    await expect(saveButton(page)).toBeVisible();

    await page.getByLabel('id', { exact: true }).fill(`T${tag}`);
    await page.getByLabel('name', { exact: true }).fill(`Tabbed ${tag}`);
    await tab(page, 'Contact').click();
    await expect(tab(page, 'Contact')).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByLabel('phone', { exact: true })).toBeVisible();
    await expect(page.getByLabel('name', { exact: true })).toBeHidden();
    await page.getByLabel('phone', { exact: true }).fill('777-1234');
    await tab(page, 'Notes').click();
    await expect(page.getByLabel('notes', { exact: true })).toBeVisible();
    await page.getByLabel('notes', { exact: true }).fill('from tabs');
    await tab(page, 'Main').click();
    await expect(page.getByLabel('name', { exact: true })).toHaveValue(`Tabbed ${tag}`);
    await saveButton(page).click();

    await expect(page).toHaveURL(/\/customers$/);
    const saved = await json(page, `/api/customers/T${tag}`);
    expect(saved.body).toMatchObject({ name: `Tabbed ${tag}`, phone: '777-1234', notes: 'from tabs' });
    // Remove the customer again so the list counts stay the same.
    await page.goto(`/customers/delete/T${tag}`);
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
  });

  test('edit page is a wizard: Next and Previous move between steps and Save shows only on the last step', async ({ page }) => {
    await page.goto('/customers/edit/C3');
    const next = page.getByRole('button', { name: 'Next', exact: true });
    const previous = page.getByRole('button', { name: 'Previous', exact: true });
    await expect(page.getByText('Step 1 of 3')).toBeVisible();
    await expect(previous).toBeDisabled();
    await expect(page.getByLabel('name', { exact: true })).toBeVisible();

    await next.click();
    await expect(page.getByText('Step 2 of 3')).toBeVisible();
    await expect(page.getByLabel('phone', { exact: true })).toBeVisible();
    await expect(page.getByLabel('name', { exact: true })).toBeHidden();
    await expect(previous).toBeEnabled();

    await previous.click();
    await expect(page.getByText('Step 1 of 3')).toBeVisible();
    await expect(page.getByLabel('name', { exact: true })).toBeVisible();

    await next.click();
    await next.click();
    await expect(page.getByText('Step 3 of 3')).toBeVisible();
    await expect(page.getByLabel('notes', { exact: true })).toBeVisible();
    await expect(next).toBeHidden();
    await expect(saveButton(page)).toBeVisible();

    await page.getByLabel('notes', { exact: true }).fill('wizard notes');
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers$/);
    expect((await json(page, '/api/customers/C3')).body).toMatchObject({ name: 'Customer 3', notes: 'wizard notes' });
  });

  // Regression: BootScript used to collect the Save buttons before they were parsed, so Save was never hidden (fixed: collected per step).
  test('wizard: Save is hidden until the last step', async ({ page }) => {
    await page.goto('/customers/edit/C3');
    await expect(saveButton(page)).toBeHidden();
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(saveButton(page)).toBeHidden();
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(saveButton(page)).toBeVisible();
  });

  test('a wizard does not leave a step that has an invalid field', async ({ page }) => {
    await page.goto('/customers/edit/C3');
    await page.getByLabel('name', { exact: true }).fill('');
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(page.getByText('Step 1 of 3')).toBeVisible();
    await expect(page.getByLabel('name', { exact: true })).toBeVisible();
  });

  test('a server validation error marks its page with a badge and shows that page', async ({ page }) => {
    await page.goto('/customers/edit/C3');
    // Go to the last step first, then post an empty name without the browser validation.
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await page.getByRole('button', { name: 'Next', exact: true }).click();
    await expect(saveButton(page)).toBeVisible();
    await page.locator('form[method=post]').evaluate((form) => {
      form.noValidate = true;
      form.querySelector('#name').value = '';
    });
    await saveButton(page).click();

    await expect(page).toHaveURL(/\/customers\/edit\/C3$/);
    // The page that holds the field is shown and the others stay hidden.
    await expect(page.locator('button[data-pk-target="pk-pane-0"]')).toHaveAttribute('aria-selected', 'true');
    await expect(pane(page, 0)).toBeVisible();
    await expect(pane(page, 1)).toBeHidden();
    await expect(pane(page, 0).locator('.pk-page-errors')).toContainText(/name/i);
    await expect(page.getByText('Step 1 of 3')).toBeVisible();
    // Only that page has the badge.
    await expect(page.locator('button[data-pk-target="pk-pane-0"] .badge.text-bg-danger')).toHaveText('1');
    await expect(page.locator('button[data-pk-target="pk-pane-1"] .badge.text-bg-danger')).toHaveCount(0);
    await expect(page.locator('button[data-pk-target="pk-pane-2"] .badge.text-bg-danger')).toHaveCount(0);
    // The error is listed under its page and not repeated in a general alert.
    await expect(page.locator('.alert-danger')).toHaveCount(1);
    expect((await json(page, '/api/customers/C3')).body.name).toBe('Customer 3');
  });

  test('an invalid add form with tabs shows the error page and badge', async ({ page }) => {
    await page.goto('/customers/add');
    await page.getByLabel('id', { exact: true }).fill('X1');
    await tab(page, 'Notes').click();
    await page.locator('form[method=post]').evaluate((form) => { form.noValidate = true; });
    await saveButton(page).click();
    await expect(page.locator('button[data-pk-target="pk-pane-0"]')).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('button[data-pk-target="pk-pane-0"] .badge.text-bg-danger')).toBeVisible();
    await expect(pane(page, 0).locator('.pk-page-errors')).toBeVisible();
    await expect(page.getByLabel('id', { exact: true })).toHaveValue('X1');
    expect((await json(page, '/api/customers/X1')).status).toBe(404);
  });
});

test.describe('customers: orders grid (master/detail)', () => {
  test('the grid on the view page pages with dp0 links and has row action links', async ({ page }) => {
    await page.goto('/customers/view/C1');
    await expect(grid(page).getByRole('heading', { name: 'orders' })).toBeVisible();
    await expect(grid(page).locator('thead th')).toHaveText(['id', 'order_date', 'status', '']);
    await expect(gridRows(page)).toHaveCount(5);
    await expect(gridRows(page).first()).toContainText('2024-01-01');
    await expect(gridRows(page).first()).toContainText('open');

    const pager = grid(page).getByRole('navigation');
    await expect(pager.getByRole('link')).toHaveText(['1', '2']);
    await expect(pager.getByRole('link', { name: '2', exact: true })).toHaveAttribute('href', '/customers/view/C1?dp0=2');
    await pager.getByRole('link', { name: '2', exact: true }).click();
    await expect(page).toHaveURL(/dp0=2$/);
    await expect(gridRows(page)).toHaveCount(3);
    await expect(gridRows(page).first()).toContainText('2024-01-21');
    await expect(pager.locator('.page-item.active')).toHaveText('2');

    // A page number that is too large is clamped to the last page.
    await page.goto('/customers/view/C1?dp0=99');
    await expect(gridRows(page)).toHaveCount(3);
    await page.goto('/customers/view/C1');

    const row = gridRows(page).first();
    await expect(row.getByRole('link', { name: 'View', exact: true })).toHaveAttribute('href', '/orders/view/1');
    await expect(row.getByRole('link', { name: 'Edit', exact: true })).toHaveAttribute('href', '/orders/edit/1?returnUrl=%2Fcustomers%2Fview%2FC1');
    await expect(row.getByRole('link', { name: 'Delete', exact: true })).toHaveAttribute('href', '/orders/delete/1?returnUrl=%2Fcustomers%2Fview%2FC1');
    await expect(grid(page).getByRole('link', { name: 'Add', exact: true })).toHaveAttribute('href', '/orders/add?m_customer_id=C1&returnUrl=%2Fcustomers%2Fview%2FC1');

    // The links work: the edit link comes back to the customer page when the order is saved.
    await row.getByRole('link', { name: 'Edit', exact: true }).click();
    await expect(page).toHaveURL(/\/orders\/edit\/1\?returnUrl=/);
    await page.getByLabel('status', { exact: true }).fill('open');
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers\/view\/C1$/);
  });

  test('a customer without orders shows an empty grid', async ({ page }) => {
    await page.goto('/customers/view/C5');
    await expect(gridRows(page)).toHaveCount(1);
    await expect(gridRows(page)).toContainText('No records.');
    await expect(grid(page).getByRole('navigation')).toHaveCount(0);
  });

  test('add order from the customer edit page fixes the customer and returns to the customer page', async ({ page }) => {
    const tag = uniqueTag();
    await page.goto('/customers/edit/C2');
    await expect(gridRows(page)).toHaveCount(5);
    await grid(page).getByRole('link', { name: 'Add', exact: true }).click();
    await expect(page).toHaveURL(/\/orders\/add\?m_customer_id=C2&returnUrl=%2Fcustomers%2Fedit%2FC2$/);
    await expect(page.getByRole('heading', { name: 'Add orders' })).toBeVisible();

    // The customer is shown read-only, and the order form is split into pills.
    const customer = page.locator('input[type=text][readonly]');
    await expect(customer).toHaveValue('C2');
    await expect(page.locator('input[name=customer_id]')).toHaveAttribute('type', 'hidden');
    await expect(page.getByLabel('customer_id', { exact: true })).toHaveCount(0);
    await expect(page.locator('[data-pk-pages="pills"]')).toBeVisible();
    await expect(tab(page, 'Header')).toHaveAttribute('aria-selected', 'true');

    await page.getByLabel('order_date', { exact: true }).fill(`2030-${tag}`);
    await page.getByLabel('status', { exact: true }).fill('new');
    await saveButton(page).click();

    // ShowOnAdd of the order -> lines link wins over the return URL: the new order's edit page opens first and carries returnUrl.
    await expect(page).toHaveURL(/\/orders\/edit\/\d+\?returnUrl=%2Fcustomers%2Fedit%2FC2$/);
    await expect(page.getByLabel('order_date', { exact: true })).toHaveValue(`2030-${tag}`);
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers\/edit\/C2$/);
    // 9 orders now: the new one is on the second grid page.
    await page.goto('/customers/edit/C2?dp0=2');
    await expect(gridRows(page)).toHaveCount(4);
    await expect(gridRows(page).last()).toContainText(`2030-${tag}`);
    await expect(gridRows(page).last()).toContainText('new');
  });

  test('a tampered preset for a missing customer returns a 404 page', async ({ page }) => {
    const response = await page.goto('/orders/add?m_customer_id=NOPE&returnUrl=%2Fcustomers');
    expect(response.status()).toBe(404);
    await expect(page.getByLabel('order_date', { exact: true })).toHaveCount(0);
    // A preset with an empty value is refused as well.
    expect((await page.goto('/orders/add?m_customer_id=')).status()).toBe(404);
    // A forged preset on the posted form does not create an order either.
    const before = (await json(page, '/api/orders')).body.length;
    await page.goto('/orders/add?m_customer_id=C1');
    await page.locator('form[method=post]').evaluate((form) => {
      form.action = '/orders/add?m_customer_id=NOPE';
    });
    await saveButton(page).click();
    expect((await json(page, '/api/orders')).body.length).toBe(before);
  });

  test('deleting a customer with orders is refused: message shown and Delete disabled', async ({ page }) => {
    await page.goto('/customers');
    await page.locator('tr', { hasText: 'Customer 1' }).getByRole('link', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/customers\/delete\/C1$/);
    await expect(page.getByRole('alert')).toHaveText('Cannot delete: 8 related orders record(s) exist.');
    await expect(page.getByRole('button', { name: 'Delete', exact: true })).toBeDisabled();
    await page.getByRole('link', { name: 'Cancel', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
    expect((await json(page, '/api/customers/C1')).status).toBe(200);
  });

  test('a delete post for a customer with orders is refused by the server', async ({ page }) => {
    await page.goto('/customers/delete/C1');
    // Enable the disabled button to post anyway.
    await page.getByRole('button', { name: 'Delete', exact: true }).evaluate((b) => { b.disabled = false; });
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page.getByText('Cannot delete', { exact: false }).first()).toBeVisible();
    expect((await json(page, '/api/customers/C1')).status).toBe(200);
    expect((await json(page, '/api/orders')).body.filter((o) => o.customerId === 'C1')).toHaveLength(8);
  });

  test('deleting a customer without orders works', async ({ page }) => {
    await page.goto('/customers/delete/C5');
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Delete', exact: true })).toBeEnabled();
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/customers$/);
    await expect(page.getByRole('cell', { name: 'Customer 5', exact: true })).toHaveCount(0);
    expect((await json(page, '/api/customers/C5')).status).toBe(404);
  });

  test('the expandable row on the customers list toggles the orders grid', async ({ page }) => {
    await page.goto('/customers');
    const customerRow = page.locator('tr', { hasText: 'Customer 3' }).first();
    const toggle = customerRow.getByRole('button', { name: 'orders', exact: true });
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const detail = page.locator('tr[data-md-row]');
    await expect(detail).toHaveCount(1);
    await expect(detail.locator('tbody tr')).toHaveCount(5);
    await expect(detail.getByRole('cell', { name: '2024-01-03', exact: true })).toBeVisible();
    // The grid pages inside the row without leaving the list.
    await detail.getByRole('link', { name: '2', exact: true }).click();
    await expect(detail.locator('tbody tr')).toHaveCount(2);
    await expect(page).toHaveURL(/\/customers$/);

    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await expect(detail).toHaveCount(0);
  });
});

test.describe('orders: lines (composite key), previews and cascade', () => {
  test('order edit shows the lines grid on the Lines tab', async ({ page }) => {
    await page.goto('/orders/edit/2');
    await expect(tab(page, 'Header')).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByLabel('customer_id', { exact: true })).toHaveValue('C2');
    await expect(grid(page)).toBeHidden();
    await tab(page, 'Lines').click();
    await expect(grid(page)).toBeVisible();
    await expect(gridRows(page)).toHaveCount(3);
    await expect(gridRows(page).first()).toContainText('Item 2-1');
    await expect(page.getByLabel('customer_id', { exact: true })).toBeHidden();
    // The lines grid is hosted on the page, so it appears once.
    await expect(page.locator('[data-md-grid]')).toHaveCount(1);
    const row = gridRows(page).first();
    await expect(row.getByRole('link', { name: 'View', exact: true })).toHaveAttribute('href', '/order-lines/view/2/1');
    await expect(row.getByRole('link', { name: 'Edit', exact: true })).toHaveAttribute('href', '/order-lines/edit/2/1?returnUrl=%2Forders%2Fedit%2F2');
    await expect(row.getByRole('link', { name: 'Delete', exact: true })).toHaveAttribute('href', '/order-lines/delete/2/1?returnUrl=%2Forders%2Fedit%2F2');
  });

  test('add a line from the order edit page with the composite key preset and return', async ({ page }) => {
    await page.goto('/orders/edit/2');
    await tab(page, 'Lines').click();
    await grid(page).getByRole('link', { name: 'Add', exact: true }).click();
    await expect(page).toHaveURL(/\/order-lines\/add\?m_order_id=2&returnUrl=%2Forders%2Fedit%2F2$/);
    await expect(page.locator('input[readonly]')).toHaveValue('2');
    await expect(page.locator('input[name=order_id]')).toHaveAttribute('type', 'hidden');
    await page.getByLabel('line_no', { exact: true }).fill('4');
    await page.getByLabel('product', { exact: true }).fill('Extra part');
    await page.getByLabel('qty', { exact: true }).fill('7');
    await page.getByLabel('price', { exact: true }).fill('9');
    await saveButton(page).click();

    await expect(page).toHaveURL(/\/orders\/edit\/2$/);
    await tab(page, 'Lines').click();
    await expect(gridRows(page)).toHaveCount(4);
    await expect(gridRows(page).last()).toContainText('Extra part');
    expect((await json(page, '/api/order-lines/2/4')).body).toMatchObject({ orderId: 2, lineNo: 4, product: 'Extra part', qty: 7, price: 9 });

    // Editing the line from the grid returns to the order.
    await gridRows(page).last().getByRole('link', { name: 'Edit', exact: true }).click();
    await expect(page).toHaveURL(/\/order-lines\/edit\/2\/4\?returnUrl=/);
    await page.getByLabel('qty', { exact: true }).fill('8');
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/orders\/edit\/2$/);
    expect((await json(page, '/api/order-lines/2/4')).body.qty).toBe(8);
  });

  // Regression: REAL columns used to get <input type=number> without step, so the browser rejected 9.5 (fixed: step="any").
  test('a REAL column accepts a fractional value in the browser', async ({ page }) => {
    await page.goto('/order-lines/add?m_order_id=2&returnUrl=%2Forders%2Fedit%2F2');
    await page.getByLabel('price', { exact: true }).fill('9.5');
    expect(await page.getByLabel('price', { exact: true }).evaluate((input) => input.checkValidity())).toBe(true);
  });

  test('adding a line with an existing composite key is refused', async ({ page }) => {
    await page.goto('/order-lines/add?m_order_id=2&returnUrl=%2Forders%2Fedit%2F2');
    await page.getByLabel('line_no', { exact: true }).fill('1');
    await page.getByLabel('product', { exact: true }).fill('Duplicate');
    await page.getByLabel('qty', { exact: true }).fill('1');
    await saveButton(page).click();
    await expect(page).not.toHaveURL(/\/orders\/edit\/2$/);
    expect((await json(page, '/api/order-lines/2/1')).body.product).toBe('Item 2-1');
  });

  test('the offcanvas preview on the orders list opens and shows the lines', async ({ page }) => {
    await page.goto('/orders');
    const offcanvas = page.locator('#pk-offcanvas');
    await page.locator('tr', { hasText: '2024-01-05' }).getByRole('link', { name: 'order_lines', exact: true }).click();
    await expect(offcanvas).toBeVisible();
    await expect(offcanvas.locator('tbody tr')).toHaveCount(3);
    await expect(offcanvas.getByRole('cell', { name: 'Item 5-1', exact: true })).toBeVisible();
    await expect(offcanvas.getByRole('cell', { name: 'Item 5-3', exact: true })).toBeVisible();
    await offcanvas.getByRole('button', { name: 'Close' }).click();
    await expect(offcanvas).toBeHidden();
    // An order without lines shows the empty grid.
    await page.locator('tr', { hasText: '2024-01-20' }).getByRole('link', { name: 'order_lines', exact: true }).click();
    await expect(offcanvas).toBeVisible();
    await expect(offcanvas).toContainText('No records.');
  });

  test('deleting an order cascades to its lines', async ({ page }) => {
    expect((await json(page, '/api/orders/6/order-lines')).body).toHaveLength(3);
    await page.goto('/orders/delete/6');
    await expect(page.getByRole('status')).toHaveText('This will also delete 3 record(s) in order_lines.');
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/orders$/);
    expect((await json(page, '/api/orders/6')).status).toBe(404);
    expect((await json(page, '/api/order-lines')).body.filter((l) => l.orderId === 6)).toHaveLength(0);
    expect((await json(page, '/api/order-lines/6/1')).status).toBe(404);
    // The other orders keep their lines.
    expect((await json(page, '/api/order-lines')).body.filter((l) => l.orderId === 7)).toHaveLength(3);
  });

  test('deleting an order from a customer grid returns to the customer page', async ({ page }) => {
    await page.goto('/customers/view/C4');
    await expect(gridRows(page)).toHaveCount(5);
    await gridRows(page).filter({ hasText: '2024-01-04' }).getByRole('link', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/orders\/delete\/4\?returnUrl=%2Fcustomers%2Fview%2FC4$/);
    await expect(page.getByRole('status')).toHaveText('This will also delete 3 record(s) in order_lines.');
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/customers\/view\/C4$/);
    await expect(gridRows(page)).toHaveCount(5);
    await expect(grid(page).getByRole('cell', { name: '2024-01-04', exact: true })).toHaveCount(0);
    expect((await json(page, '/api/orders/4')).status).toBe(404);
  });

  // Regression: MasterDetailKit.Here used to escape the "?" of the query (return URL /customers/view/C4%3Fdp0=2, a 404).
  test('a return URL keeps the query string of the master page (dp0=2)', async ({ page }) => {
    await page.goto('/customers/view/C4?dp0=2');
    await expect(gridRows(page)).toHaveCount(1);
    await gridRows(page).first().getByRole('link', { name: 'Edit', exact: true }).click();
    await page.getByLabel('status', { exact: true }).fill('same');
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers\/view\/C4\?dp0=2$/);
  });

  test('ShowOnAdd: after adding an order from the list the browser lands on its edit page', async ({ page }) => {
    const tag = uniqueTag();
    await page.goto('/orders');
    await page.getByRole('link', { name: 'Add', exact: true }).click();
    await expect(page).toHaveURL(/\/orders\/add$/);
    await page.getByLabel('customer_id', { exact: true }).fill('C3');
    await page.getByLabel('order_date', { exact: true }).fill(`2031-${tag}`);
    await expect(tab(page, 'Header')).toHaveAttribute('aria-selected', 'true');
    await saveButton(page).click();

    await expect(page).toHaveURL(/\/orders\/edit\/\d+$/);
    await expect(page.getByRole('heading', { name: 'Edit orders' })).toBeVisible();
    await expect(page.getByLabel('order_date', { exact: true })).toHaveValue(`2031-${tag}`);
    await tab(page, 'Lines').click();
    await expect(gridRows(page)).toHaveCount(1);
    await expect(gridRows(page)).toContainText('No records.');
    await expect(grid(page).getByRole('link', { name: 'Add', exact: true })).toBeVisible();
  });
});

test.describe('categories: SetNull and modal preview', () => {
  test('the modal preview opens and lists the products of the category', async ({ page }) => {
    await page.goto('/categories');
    const modal = page.locator('#pk-modal');
    await page.locator('tr', { hasText: 'Toys' }).getByRole('link', { name: 'products', exact: true }).click();
    await expect(modal).toBeVisible();
    await expect(modal.locator('tbody tr')).toHaveCount(4);
    await expect(modal.getByRole('cell', { name: 'Product 02', exact: true })).toBeVisible();
    await expect(modal.getByRole('cell', { name: 'Product 11', exact: true })).toBeVisible();
    await expect(modal.getByRole('cell', { name: 'Product 01', exact: true })).toHaveCount(0);
    await modal.getByRole('button', { name: 'Close' }).click();
    await expect(modal).toBeHidden();
  });

  test('deleting a category sets the category of its products to empty', async ({ page }) => {
    const before = (await json(page, '/api/products')).body;
    expect(before.filter((p) => p.categoryId === 1)).toHaveLength(4);
    await page.goto('/categories/delete/1');
    // SetNull is silent: no blocker and no "also delete" notice.
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByRole('status')).toHaveCount(0);
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/categories$/);
    expect((await json(page, '/api/categories/1')).status).toBe(404);

    const after = (await json(page, '/api/products')).body;
    // No product was deleted; the four products of the category are kept with an empty category.
    expect(after).toHaveLength(before.length);
    for (const id of [1, 4, 7, 10]) {
      expect(after.find((p) => p.id === id).categoryId).toBeNull();
    }
    expect(after.find((p) => p.id === 2).categoryId).toBe(2);
    await page.goto('/products/view/1');
    await expect(page.getByText('Product 01', { exact: true })).toBeVisible();
  });
});

test.describe('without JavaScript', () => {
  test.use({ javaScriptEnabled: false });

  test('multi-page forms and the view page show every page in sequence', async ({ page }) => {
    for (const url of ['/customers/add', '/customers/edit/C1']) {
      await page.goto(url);
      // Tabs and the wizard controls need scripts and are hidden; every pane is shown, one after the other.
      await expect(page.locator('.pk-pane')).toHaveCount(3);
      await expect(page.locator('.pk-pane').nth(0)).toBeVisible();
      await expect(page.locator('.pk-pane').nth(1)).toBeVisible();
      await expect(page.locator('.pk-pane').nth(2)).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Main', exact: true })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Contact', exact: true })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Notes', exact: true })).toBeVisible();
      await expect(page.getByLabel('name', { exact: true })).toBeVisible();
      await expect(page.getByLabel('phone', { exact: true })).toBeVisible();
      await expect(page.getByLabel('notes', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Next', exact: true })).toBeHidden();
      await expect(page.getByRole('button', { name: 'Previous', exact: true })).toBeHidden();
      await expect(saveButton(page)).toBeVisible();
    }

    await page.goto('/customers/view/C1');
    await expect(page.getByText('Customer 1', { exact: true })).toBeVisible();
    await expect(page.getByText('555-0001', { exact: true })).toBeVisible();
    await expect(page.getByText('Notes 1', { exact: true })).toBeVisible();

    // The order form (tabs) shows the lines page too, with its grid.
    await page.goto('/orders/edit/2');
    await expect(page.getByLabel('customer_id', { exact: true })).toBeVisible();
    await expect(grid(page)).toBeVisible();
  });

  test('a plain form post still saves without scripts', async ({ page }) => {
    await page.goto('/customers/edit/C4');
    await page.getByLabel('city', { exact: true }).fill('Nowhere');
    await saveButton(page).click();
    await expect(page).toHaveURL(/\/customers$/);
    expect((await json(page, '/api/customers/C4')).body.city).toBe('Nowhere');
  });
});
