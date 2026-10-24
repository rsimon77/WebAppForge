# ADR 001: Data access in generated sites (ADO.NET with a SQL dialect layer, not Entity Framework Core)

- Status: accepted
- Date: 2026-10-24
- Context: adding SQL Server, Azure SQL and MySQL next to SQLite

## Decision

Generated sites keep talking to the database through ADO.NET with SQL that the generator writes for the chosen
database. The generator gets one small dialect class per database (`SqlDialect`: SQLite, SQL Server, MySQL; Azure SQL
uses the SQL Server dialect). Entity Framework Core is not used as the data layer of generated sites.

We may add an optional EF Core `DbContext` and entity classes to the output later, for developers who extend a
generated site by hand. The generated pages would not depend on it.

## Context

The generator emits SQL text in many places, and much of it is assembled from parts the user configures:

- table filters and report filters, typed by the user as SQL fragments;
- record ownership and user levels, added to every query;
- the grid tools (filter builder, quick filter, multi-column sort), built from a whitelist of columns with every value
  as a parameter;
- report grouping by date intervals (year, quarter, month, day), with dates stored as text, Unix seconds or
  milliseconds, or Julian day numbers;
- lookup text in groups and filters, as a correlated subquery;
- referential integrity (restrict, cascade, set null) in one transaction.

All of this already worked on SQLite with about 11,000 lines of templates, and the output of projects that customize
nothing is pinned by golden hashes in the tests.

## Options

### Option A: EF Core as the data layer

The generator would write a `DbContext` with one entity per table and replace the SQL with LINQ.

What it would give us:

- one provider package per database and LINQ that is translated for each of them;
- change tracking, and optimistic concurrency with rowversion columns;
- a familiar model for developers who extend the site.

What speaks against it:

- MySQL has no EF Core 10 provider worth building on. Pomelo.EntityFrameworkCore.MySql, the provider most projects
  use, is at 9.0.0 and does not support EF Core 10, which the generated sites target. Oracle's
  MySql.EntityFrameworkCore 10.0.x exists but translates less and is used much less. One of the three new databases
  would be on a weaker footing from the start.
- The user's filter fragments are raw SQL. EF can only take them through `FromSqlRaw` at the root of a query, so the
  filter, owner, grid and drill-down conditions would end up as a mix of raw SQL and LINQ.
- Report grouping by date parts of values stored as Unix time or Julian days, and grouping by a lookup's display text,
  need provider-specific functions. In LINQ that means `EF.Functions` calls that differ per provider, or raw SQL
  again.
- Tables without a primary key would need keyless entities, which cannot be edited. The current pages handle them
  directly.
- Every page, report and runtime file would have to be rewritten, and all generated-site tests with them, for no
  change the user can see.
- Generated sites would start more slowly and use more memory for the EF model. That matters little for a CRUD site,
  but it is a cost without a matching gain here.

### Option B: ADO.NET with a dialect layer (chosen)

The templates stay as they are. Each place that writes database-specific SQL asks the dialect: quoting, paging, top and
limit, the last identity value, text and integer casts, concatenation and whole-number division, date text for each
storage kind, case-insensitive comparison, the login lockout table and its upsert, and transaction statements. The
ADO.NET type names (connection, command, reader, exception) and the driver package follow the provider. SQL Server
groups through a derived table, because it cannot group by a column position or by a subquery.

- SQLite output is byte-for-byte what it was; the golden tests prove it.
- Drivers: Microsoft.Data.Sqlite, Microsoft.Data.SqlClient (SQL Server and Azure SQL) and MySqlConnector (MySQL and
  MariaDB). Since SqlClient 7, Entra ID sign-in (`Authentication=Active Directory ...`) needs
  Microsoft.Data.SqlClient.Extensions.Azure at the same version; Azure SQL sites, and SQL Server sites whose connection
  string names an Active Directory mode, reference it.
- Every query stays visible in the generated code, which keeps it easy to review.
- New databases are added by writing one more dialect class and a schema reader.

### Option C: Dapper or another micro-ORM

It would map rows to objects and leave the SQL to us, so it solves only the smallest part of the problem: the
generated `FromReader` methods already do that mapping. It would add a dependency to every generated site and change
nothing about the dialect work.

## Consequences

- SQL Server, Azure SQL and MySQL are supported with the same features as SQLite: lists, forms, the JSON API, grid
  tools, master/detail with integrity, reports, login with lockout, and the editor's report preview. Tests run every
  feature against real SQL Server 2022 and MySQL 8.4 when `WEBAPPFORGE_SQLSERVER` and `WEBAPPFORGE_MYSQL` are set.
- Each new database needs a dialect, a schema reader and a run of the server tests.
- The last identity value on SQL Server is read with `@@IDENTITY`, because the insert runs as an earlier command on the
  same connection. A trigger on the table that inserts into another identity table would make it return the wrong
  value; `OUTPUT INSERTED` would avoid that if it ever matters.
- On MySQL, a cascade or set-null rule from a table to itself fails with error 1093, because MySQL cannot update a table
  that a subquery of the same statement reads.
- Revisit EF Core when Pomelo supports the .NET version the sites target, or when users ask for a `DbContext` to extend
  their sites. The optional `DbContext` output described above would then be the first step.
