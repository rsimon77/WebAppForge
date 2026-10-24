using System.Data.Common;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Schema;

/// <summary>
/// The ASP.NET Core Identity tables (the standard AspNetUsers, AspNetRoles, AspNetUserRoles, AspNetUserClaims, AspNetUserLogins,
/// AspNetUserTokens and AspNetRoleClaims, with the column names Entity Framework uses) for every supported database. The
/// generated site runs the same statements at startup; the editor can run them to create the tables before generating.
/// Every statement is idempotent: it creates its table or index only when it does not exist.
/// </summary>
public static class IdentitySchema
{
    private static readonly string[] Suffixes = ["Roles", "Users", "RoleClaims", "UserClaims", "UserLogins", "UserRoles", "UserTokens"];

    /// <summary>The identity table names for <paramref name="prefix"/>, in creation order (referenced tables first).</summary>
    public static IReadOnlyList<string> TableNames(string prefix)
    {
        RequireValidPrefix(prefix);
        return Suffixes.Select(s => prefix + s).ToList();
    }

    /// <summary>A prefix is 1 to 30 letters, digits and underscores, starting with a letter, so it can be used in SQL unquoted.</summary>
    public static bool IsValidPrefix(string? prefix) =>
        prefix is { Length: > 0 and <= 30 } && char.IsAsciiLetter(prefix[0]) && prefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>True when the site keeps its users with ASP.NET Core Identity and <paramref name="tableName"/> is one of its tables.</summary>
    public static bool IsIdentityTable(SecuritySettings security, string tableName) =>
        security.UsesIdentity && IsValidPrefix(security.Identity.TablePrefix)
        && TableNames(security.Identity.TablePrefix).Contains(tableName, StringComparer.OrdinalIgnoreCase);

    /// <summary>The statements that create the missing identity tables and indexes, for the provider key of <see cref="DatabaseProviders"/>.</summary>
    public static IReadOnlyList<string> CreateStatements(string provider, string prefix) => CreateStatements(SqlDialect.For(provider), prefix);

    internal static IReadOnlyList<string> CreateStatements(SqlDialect dialect, string prefix)
    {
        RequireValidPrefix(prefix);
        var kind = dialect == SqlDialect.SqlServer ? Kind.SqlServer : dialect == SqlDialect.MySql ? Kind.MySql : Kind.Sqlite;
        var roles = prefix + "Roles";
        var users = prefix + "Users";
        var custom = prefix != IdentitySettings.DefaultTablePrefix;
        string Index(string name) => custom ? prefix + name : name;

        var tables = new List<Table>
        {
            new(roles,
                [C("Id", T.Key, false), C("Name", T.Name), C("NormalizedName", T.Name), C("ConcurrencyStamp", T.Text)],
                ["Id"], [],
                [new Ix(Index("RoleNameIndex"), "NormalizedName", true)]),
            new(users,
                [
                    C("Id", T.Key, false), C("UserName", T.Name), C("NormalizedUserName", T.Name), C("Email", T.Name), C("NormalizedEmail", T.Name),
                    C("EmailConfirmed", T.Bool, false), C("PasswordHash", T.Text), C("SecurityStamp", T.Text), C("ConcurrencyStamp", T.Text),
                    C("PhoneNumber", T.Text), C("PhoneNumberConfirmed", T.Bool, false), C("TwoFactorEnabled", T.Bool, false),
                    C("LockoutEnd", T.Moment), C("LockoutEnabled", T.Bool, false), C("AccessFailedCount", T.Int, false),
                ],
                ["Id"], [],
                [new Ix(Index("UserNameIndex"), "NormalizedUserName", true), new Ix(Index("EmailIndex"), "NormalizedEmail", false)]),
            new(prefix + "RoleClaims",
                [C("Id", T.Identity, false), C("RoleId", T.Key, false), C("ClaimType", T.Text), C("ClaimValue", T.Text)],
                ["Id"], [new Fk("RoleId", roles)],
                [new Ix($"IX_{prefix}RoleClaims_RoleId", "RoleId", false)]),
            new(prefix + "UserClaims",
                [C("Id", T.Identity, false), C("UserId", T.Key, false), C("ClaimType", T.Text), C("ClaimValue", T.Text)],
                ["Id"], [new Fk("UserId", users)],
                [new Ix($"IX_{prefix}UserClaims_UserId", "UserId", false)]),
            new(prefix + "UserLogins",
                [C("LoginProvider", T.Key, false), C("ProviderKey", T.Key, false), C("ProviderDisplayName", T.Text), C("UserId", T.Key, false)],
                ["LoginProvider", "ProviderKey"], [new Fk("UserId", users)],
                [new Ix($"IX_{prefix}UserLogins_UserId", "UserId", false)]),
            new(prefix + "UserRoles",
                [C("UserId", T.Key, false), C("RoleId", T.Key, false)],
                ["UserId", "RoleId"], [new Fk("UserId", users), new Fk("RoleId", roles)],
                [new Ix($"IX_{prefix}UserRoles_RoleId", "RoleId", false)]),
            new(prefix + "UserTokens",
                [C("UserId", T.Key, false), C("LoginProvider", T.Key, false), C("Name", T.Key, false), C("Value", T.Text)],
                ["UserId", "LoginProvider", "Name"], [new Fk("UserId", users)],
                []),
        };

        var statements = new List<string>();
        foreach (var table in tables)
        {
            statements.AddRange(Statements(kind, dialect, table));
        }

        return statements;
    }

