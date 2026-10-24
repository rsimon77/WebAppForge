# Contributing to WebAppForge

## Branching model

| Branch | Purpose |
|---|---|
| `main` | Released code. Only changes from `develop` (release PRs) land here. |
| `develop` | Integration branch. All work is merged here first. |
| `feature/<short-name>` | A new feature. |
| `fix/<short-name>` | A bug fix. |
| `chore/<short-name>` | Maintenance: refactoring, docs, tooling, dependency updates. |

### Day-to-day work

1. Start from an up-to-date `develop`:

   ```bash
   git checkout develop
   git pull
   git checkout -b feature/my-change
   ```

2. Commit your work in small, focused commits.
3. Push the branch and open a pull request **into `develop`**.
4. Make sure the build and tests pass locally before asking for review (see below). CI runs them again on the pull
   request, including the SQL Server and MySQL tests.
5. Merge the pull request into `develop`, then delete the branch.

Never commit directly to `develop` or `main`, and never branch from `main` for regular work.

### Releases

A release is a pull request from `develop` into `main`:

1. Check that `develop` builds and passes all tests.
2. Open a PR titled `Release <version or date>` with `develop` as the source and `main` as the target.
3. After it is merged, tag `main` (for example `v0.2.0`).

### Hotfixes

An urgent fix to released code is still a `fix/` branch from `develop`, merged into `develop` and released
through the normal release PR. If the fix cannot wait, say so in the PR and release straight away.

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build WebAppForge.slnx
dotnet test WebAppForge.slnx
```

- Tests that need a Northwind SQLite file skip unless `WEBAPPFORGE_NORTHWIND_DB` points to one.
- `tests/WebAppForge.Desktop.Ui.Tests` drives the real desktop app and runs on Windows only.
- Browser tests for generated sites live in `tests/ui`; see `tests/ui/README.md`.

## Conventions

- Line endings are LF everywhere (enforced by `.gitattributes`). The generator's templates take their newlines
  from the source files, so mixed endings can break the golden-output tests.
- A change that alters the default generated output must update the golden hashes in
  `tests/WebAppForge.Core.Tests/CustomizationGolden.cs` deliberately, and the PR must say what changed.
- Never commit secrets, API keys, database files or personal paths. Translation keys come from environment
  variables only.

## Pull requests

- Keep a PR to one purpose, with a description of what changed and how it was tested.
- Link the issue it closes, if there is one.
- There is no CI yet, so run the build and tests yourself and say so in the PR.
