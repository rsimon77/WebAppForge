// Grid tools on the sales list of the reports scenario: the filter and sort panel, the quick filter, chips and reset.
// Uses UI_REPORTS_BASE_URL. Seed (tools/harness/Program.cs): sale n = 1..24 has region North, South, West in turn, falls in month
// (n-1)/4+1 of 2024 on day 3, 10, 17 or 24, has amount n * 10.5, is paid when n is even and is noted "Sale nn".
// The tests only read data, so they can run in any order.
import { test, expect } from '@playwright/test';

const REPORTS_BASE_URL = process.env.UI_REPORTS_BASE_URL;
if (!REPORTS_BASE_URL) {
  throw new Error('UI_REPORTS_BASE_URL is not set. Start the reports site with tools/generate-site.ps1 -Scenario reports and set this variable to its base URL.');
}

test.use({ baseURL: REPORTS_BASE_URL });

const sales = Array.from({ length: 24 }, (_, i) => {
  const n = i + 1;
  const month = Math.floor((n - 1) / 4) + 1;
  const day = ((n - 1) % 4) * 7 + 3;
  return {
    region: ['North', 'South', 'West'][(n - 1) % 3],
    date: `2024-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`,
    amount: n * 10.5,
    paid: n % 2 === 0,
    note: `Sale ${String(n).padStart(2, '0')}`,
  };
});

const notes = (rows) => rows.map((s) => s.note);
const listed = (page) => page.locator('table tbody tr td').filter({ hasText: /^Sale \d\d$/ });
const open = (page) => page.locator('details.gk-builder > summary').click();

test.describe('grid tools', () => {
  test('the panel shows only the operators of the chosen column and a second value for between', async ({ page }) => {
    await page.goto('/sales');
    await open(page);
    const field = page.locator('select[name="ff0"]');
    const op = page.locator('select[name="fo0"]');
    await field.selectOption('amount');
    await expect(op.locator('option:not([hidden])')).toHaveText(['=', '≠', '>', '≥', '<', '≤', 'between', 'is empty', 'is not empty']);
    await expect(page.locator('input[name="fv0"]')).toHaveAttribute('type', 'number');
    await expect(page.locator('input[name="fx0"]')).toBeHidden();
    await op.selectOption('bt');
    await expect(page.locator('input[name="fx0"]')).toBeVisible();

    await field.selectOption('day');
    await expect(page.locator('input[name="fv0"]')).toHaveAttribute('type', 'date');
    await field.selectOption('paid');
    await expect(op.locator('option:not([hidden])')).toHaveText(['is yes', 'is no', 'is empty', 'is not empty']);
    await expect(page.locator('input[name="fv0"]')).toBeHidden();
  });

  test('conditions joined by any and two sort levels filter and order the list', async ({ page }) => {
    await page.goto('/sales');
    await open(page);
    await page.locator('select[name="fm"]').selectOption('any');
    await page.locator('select[name="ff0"]').selectOption('amount');
    await page.locator('select[name="fo0"]').selectOption('ge');
    await page.locator('input[name="fv0"]').fill('241.5');
    await page.locator('select[name="s0"]').selectOption('paid');
    await page.locator('select[name="d0"]').selectOption('desc');
    await page.locator('select[name="s1"]').selectOption('amount');
    await page.getByRole('button', { name: 'Apply', exact: true }).last().click();

    // Only one condition was filled in, so any/all makes no difference yet: sales 23 and 24, paid first.
    await expect(listed(page)).toHaveText(['Sale 24', 'Sale 23']);
    await expect(page.locator('.gk-chip')).toHaveText([/amount ≥ 241\.5/, /Sort by paid ↓, amount ↑/]);

    // A second condition from the form that is now pre-filled: OR adds the January sales.
    await open(page);
    await page.locator('select[name="ff1"]').selectOption('day');
    await page.locator('select[name="fo1"]').selectOption('lt');
    await page.locator('input[name="fv1"]').fill('2024-02-01');
    await page.getByRole('button', { name: 'Apply', exact: true }).last().click();
    const expected = sales.filter((s) => s.amount >= 241.5 || s.date < '2024-02-01')
      .sort((a, b) => Number(b.paid) - Number(a.paid) || a.amount - b.amount);
    await expect(listed(page)).toHaveText(notes(expected));
  });

  test('a chip removes its condition and reset clears everything', async ({ page }) => {
    await page.goto('/sales?qf=-west&ff0=paid&fo0=yes&s0=amount&d0=desc');
    const paidNotWest = sales.filter((s) => s.paid && s.region !== 'West').sort((a, b) => b.amount - a.amount);
    await expect(listed(page)).toHaveText(notes(paidNotWest));
    await expect(page.getByRole('searchbox', { name: 'Quick filter' })).toHaveValue('-west');

    await page.locator('.gk-chip').filter({ hasText: 'paid is yes' }).getByRole('link', { name: 'Remove' }).click();
    await expect(listed(page)).toHaveText(notes(sales.filter((s) => s.region !== 'West').sort((a, b) => b.amount - a.amount)));

    await page.getByRole('link', { name: 'Reset', exact: true }).click();
    await expect(page).toHaveURL(/\/sales$/);
    await expect(listed(page)).toHaveCount(24);
    await expect(page.locator('.gk-reset')).toHaveClass(/disabled/);
  });

  test('the quick filter matches every term, phrases and columns', async ({ page }) => {
    await page.goto('/sales');
    const box = page.getByRole('searchbox', { name: 'Quick filter' });
    await box.fill('region:north 2024-03');
    await box.press('Enter');
    await expect(listed(page)).toHaveText(notes(sales.filter((s) => s.region === 'North' && s.date.startsWith('2024-03'))));
    await box.fill('"sale 1"');
    await box.press('Enter');
    await expect(listed(page)).toHaveText(notes(sales.filter((s) => s.note.startsWith('Sale 1'))));
  });

  test('the detail grid of a region filters, sorts by a header and resets', async ({ page }) => {
    await page.goto('/regions/view/N');
    const grid = page.locator('#md-grid-0');
    const rows = grid.locator('tbody tr td').filter({ hasText: /^Sale \d\d$/ });
    const north = sales.filter((s) => s.region === 'North');
    await expect(rows).toHaveText(notes(north));

    const box = grid.getByRole('searchbox', { name: 'Quick filter' });
    await box.fill('"sale 1" -"sale 10"');
    await box.press('Enter');
    await expect(page).toHaveURL(/#md-grid-0$/);
    const filtered = north.filter((s) => s.note.startsWith('Sale 1') && s.note !== 'Sale 10');
    await expect(rows).toHaveText(notes(filtered));

    await grid.getByRole('link', { name: 'amount', exact: true }).click();
    await grid.getByRole('link', { name: /^amount/ }).click();
    await expect(rows).toHaveText(notes([...filtered].sort((a, b) => b.amount - a.amount)));
    await expect(box).toHaveValue('"sale 1" -"sale 10"');

    await grid.getByRole('link', { name: 'Reset', exact: true }).click();
    await expect(rows).toHaveText(notes(north));
    await expect(grid.locator('.gk-reset')).toHaveClass(/disabled/);
  });
});
