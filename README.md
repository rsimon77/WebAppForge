# WebAppForge

A web application generator written in C#. Point it at a database, tune how every table, field and page looks and
behaves, and it generates a complete ASP.NET Core 10 web application, optionally builds and publishes it, and can
produce a Dockerfile and compose file.

> Status: early development. Supported databases: SQLite, SQL Server, Azure SQL and MySQL.

## What it does

- Reads a SQLite, SQL Server, Azure SQL or MySQL database (tables, columns, keys, identity columns, foreign keys) and
  generates a server-rendered ASP.NET Core 10 site: list, view, add, copy, edit, delete and search pages, plus a JSON
  API. Every feature below works on all four. A server connection string can live in an environment variable, so it is
  kept out of the project file, appsettings.json and the Docker image. Generated sites run SQL written for the chosen
  database through Dapper's async API; see [ADR 001](docs/adr-001-data-access.md) for why Dapper and not Entity
  Framework Core.
- Customization per field (edit control, validation, lookups, formats, badges, styling), per table (menu, row
  styles) and per page (layouts, columns, modal and side-panel pages, custom CSS and script).
- Optional grid tools on list pages, each a checkbox in the Pages tab: a filter and sort panel (conditions per column
  such as contains, =, >, between or empty, joined by all or any, and up to three sort levels), a quick filter box
  (every word must match; "phrases", column:value and -word are understood) and a reset button that clears the
  search, the filters and the sorting. A button copies these settings to every list. Detail grids can have a quick
  filter, sortable column headers and a reset on the master's view page. They work without script and every value is
  bound as a SQL parameter.
- Master/detail relationships with referential integrity, and multi-page forms (tabs, pills, accordion, wizard).
- Report pages: summary (groups with subtotals and a grand total, optionally listing the rows), crosstab, chart (bar,
  horizontal bar, line, area, pie, doughnut, drawn on the server as SVG), month calendar and dashboard. Reports honor the
  table's filter, record ownership and user levels, and show lookup text for grouped fields. Report pages offer a date
  range and pick-list filters, and their numbers, bars and cells open the rows behind them. Number columns that hold
  Unix time or Julian day numbers can be grouped and filtered as dates.
- Login, registration, password change, lockout, record-level ownership and user-level permissions.
- Multi-language sites with opt-in translation through Google Cloud Translation or the Claude API
  (keys are read from the `GOOGLE_TRANSLATE_API_KEY` and `ANTHROPIC_API_KEY` environment variables only).
- Build, publish and Docker generation, with a build console in the editor.
- A cross-platform desktop editor built with Avalonia: a sidebar of sections in workflow order (Setup, Data, Site,
  Build), an app bar with project search (Ctrl+K) and Generate, compact controls, and light and dark themes.

## Layout

| Path | Contents |
|---|---|
| `src/WebAppForge.Core` | Project model, project file, schema readers, generator with its SQL dialects, build and Docker services |
| `src/WebAppForge.Desktop` | Avalonia desktop editor |
| `tests/WebAppForge.Core.Tests` | Core unit and generated-site tests |
| `tests/WebAppForge.Desktop.Tests` | View model and headless UI tests |
| `tests/WebAppForge.Desktop.Ui.Tests` | Windows-only FlaUI tests that drive the real app |
| `tests/ui` | Playwright browser tests for generated sites (see `tests/ui/README.md`) |
| `docs` | Notes and architecture decisions |

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build WebAppForge.slnx
dotnet test WebAppForge.slnx
```

Some tests need a Northwind SQLite file, such as `dist/northwind.db` from
[jpwhite3/northwind-SQLite3](https://github.com/jpwhite3/northwind-SQLite3). They skip with a message unless the
`WEBAPPFORGE_NORTHWIND_DB` environment variable points to one.

The server tests run every feature of a generated site against SQL Server and MySQL. Each server's tests return early
unless its admin connection string is set. They create a database of their own and drop it afterwards:

```bash
docker run -d --name wf-mssql -p 1433:1433 -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Forge-Test-42!' mcr.microsoft.com/mssql/server:2022-latest
docker run -d --name wf-mysql -p 3306:3306 -e MYSQL_ROOT_PASSWORD=forge-test-42 mysql:8.4
export WEBAPPFORGE_SQLSERVER='Server=127.0.0.1,1433;User ID=sa;Password=Forge-Test-42!;TrustServerCertificate=True;Encrypt=False'
export WEBAPPFORGE_MYSQL='Server=127.0.0.1;Port=3306;User ID=root;Password=forge-test-42'
dotnet test WebAppForge.slnx
```

The CI workflow in `.github/workflows/ci.yml` runs the strict build and the Core and Desktop tests, with Northwind, SQL
Server and MySQL, on every push to and pull request into `develop` and `main`.
 The report tests generate a Northwind site with every
report kind and check its numbers against the database. The FlaUI tests need Windows and a built desktop app. The browser tests in
`tests/ui` need Node.js; see its README.

## Branches

`main` holds released code and `develop` is where work is integrated. Start every feature, fix and chore branch
from `develop` and open its pull request into `develop`; `develop` is merged into `main` by a release pull request.
See [CONTRIBUTING.md](CONTRIBUTING.md).
