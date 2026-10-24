# Browser UI tests

Playwright (Node) tests for the server-rendered pages that the generator produces. The tests drive a generated
ASP.NET Core site in a real browser. They do not drive the Avalonia desktop window; Playwright cannot control it.

## Layout

| Path | Purpose |
| --- | --- |
| `package.json`, `playwright.config.mjs` | Playwright 1.64.1, reporter `list`, 1 worker, trace off |
| `tools/harness/` | Console project (references `src\WebAppForge.Core`). Recreates the SQLite database for a scenario and calls `ProjectGenerator.Generate` |
| `tools/generate-site.ps1` | Harness, then `dotnet build` of the generated site, then start it on a free port |
| `tools/stop-site.ps1` | Stops the site started for a target folder |
| `specs/*.spec.mjs` | Test files. `shop.spec.mjs` uses `UI_BASE_URL`; `secure-*.spec.mjs` use `UI_SECURE_BASE_URL`; `designed.spec.mjs` uses `UI_DESIGNED_BASE_URL`; `relations.spec.mjs` uses `UI_RELATIONS_BASE_URL`; `reports.spec.mjs` and `grid.spec.mjs` use `UI_REPORTS_BASE_URL`; `audit.spec.mjs` uses `UI_AUDIT_BASE_URL`; `identity.spec.mjs` uses `UI_IDENTITY_BASE_URL` |
| `.sites/` | Generated sites, databases and logs (recreated on each run) |

Scenarios:

- `shop`: `customers` (25 seeded rows) and `orders` tables, no login.
- `secure`: `users` (username, password, level), `notes` (owner = user name), `reports` (EditMinLevel 2). Login,
  registration and change password are on, lockout after 5 failures. Seeded users `alice` (level 1) and `bob`
  (level 0), both with password `secret-pass-123`. The password hash comes from the file given by `-PasswordHashFile`,
  or the `UI_PASSWORD_HASH_FILE` environment variable. The file must contain the ASP.NET Core Identity password hash (PasswordHasher, V3 format) of that password.
- `designed`: `categories` (3 rows) and `products` (30 rows) with per-page designs: products list as Cards (columns
  name, price, category_id; page sizes 10/20/50; title, subtitle, footer), Add/Edit as TwoColumns modals, View as a
  modal, Delete as an offcanvas, a category lookup, Currency price, Badge and row-style rules on `discontinued`, a name
  pattern, and Hungarian (`hu`) manual translations of the products caption, field captions and the Add, Edit, Delete,
  Search, Save, Cancel strings. Categories list is Compact with sticky header and row numbers. No password hash needed.
- `relations`: master/detail links and multi-page forms. Tables `customers` (text key C1..C5; C5 has no orders), `orders`
  (30 rows, customer C((n-1)%4+1)), `order_lines` (composite key order_id + line_no, 3 lines for orders 1..10),
  `categories` (3) and `products` (12), foreign keys on. Links: Customers -> Orders (Restrict, expandable-row preview,
  page size 5, columns id/order_date/status), Orders -> order_lines (Cascade, offcanvas preview, ShowOnAdd, grid on the
  "Lines" form page), Categories -> Products (SetNull, modal preview). Customers forms: Main/Contact/Notes pages, Add =
  Tabs, Edit = Wizard, View = Accordion. Orders forms: Header/Lines pages, Add = Pills, Edit = Tabs.

- `reports`: `regions` (North, South, West) and `sales` (24 rows; sale n has region N/S/W in turn, month (n-1)/4+1 of 2024
  on day 3, 10, 17 or 24, amount n * 10.5, paid when n is even, and a Unix-time `stamp` column). Reports: a summary by
  region and month with a date range, region and paid pick lists and drill-down; a chart of the stamp by month; a
  crosstab of region by paid; a calendar with a region filter; a dashboard. The sales list has the filter and sort panel,
  the quick filter and the reset button, and the view page of a region lists its sales in a
  detail grid with a quick filter and sortable headers (`grid.spec.mjs`). No login. The tests only read data.
- `audit`: the shop scenario (customers 25 rows, orders 3 rows, no login) with an audit trail on both tables. `audit.spec.mjs`
  adds, edits and deletes through the pages and the API, then checks the `/audit-trail` page: entries, old/new values,
  the table, action, record, user and date filters, paging after 50 entries and HTML encoding. Generate the site again
  before every run (the entries accumulate).
- `identity`: ASP.NET Core Identity user store. The site creates its AspNet* tables at startup and the administrator `admin`;
  `generate-site.ps1` passes its password (`Admin-pass-42`, a test value) in `WEBAPPFORGE_ADMIN_PASSWORD`. Tables `notes`
  (owned by the user name), `secrets` (level 5) and `users` (a plain project table whose `/users` pages sit beside the
  `/admin/users` and `/admin/roles` administration). Registration and change password on, lockout after 3 failures.
  `identity.spec.mjs` covers the admin pages (add, edit, roles, lock, unlock, set password, delete, no self-delete), role
  levels with 403 for lower levels, registration, owned rows, change password and logout. Generate the site again before
  every run (the users accumulate).

