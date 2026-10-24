using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// The SQL and ADO.NET differences between the supported databases. The templates are written for SQLite; each method returns
/// the SQLite text unchanged for <see cref="Sqlite"/>, so SQLite sites are generated exactly as before other databases existed.
/// </summary>
internal abstract class SqlDialect
{
    public static readonly SqlDialect Sqlite = new SqliteDialect();

    public static readonly SqlDialect SqlServer = new SqlServerDialect();

    public static readonly SqlDialect MySql = new MySqlDialect();

    public static SqlDialect For(string? provider) => DatabaseProviders.Normalize(provider) switch
    {
        DatabaseProviders.Sqlite => Sqlite,
        DatabaseProviders.SqlServer or DatabaseProviders.AzureSql => SqlServer,
        DatabaseProviders.MySql => MySql,
        var other => throw new NotSupportedException($"Provider '{other}' is not supported."),
    };

    public abstract string Name { get; }

    // ---- ADO.NET ---------------------------------------------------------------------------------

    /// <summary>The PackageReference line of the driver in the generated .csproj (with its leading indentation).</summary>
    public abstract string PackageReferences { get; }

    /// <summary>
    /// Rewrites the ADO.NET names of a generated C# file written for SQLite (namespace, connection, command, reader, exception)
    /// to this driver's names. The SQL in the file is already in this dialect.
    /// </summary>
    public virtual string AdaptCode(string code) => code;

    /// <summary>The C# condition, on <c>ex</c> of the driver's exception type, that a unique or primary key constraint was violated.</summary>
    public abstract string UniqueViolation { get; }

    // ---- identifiers and literals ----------------------------------------------------------------

    public abstract string Quote(string name);

    /// <summary>A SQL string literal.</summary>
    public virtual string Text(string value) => "'" + value.Replace("'", "''") + "'";

    // ---- queries ---------------------------------------------------------------------------------

    /// <summary>The paging clause after ORDER BY, with the parameters @limit and @offset.</summary>
    public virtual string Page => " LIMIT @limit OFFSET @offset";

    /// <summary>ORDER BY used when a paged query has no order of its own (SQL Server cannot page without one).</summary>
    public virtual string FallbackOrder => string.Empty;

    /// <summary>The text after SELECT that limits a query to <paramref name="rows"/> rows (SQL Server's TOP).</summary>
    public virtual string Top(int rows) => string.Empty;

    /// <summary>The clause at the end of a query that limits it to <paramref name="rows"/> rows.</summary>
    public virtual string Limit(int rows) => " LIMIT " + rows;

    /// <summary>SQL that returns the identity value of the last insert on the same connection.</summary>
    public abstract string LastInsertId { get; }

    /// <summary>SQL appended to an INSERT so that the same command returns the new identity value.</summary>
    public virtual string IdentityAfterInsert => "; " + LastInsertId;

    /// <summary>True when GROUP BY can name select-list positions and subqueries (not SQL Server).</summary>
    public virtual bool GroupsByPosition => true;

    // ---- expressions -----------------------------------------------------------------------------

    public virtual string TextCast(string expression) => $"CAST({expression} AS TEXT)";

    public virtual string IntegerCast(string expression) => $"CAST({expression} AS INTEGER)";

    /// <summary>Whole-number division of two integer expressions.</summary>
    public virtual string IntegerDivide(string dividend, string divisor) => $"{dividend} / {divisor}";

    /// <summary>True when ORDER BY of a query that lists a column twice needs the column qualified with its table (SQL Server).</summary>
    public virtual bool QualifiesOrder => false;

    public virtual string Concat(params string[] parts) => string.Join(" || ", parts);

    public virtual string Substring(string expression, int start, int length) => $"substr({expression}, {start}, {length})";

    /// <summary>ISO text (yyyy-MM-dd or yyyy-MM-dd HH:mm:ss) of a date column stored as <paramref name="storage"/>.</summary>
    public abstract string DateText(string column, FieldType type, DateStorage storage);