    /// <summary>The identity tables that exist in the database of <paramref name="connection"/> (an open connection).</summary>
    public static async Task<IReadOnlyList<string>> ExistingTablesAsync(DbConnection connection, string provider, string prefix, CancellationToken cancellationToken = default)
    {
        var names = TableNames(prefix);
        var sql = DatabaseProviders.Normalize(provider) switch
        {
            DatabaseProviders.Sqlite => "SELECT name FROM sqlite_master WHERE type = 'table'",
            DatabaseProviders.MySql => "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()",
            _ => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'",
        };

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(0));
            }
        }

        return names.Where(existing.Contains).ToList();
    }

    /// <summary>The identity tables that do not exist yet in the database of <paramref name="connection"/> (an open connection).</summary>
    public static async Task<IReadOnlyList<string>> MissingTablesAsync(DbConnection connection, string provider, string prefix, CancellationToken cancellationToken = default)
    {
        var existing = await ExistingTablesAsync(connection, provider, prefix, cancellationToken);
        return TableNames(prefix).Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Creates the missing identity tables and indexes. Returns the tables that were missing.</summary>
    public static async Task<IReadOnlyList<string>> CreateMissingTablesAsync(DbConnection connection, string provider, string prefix, CancellationToken cancellationToken = default)
    {
        var missing = await MissingTablesAsync(connection, provider, prefix, cancellationToken);
        foreach (var statement in CreateStatements(provider, prefix))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return missing;
    }

    /// <summary>Creates the missing identity tables in the project's database, with its user store's prefix. Returns the tables that were missing.</summary>
    public static async Task<IReadOnlyList<string>> CreateMissingTablesAsync(Project project, string projectFolder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var provider = project.Database.Provider;
        await using var connection = DatabaseConnections.Create(provider, project.Database.ResolveConnectionString(projectFolder));
        await connection.OpenAsync(cancellationToken);
        return await CreateMissingTablesAsync(connection, provider, project.Security.Identity.TablePrefix, cancellationToken);
    }

    private static void RequireValidPrefix(string prefix)
    {
        if (!IsValidPrefix(prefix))
        {
            throw new ArgumentException($"'{prefix}' is not a valid identity table prefix: use 1 to 30 letters, digits and underscores, starting with a letter.", nameof(prefix));
        }
    }

    private enum Kind
    {
        Sqlite,
        SqlServer,
        MySql,
    }

    private enum T
    {
        Key,
        Name,
        Text,
        Bool,
        Int,
        Moment,
        Identity,
    }

    private sealed record Col(string Name, T Type, bool Nullable);

    private sealed record Fk(string Column, string Table);

    private sealed record Ix(string Name, string Column, bool Unique);

    private sealed record Table(string Name, Col[] Columns, string[] Key, Fk[] Keys, Ix[] Indexes);

    private static Col C(string name, T type, bool nullable = true) => new(name, type, nullable);

    private static string TypeName(Kind kind, T type) => (kind, type) switch
    {
        (Kind.Sqlite, T.Bool or T.Int) => "INTEGER",
        (Kind.Sqlite, T.Identity) => "INTEGER",
        (Kind.Sqlite, _) => "TEXT",
        (Kind.SqlServer, T.Key) => "nvarchar(450)",
        (Kind.SqlServer, T.Name) => "nvarchar(256)",
        (Kind.SqlServer, T.Text) => "nvarchar(max)",
        (Kind.SqlServer, T.Bool) => "bit",
        (Kind.SqlServer, T.Int) => "int",
        (Kind.SqlServer, T.Moment) => "datetimeoffset",
        (Kind.SqlServer, T.Identity) => "int IDENTITY(1,1)",
        (Kind.MySql, T.Key) => "varchar(255)",
        (Kind.MySql, T.Name) => "varchar(256)",
        (Kind.MySql, T.Text) => "longtext",
        (Kind.MySql, T.Bool) => "tinyint(1)",
        (Kind.MySql, T.Int) => "int",
        (Kind.MySql, T.Moment) => "datetime(6)",
        (Kind.MySql, T.Identity) => "int AUTO_INCREMENT",
        _ => throw new InvalidOperationException(),
    };

    private static IEnumerable<string> Statements(Kind kind, SqlDialect dialect, Table table)
    {
        var q = dialect.Quote;
        var parts = new List<string>();
        foreach (var column in table.Columns)
        {
            // SQLite only auto-increments a column declared INTEGER PRIMARY KEY.
            if (kind == Kind.Sqlite && column.Type == T.Identity)
            {
                parts.Add($"{q(column.Name)} INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT");
                continue;
            }

            parts.Add($"{q(column.Name)} {TypeName(kind, column.Type)} {(column.Nullable ? "NULL" : "NOT NULL")}");
        }

        if (!(kind == Kind.Sqlite && table.Columns.Any(c => c.Type == T.Identity)))
        {
            parts.Add($"CONSTRAINT {q("PK_" + table.Name)} PRIMARY KEY ({string.Join(", ", table.Key.Select(q))})");
        }

        foreach (var fk in table.Keys)
        {
            parts.Add($"CONSTRAINT {q($"FK_{table.Name}_{fk.Table}_{fk.Column}")} FOREIGN KEY ({q(fk.Column)}) REFERENCES {q(fk.Table)} ({q("Id")}) ON DELETE CASCADE");
        }

        if (kind == Kind.MySql)
        {
            // MySQL has no CREATE INDEX IF NOT EXISTS, so the indexes are part of the table.
            foreach (var index in table.Indexes)
            {
                parts.Add($"{(index.Unique ? "UNIQUE KEY" : "KEY")} {q(index.Name)} ({q(index.Column)})");
            }
        }

        var body = $"{q(table.Name)} ({string.Join(", ", parts)})";
        yield return kind switch
        {
            Kind.SqlServer => $"IF OBJECT_ID(N'{table.Name}', N'U') IS NULL CREATE TABLE {body}",
            Kind.MySql => $"CREATE TABLE IF NOT EXISTS {body} CHARACTER SET utf8mb4",
            _ => $"CREATE TABLE IF NOT EXISTS {body}",
        };

        if (kind == Kind.MySql)
        {
            yield break;
        }

        foreach (var index in table.Indexes)
        {
            var unique = index.Unique ? "UNIQUE " : string.Empty;
            yield return kind == Kind.SqlServer
                // A unique index of SQL Server admits one NULL only, so it skips the NULLs, as Entity Framework's does.
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{index.Name}' AND object_id = OBJECT_ID(N'{table.Name}')) "
                  + $"CREATE {unique}INDEX {q(index.Name)} ON {q(table.Name)} ({q(index.Column)}){(index.Unique ? $" WHERE {q(index.Column)} IS NOT NULL" : string.Empty)}"
                : $"CREATE {unique}INDEX IF NOT EXISTS {q(index.Name)} ON {q(table.Name)} ({q(index.Column)})";
        }
    }
}
