namespace WebAppForge.Core.Model;

/// <summary>A generator project: one database, its table settings and the output options.</summary>
public sealed class Project
{
    public string Name { get; set; } = "Project1";

    /// <summary>Stable identifier used in generated code; kept when a project is copied.</summary>
    public Guid ProjectId { get; set; } = Guid.NewGuid();

    public DatabaseSettings Database { get; set; } = new();

    public AspNetSettings AspNet { get; set; } = new();

    public SecuritySettings Security { get; set; } = new();

    /// <summary>Audit trail of inserts, updates and deletes (off by default).</summary>
    public AuditSettings Audit { get; set; } = new();

    /// <summary>Multi-language support of the generated site (off by default) and its stored translations.</summary>
    public LocalizationSettings Localization { get; set; } = new();

    public BuildSettings Build { get; set; } = new();

    /// <summary>Project-wide metadata and runtime options for the generated site.</summary>
    public ProjectMetadata Meta { get; set; } = new();

    /// <summary>Output folder, relative to the project file or absolute.</summary>
    public string OutputFolder { get; set; } = "generated";

    public List<TableInfo> Tables { get; } = [];

    /// <summary>Summary, crosstab, chart, calendar and dashboard pages of the generated site.</summary>
    public List<ReportInfo> Reports { get; } = [];

    /// <summary>Export menu and import pages of the generated site (off by default).</summary>
    public ImportExportSettings ImportExport { get; set; } = new();
}

/// <summary>The database products the generator supports. The keys are stored in the project file.</summary>
public static class DatabaseProviders
{
    public const string Sqlite = "sqlite";

    public const string SqlServer = "sqlserver";

    /// <summary>Azure SQL Database: the SQL Server dialect and driver, with Entra ID sign-in through the connection string.</summary>
    public const string AzureSql = "azuresql";

    public const string MySql = "mysql";

    public static IReadOnlyList<string> All { get; } = [Sqlite, SqlServer, AzureSql, MySql];

    public static bool IsKnown(string? provider) => All.Contains(Normalize(provider));

    /// <summary>The key in lower case, with "sqlite" for an empty value.</summary>
    public static string Normalize(string? provider) => string.IsNullOrWhiteSpace(provider) ? Sqlite : provider.Trim().ToLowerInvariant();

    /// <summary>True for SQL Server and Azure SQL, which share the dialect and the driver.</summary>
    public static bool IsSqlServer(string? provider) => Normalize(provider) is SqlServer or AzureSql;

    /// <summary>True for the providers that connect to a server with a connection string instead of opening a file.</summary>
    public static bool IsServer(string? provider) => Normalize(provider) != Sqlite;

    public static string DisplayName(string? provider) => Normalize(provider) switch
    {
        SqlServer => "SQL Server",
        AzureSql => "Azure SQL",
        MySql => "MySQL",
        _ => "SQLite",
    };
}

public sealed class DatabaseSettings
{
    /// <summary>Provider key, one of <see cref="DatabaseProviders.All"/>.</summary>
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    /// <summary>SQLite file path, relative to the project file folder (or absolute). Forward slashes.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Connection string of a server database. It is saved in the project file and written to the generated appsettings.json,
    /// so a password in it is stored in plain text; <see cref="ConnectionStringVariable"/> keeps it out of both.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Name of an environment variable that holds the connection string of a server database. When set, the editor reads the
    /// connection from it, the generated appsettings.json leaves the connection empty and the generated site reads the variable.
    /// </summary>
    public string ConnectionStringVariable { get; set; } = string.Empty;

    public bool IsServer => DatabaseProviders.IsServer(Provider);

    /// <summary>Resolves the connection string against the folder that contains the project file.</summary>
    public string ResolveConnectionString(string projectFolder)
    {
        if (IsServer)
        {
            var variable = ConnectionStringVariable.Trim();
            if (variable.Length > 0 && Environment.GetEnvironmentVariable(variable) is { Length: > 0 } fromVariable)
            {
                return fromVariable;
            }

            return variable.Length > 0 ? string.Empty : ConnectionString;
        }

        var full = System.IO.Path.IsPathRooted(Path)
            ? Path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(projectFolder, Path));
        return $"Data Source={full}";
    }

    /// <summary>The connection string written to the generated appsettings.json: empty when it comes from an environment variable.</summary>
    public string GeneratedConnectionString(string projectFolder)
        => IsServer && ConnectionStringVariable.Trim().Length > 0 ? string.Empty : ResolveConnectionString(projectFolder);
}

