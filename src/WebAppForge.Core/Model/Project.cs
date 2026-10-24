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

    /// <summary>Multi-language support of the generated site (off by default) and its stored translations.</summary>
    public LocalizationSettings Localization { get; set; } = new();

    public BuildSettings Build { get; set; } = new();

    /// <summary>Project-wide metadata and runtime options for the generated site.</summary>
    public ProjectMetadata Meta { get; set; } = new();

    /// <summary>Output folder, relative to the project file or absolute.</summary>
    public string OutputFolder { get; set; } = "generated";

    public List<TableInfo> Tables { get; } = [];

    /// <summary>Summary, crosstab, chart, calendar and dashboard reports.</summary>
    public List<ReportDef> Reports { get; } = [];
}

public sealed class DatabaseSettings
{
    /// <summary>Provider key. Only "sqlite" is implemented in this version.</summary>
    public string Provider { get; set; } = "sqlite";

    /// <summary>SQLite file path, relative to the project file folder (or absolute). Forward slashes.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Resolves the connection string against the folder that contains the project file.</summary>
    public string ResolveConnectionString(string projectFolder)
    {
        var full = System.IO.Path.IsPathRooted(Path)
            ? Path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(projectFolder, Path));
        return $"Data Source={full}";
    }
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

    /// <summary>Detail tables of this master table (master/detail and multiple master/detail).</summary>
    public List<DetailLink> Details { get; } = [];

    /// <summary>Foreign keys read from the database (metadata for suggestions).</summary>
    public List<ForeignKeyInfo> ForeignKeys { get; } = [];

    /// <summary>Pages of the multi-page forms of this table, in order. Used when a design sets a MultiPage mode.</summary>
    public List<FormPageDef> FormPages { get; } = [];

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