    /// <summary>The day (yyyy-MM-dd text) of an ISO date text.</summary>
    public virtual string Day(string dateText) => Substring(dateText, 1, 10);

    /// <summary>
    /// The fallback of a text value that is otherwise a column (ELSE of a CASE, last argument of COALESCE). SQL Server would convert
    /// the text to the column's type, so it gets the column as text.
    /// </summary>
    public virtual string TextFallback(string column) => column;

    /// <summary>Appended to a text comparison to ignore case. SQL Server and MySQL compare without case by default.</summary>
    public virtual string NoCase => string.Empty;

    // ---- statements ------------------------------------------------------------------------------

    public abstract string BeginTransaction { get; }

    public virtual string Commit => "COMMIT";

    public virtual string Rollback => "ROLLBACK";

    /// <summary>Creates the login_attempts table of the lockout store when it does not exist.</summary>
    public abstract string CreateLoginAttempts { get; }

    /// <summary>Inserts or updates the login_attempts row of @username with @failures and @windowStart.</summary>
    public abstract string UpsertLoginAttempt { get; }

    /// <summary>Creates the audit trail table <paramref name="name"/> (unquoted) when it does not exist.</summary>
    public abstract string CreateAuditTable(string name);

    private sealed class SqliteDialect : SqlDialect
    {
        public override string Name => DatabaseProviders.Sqlite;

        public override string PackageReferences => """    <PackageReference Include="Microsoft.Data.Sqlite" Version="@@SQLITE@@" />""";

        public override string UniqueViolation => "ex.SqliteErrorCode == 19";

        public override string Quote(string name)
        {
            if (name.Contains('"'))
            {
                throw new NotSupportedException($"Identifier '{name}' contains a double quote and cannot be generated.");
            }

            return "\"" + name + "\"";
        }

        public override string Substring(string expression, int start, int length) => $"substr({expression}, {start}, {length})";

        public override string DateText(string column, FieldType type, DateStorage storage) => storage switch
        {
            DateStorage.UnixSeconds => $"datetime({column}, 'unixepoch')",
            DateStorage.UnixMilliseconds => $"datetime({column} / 1000.0, 'unixepoch')",
            DateStorage.JulianDay => $"datetime({column})",
            _ => column,
        };

        public override string Day(string dateText) => $"date({dateText})";

        public override string NoCase => " COLLATE NOCASE";

        public override string LastInsertId => "SELECT last_insert_rowid()";

        // BEGIN IMMEDIATE takes SQLite's write lock before the first read.
        public override string BeginTransaction => "BEGIN IMMEDIATE";

        public override string CreateLoginAttempts =>
            "CREATE TABLE IF NOT EXISTS login_attempts (username TEXT PRIMARY KEY, failures INTEGER NOT NULL, window_start TEXT NOT NULL)";

        public override string UpsertLoginAttempt =>
            "INSERT INTO login_attempts (username, failures, window_start) VALUES (@username, @failures, @windowStart) "
            + "ON CONFLICT(username) DO UPDATE SET failures = excluded.failures, window_start = excluded.window_start";

        public override string CreateAuditTable(string name) =>
            $"CREATE TABLE IF NOT EXISTS {Quote(name)} (id INTEGER PRIMARY KEY, occurred_at TEXT NOT NULL, user_name TEXT, ip_address TEXT, "
            + "table_name TEXT NOT NULL, action TEXT NOT NULL, record_key TEXT, changes TEXT)";
    }

    private sealed class SqlServerDialect : SqlDialect
    {
        public override string Name => DatabaseProviders.SqlServer;

        public override string PackageReferences => """
                <PackageReference Include="Microsoft.Data.SqlClient" Version="7.1.1" />
                <!-- Microsoft.Data.SqlClient 7.1.1 brings 9.0.18, which has known vulnerabilities. -->
                <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />
            """;

