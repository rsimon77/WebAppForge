# ASP.NET Maker 2026 - reverse engineering notes

Clean-room rebuild. Observations below come from the installed app
(`C:\Program Files (x86)\ASP.NET Maker 2026\`) and the public docs at
https://aspnetmaker.dev/docs/. No decompiled code is copied into this repository.

## Installed application

| Item | Observation |
|---|---|
| UI host | `ASPNETMaker.exe` (39 MB). Core assembly `aspnetmkr26.dll` (v26.5.0.0, IL-only, runtime `.NETCoreApp,Version=v10.0`, 425 types, 7,811 methods). |
| UI stack | Ookii.Dialogs.WinForms and WebView2 (WinForms plus WebView2 UI). |
| Data access | Devart.Data.* (MySQL, Oracle, PostgreSQL, SQLite, SQL Server), Microsoft.Data.SqlClient, Microsoft.Data.Sqlite. |
| Scripting | ClearScript V8 (JavaScript), Lua 5.1 (`lua5.1.dll`). |
| Other | LibGit2Sharp (project history), MailKit/MimeKit, AngleSharp, Antlr4, SqlParserSharp, BCrypt, Azure.Identity, AWS SDK, Newtonsoft.Json, Pluralize.NET. |

## Code generation pipeline

- Generated output is not produced by the .NET code alone. The installed folder
  ships a Node.js template engine: `appdata/node_modules/@aspnetmaker/core`
  and `appdata/node_modules/@aspnetmaker/aspnet2026` (template v26.5.1, 517 files:
  244 `.cs`, 98 `.cshtml`, 77 `.js`, plus scss/css/xml/json).
- Templates are driven by `control.xml` (see docs: Customizing Template ->
  Control File). Snippet templates live in `appdata/code/*.xml`.
- Bundled npm dependencies include ejs, sass, terser, rtlcss, jsonwebtoken,
  web-push, xml-js and pluralize.
- Requirement per docs: Node.js LTS >= 22 and .NET SDK 10.

**Implication for the port:** the .NET version must either (a) embed its own
template engine and write its own templates (clean-room, as agreed), or
(b) call Node.js. Option (a) is the plan. The vendor's `@aspnetmaker/*`
templates are not copied here.

## Documented product surface (docs mirror)

- Project file: utf-8 XML, one node per object (database, table, field).
  Each project has a name and a GUID project ID.
- Setup areas: Database, ASP.NET, HTML, Security, Generate, Table, Field,
  Lookup, server events and client scripts, static members, custom templates,
  Summary/Crosstab/Calendar/Chart/Dashboard reports, custom files and fields,
  linked tables, multi-language, query builder, import, custom view,
  authentication, two-factor auth, web push, chat, ORM.
- Databases: MySQL, PostgreSQL, SQL Server, Oracle, SQLite.
- Generated apps: ASP.NET Core 10, Razor, Bootstrap/AdminLTE, pages list,
  view, add, copy, edit, delete, update, search, master/detail, reports and charts.

## Decisions (answered by the user)

- Approach: clean-room rebuild.
- UI: Avalonia (cross-platform, targets Windows, Mac and Linux).
- Data providers: free providers (Microsoft.Data.SqlClient, Npgsql,
  MySqlConnector, Microsoft.Data.Sqlite, Oracle.ManagedDataAccess.Core).
- First milestone: generator core and project model.

## Repository layout

- `src/AspNetMaker.Core` - project model and generator (class library, net10.0)
- `src/AspNetMaker.Desktop` - Avalonia MVVM app (net10.0)
- `tests/AspNetMaker.Core.Tests` - xUnit tests

## Milestone 1 status (generator core and project model)

Done:
- Project model (`Model/Project.cs`) and XML project file read/write
  (`Model/ProjectFile.cs`). The XML schema is ours and is not checked against any vendor file.
- SQLite schema reader (`Schema/SqliteSchemaReader.cs`) using PRAGMA metadata and SQLite type affinity.
- ASP.NET Core 10 minimal API generator (`Generation/ProjectGenerator.cs`): one model
  and one endpoint class per table, with list, get, add, edit and delete.
- Verified end to end: a generated project from a live SQLite schema builds with
  0 warnings, and every CRUD route was exercised over HTTP.
- 8 unit tests pass.

Not done:
- Providers other than SQLite (the generator throws `NotSupportedException`).
- Razor UI pages, search, master/detail, reports and the desktop editor.
- Templates and output are our own; the vendor's `@aspnetmaker/*` templates are not used.

## Feature parity (SQLite scope)

| Documented area | Status |
|---|---|
| SQLite connection (path relative to project folder) | Done (Core model, Desktop UI in progress) |
| Table list, Generate flag, Caption, Filter, Sort | Done (model, XML, generator) |
| Pages: list, view, add, copy, edit, delete, search | Done (generator, verified over HTTP) |
| Quick search and per-field search, paging | Done |
| Field settings: caption, type, required, list/view/add/edit/search flags | Done (model and generator) |
| Tables without primary key (list and search only) | Done |
| Text primary keys, composite keys | Text done; composite keys rejected with NotSupportedException |
| Desktop editor (Database, Tables, Fields, AspNet, Generate tabs) | In progress |
| Login with cookie auth, failed-attempt lockout, logout, sign-in required on pages and API | Done (generator, Desktop Security tab) |
| Password storage | Done with ASP.NET Core PBKDF2 hasher (docs also list bcrypt and MD5; not implemented) |
| Password hash hidden from HTML pages and JSON API | Done |
| Registration, change password | In progress |
| Per-record (User ID) and per-level (User Level) permissions | Not started |
| Two-factor, external providers, SAML, LDAP, CAPTCHA, concurrent-session limits | Not started |
| Lockout counter across restarts or processes | Not started (in memory only) |
| Master/detail, multi-page, return pages, audit trail, email | Not started |
| Summary, crosstab, calendar, chart, dashboard reports | Done (see the report section at the end) |
| Lookup tables, custom views, linked tables, custom fields | Not started |
| Multi-language, REST/export API options, tools menu | Not started |
| Other database providers (MySQL, PostgreSQL, SQL Server, Oracle) | Deferred by request |

Known gaps in the generated output:
- The HTML pages have no anti-forgery tokens and no authentication.
- The Edit form for a table with no key is not generated.

## Not yet done

See the parity table above. The largest gaps are authentication, master/detail and the report pages.

## RAD tool roadmap (requested 2026-10-23)

Requested: multi-language with opt-in AI/Google translation, full Project settings page, full Application Options
dialog plus File > Exit, per-table/per-field customization page, build and compile after generate with optional
Docker, and per-page customization (add, edit, list, report, modal, ...).

Phase 1 (in progress, parallel agents)
- Build, compile and Docker generation (Core: Build\, BuildSettings).
- Multi-language model, translation providers (Google Cloud Translation v2, Claude Messages API), generated localization.
- Application Options dialog and File > Exit (Desktop).

Phase 2 (after Phase 1, because it shares Generation\Templates.cs)
- Field and table customization model: caption, control type, format, validation, default, help text, CSS class, width,
  visibility per page; generator consumes it; GUI page to edit it.
- Page customization model per page type (list, add, edit, view, delete, search, modal): title, header and footer text,
  buttons, CSS, columns; GUI page to edit it.
- Full Project settings page (needs new project-level fields in the model).
- Build and Docker output console in the Generate tab.
- Language manager UI with translate-missing and review flags.

Defaults chosen without asking (change on request)
- API keys come from environment variables (GOOGLE_TRANSLATE_API_KEY, ANTHROPIC_API_KEY) and are never stored in project files.
- Docker: multi-stage Dockerfile on the official dotnet 10 images, non-root user, compose file with a volume for the SQLite file.
- "Everything customizable" is delivered in layers; the first layer covers the properties listed above, not every possible template change.

## Status after RAD phases 1 and 2 (verified 2026-10-23)

Build: 0 errors, 0 warnings. Tests: 258 Core, 267 Desktop, 6 live FlaUI (drives the real app), 43 Playwright
(browser UI for generated sites; run per tests\ui\README.md).

Done
- Build, compile, publish, Docker and compose generation; streaming build service with cancel, timeout, proxy and
  configurable tool paths; GenerateAndBuild flow; build console, Run site, diagnostics grid in the Generate tab.
- Multi-language: language model, Google and Claude translation providers (keys from environment variables only),
  opt-in guard, review and stale tracking, CSV import and export, generated localized sites with language switcher.
- Customization contract (FieldUi, TableUi, PageDesigns, ProjectMetadata) persisted in the project file and honored by
  the generator: edit controls, validation, lookups, formats, badges, sorting, row styles, menus, page layouts,
  cards and compact lists, modal and offcanvas pages, flash messages, custom CSS and script, project runtime options.
- Editor: Project, Fields and Pages designer tabs, Languages tab, Options dialog, File > Exit, 19 palettes, dark mode.

Known gaps
- Report pages (summary, crosstab, chart, calendar, dashboard) do not exist yet.
- Master/detail, multi-page forms, import, audit trail, e-mail notification, export formats, REST API options.
- Other database providers (deferred by request).
- Some new strings are English only (Rows per page, Save changes?, Are you sure?, modal Close).
- The desktop editor has not had a human click-through; only headless and FlaUI checks.
- Original product comparison: its generated products and orders pages fail on Northwind (ignored by request).

## Status after master/detail and multi-page forms (verified 2026-10-23)

Build: 0 errors, 0 warnings. Tests: 375 Core, 318 Desktop, 6 live FlaUI; 71 Playwright browser tests (agent-run, scenarios
shop, secure, designed, relations; see tests\ui\README.md).

Added
- Master/detail: DetailLink model, detail grids on view and edit pages, composite links, multiple details per master,
  locked link presets with server-side verification, returnUrl, previews (expandable row, modal, offcanvas),
  referential integrity on delete (Restrict, Cascade incl. nested, Set null, None) in one transaction, update cascade,
  API detail list, foreign keys read from SQLite, editor Relations tab with foreign-key suggestions and validation.
- Multi-page forms: tabs, pills, accordion, wizard; field-to-page assignment; error pages marked; editor support.
- Bug fixes found by browser tests: wizard Save visibility, decimal inputs now step="any" (default output changed on
  purpose; golden hashes updated), returnUrl on paged master pages.

Known gaps
- Report pages (summary, crosstab, chart, calendar, dashboard) are not built.
- Several detail rows entered inside the master's own add form (grid-add) is not built.
- Import, audit trail, e-mail notification, export formats, REST API options, other database providers.
- Master/detail grids under ownership and level security are covered by text tests only; no runtime test.
- Some new strings are English only (no records, cannot delete, previous, next, step n of m, and others).
- Desktop editor visuals have only headless and automated checks; no human click-through yet.

## Project renamed to WebAppForge (2026-10-23)

This project was renamed from its working name (which used a third party's product name) to WebAppForge.
Solution WebAppForge.slnx; projects WebAppForge.Core, WebAppForge.Desktop and the WebAppForge.*.Tests projects;
code namespace WebAppForge; environment variable prefix WEBAPPFORGE_; user settings folder
%LOCALAPPDATA%\WebAppForge (settings saved under the old folder name are not migrated).
The default title of generated sites changed to "Web Application" (golden hashes updated; the only change in
generated output). This file keeps the original product's name only because it describes that product as the
subject of the analysis.

Still to do by hand: the repository folder on disk still carries the old name; rename it when no tool or session
has it open.

## Report pages (2026-10-24)

Build: 0 errors, 0 warnings. Tests: 398 Core and 347 Desktop with `WEBAPPFORGE_NORTHWIND_DB` set (Northwind from
jpwhite3/northwind-SQLite3: 16,282 orders, 609,283 order lines); without it 6 Core and 12 Desktop tests skip.

Added
- Report model (`Model/Reports.cs`, `Project.Reports`) stored in the project file under `<reports>`; the element is left
  out when a project has no reports, and the generated output of such a project is unchanged (golden hashes untouched).
- Generated `/reports/{name}` pages, one `Reports/{Name}Report.cs` each, and the runtime `Web/ReportKit.cs`:
  - Summary: group levels (dates by year, quarter, month or day), Count, Sum, Average, Min and Max, subtotals per group,
    grand total, optional row listing (first 5,000 rows listed; totals cover every row).
  - Crosstab: row field by column field with row, column and grand totals; columns capped (default 50).
  - Chart: bar, horizontal bar, line, area, pie and doughnut as server-side SVG with tooltips, an optional series field,
    order by value with top N, and the data table below.
  - Calendar: month grid with previous and next month, multi-day rows through an end date field, entries linked to the
    view page.
  - Dashboard: other reports in Bootstrap cards with a width per item.
- Aggregation runs in SQL (COUNT, SUM, MIN, MAX per group) and ReportKit combines the groups, so averages of subtotals are
  exact. Date grouping uses the leading characters of ISO date text, which covers both `YYYY-MM-DD` and
  `YYYY-MM-DD HH:MM:SS` values (Northwind has both).
- Security: reports are behind login when it is on; the report's own level and the table's view level both apply (the
  stricter wins); rows are limited to the owner when the table has an owner field; dashboards leave out items the user
  may not open; fields the site hides (password hash, user level) cannot be used.
- Grouped values show the lookup display text or static option text of the field.
- Localization: report captions, custom total headings and the report UI texts go through the translation store, only
  for projects with reports.
- Editor: Reports tab with the report list, kind-specific settings and the validation list (`ReportValidator`, shared
  with the generator). Pill tab headers are 16px so the eleven tabs fit on one row of the default window.

Known gaps
- Charts are static SVG (tooltips, no zoom or click-through). No date-range or other runtime filter on report pages yet.
- Dates stored as numbers (Unix or Julian) are not grouped by interval.
- Detail columns of a summary are typed as a comma-separated list in the editor.
- No Playwright scenario for reports yet; the pages are covered by HTTP tests against generated sites.

## Report filters, drill-down and number dates (2026-10-24)

The gaps listed above are closed:
- Runtime filters: a report can offer a From/To date range on a date field and pick lists of the values of chosen fields
  (with lookup or option text). The choices honor the table filter and the owner. Filters are bound parameters and are
  kept in drill-down links, the month links of a calendar and the "Back to the report" link.
- Drill-down: summary totals, crosstab cells and totals, and chart bars, points, slices and data table values link to
  `/reports/{name}/rows`, which lists the rows of that group 50 at a time with links to their view page. Group values
  travel typed (i:, r:, s:) so they compare with the database value exactly; an unreadable value matches no row. The
  page applies the same level, owner and filters as the report.
- Dates stored as numbers: `FieldInfo.DateStorage` (Text, UnixSeconds, UnixMilliseconds, JulianDay, set on the Fields tab
  for number columns) lets reports group, filter and place those columns on a calendar through SQLite `datetime()`.
- Editor: the row columns and the pick-list filters are lists with field pickers; the date range field and drill-down
  are on the Reports tab.
- Browser tests: `tests/ui` has a `reports` scenario and `reports.spec.mjs` (8 tests: filters, drill-down to the view
  page and back, Unix-time chart, crosstab, calendar with filter across months, dashboard). `generate-site.ps1` now
  starts the app host without `.exe` on macOS and Linux.
- Fixed while writing the browser tests: applying a filter on a calendar kept the month it showed.

Tests: 403 Core and 348 Desktop with Northwind (Northwind adds a filtered summary checked against SQL and drill-down with
paging), 8 Playwright for reports. Still not done: zoom on charts.
