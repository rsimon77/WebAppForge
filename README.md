# WebAppForge

A web application generator written in C#. Point it at a database, tune how every table, field and page looks and
behaves, and it generates a complete ASP.NET Core 10 web application, optionally builds and publishes it, and can
produce a Dockerfile and compose file.

> Status: early development. SQLite is the only supported database so far.

## What it does

- Reads a SQLite database (tables, columns, keys, foreign keys) and generates a server-rendered ASP.NET Core 10
  site: list, view, add, copy, edit, delete and search pages, plus a JSON API.
- Customization per field (edit control, validation, lookups, formats, badges, styling), per table (menu, row
  styles) and per page (layouts, columns, modal and side-panel pages, custom CSS and script).
- Master/detail relationships with referential integrity, and multi-page forms (tabs, pills, accordion, wizard).
- Report pages: summary (groups with subtotals and a grand total, optionally listing the rows), crosstab, chart (bar,
  horizontal bar, line, area, pie, doughnut, drawn on the server as SVG), month calendar and dashboard. Reports honor the
  table's filter, record ownership and user levels, and show lookup text for grouped fields.
- Login, registration, password change, lockout, record-level ownership and user-level permissions.
- Multi-language sites with opt-in translation through Google Cloud Translation or the Claude API
  (keys are read from the `GOOGLE_TRANSLATE_API_KEY` and `ANTHROPIC_API_KEY` environment variables only).
- Build, publish and Docker generation, with a build console in the editor.
- A cross-platform desktop editor built with Avalonia.

## Layout

| Path | Contents |
|---|---|
| `src/WebAppForge.Core` | Project model, project file, schema reader, generator, build and Docker services |
| `src/WebAppForge.Desktop` | Avalonia desktop editor |
| `tests/WebAppForge.Core.Tests` | Core unit and generated-site tests |
| `tests/WebAppForge.Desktop.Tests` | View model and headless UI tests |
| `tests/WebAppForge.Desktop.Ui.Tests` | Windows-only FlaUI tests that drive the real app |
| `tests/ui` | Playwright browser tests for generated sites (see `tests/ui/README.md`) |
| `docs` | Notes |

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build WebAppForge.slnx
dotnet test WebAppForge.slnx
```

Some tests need a Northwind SQLite file, such as `dist/northwind.db` from
[jpwhite3/northwind-SQLite3](https://github.com/jpwhite3/northwind-SQLite3). They skip with a message unless the
`WEBAPPFORGE_NORTHWIND_DB` environment variable points to one. The report tests generate a Northwind site with every
report kind and check its numbers against the database. The FlaUI tests need Windows and a built desktop app. The browser tests in
`tests/ui` need Node.js; see its README.

## Branches

`main` holds released code and `develop` is where work is integrated. Start every feature, fix and chore branch
from `develop` and open its pull request into `develop`; `develop` is merged into `main` by a release pull request.
See [CONTRIBUTING.md](CONTRIBUTING.md).