        public override string AdaptCode(string code) => code
            .Replace("Microsoft.Data.Sqlite", "Microsoft.Data.SqlClient")
            .Replace("SqliteConnection", "SqlConnection")
            .Replace("SqliteCommand", "SqlCommand")
            .Replace("SqliteDataReader", "SqlDataReader")
            .Replace("SqliteException", "SqlException");

        // 2627: violation of a PRIMARY KEY or UNIQUE constraint; 2601: duplicate key in a unique index.
        public override string UniqueViolation => "ex.Number is 2627 or 2601";

        /// <summary>[name], or [schema].[table] for a name with a dot (tables outside dbo).</summary>
        public override string Quote(string name)
        {
            if (name.Contains(']'))
            {
                throw new NotSupportedException($"Identifier '{name}' contains a closing bracket and cannot be generated.");
            }

            var dot = name.IndexOf('.');
            return dot > 0 && dot < name.Length - 1 ? "[" + name[..dot] + "].[" + name[(dot + 1)..] + "]" : "[" + name + "]";
        }

        public override string Text(string value) => "N'" + value.Replace("'", "''") + "'";

        public override string Page => " OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY";

        public override string FallbackOrder => " ORDER BY (SELECT NULL)";

        public override string Top(int rows) => "TOP " + rows + " ";

        public override string Limit(int rows) => string.Empty;

        // SCOPE_IDENTITY() only sees the inserts of its own batch, which is why the insert and this run as one command. It ignores
        // identity values that triggers create in other tables, unlike @@IDENTITY. OUTPUT INSERTED is not used, because SQL Server
        // refuses it on a table with triggers.
        public override string LastInsertId => "SELECT CAST(SCOPE_IDENTITY() AS bigint)";

        public override bool GroupsByPosition => false;

        public override bool QualifiesOrder => true;

        public override string TextCast(string expression) => $"CAST({expression} AS nvarchar(max))";

        public override string IntegerCast(string expression) => $"CAST({expression} AS int)";

        public override string TextFallback(string column) => TextCast(column);

        public override string Concat(params string[] parts) => "CONCAT(" + string.Join(", ", parts) + ")";

        public override string Substring(string expression, int start, int length) => $"SUBSTRING({expression}, {start}, {length})";

        public override string DateText(string column, FieldType type, DateStorage storage) => storage switch
        {
            DateStorage.UnixSeconds => $"CONVERT(varchar(19), DATEADD(second, {column}, '19700101'), 120)",
            DateStorage.UnixMilliseconds => $"CONVERT(varchar(19), DATEADD(second, CAST({column} / 1000 AS int), '19700101'), 120)",
            DateStorage.JulianDay => $"CONVERT(varchar(19), DATEADD(second, CAST(ROUND(({column} - 2440587.5) * 86400, 0) AS int), '19700101'), 120)",
            _ when type is FieldType.Date or FieldType.DateTime => $"CONVERT(varchar(19), {column}, 120)",
            _ => column,
        };

        public override string BeginTransaction => "BEGIN TRANSACTION";

        public override string Commit => "COMMIT TRANSACTION";

        public override string Rollback => "IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION";

        public override string CreateLoginAttempts =>
            "IF OBJECT_ID(N'login_attempts', N'U') IS NULL CREATE TABLE login_attempts "
            + "(username nvarchar(256) NOT NULL PRIMARY KEY, failures int NOT NULL, window_start nvarchar(40) NOT NULL)";

        public override string UpsertLoginAttempt =>
            "MERGE login_attempts WITH (HOLDLOCK) AS t USING (SELECT @username AS username) AS s ON t.username = s.username "
            + "WHEN MATCHED THEN UPDATE SET failures = @failures, window_start = @windowStart "
            + "WHEN NOT MATCHED THEN INSERT (username, failures, window_start) VALUES (@username, @failures, @windowStart);";

