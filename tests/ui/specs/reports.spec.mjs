// Reports scenario: summary with filters and drill-down, chart, crosstab, calendar and dashboard. Uses UI_REPORTS_BASE_URL.
// Seed (tools/harness/Program.cs): sale n = 1..24 has region North, South, West in turn, falls in month (n-1)/4+1 of 2024 on day
// 3, 10, 17 or 24, has amount n * 10.5, is paid when n is even and is noted "Sale nn". The expectations below come from that rule.
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
    n,
    region: ['North', 'South', 'West'][(n - 1) % 3],
    month: `2024-${String(month).padStart(2, '0')}`,
    date: `2024-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`,
    amount: n * 10.5,
    paid: n % 2 === 0,
    note: `Sale ${String(n).padStart(2, '0')}`,
  };
});

const money = (value) => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const sum = (rows) => rows.reduce((total, row) => total + row.amount, 0);
const grandTotal = (page) => page.locator('.report-summary tfoot tr');

test.describe('summary report', () => {
  test('the home page lists the reports under their menu group', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Reports', exact: true })).toBeVisible();
    for (const name of ['Sales by region', 'Amount per month', 'Sales by region and payment', 'Sales calendar', 'Overview']) {
      await expect(page.getByRole('link', { name, exact: true })).toBeVisible();
    }
  });

  test('groups by region and month with subtotals and a grand total', async ({ page }) => {
    await page.goto('/reports/sales');
    const north = sales.filter((s) => s.region === 'North');
    const northJanuary = north.filter((s) => s.month === '2024-01');
    const first = page.locator('.report-summary tbody tr').first();
    await expect(first).toHaveText(new RegExp(`North\\s*2024-01\\s*${northJanuary.length}\\s*${money(sum(northJanuary))}`));
    await expect(page.locator('.report-subtotal').filter({ hasText: 'Total North' })).toContainText(`${north.length}${money(sum(north))}`);
    await expect(grandTotal(page)).toContainText(`Grand total${sales.length}${money(sum(sales))}`);
  });

  test('the date range and the pick lists filter the report and are kept in the form', async ({ page }) => {
    await page.goto('/reports/sales');
    await page.getByLabel('From (day)').fill('2024-02-01');
    await page.getByLabel('To (day)').fill('2024-03-31');
    await page.getByLabel('region', { exact: true }).selectOption({ label: 'South' });
    await page.getByLabel('paid', { exact: true }).selectOption({ label: 'Yes' });
    await page.getByRole('button', { name: 'Apply', exact: true }).click();

    await expect(page).toHaveURL(/from=2024-02-01/);
    const expected = sales.filter((s) => s.region === 'South' && s.paid && s.date >= '2024-02-01' && s.date <= '2024-03-31');
    await expect(grandTotal(page)).toContainText(`Grand total${expected.length}${money(sum(expected))}`);
    await expect(page.getByLabel('region', { exact: true })).toHaveValue('S');
    await expect(page.getByLabel('From (day)')).toHaveValue('2024-02-01');

    await page.getByRole('link', { name: 'Clear', exact: true }).click();
    await expect(grandTotal(page)).toContainText(`Grand total${sales.length}`);
  });

  test('a number opens the rows behind it, with the filters, and a row opens its view page', async ({ page }) => {
    await page.goto('/reports/sales?f_paid=0');
    const unpaidWestApril = sales.filter((s) => s.region === 'West' && s.month === '2024-04' && !s.paid);
    // Unpaid sales only: April has North and West rows, and each number links to the rows of its region and month.
    const links = page.locator('.report-summary a[href="/reports/sales/rows?f_paid=0&k0=s%3AWest&k1=s%3A2024-04"]');
    await expect(links).toHaveText([String(unpaidWestApril.length), money(sum(unpaidWestApril))]);
    await links.first().click();

    await expect(page).toHaveURL(/\/reports\/sales\/rows\?/);
    await expect(page.locator('.report-keys')).toHaveText('region: West · day: 2024-04');
    await expect(page.locator('.report-count')).toHaveText(`${unpaidWestApril.length} rows`);
    await expect(page.locator('.report-rows tbody tr')).toHaveCount(unpaidWestApril.length);
    await expect(page.locator('.report-rows thead th')).toHaveText(['note', 'day', 'amount']);

    await page.getByRole('link', { name: unpaidWestApril[0].note, exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/sales/view/${unpaidWestApril[0].n}$`));
    await page.goBack();
    await page.getByRole('link', { name: /Back to the report/ }).click();
    await expect(page).toHaveURL(/\/reports\/sales\?f_paid=0$/);
  });
});

test.describe('chart, crosstab, calendar and dashboard', () => {
  test('the chart groups Unix-time stamps by month and its bars open the rows of that month', async ({ page }) => {
    await page.goto('/reports/monthly');
    const months = [...new Set(sales.map((s) => s.month))];
    await expect(page.locator('.report-data tbody tr')).toHaveCount(months.length);
    const march = sales.filter((s) => s.month === '2024-03');
    await expect(page.locator('.report-data tbody tr').filter({ hasText: '2024-03' })).toContainText(money(sum(march)));

    await page.locator('.report-chart a').nth(2).click();
    await expect(page).toHaveURL(/\/reports\/monthly\/rows\?k0=s%3A2024-03$/);
    await expect(page.locator('.report-count')).toHaveText(`${march.length} rows`);
  });

  test('crosstab cells and totals open their rows', async ({ page }) => {
    await page.goto('/reports/grid');
    const southPaid = sales.filter((s) => s.region === 'South' && s.paid);
    const south = page.locator('.report-crosstab tbody tr').filter({ hasText: 'South' });
    await expect(south.locator('td')).toHaveText([String(sales.filter((s) => s.region === 'South' && !s.paid).length), String(southPaid.length), '8']);
    await south.locator('td').nth(1).getByRole('link').click();
    await expect(page.locator('.report-keys')).toHaveText('region: South · paid: 1');
    await expect(page.locator('.report-rows tbody tr')).toHaveCount(southPaid.length);

    await page.goto('/reports/grid');
    await page.locator('.report-crosstab tfoot td').last().getByRole('link').click();
    await expect(page.locator('.report-count')).toHaveText('24 rows');
  });

  test('the calendar moves by month, keeps its filter and links entries to their view page', async ({ page }) => {
    await page.goto('/reports/days?month=2024-02');
    await expect(page.getByRole('heading', { name: 'February 2024' })).toBeVisible();
    const february = sales.filter((s) => s.month === '2024-02');
    for (const sale of february) {
      await expect(page.locator(`td[data-day="${sale.date}"]`)).toContainText(sale.note);
    }

    await page.getByLabel('region', { exact: true }).selectOption({ label: 'North' });
    await page.getByRole('button', { name: 'Apply', exact: true }).click();
    await expect(page).toHaveURL(/f_region=N&month=2024-02$/);
    const northFebruary = february.filter((s) => s.region === 'North');
    await expect(page.locator('.report-entry')).toHaveCount(northFebruary.length);

    await page.getByRole('link', { name: /Next/ }).click();
    await expect(page).toHaveURL(/f_region=N&month=2024-03$/);
    await expect(page.getByRole('heading', { name: 'March 2024' })).toBeVisible();
    const northMarch = sales.filter((s) => s.month === '2024-03' && s.region === 'North');
    await expect(page.locator('.report-entry')).toHaveCount(northMarch.length);
    await page.getByRole('link', { name: northMarch[0].note, exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/sales/view/${northMarch[0].n}$`));
  });

  test('the dashboard shows its reports in cards that link to them', async ({ page }) => {
    await page.goto('/reports/board');
    const cards = page.locator('.report-dashboard .card');
    await expect(cards).toHaveCount(2);
    await expect(cards.first().locator('svg')).toBeVisible();
    await cards.nth(1).getByRole('link', { name: 'Sales by region and payment', exact: true }).click();
    await expect(page).toHaveURL(/\/reports\/grid$/);
  });
});