## Commands

Run from `tests\ui` in PowerShell. Install once:

```powershell
cd "tests\ui"
npm install
npx playwright install chromium   # only if the browser for Playwright 1.64.1 is not installed yet
```

Generate and start the sites (each prints `UI_BASE_URL=...`):

```powershell
.\tools\generate-site.ps1 -Target .\.sites\shop   -Scenario shop
.\tools\generate-site.ps1 -Target .\.sites\secure -Scenario secure
.\tools\generate-site.ps1 -Target .\.sites\designed -Scenario designed
.\tools\generate-site.ps1 -Target .\.sites\relations -Scenario relations
.\tools\generate-site.ps1 -Target .\.sites\reports -Scenario reports
.\tools\generate-site.ps1 -Target .\.sites\audit -Scenario audit
.\tools\generate-site.ps1 -Target .\.sites\identity -Scenario identity
```

Run the tests with all base URLs set:

```powershell
$env:UI_BASE_URL        = (Get-Content .\.sites\shop\site.json   | ConvertFrom-Json).baseUrl
$env:UI_SECURE_BASE_URL = (Get-Content .\.sites\secure\site.json | ConvertFrom-Json).baseUrl
$env:UI_DESIGNED_BASE_URL = (Get-Content .\.sites\designed\site.json | ConvertFrom-Json).baseUrl
$env:UI_RELATIONS_BASE_URL = (Get-Content .\.sites\relations\site.json | ConvertFrom-Json).baseUrl
$env:UI_REPORTS_BASE_URL = (Get-Content .\.sites\reports\site.json | ConvertFrom-Json).baseUrl
$env:UI_AUDIT_BASE_URL = (Get-Content .\.sites\audit\site.json | ConvertFrom-Json).baseUrl
$env:UI_IDENTITY_BASE_URL = (Get-Content .\.sites\identity\site.json | ConvertFrom-Json).baseUrl
npx playwright test
```

Stop the sites:

```powershell
.\tools\stop-site.ps1 -Target .\.sites\shop
.\tools\stop-site.ps1 -Target .\.sites\secure
.\tools\stop-site.ps1 -Target .\.sites\designed
.\tools\stop-site.ps1 -Target .\.sites\relations
.\tools\stop-site.ps1 -Target .\.sites\reports
.\tools\stop-site.ps1 -Target .\.sites\audit
.\tools\stop-site.ps1 -Target .\.sites\identity
```

Each `generate-site.ps1` run stops any site still recorded in its target folder, deletes the old database and code, and
recreates them, so the seed data is the same on every run. Run a single file with `npx playwright test specs/shop.spec.mjs`.

## Notes

- `reports.spec.mjs` and `grid.spec.mjs` do not import `helpers.mjs`, so they run alone:
  `$env:UI_REPORTS_BASE_URL=...; npx playwright test specs/reports.spec.mjs specs/grid.spec.mjs`. Its charts are server-side SVG, so it does not
  need the Bootstrap script from the CDN. `generate-site.ps1` starts the extensionless app host on macOS and Linux.

- Lockout state is kept in memory per site process. `secure-lockout.spec.mjs` uses a freshly registered user, so the
  other tests are not affected. Restart the secure site (generate it again) to clear it.
- The owner column of `notes` is hidden from the HTML pages. `secure-ownership.spec.mjs` checks it through `/api/notes`.
- Bootstrap is loaded from a CDN, so pages are unstyled when offline. The tests do not depend on styling.
- The designed specs add and delete products, so counts assume a freshly generated site; generate it again before a rerun.
- The Cards layout applies row style rules (`border-<style> bg-<style>-subtle` on the card) and has a sort bar above the cards.
- Currency text follows the server culture (the site shows `11,00 Ft`), so the specs accept `.` or `,` as the separator.
- The relations specs run serially in file order and change data (orders, lines, customers, categories); generate the
  relations site again before every full run. `generate-site.ps1` keeps the site process holding its output pipes, so
  start it detached (for example `Start-Process pwsh ... -RedirectStandardOutput`) and poll `site.json` for the port.
- `relations.spec.mjs` does not import `helpers.mjs` (which requires `UI_SECURE_BASE_URL`), so it can run alone:
  `$env:UI_RELATIONS_BASE_URL=...; npx playwright test specs/relations.spec.mjs`.
- Three generator defects found by these specs are fixed and their tests are normal tests (wizard Save hidden before the last
  step; decimal inputs have `step="any"`; the return URL of a paged master page such as `?dp0=2` is escaped once).
  Also: ShowOnAdd takes precedence over `returnUrl` when an order is added from a customer page (the edit page opens
  first and carries `returnUrl`); the tests expect that.
