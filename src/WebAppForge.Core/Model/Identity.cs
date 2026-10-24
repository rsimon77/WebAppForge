namespace WebAppForge.Core.Model;

/// <summary>Where the generated site keeps its users.</summary>
public enum UserStore
{
    /// <summary>A table of the project (<see cref="SecuritySettings.UserTable"/>) with a user name and a password hash column.</summary>
    ProjectTable,

    /// <summary>
    /// ASP.NET Core Identity over the standard AspNet* tables (AspNetUsers, AspNetRoles, ...). The site keeps them with its own
    /// Dapper stores, so no Entity Framework is needed, and creates the missing ones at startup.
    /// </summary>
    AspNetIdentity,
}

/// <summary>
/// Options of <see cref="UserStore.AspNetIdentity"/>. User levels are claims: a user's own "user_level" claim and the
/// "user_level" claims of their roles; the highest one is the user's level, which the table levels are checked against.
/// </summary>
public sealed class IdentitySettings
{
    public const string DefaultTablePrefix = "AspNet";

    public const string DefaultAdminPasswordVariable = "WEBAPPFORGE_ADMIN_PASSWORD";

    /// <summary>The site creates the identity tables it does not find, at startup (idempotent).</summary>
    public bool CreateTablesAtStartup { get; set; } = true;

    /// <summary>Prefix of the identity table names: "AspNet" gives AspNetUsers, AspNetRoles and the other standard names.</summary>
    public string TablePrefix { get; set; } = DefaultTablePrefix;

    public int MinPasswordLength { get; set; } = 8;

    public bool RequireDigit { get; set; }

    public bool RequireUppercase { get; set; }

    public bool RequireLowercase { get; set; }

    public bool RequireNonAlphanumeric { get; set; }

    /// <summary>Users with an unconfirmed e-mail address cannot sign in. An administrator confirms it on the user's edit page.</summary>
    public bool RequireConfirmedEmail { get; set; }

    /// <summary>User name of the administrator the site creates at startup when it has no users yet.</summary>
    public string AdminUserName { get; set; } = "admin";

    /// <summary>
    /// Environment variable that holds the first administrator's password. The password is never stored in the project or the
    /// generated files; without the variable the site creates no administrator.
    /// </summary>
    public string AdminPasswordVariable { get; set; } = DefaultAdminPasswordVariable;

    /// <summary>User level of administrators: they manage users and roles. The first administrator gets this level.</summary>
    public int AdminLevel { get; set; } = 100;

    /// <summary>User level given to users who sign up themselves at /register.</summary>
    public int RegisteredUserLevel { get; set; }

    public bool IsDefault() =>
        CreateTablesAtStartup && TablePrefix == DefaultTablePrefix && MinPasswordLength == 8 && !RequireDigit && !RequireUppercase
        && !RequireLowercase && !RequireNonAlphanumeric && !RequireConfirmedEmail && AdminUserName == "admin"
        && AdminPasswordVariable == DefaultAdminPasswordVariable && AdminLevel == 100 && RegisteredUserLevel == 0;
}