/// <summary>Application-level settings that apply to every generated page.</summary>
public sealed class AspNetSettings
{
    public string Title { get; set; } = "Web Application";

    public string Theme { get; set; } = "bootstrap";

    public bool IncludeHomePage { get; set; } = true;
}

/// <summary>
/// Login security for the generated site. When <see cref="Enabled"/> is set, the generated site uses cookie
/// login against <see cref="UserTable"/>, and every page and API route requires a signed-in user except /login and /logout.
/// </summary>
public sealed class SecuritySettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>Name of the generated table that holds the users. Must be one of the generated tables when enabled.</summary>
    public string UserTable { get; set; } = string.Empty;

    /// <summary>Column in <see cref="UserTable"/> that holds the user name.</summary>
    public string UserNameField { get; set; } = string.Empty;

    /// <summary>Column in <see cref="UserTable"/> that holds the password hash (see Security\Passwords.cs in the generated site).</summary>
    public string PasswordField { get; set; } = string.Empty;

    /// <summary>Whether failed logins are counted per user name and the user is locked out after too many.</summary>
    public bool TrackFailedAttempts { get; set; } = true;

    /// <summary>Failures allowed inside one window before logins for that user name are refused.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Length of the failed-attempt window in minutes. The window starts at the first failure.</summary>
    public int FailedAttemptsWindowMinutes { get; set; } = 10;

    /// <summary>
    /// Integer column in <see cref="UserTable"/> that holds the user's level (User Level security, table level).
    /// Empty means no level security. Only used when <see cref="Enabled"/> is set.
    /// </summary>
    public string UserLevelField { get; set; } = string.Empty;

    /// <summary>When set (and login is enabled), anonymous visitors can sign up at /register. Off by default.</summary>
    public bool AllowRegistration { get; set; } = false;

    /// <summary>When set (and login is enabled), signed-in users can change their own password at /changepassword.</summary>
    public bool AllowChangePassword { get; set; } = true;

    /// <summary>
    /// Where the users are kept. With <see cref="UserStore.AspNetIdentity"/> the user table settings above are not used, and
    /// User Level security is always on (levels come from the identity claims, see <see cref="IdentitySettings"/>).
    /// </summary>
    public UserStore UserStore { get; set; } = UserStore.ProjectTable;

    /// <summary>Options of the ASP.NET Core Identity user store.</summary>
    public IdentitySettings Identity { get; set; } = new();

    /// <summary>True when login is enabled and the users are kept by ASP.NET Core Identity.</summary>
    public bool UsesIdentity => Enabled && UserStore == UserStore.AspNetIdentity;

    /// <summary>True when User Level security applies: login with a user level field, or with ASP.NET Core Identity.</summary>
    public bool HasUserLevels => Enabled && (UserStore == UserStore.AspNetIdentity || !string.IsNullOrEmpty(UserLevelField));
}