        public override string CreateAuditTable(string name) =>
            $"IF OBJECT_ID({Text(name)}, N'U') IS NULL CREATE TABLE {Quote(name)} (id bigint IDENTITY(1,1) PRIMARY KEY, occurred_at datetime2 NOT NULL, "
            + "user_name nvarchar(256) NULL, ip_address nvarchar(64) NULL, table_name nvarchar(256) NOT NULL, action nvarchar(10) NOT NULL, "
            + "record_key nvarchar(400) NULL, changes nvarchar(max) NULL)";
    }

    private sealed class MySqlDialect : SqlDialect
    {
        public override string Name => DatabaseProviders.MySql;

        public override string PackageReferences => """    <PackageReference Include="MySqlConnector" Version="2.6.2" />""";

        public override string AdaptCode(string code) => code
            .Replace("using Microsoft.Data.Sqlite;", "using MySqlConnector;")
            .Replace("SqliteConnection", "MySqlConnection")
            .Replace("SqliteCommand", "MySqlCommand")
            .Replace("SqliteDataReader", "MySqlDataReader")
            .Replace("SqliteException", "MySqlException");

        public override string UniqueViolation => "ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry";

        public override string Quote(string name)
        {
            if (name.Contains('`'))
            {
                throw new NotSupportedException($"Identifier '{name}' contains a backtick and cannot be generated.");
            }

            return "`" + name + "`";
        }

        // MySQL treats a backslash in a string literal as an escape character.
        public override string Text(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";

        public override string LastInsertId => "SELECT LAST_INSERT_ID()";

        public override string TextCast(string expression) => $"CAST({expression} AS CHAR)";

        public override string IntegerCast(string expression) => $"CAST({expression} AS SIGNED)";

        // MySQL's / returns a decimal; DIV is the whole-number division.
        public override string IntegerDivide(string dividend, string divisor) => $"{dividend} DIV {divisor}";

        public override string Concat(params string[] parts) => "CONCAT(" + string.Join(", ", parts) + ")";

        public override string Substring(string expression, int start, int length) => $"SUBSTRING({expression}, {start}, {length})";

        // DATE_ADD from the epoch keeps Unix times in UTC; FROM_UNIXTIME would use the session time zone.
        public override string DateText(string column, FieldType type, DateStorage storage) => storage switch
        {
            DateStorage.UnixSeconds => $"DATE_FORMAT(DATE_ADD('1970-01-01', INTERVAL {column} SECOND), '%Y-%m-%d %H:%i:%s')",
            DateStorage.UnixMilliseconds => $"DATE_FORMAT(DATE_ADD('1970-01-01', INTERVAL FLOOR({column} / 1000) SECOND), '%Y-%m-%d %H:%i:%s')",
            DateStorage.JulianDay => $"DATE_FORMAT(DATE_ADD('1970-01-01', INTERVAL ROUND(({column} - 2440587.5) * 86400) SECOND), '%Y-%m-%d %H:%i:%s')",
            _ when type is FieldType.Date or FieldType.DateTime => $"DATE_FORMAT({column}, '%Y-%m-%d %H:%i:%s')",
            _ => column,
        };

        public override string BeginTransaction => "START TRANSACTION";

        public override string CreateLoginAttempts =>
            "CREATE TABLE IF NOT EXISTS login_attempts (username VARCHAR(255) NOT NULL PRIMARY KEY, failures INT NOT NULL, window_start VARCHAR(40) NOT NULL)";

        public override string UpsertLoginAttempt =>
            "INSERT INTO login_attempts (username, failures, window_start) VALUES (@username, @failures, @windowStart) "
            + "ON DUPLICATE KEY UPDATE failures = @failures, window_start = @windowStart";

        public override string CreateAuditTable(string name) =>
            $"CREATE TABLE IF NOT EXISTS {Quote(name)} (id BIGINT AUTO_INCREMENT PRIMARY KEY, occurred_at DATETIME(6) NOT NULL, user_name VARCHAR(256) NULL, "
            + "ip_address VARCHAR(64) NULL, table_name VARCHAR(256) NOT NULL, action VARCHAR(10) NOT NULL, record_key VARCHAR(400) NULL, changes LONGTEXT NULL)";
    }
}
