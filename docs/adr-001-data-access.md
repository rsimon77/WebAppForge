# ADR 001: Data access in generated sites (ADO.NET with a SQL dialect layer, not Entity Framework Core or Dapper)

- Status: accepted
- Date: 2026-10-24
- Context: adding SQL Server, Azure SQL and MySQL next to SQLite

## Decision

Generated sites keep talking to the database through ADO.NET with SQL that the generator writes for the chosen
database. The generator gets one small dialect class per database (`SqlDialect`: SQLite, SQL Server, MySQL; Azure SQL
uses the SQL Server dialect). Neither Entity Framework Core nor Dapper is used as the data layer of generated sites.

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

### Option C: Dapper

Dapper maps rows to objects and parameters from objects, and leaves the SQL to us. It was checked on 24 October 2026.

Package: Dapper 2.1.89, published September 2026, actively maintained, targets net10.0 and has no dependencies. It
generates its mapping code at run time with Reflection.Emit, so it does not suit Native AOT (Dapper.AOT 1.1.0 covers
that with a source generator). WebAppForge publishes neither trimmed nor AOT builds, so this does not matter today.

A prototype ran the generated model types (long keys, bool, decimal?, DateTime?, byte[]?, string?) against SQLite,
SQL Server 2022 and MySQL 8.4:

| Check | SQLite | SQL Server | MySQL |
|---|---|---|---|
| Insert from an object, null `byte[]` into a binary column | ok | ok | ok |
| `Query<T>`: integer keys into long, bit/TINYINT(1)/INTEGER into bool, decimals, dates (SQLite text dates too) | ok | ok | ok |
| `DynamicParameters` for a WHERE built at run time | ok | ok | ok |
| `DateOnly` parameter | ok | ok | ok |

It mapped 20,000 SQLite rows in about 81 ms, the same as the generated hand-written `FromReader` once warm. The first
query of each shape costs about 18 ms more while Dapper builds its mapper.

What it would change in a generated site:

- `FromReader` and the `reader.Get...` expressions per dialect would go. The SELECT lists would alias each column to
  its property name (`"order date" AS OrderDate`), because property names are Pascal case and some get a suffix.
- `Parameters.AddWithValue` lines would become parameter objects, and the typed-null special case for SQL Server
  binary columns would go, because Dapper takes the type from the property.
- In the three samples measured (master/detail, reports, secure), commands, parameters and reader loops are 4 to 10% of
  the generated lines (about 380 of 4,500 lines in the master/detail sample). Dapper would remove roughly half of those.

What it would not change:

- The dialect layer, the user's SQL filters, the grid tools, report grouping, lookups and the integrity transactions
  all remain our SQL. That is where the database work is.
- Reports read untyped values into their own accumulators, which suits a plain reader better than Dapper's dynamic
  rows.

Costs: a package in every generated site, a rewrite of the model, insert, update, key and list templates, new golden
hashes for SQLite output, and a full retest on all four databases. Users would see no change.

Verdict: Dapper works well with all three drivers and would be the better choice over EF Core if we want a mapping
library. It would make generated code shorter and easier to extend by hand, but it removes plumbing that already works
and is tested, and it leaves the hard part (the SQL) as it is. We do not switch now. It becomes worth doing when hand
extension of generated sites becomes a goal, or when the page templates are rewritten for another reason.

## Consequences

- SQL Server, Azure SQL and MySQL are supported with the same features as SQLite: lists, forms, the JSON API, grid
  tools, master/detail with integrity, reports, login with lockout, and the editor's report preview. Tests run every
  feature against real SQL Server 2022 and MySQL 8.4 when `WEBAPPFORGE_SQLSERVER` and `WEBAPPFORGE_MYSQL` are set.
- Each new database needs a dialect, a schema reader and a run of the server tests.
- On SQL Server an insert returns its identity in the same batch (`SELECT CAST(SCOPE_IDENTITY() AS bigint)` after the
  INSERT), so identity values that triggers create in other tables are ignored. `OUTPUT INSERTED` is not used because
  SQL Server refuses it on tables with triggers. A test inserts into a table whose trigger writes to an audit table.
- On MySQL, a cascade or set-null rule from a table to itself fails with error 1093, because MySQL cannot update a table
  that a subquery of the same statement reads.
- Revisit EF Core when Pomelo supports the .NET version the sites target, or when users ask for a `DbContext` to extend
  their sites. The optional `DbContext` output described above would then be the first step.