public sealed class TableInfo
{
    public string Name { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    /// <summary>Whether this table is generated at all.</summary>
    public bool Generate { get; set; } = true;

    /// <summary>Optional WHERE clause fragment (SQL, without the WHERE keyword).</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// Column that holds the owning user's name (User ID security, record level). Empty means the table has no
    /// record-level security. Only used when <see cref="SecuritySettings.Enabled"/> is set; otherwise it is ignored.
    /// </summary>
    public string OwnerField { get; set; } = string.Empty;

    /// <summary>
    /// Lowest user level that may list, search and view this table. 0 means any signed-in user. Only used when
    /// <see cref="SecuritySettings.UserLevelField"/> is set and security is enabled; otherwise it is ignored.
    /// </summary>
    public int ViewMinLevel { get; set; }

    /// <summary>
    /// Lowest user level that may add, copy, edit and delete rows of this table, and use the API POST/PUT/DELETE routes.
    /// 0 means any signed-in user. Same rules as <see cref="ViewMinLevel"/>.
    /// </summary>
    public int EditMinLevel { get; set; }

    /// <summary>ORDER BY fields, applied in order.</summary>
    public List<SortField> Sort { get; } = [];

    public PageSettings Pages { get; set; } = new();

    /// <summary>Table-level look and menu placement.</summary>
    public TableUi Ui { get; set; } = new();

    /// <summary>One design per page type (title, layout, columns, buttons, modal, CSS).</summary>
    public PageDesigns Designs { get; set; } = new();

    /// <summary>Whether and how changes to this table are written to the audit trail.</summary>
    public TableAudit Audit { get; set; } = new();

    /// <summary>Detail tables of this master table (master/detail and multiple master/detail).</summary>
    public List<DetailLink> Details { get; } = [];

    /// <summary>Foreign keys read from the database (metadata for suggestions).</summary>
    public List<ForeignKeyInfo> ForeignKeys { get; } = [];

    /// <summary>Pages of the multi-page forms of this table, in order. Used when a design sets a MultiPage mode.</summary>
    public List<FormPageDef> FormPages { get; } = [];

    /// <summary>Export and import of this table; used when <see cref="ImportExportSettings.Enabled"/> is set.</summary>
    public TableImportExport ImportExport { get; set; } = new();

    public List<FieldInfo> Fields { get; } = [];

    /// <summary>Primary key columns in key order. Empty when the table has no key.</summary>
    public IReadOnlyList<FieldInfo> PrimaryKeys => Fields
        .Where(f => f.IsPrimaryKey)
        .OrderBy(f => f.KeyPosition > 0 ? f.KeyPosition : int.MaxValue)
        .ToList();

    /// <summary>First primary key column, or null when the table has no key.</summary>
    public FieldInfo? PrimaryKey => PrimaryKeys.FirstOrDefault();
}

public sealed class SortField
{
    public string Field { get; set; } = string.Empty;

    public bool Descending { get; set; }
}

/// <summary>Which pages are generated for a table, and list paging.</summary>
public sealed class PageSettings
{
    public bool List { get; set; } = true;

    public bool View { get; set; } = true;

    public bool Add { get; set; } = true;

    public bool Copy { get; set; } = true;

    public bool Edit { get; set; } = true;

    public bool Delete { get; set; } = true;

    public bool Search { get; set; } = true;

    public int PageSize { get; set; } = 20;
}

public sealed class FieldInfo
{
    public string Name { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public FieldType Type { get; set; } = FieldType.Text;

    public bool IsPrimaryKey { get; set; }

    /// <summary>1-based position of this column in a composite primary key. 0 when unknown, which keeps field order.</summary>
    public int KeyPosition { get; set; }

    public bool IsAutoIncrement { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>True when the column has a DEFAULT value, so an insert may leave it out.</summary>
    public bool HasDefault { get; set; }

    public bool ShowInList { get; set; } = true;

    public bool ShowInView { get; set; } = true;

    public bool ShowInAdd { get; set; } = true;

    public bool ShowInEdit { get; set; } = true;

    public bool Searchable { get; set; } = true;

    /// <summary>Secret values (such as password hashes) are never serialized by the generated JSON API.</summary>
    public bool IsSecret { get; set; }

    /// <summary>How the field is edited, validated and displayed.</summary>
    public FieldUi Ui { get; set; } = new();

    /// <summary>
    /// How a number column holds a date, for reports: grouping by year, quarter, month or day, calendars and date filters.
    /// <see cref="DateStorage.Text"/> (the default) means the column holds ISO text, as DATE and DATETIME columns do.
    /// </summary>
    public DateStorage DateStorage { get; set; } = DateStorage.Text;
}

/// <summary>How a column stores a date.</summary>
public enum DateStorage
{
    /// <summary>ISO 8601 text, such as 2024-05-01 or 2024-05-01 13:45:00.</summary>
    Text,

    /// <summary>Seconds since 1970-01-01 UTC.</summary>
    UnixSeconds,

    /// <summary>Milliseconds since 1970-01-01 UTC.</summary>
    UnixMilliseconds,

    /// <summary>Julian day number (SQLite julianday()).</summary>
    JulianDay,
}

public enum FieldType
{
    Text,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Binary,
}
