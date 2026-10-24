using System.Globalization;
using System.Text.RegularExpressions;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Generation;

/// <summary>
/// The files of a site that keeps its users with ASP.NET Core Identity (<see cref="UserStore.AspNetIdentity"/>): Dapper stores over
/// the standard AspNet* tables, the Identity setup, the login and account pages and the user and role management pages.
/// Fixed texts come from the UI string catalog (<c>@@UI:id@@</c>), so they are translatable.
/// </summary>
internal static partial class Templates
{
    /// <summary>Replaces the placeholders of an identity template: namespace, localization, UI texts and table names.</summary>
    private static string IdentityText(string text, string ns, SecuritySettings security, IReadOnlyDictionary<string, string>? values = null)
    {
        var prefix = security.Identity.TablePrefix;
        text = text
            .Replace("@@NS@@", ns)
            .Replace("@@USING_LOCALIZATION@@", Localized ? $"using {ns}.Localization;" : string.Empty);
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
        {
            text = text.Replace("@@" + key + "@@", value);
        }

        // Table names are always inside C# string literals of the templates, so the quoted name is escaped for one.
        text = Regex.Replace(text, @"@@T:(\w+)@@", m => Db.Quote(prefix + m.Groups[1].Value).Replace("\\", "\\\\").Replace("\"", "\\\""));
        text = Regex.Replace(text, @"@@UI:(\w+)@@", m => Ui(m.Groups[1].Value));
        // A template line that is only a removed using directive leaves an empty line behind; keep the file tidy.
        return text.Replace("\n\n\nnamespace", "\n\nnamespace");
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Security/Permissions.cs of an identity site: the level is the highest "user_level" claim (the user's own and their roles').</summary>
    public static string IdentityPermissions(string ns) => ReplaceOnce(AuthTemplates.Permissions(ns),
        """
            /// <summary>The signed-in user's level, or 0 when the claim is missing or not a number.</summary>
            public static int LevelOf(ClaimsPrincipal user) =>
                int.TryParse(user.FindFirst(LevelClaimType)?.Value, out var level) ? level : 0;
        """,
        """
            /// <summary>
            /// The signed-in user's level: the highest "user_level" claim, from the user's own claims and those of their roles.
            /// 0 when there is none; a claim that is not a number counts as 0.
            /// </summary>
            public static int LevelOf(ClaimsPrincipal user) =>
                user.FindAll(LevelClaimType).Select(c => int.TryParse(c.Value, out var level) ? level : 0).DefaultIfEmpty(0).Max();
        """);

    /// <summary>Security/SiteIdentity.cs: the Identity services, the table creation and the first administrator.</summary>
    public static string IdentitySetup(string ns, SecuritySettings security)
    {
        var identity = security.Identity;
        var create = identity.CreateTablesAtStartup
            ? "        await using (var connection = new SqliteConnection(connectionString))\n"
              + "        {\n"
              + "            await connection.OpenAsync();\n"
              + "            foreach (var statement in CreateStatements)\n"
              + "            {\n"
              + "                await connection.ExecuteAsync(statement);\n"
              + "            }\n"
              + "        }\n\n"
            : string.Empty;
        var statements = identity.CreateTablesAtStartup
            ? "    // Creates each identity table and index that does not exist (see WebAppForge's IdentitySchema).\n"
              + "    private static readonly string[] CreateStatements =\n    [\n"
              + string.Concat(IdentitySchema.CreateStatements(Db, identity.TablePrefix).Select(s => "        " + Lit(s) + ",\n"))
              + "    ];\n\n"
            : string.Empty;
        return IdentityText(IdentitySetupText, ns, security, new Dictionary<string, string>
        {
            ["ADMIN_LEVEL"] = Int(identity.AdminLevel),
            ["REGISTERED_LEVEL"] = Int(identity.RegisteredUserLevel),
            ["ADMIN_NAME"] = Lit(identity.AdminUserName.Trim()),
            ["ADMIN_VARIABLE"] = Lit(identity.AdminPasswordVariable.Trim()),
            ["MIN_LENGTH"] = Int(identity.MinPasswordLength),
            ["DIGIT"] = Bool(identity.RequireDigit),
            ["UPPER"] = Bool(identity.RequireUppercase),
            ["LOWER"] = Bool(identity.RequireLowercase),
            ["SYMBOL"] = Bool(identity.RequireNonAlphanumeric),
            ["CONFIRMED"] = Bool(identity.RequireConfirmedEmail),
            ["TRACK"] = Bool(security.TrackFailedAttempts),
            ["MAX_FAILURES"] = Int(Math.Max(1, security.MaxFailedAttempts)),
            ["WINDOW"] = Int(Math.Max(1, security.FailedAttemptsWindowMinutes)),
            ["STATEMENTS"] = statements,
            ["CREATE"] = create,
        });
    }

    /// <summary>Security/IdentityStores.cs: the Dapper user and role stores.</summary>
    public static string IdentityStores(string ns, SecuritySettings security) =>
        IdentityText(IdentityStoresText, ns, security, new Dictionary<string, string> { ["PAGE"] = Db.Page });

    /// <summary>Pages/AuthPages.cs of an identity site: login and logout through SignInManager.</summary>
    public static string IdentityAuthPages(string ns, SecuritySettings security)
    {
        var registered = !security.AllowRegistration
            ? string.Empty
            : security.Identity.RequireConfirmedEmail
                ? "        if (message is null && context.Request.Query.ContainsKey(\"registered\"))\n        {\n            body.Append(\"<div class=\\\"alert alert-success\\\">\").Append(Html.Encode(@@UI:idRegistrationPending@@)).Append(\"</div>\");\n        }\n\n"
                : "        if (message is null && context.Request.Query.ContainsKey(\"registered\"))\n        {\n            body.Append(\"<div class=\\\"alert alert-success\\\">\").Append(Html.Encode(@@UI:registrationComplete@@)).Append(\"</div>\");\n        }\n\n";
        var registerLink = security.AllowRegistration
            ? "        body.Append(Html.Link(\"/register\", @@UI:register@@, \"btn btn-link\"));\n"
            : string.Empty;
        return IdentityText(IdentityAuthPagesText, ns, security, new Dictionary<string, string>
        {
            ["TRACK"] = Bool(security.TrackFailedAttempts),
            ["REGISTERED"] = registered,
            ["REGISTER_LINK"] = registerLink,
        });
    }

    /// <summary>Pages/UserAccountPages.cs of an identity site: sign-up and password change through UserManager.</summary>
    public static string IdentityAccountPages(string ns, SecuritySettings security)
    {
        var level = security.Identity.RegisteredUserLevel != 0
            ? "        await SiteIdentity.SetLevelAsync(users, user, SiteIdentity.RegisteredUserLevel);\n"
            : string.Empty;
        var members = (security.AllowRegistration ? IdentityRegisterText.Replace("@@LEVEL@@", level) : string.Empty)
            + (security.AllowChangePassword ? IdentityChangePasswordText : string.Empty);
        return IdentityText(IdentityAccountHeadText + members + IdentityAccountTailText, ns, security);
    }

    /// <summary>Pages/UserAdminPages.cs: user and role management for administrators.</summary>
    public static string IdentityAdminPages(string ns, SecuritySettings security) =>
        IdentityText(IdentityAdminPagesText, ns, security, new Dictionary<string, string>
        {
            ["COUNT_SQL"] = Lit("SELECT COUNT(*) FROM @@T:Users@@ WHERE NormalizedUserName LIKE @q OR NormalizedEmail LIKE @q"),
            ["PAGE_SQL"] = Lit("SELECT * FROM @@T:Users@@ WHERE NormalizedUserName LIKE @q OR NormalizedEmail LIKE @q ORDER BY NormalizedUserName" + Db.Page),
        });

    private const string IdentitySetupText = """
        using System.Data;
        using System.Globalization;
        using System.Security.Claims;
        using Dapper;
        using Microsoft.AspNetCore.Identity;
        using Microsoft.Data.Sqlite;
        @@USING_LOCALIZATION@@

        namespace @@NS@@.Security;

        /// <summary>The connection string of the identity stores.</summary>
        public sealed record IdentityDb(string ConnectionString);

        /// <summary>
        /// ASP.NET Core Identity of the site, without Entity Framework: the Dapper stores keep the users and roles in the standard
        /// AspNet* tables. User levels are "user_level" claims of the users and the roles.
        /// </summary>
        public static class SiteIdentity
        {
            /// <summary>Users with this level manage the users and roles.</summary>
            public const int AdminLevel = @@ADMIN_LEVEL@@;

            /// <summary>Level of the users who sign up themselves.</summary>
            public const int RegisteredUserLevel = @@REGISTERED_LEVEL@@;

            private const string AdminUserName = @@ADMIN_NAME@@;

            // The first administrator's password is read from this environment variable; it is never stored by the site.
            private const string AdminPasswordVariable = @@ADMIN_VARIABLE@@;

        @@STATEMENTS@@    public static IServiceCollection AddSiteIdentity(this IServiceCollection services, string connectionString)
            {
                SqlMapper.AddTypeHandler(new MomentHandler());
                services.AddSingleton(new IdentityDb(connectionString));
                services.AddIdentity<IdentityUser, IdentityRole>(options =>
                    {
                        options.Password.RequiredLength = @@MIN_LENGTH@@;
                        options.Password.RequireDigit = @@DIGIT@@;
                        options.Password.RequireUppercase = @@UPPER@@;
                        options.Password.RequireLowercase = @@LOWER@@;
                        options.Password.RequireNonAlphanumeric = @@SYMBOL@@;
                        options.Password.RequiredUniqueChars = 1;
                        options.Lockout.AllowedForNewUsers = @@TRACK@@;
                        options.Lockout.MaxFailedAccessAttempts = @@MAX_FAILURES@@;
                        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(@@WINDOW@@);
                        options.SignIn.RequireConfirmedEmail = @@CONFIRMED@@;
                        options.User.RequireUniqueEmail = false;
                    })
                    .AddUserStore<DapperUserStore>()
                    .AddRoleStore<DapperRoleStore>()
                    .AddErrorDescriber<SiteIdentityErrors>();
                services.ConfigureApplicationCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                    options.AccessDeniedPath = "/login";
                });
                return services;
            }

            /// <summary>
            /// Creates the missing identity tables and, when the site has no users yet, the administrator whose password is in the
            /// environment variable. Called once at startup, before the site serves requests.
            /// </summary>
            public static async Task InitializeAsync(IServiceProvider services, string connectionString)
            {
        @@CREATE@@        await using var scope = services.CreateAsyncScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                await using (var connection = new SqliteConnection(connectionString))
                {
                    await connection.OpenAsync();
                    if (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM @@T:Users@@") > 0)
                    {
                        return;
                    }
                }

                var password = Environment.GetEnvironmentVariable(AdminPasswordVariable);
                if (string.IsNullOrEmpty(password))
                {
                    Console.Error.WriteLine($"The site has no users. Set the {AdminPasswordVariable} environment variable to create the administrator '{AdminUserName}'.");
                    return;
                }

                var admin = new IdentityUser(AdminUserName) { EmailConfirmed = true };
                var created = await users.CreateAsync(admin, password);
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException($"The administrator '{AdminUserName}' could not be created: " + string.Join(" ", created.Errors.Select(e => e.Description)));
                }

                await SetLevelAsync(users, admin, AdminLevel);
            }

            /// <summary>The user's own level (the highest of their "user_level" claims, without their roles), or 0.</summary>
            public static async Task<int> OwnLevelAsync(UserManager<IdentityUser> users, IdentityUser user) =>
                Levels(await users.GetClaimsAsync(user)).DefaultIfEmpty(0).Max();

            /// <summary>The level of a role, or 0.</summary>
            public static async Task<int> RoleLevelAsync(RoleManager<IdentityRole> roles, IdentityRole role) =>
                Levels(await roles.GetClaimsAsync(role)).DefaultIfEmpty(0).Max();

            /// <summary>Replaces the user's own "user_level" claim. Level 0 leaves the user without one.</summary>
            public static async Task<IdentityResult> SetLevelAsync(UserManager<IdentityUser> users, IdentityUser user, int level)
            {
                var existing = (await users.GetClaimsAsync(user)).Where(c => c.Type == Permissions.LevelClaimType).ToList();
                if (existing.Count > 0)
                {
                    var removed = await users.RemoveClaimsAsync(user, existing);
                    if (!removed.Succeeded)
                    {
                        return removed;
                    }
                }

                return level == 0 ? IdentityResult.Success : await users.AddClaimAsync(user, LevelClaim(level));
            }

            /// <summary>Replaces the "user_level" claim of a role. Level 0 leaves the role without one.</summary>
            public static async Task<IdentityResult> SetRoleLevelAsync(RoleManager<IdentityRole> roles, IdentityRole role, int level)
            {
                foreach (var claim in (await roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.LevelClaimType).ToList())
                {
                    var removed = await roles.RemoveClaimAsync(role, claim);
                    if (!removed.Succeeded)
                    {
                        return removed;
                    }
                }

                return level == 0 ? IdentityResult.Success : await roles.AddClaimAsync(role, LevelClaim(level));
            }

            private static Claim LevelClaim(int level) => new(Permissions.LevelClaimType, level.ToString(CultureInfo.InvariantCulture));

            private static IEnumerable<int> Levels(IEnumerable<Claim> claims) =>
                claims.Where(c => c.Type == Permissions.LevelClaimType)
                    .Select(c => int.TryParse(c.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : 0);

            /// <summary>
            /// The lockout end: SQLite returns text, SQL Server a datetimeoffset and MySQL a DATETIME, all in UTC. It is written as a UTC
            /// date and time, which every database stores.
            /// </summary>
            private sealed class MomentHandler : SqlMapper.TypeHandler<DateTimeOffset>
            {
                public override DateTimeOffset Parse(object value) => value switch
                {
                    DateTimeOffset moment => moment,
                    DateTime utc => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
                    _ => DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                };

                public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value.UtcDateTime;
            }
        }

        /// <summary>The Identity error messages of the site, from its UI texts.</summary>
        public sealed class SiteIdentityErrors : IdentityErrorDescriber
        {
            public override IdentityError PasswordTooShort(int length) =>
                new() { Code = nameof(PasswordTooShort), Description = string.Format(CultureInfo.InvariantCulture, @@UI:passwordTooShort@@, length) };

            public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = @@UI:idPasswordDigit@@ };

            public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = @@UI:idPasswordUpper@@ };

            public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = @@UI:idPasswordLower@@ };

            public override IdentityError PasswordRequiresNonAlphanumeric() =>
                new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = @@UI:idPasswordSymbol@@ };

            public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = @@UI:currentPasswordWrong@@ };

            public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = @@UI:userNameTaken@@ };

            public override IdentityError InvalidUserName(string? userName) =>
                new() { Code = nameof(InvalidUserName), Description = string.Format(CultureInfo.InvariantCulture, @@UI:idInvalidUserName@@, userName) };

            public override IdentityError DuplicateRoleName(string role) =>
                new() { Code = nameof(DuplicateRoleName), Description = string.Format(CultureInfo.InvariantCulture, @@UI:idRoleTaken@@, role) };

            public override IdentityError InvalidRoleName(string? role) =>
                new() { Code = nameof(InvalidRoleName), Description = string.Format(CultureInfo.InvariantCulture, @@UI:idInvalidRoleName@@, role) };

            public override IdentityError InvalidEmail(string? email) =>
                new() { Code = nameof(InvalidEmail), Description = string.Format(CultureInfo.InvariantCulture, @@UI:idInvalidEmail@@, email) };

            public override IdentityError ConcurrencyFailure() => new() { Code = nameof(ConcurrencyFailure), Description = @@UI:idConcurrency@@ };

            public override IdentityError DefaultError() => new() { Code = nameof(DefaultError), Description = @@UI:idError@@ };
        }
        """;

    private const string IdentityStoresText = """
        using System.Security.Claims;
        using Dapper;
        using Microsoft.AspNetCore.Identity;
        using Microsoft.Data.Sqlite;

        namespace @@NS@@.Security;

        /// <summary>
        /// The users of ASP.NET Core Identity in the AspNet* tables, with Dapper. The base class keeps the user's properties (password
        /// hash, lockout, security stamp, e-mail); UserManager then calls UpdateAsync, which writes them.
        /// </summary>
        public sealed class DapperUserStore
            : UserStoreBase<IdentityUser, IdentityRole, string, IdentityUserClaim<string>, IdentityUserRole<string>, IdentityUserLogin<string>, IdentityUserToken<string>, IdentityRoleClaim<string>>
        {
            private const string Columns =
                "Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, "
                + "PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount";

            private readonly string _connectionString;

            public DapperUserStore(IdentityDb db, IdentityErrorDescriber describer)
                : base(describer)
            {
                _connectionString = db.ConnectionString;
            }

            /// <summary>Every user, read at once. The site's own pages page through the users with SQL instead.</summary>
            public override IQueryable<IdentityUser> Users
            {
                get
                {
                    using var connection = new SqliteConnection(_connectionString);
                    return connection.Query<IdentityUser>("SELECT " + Columns + " FROM @@T:Users@@").AsList().AsQueryable();
                }
            }

            private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
            {
                var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                return connection;
            }

            public override async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                user.ConcurrencyStamp ??= Guid.NewGuid().ToString();
                await connection.ExecuteAsync(
                    "INSERT INTO @@T:Users@@ (" + Columns + ") VALUES (@Id, @UserName, @NormalizedUserName, @Email, @NormalizedEmail, @EmailConfirmed, @PasswordHash, "
                    + "@SecurityStamp, @ConcurrencyStamp, @PhoneNumber, @PhoneNumberConfirmed, @TwoFactorEnabled, @LockoutEnd, @LockoutEnabled, @AccessFailedCount)",
                    user);
                return IdentityResult.Success;
            }

            /// <summary>Writes every property. A row changed by someone else since it was read (another concurrency stamp) is not overwritten.</summary>
            public override async Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                var read = user.ConcurrencyStamp;
                user.ConcurrencyStamp = Guid.NewGuid().ToString();
                var args = new DynamicParameters(user);
                args.Add("ReadStamp", read);
                var changed = await connection.ExecuteAsync(
                    "UPDATE @@T:Users@@ SET UserName = @UserName, NormalizedUserName = @NormalizedUserName, Email = @Email, NormalizedEmail = @NormalizedEmail, "
                    + "EmailConfirmed = @EmailConfirmed, PasswordHash = @PasswordHash, SecurityStamp = @SecurityStamp, ConcurrencyStamp = @ConcurrencyStamp, "
                    + "PhoneNumber = @PhoneNumber, PhoneNumberConfirmed = @PhoneNumberConfirmed, TwoFactorEnabled = @TwoFactorEnabled, LockoutEnd = @LockoutEnd, "
                    + "LockoutEnabled = @LockoutEnabled, AccessFailedCount = @AccessFailedCount "
                    + "WHERE Id = @Id AND (ConcurrencyStamp = @ReadStamp OR ConcurrencyStamp IS NULL)",
                    args);
                if (changed == 0)
                {
                    user.ConcurrencyStamp = read;
                    return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
                }

                return IdentityResult.Success;
            }

            /// <summary>Deletes the user with their roles, claims, logins and tokens, in one transaction.</summary>
            public override async Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                foreach (var table in new[] { "@@T:UserRoles@@", "@@T:UserClaims@@", "@@T:UserLogins@@", "@@T:UserTokens@@" })
                {
                    await connection.ExecuteAsync("DELETE FROM " + table + " WHERE UserId = @Id", new { user.Id }, transaction);
                }

                await connection.ExecuteAsync("DELETE FROM @@T:Users@@ WHERE Id = @Id", new { user.Id }, transaction);
                await transaction.CommitAsync(cancellationToken);
                return IdentityResult.Success;
            }

            public override async Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                return await FindUserAsync(userId, cancellationToken);
            }

            public override async Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUser>("SELECT " + Columns + " FROM @@T:Users@@ WHERE NormalizedUserName = @name", new { name = normalizedUserName });
            }

            public override async Task<IdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUser>("SELECT " + Columns + " FROM @@T:Users@@ WHERE NormalizedEmail = @email", new { email = normalizedEmail });
            }

            protected override async Task<IdentityUser?> FindUserAsync(string userId, CancellationToken cancellationToken)
            {
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUser>("SELECT " + Columns + " FROM @@T:Users@@ WHERE Id = @userId", new { userId });
            }

            // ---- roles ------------------------------------------------------------------------------

            protected override async Task<IdentityRole?> FindRoleAsync(string normalizedRoleName, CancellationToken cancellationToken)
            {
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityRole>(
                    "SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM @@T:Roles@@ WHERE NormalizedName = @name", new { name = normalizedRoleName });
            }

            protected override async Task<IdentityUserRole<string>?> FindUserRoleAsync(string userId, string roleId, CancellationToken cancellationToken)
            {
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUserRole<string>>(
                    "SELECT UserId, RoleId FROM @@T:UserRoles@@ WHERE UserId = @userId AND RoleId = @roleId", new { userId, roleId });
            }

            public override async Task AddToRoleAsync(IdentityUser user, string normalizedRoleName, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                var role = await FindRoleAsync(normalizedRoleName, cancellationToken)
                    ?? throw new InvalidOperationException($"Role {normalizedRoleName} does not exist.");
                if (await FindUserRoleAsync(user.Id, role.Id, cancellationToken) is not null)
                {
                    return;
                }

                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync("INSERT INTO @@T:UserRoles@@ (UserId, RoleId) VALUES (@userId, @roleId)", new { userId = user.Id, roleId = role.Id });
            }

            public override async Task RemoveFromRoleAsync(IdentityUser user, string normalizedRoleName, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                if (await FindRoleAsync(normalizedRoleName, cancellationToken) is not { } role)
                {
                    return;
                }

                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync("DELETE FROM @@T:UserRoles@@ WHERE UserId = @userId AND RoleId = @roleId", new { userId = user.Id, roleId = role.Id });
            }

            public override async Task<IList<string>> GetRolesAsync(IdentityUser user, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return (await connection.QueryAsync<string>(
                    "SELECT r.Name FROM @@T:Roles@@ r INNER JOIN @@T:UserRoles@@ ur ON ur.RoleId = r.Id WHERE ur.UserId = @userId ORDER BY r.Name", new { userId = user.Id })).AsList();
            }

            public override async Task<bool> IsInRoleAsync(IdentityUser user, string normalizedRoleName, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                return await FindRoleAsync(normalizedRoleName, cancellationToken) is { } role && await FindUserRoleAsync(user.Id, role.Id, cancellationToken) is not null;
            }

            public override async Task<IList<IdentityUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return (await connection.QueryAsync<IdentityUser>(
                    "SELECT u.* FROM @@T:Users@@ u INNER JOIN @@T:UserRoles@@ ur ON ur.UserId = u.Id INNER JOIN @@T:Roles@@ r ON r.Id = ur.RoleId WHERE r.NormalizedName = @name",
                    new { name = normalizedRoleName })).AsList();
            }

            // ---- claims -----------------------------------------------------------------------------

            public override async Task<IList<Claim>> GetClaimsAsync(IdentityUser user, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                var rows = await connection.QueryAsync<(string? Type, string? Value)>(
                    "SELECT ClaimType, ClaimValue FROM @@T:UserClaims@@ WHERE UserId = @userId ORDER BY Id", new { userId = user.Id });
                return rows.Select(r => new Claim(r.Type ?? string.Empty, r.Value ?? string.Empty)).ToList();
            }

            public override async Task AddClaimsAsync(IdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                foreach (var claim in claims)
                {
                    await connection.ExecuteAsync("INSERT INTO @@T:UserClaims@@ (UserId, ClaimType, ClaimValue) VALUES (@userId, @type, @value)",
                        new { userId = user.Id, type = claim.Type, value = claim.Value });
                }
            }

            public override async Task ReplaceClaimAsync(IdentityUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync(
                    "UPDATE @@T:UserClaims@@ SET ClaimType = @newType, ClaimValue = @newValue WHERE UserId = @userId AND ClaimType = @type AND ClaimValue = @value",
                    new { userId = user.Id, type = claim.Type, value = claim.Value, newType = newClaim.Type, newValue = newClaim.Value });
            }

            public override async Task RemoveClaimsAsync(IdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                foreach (var claim in claims)
                {
                    await connection.ExecuteAsync("DELETE FROM @@T:UserClaims@@ WHERE UserId = @userId AND ClaimType = @type AND ClaimValue = @value",
                        new { userId = user.Id, type = claim.Type, value = claim.Value });
                }
            }

            public override async Task<IList<IdentityUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return (await connection.QueryAsync<IdentityUser>(
                    "SELECT u.* FROM @@T:Users@@ u WHERE u.Id IN (SELECT c.UserId FROM @@T:UserClaims@@ c WHERE c.ClaimType = @type AND c.ClaimValue = @value)",
                    new { type = claim.Type, value = claim.Value })).AsList();
            }

            // ---- external logins and tokens -----------------------------------------------------------

            protected override async Task<IdentityUserLogin<string>?> FindUserLoginAsync(string userId, string loginProvider, string providerKey, CancellationToken cancellationToken)
            {
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUserLogin<string>>(
                    "SELECT LoginProvider, ProviderKey, ProviderDisplayName, UserId FROM @@T:UserLogins@@ WHERE UserId = @userId AND LoginProvider = @loginProvider AND ProviderKey = @providerKey",
                    new { userId, loginProvider, providerKey });
            }

            protected override async Task<IdentityUserLogin<string>?> FindUserLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
            {
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUserLogin<string>>(
                    "SELECT LoginProvider, ProviderKey, ProviderDisplayName, UserId FROM @@T:UserLogins@@ WHERE LoginProvider = @loginProvider AND ProviderKey = @providerKey",
                    new { loginProvider, providerKey });
            }

            public override async Task AddLoginAsync(IdentityUser user, UserLoginInfo login, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync(
                    "INSERT INTO @@T:UserLogins@@ (LoginProvider, ProviderKey, ProviderDisplayName, UserId) VALUES (@LoginProvider, @ProviderKey, @ProviderDisplayName, @userId)",
                    new { login.LoginProvider, login.ProviderKey, login.ProviderDisplayName, userId = user.Id });
            }

            public override async Task RemoveLoginAsync(IdentityUser user, string loginProvider, string providerKey, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync("DELETE FROM @@T:UserLogins@@ WHERE UserId = @userId AND LoginProvider = @loginProvider AND ProviderKey = @providerKey",
                    new { userId = user.Id, loginProvider, providerKey });
            }

            public override async Task<IList<UserLoginInfo>> GetLoginsAsync(IdentityUser user, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                var rows = await connection.QueryAsync<IdentityUserLogin<string>>(
                    "SELECT LoginProvider, ProviderKey, ProviderDisplayName, UserId FROM @@T:UserLogins@@ WHERE UserId = @userId", new { userId = user.Id });
                return rows.Select(r => new UserLoginInfo(r.LoginProvider, r.ProviderKey, r.ProviderDisplayName)).ToList();
            }

            protected override async Task<IdentityUserToken<string>?> FindTokenAsync(IdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
            {
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityUserToken<string>>(
                    "SELECT UserId, LoginProvider, Name, Value FROM @@T:UserTokens@@ WHERE UserId = @userId AND LoginProvider = @loginProvider AND Name = @name",
                    new { userId = user.Id, loginProvider, name });
            }

            /// <summary>The base class changes the value of a token it found in memory only; this store writes it.</summary>
            public override async Task SetTokenAsync(IdentityUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                await RemoveUserTokenAsync(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = loginProvider, Name = name });
                await AddUserTokenAsync(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = loginProvider, Name = name, Value = value });
            }

            protected override async Task AddUserTokenAsync(IdentityUserToken<string> token)
            {
                await using var connection = await OpenAsync(CancellationToken.None);
                await connection.ExecuteAsync("INSERT INTO @@T:UserTokens@@ (UserId, LoginProvider, Name, Value) VALUES (@UserId, @LoginProvider, @Name, @Value)", token);
            }

            protected override async Task RemoveUserTokenAsync(IdentityUserToken<string> token)
            {
                await using var connection = await OpenAsync(CancellationToken.None);
                await connection.ExecuteAsync("DELETE FROM @@T:UserTokens@@ WHERE UserId = @UserId AND LoginProvider = @LoginProvider AND Name = @Name", token);
            }
        }

        /// <summary>The roles of ASP.NET Core Identity in the AspNet* tables, with Dapper.</summary>
        public sealed class DapperRoleStore : RoleStoreBase<IdentityRole, string, IdentityUserRole<string>, IdentityRoleClaim<string>>
        {
            private const string Columns = "Id, Name, NormalizedName, ConcurrencyStamp";

            private readonly string _connectionString;

            public DapperRoleStore(IdentityDb db, IdentityErrorDescriber describer)
                : base(describer)
            {
                _connectionString = db.ConnectionString;
            }

            public override IQueryable<IdentityRole> Roles
            {
                get
                {
                    using var connection = new SqliteConnection(_connectionString);
                    return connection.Query<IdentityRole>("SELECT " + Columns + " FROM @@T:Roles@@ ORDER BY Name").AsList().AsQueryable();
                }
            }

            private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
            {
                var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                return connection;
            }

            public override async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                role.ConcurrencyStamp ??= Guid.NewGuid().ToString();
                await connection.ExecuteAsync("INSERT INTO @@T:Roles@@ (" + Columns + ") VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp)", role);
                return IdentityResult.Success;
            }

            public override async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                var read = role.ConcurrencyStamp;
                role.ConcurrencyStamp = Guid.NewGuid().ToString();
                var changed = await connection.ExecuteAsync(
                    "UPDATE @@T:Roles@@ SET Name = @Name, NormalizedName = @NormalizedName, ConcurrencyStamp = @ConcurrencyStamp "
                    + "WHERE Id = @Id AND (ConcurrencyStamp = @ReadStamp OR ConcurrencyStamp IS NULL)",
                    new { role.Id, role.Name, role.NormalizedName, role.ConcurrencyStamp, ReadStamp = read });
                if (changed == 0)
                {
                    role.ConcurrencyStamp = read;
                    return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
                }

                return IdentityResult.Success;
            }

            /// <summary>Deletes the role with its claims and memberships, in one transaction.</summary>
            public override async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await connection.ExecuteAsync("DELETE FROM @@T:UserRoles@@ WHERE RoleId = @Id", new { role.Id }, transaction);
                await connection.ExecuteAsync("DELETE FROM @@T:RoleClaims@@ WHERE RoleId = @Id", new { role.Id }, transaction);
                await connection.ExecuteAsync("DELETE FROM @@T:Roles@@ WHERE Id = @Id", new { role.Id }, transaction);
                await transaction.CommitAsync(cancellationToken);
                return IdentityResult.Success;
            }

            public override async Task<IdentityRole?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityRole>("SELECT " + Columns + " FROM @@T:Roles@@ WHERE Id = @id", new { id });
            }

            public override async Task<IdentityRole?> FindByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                return await connection.QueryFirstOrDefaultAsync<IdentityRole>("SELECT " + Columns + " FROM @@T:Roles@@ WHERE NormalizedName = @name", new { name = normalizedName });
            }

            public override async Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                var rows = await connection.QueryAsync<(string? Type, string? Value)>(
                    "SELECT ClaimType, ClaimValue FROM @@T:RoleClaims@@ WHERE RoleId = @roleId ORDER BY Id", new { roleId = role.Id });
                return rows.Select(r => new Claim(r.Type ?? string.Empty, r.Value ?? string.Empty)).ToList();
            }

            public override async Task AddClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync("INSERT INTO @@T:RoleClaims@@ (RoleId, ClaimType, ClaimValue) VALUES (@roleId, @type, @value)",
                    new { roleId = role.Id, type = claim.Type, value = claim.Value });
            }

            public override async Task RemoveClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                await using var connection = await OpenAsync(cancellationToken);
                await connection.ExecuteAsync("DELETE FROM @@T:RoleClaims@@ WHERE RoleId = @roleId AND ClaimType = @type AND ClaimValue = @value",
                    new { roleId = role.Id, type = claim.Type, value = claim.Value });
            }
        }
        """;

    private const string IdentityAuthPagesText = """
        using System.Text;
        using Microsoft.AspNetCore.Antiforgery;
        using Microsoft.AspNetCore.Identity;
        @@USING_LOCALIZATION@@
        using @@NS@@.Web;

        namespace @@NS@@.Pages;

        /// <summary>
        /// Login and logout pages, through ASP.NET Core Identity's SignInManager. These are the only pages that do not require a
        /// signed-in user. Unknown users and wrong passwords get the same message.
        /// </summary>
        public static class AuthPages
        {
            private const string LoginRoute = "/login";
            private const string LogoutRoute = "/logout";

            public static IEndpointRouteBuilder MapAuthPages(this IEndpointRouteBuilder app, string connectionString)
            {
                app.MapGet(LoginRoute, (HttpContext context, IAntiforgery antiforgery) => LoginForm(context, antiforgery, null, context.Request.Query["ReturnUrl"].ToString()));
                app.MapPost(LoginRoute, (HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn) => LoginPost(context, antiforgery, signIn));
                app.MapPost(LogoutRoute, (HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn) => LogoutPost(context, antiforgery, signIn));
                return app;
            }

            /// <summary>Sign-out form for the home page. Signing out is a POST protected by the anti-forgery token.</summary>
            public static string LogoutForm(HttpContext context, IAntiforgery antiforgery) =>
                "<form method=\"post\" action=\"" + Html.Attr(LogoutRoute) + "\">"
                + Html.AntiforgeryField(context, antiforgery)
                + "<button type=\"submit\" class=\"btn btn-outline-secondary\">" + Html.Encode(@@UI:logOut@@) + "</button></form>";

            private static IResult LoginForm(HttpContext context, IAntiforgery antiforgery, string? message, string returnUrl)
            {
                var body = new StringBuilder();
                if (message is not null)
                {
                    body.Append("<div class=\"alert alert-danger\">").Append(Html.Encode(message)).Append("</div>");
                }

        @@REGISTERED@@        body.Append("<form method=\"post\" action=\"").Append(Html.Attr(LoginRoute)).Append("\">");
                body.Append(Html.AntiforgeryField(context, antiforgery));
                body.Append("<input type=\"hidden\" name=\"ReturnUrl\" value=\"").Append(Html.Attr(returnUrl)).Append("\">");
                body.Append(Html.Input("username", @@UI:userName@@, "text", string.Empty, true));
                body.Append(Html.Input("password", @@UI:password@@, "password", string.Empty, true));
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:logIn@@)).Append("</button></form>");
        @@REGISTER_LINK@@        return Html.Page(@@UI:logIn@@, body.ToString());
            }

            private static async Task<IResult> LoginPost(HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var userName = form["username"].ToString().Trim();
                var password = form["password"].ToString();
                var returnUrl = form["ReturnUrl"].ToString();
                if (userName.Length == 0 || password.Length == 0)
                {
                    return LoginForm(context, antiforgery, @@UI:invalidLogin@@, returnUrl);
                }

                // A locked-out user is refused before the password is checked, so even the right password does not get in.
                var result = await signIn.PasswordSignInAsync(userName, password, isPersistent: false, lockoutOnFailure: @@TRACK@@);
                if (result.IsLockedOut)
                {
                    return LoginForm(context, antiforgery, @@UI:tooManyAttempts@@, returnUrl);
                }

                if (result.IsNotAllowed)
                {
                    return LoginForm(context, antiforgery, @@UI:idNotAllowed@@, returnUrl);
                }

                if (!result.Succeeded)
                {
                    return LoginForm(context, antiforgery, @@UI:invalidLogin@@, returnUrl);
                }

                return Results.Redirect(IsLocalPath(returnUrl) ? returnUrl : "/");
            }

            private static async Task<IResult> LogoutPost(HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                await signIn.SignOutAsync();
                return Results.Redirect(LoginRoute);
            }

            // Only a path on this site may be a login redirect target. "//" and "/\" start another host in browsers,
            // a scheme or a backslash is not a plain path, and control characters are stripped by browsers, so they are refused too.
            private static bool IsLocalPath(string url) =>
                url.StartsWith("/", StringComparison.Ordinal)
                && !url.StartsWith("//", StringComparison.Ordinal)
                && !url.StartsWith("/\\", StringComparison.Ordinal)
                && url.IndexOf(':') < 0
                && url.IndexOf('\\') < 0
                && !url.Any(char.IsControl);
        }
        """;

    private const string IdentityAccountHeadText = """
        using System.Text;
        using Microsoft.AspNetCore.Antiforgery;
        using Microsoft.AspNetCore.Identity;
        using @@NS@@.Security;
        @@USING_LOCALIZATION@@
        using @@NS@@.Web;

        namespace @@NS@@.Pages;

        /// <summary>Sign-up and password change, through ASP.NET Core Identity's UserManager and its password rules.</summary>
        public static class UserAccountPages
        {

        """;

    private const string IdentityRegisterText = """
            public static IEndpointRouteBuilder MapRegistrationPages(this IEndpointRouteBuilder app, string connectionString)
            {
                app.MapGet("/register", (HttpContext context, IAntiforgery antiforgery) => RegisterForm(context, antiforgery, new List<string>(), string.Empty, string.Empty));
                app.MapPost("/register", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users) => RegisterPost(context, antiforgery, users));
                return app;
            }

            private static IResult RegisterForm(HttpContext context, IAntiforgery antiforgery, List<string> errors, string userName, string email)
            {
                var body = new StringBuilder();
                AppendErrors(body, errors);
                body.Append("<form method=\"post\" action=\"/register\">");
                body.Append(Html.AntiforgeryField(context, antiforgery));
                body.Append(Html.Input("username", @@UI:userName@@, "text", userName, true));
                body.Append(Html.Input("email", @@UI:idEmail@@, "email", email, false));
                body.Append(Html.Input("password", @@UI:password@@, "password", string.Empty, true));
                body.Append(Html.Input("confirm", @@UI:confirmPassword@@, "password", string.Empty, true));
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:register@@)).Append("</button></form>");
                body.Append(Html.Link("/login", @@UI:backToLogIn@@, "btn btn-link"));
                return Html.Page(@@UI:register@@, body.ToString());
            }

            private static async Task<IResult> RegisterPost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var userName = form["username"].ToString().Trim();
                var email = form["email"].ToString().Trim();
                var password = form["password"].ToString();
                var errors = new List<string>();
                if (userName.Length == 0)
                {
                    errors.Add(@@UI:enterUserName@@);
                }

                CheckPassword(password, form["confirm"].ToString(), errors);
                if (errors.Count > 0)
                {
                    return RegisterForm(context, antiforgery, errors, userName, email);
                }

                var user = new IdentityUser(userName) { Email = email.Length == 0 ? null : email };
                var created = await users.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    return RegisterForm(context, antiforgery, created.Errors.Select(e => e.Description).ToList(), userName, email);
                }

        @@LEVEL@@        return Results.Redirect("/login?registered=1");
            }


        """;

    private const string IdentityChangePasswordText = """
            public static IEndpointRouteBuilder MapChangePasswordPages(this IEndpointRouteBuilder app, string connectionString)
            {
                app.MapGet("/changepassword", (HttpContext context, IAntiforgery antiforgery) => ChangePasswordForm(context, antiforgery, new List<string>(), null));
                app.MapPost("/changepassword", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
                    ChangePasswordPost(context, antiforgery, users, signIn));
                return app;
            }

            private static IResult ChangePasswordForm(HttpContext context, IAntiforgery antiforgery, List<string> errors, string? success)
            {
                var body = new StringBuilder();
                if (success is not null)
                {
                    body.Append("<div class=\"alert alert-success\">").Append(Html.Encode(success)).Append("</div>");
                }

                AppendErrors(body, errors);
                body.Append("<form method=\"post\" action=\"/changepassword\">");
                body.Append(Html.AntiforgeryField(context, antiforgery));
                body.Append(Html.Input("current", @@UI:currentPassword@@, "password", string.Empty, true));
                body.Append(Html.Input("password", @@UI:newPassword@@, "password", string.Empty, true));
                body.Append(Html.Input("confirm", @@UI:confirmNewPassword@@, "password", string.Empty, true));
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:changePassword@@)).Append("</button></form>");
                return Html.Page(@@UI:changePassword@@, body.ToString());
            }

            private static async Task<IResult> ChangePasswordPost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var current = form["current"].ToString();
                var password = form["password"].ToString();
                var errors = new List<string>();
                if (current.Length == 0)
                {
                    errors.Add(@@UI:enterCurrentPassword@@);
                }

                CheckPassword(password, form["confirm"].ToString(), errors);
                if (errors.Count > 0)
                {
                    return ChangePasswordForm(context, antiforgery, errors, null);
                }

                if (await users.GetUserAsync(context.User) is not { } user)
                {
                    return Results.Redirect("/login");
                }

                var changed = await users.ChangePasswordAsync(user, current, password);
                if (!changed.Succeeded)
                {
                    return ChangePasswordForm(context, antiforgery, changed.Errors.Select(e => e.Description).ToList(), null);
                }

                // The password change renews the security stamp; signing in again keeps this session valid.
                await signIn.RefreshSignInAsync(user);
                return ChangePasswordForm(context, antiforgery, new List<string>(), @@UI:passwordChanged@@);
            }


        """;

    private const string IdentityAccountTailText = """
            private static void AppendErrors(StringBuilder body, List<string> errors)
            {
                if (errors.Count == 0)
                {
                    return;
                }

                body.Append("<div class=\"alert alert-danger\"><ul class=\"mb-0\">");
                foreach (var error in errors)
                {
                    body.Append("<li>").Append(Html.Encode(error)).Append("</li>");
                }

                body.Append("</ul></div>");
            }

            /// <summary>The checks before UserManager applies its password rules.</summary>
            private static void CheckPassword(string password, string confirm, List<string> errors)
            {
                if (password.Length == 0)
                {
                    errors.Add(@@UI:enterPassword@@);
                }

                if (password != confirm)
                {
                    errors.Add(@@UI:passwordsDiffer@@);
                }
            }
        }

        """;

    private const string IdentityAdminPagesText = """
        using System.Globalization;
        using System.Text;
        using Dapper;
        using Microsoft.AspNetCore.Antiforgery;
        using Microsoft.AspNetCore.Identity;
        using Microsoft.Data.Sqlite;
        using @@NS@@.Security;
        @@USING_LOCALIZATION@@
        using @@NS@@.Web;

        namespace @@NS@@.Pages;

        /// <summary>
        /// User and role management for administrators (users with <see cref="SiteIdentity.AdminLevel"/>): list and search users,
        /// add them, change their e-mail, level and roles, lock and unlock them, set a password and delete them; add, rename and
        /// delete roles and set their level. Every change goes through UserManager or RoleManager and is a POST with an anti-forgery token.
        /// </summary>
        public static class UserAdminPages
        {
            private const string UsersRoute = "/users";
            private const string RolesRoute = "/roles";
            private const int PageSize = 20;
            private const string CountSql = @@COUNT_SQL@@;
            private const string PageSql = @@PAGE_SQL@@;

            /// <summary>Links to the management pages for the home page; empty for users who are not administrators.</summary>
            public static string AdminLinks(HttpContext context) =>
                Permissions.Allowed(context.User, SiteIdentity.AdminLevel)
                    ? "<p class=\"mt-3\">" + Html.Link(UsersRoute, @@UI:idUsers@@) + " " + Html.Link(RolesRoute, @@UI:idRoles@@) + "</p>"
                    : string.Empty;

            public static IEndpointRouteBuilder MapUserAdminPages(this IEndpointRouteBuilder app, string connectionString)
            {
                var admin = app.MapGroup(string.Empty).AddEndpointFilter(async (context, next) =>
                {
                    if (Permissions.Allowed(context.HttpContext.User, SiteIdentity.AdminLevel))
                    {
                        return await next(context);
                    }

                    return Results.Text(@@UI:idAdminOnly@@, "text/plain; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
                });

                admin.MapGet(UsersRoute, (HttpContext context, UserManager<IdentityUser> users) => UserList(context, users, connectionString));
                admin.MapGet(UsersRoute + "/add", (HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles) =>
                    UserAddForm(context, antiforgery, AllRoles(roles), new List<string>(), string.Empty, string.Empty, "0", false, new List<string>()));
                admin.MapPost(UsersRoute + "/add", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserAddPost(context, antiforgery, users, roles));
                admin.MapGet(UsersRoute + "/edit", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserEditForm(context, antiforgery, users, roles, context.Request.Query["id"].ToString(), new List<string>()));
                admin.MapPost(UsersRoute + "/edit", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserEditPost(context, antiforgery, users, roles));
                admin.MapPost(UsersRoute + "/lock", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserLockPost(context, antiforgery, users, roles, true));
                admin.MapPost(UsersRoute + "/unlock", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserLockPost(context, antiforgery, users, roles, false));
                admin.MapPost(UsersRoute + "/password", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserPasswordPost(context, antiforgery, users, roles));
                admin.MapPost(UsersRoute + "/delete", (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) =>
                    UserDeletePost(context, antiforgery, users, roles));
                admin.MapGet(RolesRoute, (HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles) =>
                    RoleList(context, antiforgery, roles, new List<string>(), string.Empty, "0"));
                admin.MapPost(RolesRoute + "/add", (HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles) => RoleAddPost(context, antiforgery, roles));
                admin.MapGet(RolesRoute + "/edit", (HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles) =>
                    RoleEditForm(context, antiforgery, roles, context.Request.Query["id"].ToString(), new List<string>()));
                admin.MapPost(RolesRoute + "/edit", (HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles) => RoleEditPost(context, antiforgery, roles));
                admin.MapPost(RolesRoute + "/delete", (HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles) => RoleDeletePost(context, antiforgery, roles));
                return app;
            }

            // ---- users ----------------------------------------------------------------------------------

            private static async Task<IResult> UserList(HttpContext context, UserManager<IdentityUser> users, string connectionString)
            {
                var term = context.Request.Query["q"].ToString().Trim();
                var args = new DynamicParameters();
                args.Add("@q", "%" + term.ToUpperInvariant() + "%");
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                var total = await connection.ExecuteScalarAsync<long>(CountSql, args);
                var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
                var page = Math.Clamp(int.TryParse(context.Request.Query["page"], out var p) ? p : 1, 1, pageCount);
                args.Add("@limit", PageSize);
                args.Add("@offset", (page - 1) * PageSize);
                var found = (await connection.QueryAsync<IdentityUser>(PageSql, args)).AsList();

                var body = new StringBuilder();
                AppendDone(body, context);
                body.Append("<form method=\"get\" action=\"").Append(UsersRoute).Append("\" class=\"row g-2 mb-3\"><div class=\"col-auto\">")
                    .Append("<input class=\"form-control\" name=\"q\" value=\"").Append(Html.Attr(term)).Append("\" placeholder=\"").Append(Html.Attr(@@UI:search@@)).Append("\"></div>")
                    .Append("<div class=\"col-auto\"><button class=\"btn btn-outline-primary\" type=\"submit\">").Append(Html.Encode(@@UI:search@@)).Append("</button></div>")
                    .Append("<div class=\"col-auto\">").Append(Html.Link(UsersRoute + "/add", @@UI:idAddUser@@, "btn btn-primary")).Append(' ')
                    .Append(Html.Link(RolesRoute, @@UI:idRoles@@, "btn btn-outline-secondary")).Append("</div></form>");
                if (found.Count == 0)
                {
                    body.Append("<p>").Append(Html.Encode(@@UI:idNoUsers@@)).Append("</p>");
                }
                else
                {
                    body.Append("<table class=\"table table-striped\"><thead><tr><th>").Append(Html.Encode(@@UI:userName@@)).Append("</th><th>")
                        .Append(Html.Encode(@@UI:idEmail@@)).Append("</th><th>").Append(Html.Encode(@@UI:idLevel@@)).Append("</th><th>")
                        .Append(Html.Encode(@@UI:idRoles@@)).Append("</th><th>").Append(Html.Encode(@@UI:idLocked@@)).Append("</th><th></th></tr></thead><tbody>");
                    foreach (var user in found)
                    {
                        var level = await SiteIdentity.OwnLevelAsync(users, user);
                        var roles = await users.GetRolesAsync(user);
                        body.Append("<tr><td>").Append(Html.Encode(user.UserName)).Append("</td><td>").Append(Html.Encode(user.Email))
                            .Append("</td><td>").Append(level.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(Html.Encode(string.Join(", ", roles)))
                            .Append("</td><td>").Append(Html.Encode(IsLocked(user))).Append("</td><td>")
                            .Append(Html.Link(UsersRoute + "/edit?id=" + Uri.EscapeDataString(user.Id), @@UI:edit@@)).Append("</td></tr>");
                    }

                    body.Append("</tbody></table>");
                }

                body.Append(Html.PageLinks(context.Request, page, pageCount, UsersRoute));
                return Html.Page(@@UI:idUsers@@, body.ToString());
            }

            private static IResult UserAddForm(HttpContext context, IAntiforgery antiforgery, List<IdentityRole> allRoles, List<string> errors,
                string userName, string email, string level, bool emailConfirmed, List<string> selectedRoles)
            {
                var body = new StringBuilder();
                AppendErrors(body, errors);
                body.Append("<form method=\"post\" action=\"").Append(UsersRoute).Append("/add\">");
                body.Append(Html.AntiforgeryField(context, antiforgery));
                body.Append(Html.Input("username", @@UI:userName@@, "text", userName, true));
                body.Append(Html.Input("email", @@UI:idEmail@@, "email", email, false));
                body.Append(Html.Checkbox("emailConfirmed", @@UI:idEmailConfirmed@@, emailConfirmed));
                body.Append(Html.Input("password", @@UI:password@@, "password", string.Empty, true));
                body.Append(Html.Input("confirm", @@UI:confirmPassword@@, "password", string.Empty, true));
                body.Append(Html.Input("level", @@UI:idLevel@@, "number", level, true));
                AppendRoleBoxes(body, allRoles, selectedRoles);
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:save@@)).Append("</button> ")
                    .Append(Html.Link(UsersRoute, @@UI:cancel@@, "btn btn-link")).Append("</form>");
                return Html.Page(@@UI:idAddUser@@, body.ToString());
            }

            private static async Task<IResult> UserAddPost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var userName = form["username"].ToString().Trim();
                var email = form["email"].ToString().Trim();
                var password = form["password"].ToString();
                var levelText = form["level"].ToString().Trim();
                var emailConfirmed = form["emailConfirmed"].ToString() == "true";
                var allRoles = AllRoles(roles);
                var selected = SelectedRoles(form, allRoles);
                var errors = new List<string>();
                if (userName.Length == 0)
                {
                    errors.Add(@@UI:enterUserName@@);
                }

                if (password.Length == 0)
                {
                    errors.Add(@@UI:enterPassword@@);
                }

                if (password != form["confirm"].ToString())
                {
                    errors.Add(@@UI:passwordsDiffer@@);
                }

                if (!int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                {
                    errors.Add(@@UI:idNotNumber@@);
                }

                if (errors.Count > 0)
                {
                    return UserAddForm(context, antiforgery, allRoles, errors, userName, email, levelText, emailConfirmed, selected);
                }

                var user = new IdentityUser(userName) { Email = email.Length == 0 ? null : email, EmailConfirmed = emailConfirmed };
                var result = await users.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    result = await SiteIdentity.SetLevelAsync(users, user, level);
                }

                if (result.Succeeded && selected.Count > 0)
                {
                    result = await users.AddToRolesAsync(user, selected);
                }

                if (!result.Succeeded)
                {
                    return UserAddForm(context, antiforgery, allRoles, Describe(result), userName, email, levelText, emailConfirmed, selected);
                }

                return Results.Redirect(UsersRoute + "/edit?id=" + Uri.EscapeDataString(user.Id) + "&done=created");
            }

            private static async Task<IResult> UserEditForm(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles,
                string id, List<string> errors)
            {
                if (id.Length == 0 || await users.FindByIdAsync(id) is not { } user)
                {
                    return NotFound();
                }

                var level = await SiteIdentity.OwnLevelAsync(users, user);
                var selected = (await users.GetRolesAsync(user)).ToList();
                var token = Html.AntiforgeryField(context, antiforgery) + Html.Hidden("id", user.Id);
                var body = new StringBuilder();
                AppendDone(body, context);
                AppendErrors(body, errors);
                body.Append("<form method=\"post\" action=\"").Append(UsersRoute).Append("/edit\" class=\"mb-4\">").Append(token);
                body.Append(Html.Input("email", @@UI:idEmail@@, "email", user.Email ?? string.Empty, false));
                body.Append(Html.Checkbox("emailConfirmed", @@UI:idEmailConfirmed@@, user.EmailConfirmed));
                body.Append(Html.Input("level", @@UI:idLevel@@, "number", level.ToString(CultureInfo.InvariantCulture), true));
                AppendRoleBoxes(body, AllRoles(roles), selected);
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:save@@)).Append("</button> ")
                    .Append(Html.Link(UsersRoute, @@UI:idUsers@@, "btn btn-link")).Append("</form>");

                body.Append("<h2 class=\"h5\">").Append(Html.Encode(@@UI:idSetPassword@@)).Append("</h2>");
                body.Append("<form method=\"post\" action=\"").Append(UsersRoute).Append("/password\" class=\"mb-4\">").Append(token);
                body.Append(Html.Input("password", @@UI:newPassword@@, "password", string.Empty, true));
                body.Append(Html.Input("confirm", @@UI:confirmNewPassword@@, "password", string.Empty, true));
                body.Append("<button type=\"submit\" class=\"btn btn-outline-primary\">").Append(Html.Encode(@@UI:idSetPassword@@)).Append("</button></form>");

                var locked = IsLocked(user);
                body.Append("<form method=\"post\" action=\"").Append(UsersRoute).Append(locked ? "/unlock" : "/lock").Append("\" class=\"d-inline\">").Append(token)
                    .Append("<button type=\"submit\" class=\"btn btn-outline-warning\">").Append(Html.Encode(locked ? @@UI:idUnlock@@ : @@UI:idLock@@)).Append("</button></form> ");
                body.Append("<form method=\"post\" action=\"").Append(UsersRoute).Append("/delete\" class=\"d-inline\" onsubmit=\"return confirm(")
                    .Append(Html.Attr(System.Text.Json.JsonSerializer.Serialize(@@UI:confirmDelete@@))).Append(")\">").Append(token)
                    .Append("<button type=\"submit\" class=\"btn btn-outline-danger\">").Append(Html.Encode(@@UI:delete@@)).Append("</button></form>");
                return Html.Page(string.Format(CultureInfo.InvariantCulture, @@UI:idEditUser@@, user.UserName), body.ToString());
            }

            private static async Task<IResult> UserEditPost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var id = form["id"].ToString();
                if (await users.FindByIdAsync(id) is not { } user)
                {
                    return NotFound();
                }

                if (!int.TryParse(form["level"].ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, new List<string> { @@UI:idNotNumber@@ });
                }

                // An administrator cannot take away their own administration: someone must be left to manage the users.
                if (IsSelf(context, users, user) && level < SiteIdentity.AdminLevel && await SiteIdentity.OwnLevelAsync(users, user) >= SiteIdentity.AdminLevel)
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, new List<string> { @@UI:idKeepAdmin@@ });
                }

                var email = form["email"].ToString().Trim();
                var result = await users.SetEmailAsync(user, email.Length == 0 ? null : email);
                if (result.Succeeded)
                {
                    user.EmailConfirmed = form["emailConfirmed"].ToString() == "true";
                    result = await users.UpdateAsync(user);
                }

                if (result.Succeeded)
                {
                    result = await SiteIdentity.SetLevelAsync(users, user, level);
                }

                if (result.Succeeded)
                {
                    var selected = SelectedRoles(form, AllRoles(roles));
                    var current = await users.GetRolesAsync(user);
                    var removed = current.Except(selected, StringComparer.OrdinalIgnoreCase).ToList();
                    var added = selected.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
                    if (removed.Count > 0)
                    {
                        result = await users.RemoveFromRolesAsync(user, removed);
                    }

                    if (result.Succeeded && added.Count > 0)
                    {
                        result = await users.AddToRolesAsync(user, added);
                    }
                }

                if (!result.Succeeded)
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, Describe(result));
                }

                return Results.Redirect(UsersRoute + "/edit?id=" + Uri.EscapeDataString(user.Id) + "&done=saved");
            }

            /// <summary>Locks the user out until it is unlocked (100 years), or unlocks them. Their other sessions end at the next check of the security stamp.</summary>
            private static async Task<IResult> UserLockPost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles, bool lockOut)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var id = form["id"].ToString();
                if (await users.FindByIdAsync(id) is not { } user)
                {
                    return NotFound();
                }

                if (lockOut && IsSelf(context, users, user))
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, new List<string> { @@UI:idNotSelf@@ });
                }

                IdentityResult result;
                if (lockOut)
                {
                    result = await users.SetLockoutEnabledAsync(user, true);
                    if (result.Succeeded)
                    {
                        result = await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                    }

                    if (result.Succeeded)
                    {
                        result = await users.UpdateSecurityStampAsync(user);
                    }
                }
                else
                {
                    result = await users.SetLockoutEndDateAsync(user, null);
                    if (result.Succeeded)
                    {
                        result = await users.ResetAccessFailedCountAsync(user);
                    }
                }

                if (!result.Succeeded)
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, Describe(result));
                }

                return Results.Redirect(UsersRoute + "/edit?id=" + Uri.EscapeDataString(user.Id) + "&done=saved");
            }

            /// <summary>Sets a new password, checked by the site's password rules. It renews the security stamp, which ends the user's other sessions.</summary>
            private static async Task<IResult> UserPasswordPost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var id = form["id"].ToString();
                if (await users.FindByIdAsync(id) is not { } user)
                {
                    return NotFound();
                }

                var password = form["password"].ToString();
                var errors = new List<string>();
                if (password.Length == 0)
                {
                    errors.Add(@@UI:enterPassword@@);
                }
                else if (password != form["confirm"].ToString())
                {
                    errors.Add(@@UI:passwordsDiffer@@);
                }
                else
                {
                    foreach (var validator in users.PasswordValidators)
                    {
                        errors.AddRange(Describe(await validator.ValidateAsync(users, user, password)));
                    }
                }

                if (errors.Count > 0)
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, errors);
                }

                user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
                var result = await users.UpdateSecurityStampAsync(user);
                if (!result.Succeeded)
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, Describe(result));
                }

                return Results.Redirect(UsersRoute + "/edit?id=" + Uri.EscapeDataString(user.Id) + "&done=password");
            }

            private static async Task<IResult> UserDeletePost(HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var id = form["id"].ToString();
                if (await users.FindByIdAsync(id) is not { } user)
                {
                    return NotFound();
                }

                if (IsSelf(context, users, user))
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, new List<string> { @@UI:idNotSelf@@ });
                }

                var result = await users.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    return await UserEditForm(context, antiforgery, users, roles, id, Describe(result));
                }

                return Results.Redirect(UsersRoute + "?done=deleted");
            }

            // ---- roles ----------------------------------------------------------------------------------

            private static async Task<IResult> RoleList(HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles, List<string> errors, string name, string level)
            {
                var body = new StringBuilder();
                AppendDone(body, context);
                AppendErrors(body, errors);
                var all = AllRoles(roles);
                if (all.Count == 0)
                {
                    body.Append("<p>").Append(Html.Encode(@@UI:idNoRoles@@)).Append("</p>");
                }
                else
                {
                    body.Append("<table class=\"table table-striped\"><thead><tr><th>").Append(Html.Encode(@@UI:idRoleName@@)).Append("</th><th>")
                        .Append(Html.Encode(@@UI:idLevel@@)).Append("</th><th></th></tr></thead><tbody>");
                    foreach (var role in all)
                    {
                        body.Append("<tr><td>").Append(Html.Encode(role.Name)).Append("</td><td>")
                            .Append((await SiteIdentity.RoleLevelAsync(roles, role)).ToString(CultureInfo.InvariantCulture)).Append("</td><td>")
                            .Append(Html.Link(RolesRoute + "/edit?id=" + Uri.EscapeDataString(role.Id), @@UI:edit@@)).Append("</td></tr>");
                    }

                    body.Append("</tbody></table>");
                }

                body.Append("<h2 class=\"h5\">").Append(Html.Encode(@@UI:idAddRole@@)).Append("</h2>");
                body.Append("<form method=\"post\" action=\"").Append(RolesRoute).Append("/add\">").Append(Html.AntiforgeryField(context, antiforgery));
                body.Append(Html.Input("name", @@UI:idRoleName@@, "text", name, true));
                body.Append(Html.Input("level", @@UI:idLevel@@, "number", level, true));
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:idAddRole@@)).Append("</button> ")
                    .Append(Html.Link(UsersRoute, @@UI:idUsers@@, "btn btn-link")).Append("</form>");
                return Html.Page(@@UI:idRoles@@, body.ToString());
            }

            private static async Task<IResult> RoleAddPost(HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var name = form["name"].ToString().Trim();
                var levelText = form["level"].ToString().Trim();
                var errors = new List<string>();
                if (name.Length == 0)
                {
                    errors.Add(string.Format(CultureInfo.InvariantCulture, @@UI:required@@, @@UI:idRoleName@@));
                }

                if (!int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                {
                    errors.Add(@@UI:idNotNumber@@);
                }

                if (errors.Count > 0)
                {
                    return await RoleList(context, antiforgery, roles, errors, name, levelText);
                }

                var role = new IdentityRole(name);
                var result = await roles.CreateAsync(role);
                if (result.Succeeded)
                {
                    result = await SiteIdentity.SetRoleLevelAsync(roles, role, level);
                }

                if (!result.Succeeded)
                {
                    return await RoleList(context, antiforgery, roles, Describe(result), name, levelText);
                }

                return Results.Redirect(RolesRoute + "?done=created");
            }

            private static async Task<IResult> RoleEditForm(HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles, string id, List<string> errors)
            {
                if (id.Length == 0 || await roles.FindByIdAsync(id) is not { } role)
                {
                    return NotFound();
                }

                var token = Html.AntiforgeryField(context, antiforgery) + Html.Hidden("id", role.Id);
                var body = new StringBuilder();
                AppendDone(body, context);
                AppendErrors(body, errors);
                body.Append("<form method=\"post\" action=\"").Append(RolesRoute).Append("/edit\" class=\"mb-4\">").Append(token);
                body.Append(Html.Input("name", @@UI:idRoleName@@, "text", role.Name ?? string.Empty, true));
                body.Append(Html.Input("level", @@UI:idLevel@@, "number", (await SiteIdentity.RoleLevelAsync(roles, role)).ToString(CultureInfo.InvariantCulture), true));
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:save@@)).Append("</button> ")
                    .Append(Html.Link(RolesRoute, @@UI:idRoles@@, "btn btn-link")).Append("</form>");
                body.Append("<form method=\"post\" action=\"").Append(RolesRoute).Append("/delete\" onsubmit=\"return confirm(")
                    .Append(Html.Attr(System.Text.Json.JsonSerializer.Serialize(@@UI:confirmDelete@@))).Append(")\">").Append(token)
                    .Append("<button type=\"submit\" class=\"btn btn-outline-danger\">").Append(Html.Encode(@@UI:delete@@)).Append("</button></form>");
                return Html.Page(string.Format(CultureInfo.InvariantCulture, @@UI:idEditRole@@, role.Name), body.ToString());
            }

            private static async Task<IResult> RoleEditPost(HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var id = form["id"].ToString();
                if (await roles.FindByIdAsync(id) is not { } role)
                {
                    return NotFound();
                }

                var name = form["name"].ToString().Trim();
                if (name.Length == 0)
                {
                    return await RoleEditForm(context, antiforgery, roles, id, new List<string> { string.Format(CultureInfo.InvariantCulture, @@UI:required@@, @@UI:idRoleName@@) });
                }

                if (!int.TryParse(form["level"].ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                {
                    return await RoleEditForm(context, antiforgery, roles, id, new List<string> { @@UI:idNotNumber@@ });
                }

                var result = await roles.SetRoleNameAsync(role, name);
                if (result.Succeeded)
                {
                    result = await roles.UpdateAsync(role);
                }

                if (result.Succeeded)
                {
                    result = await SiteIdentity.SetRoleLevelAsync(roles, role, level);
                }

                if (!result.Succeeded)
                {
                    return await RoleEditForm(context, antiforgery, roles, id, Describe(result));
                }

                return Results.Redirect(RolesRoute + "/edit?id=" + Uri.EscapeDataString(role.Id) + "&done=saved");
            }

            private static async Task<IResult> RoleDeletePost(HttpContext context, IAntiforgery antiforgery, RoleManager<IdentityRole> roles)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                if (await roles.FindByIdAsync(form["id"].ToString()) is not { } role)
                {
                    return NotFound();
                }

                var result = await roles.DeleteAsync(role);
                if (!result.Succeeded)
                {
                    return await RoleEditForm(context, antiforgery, roles, role.Id, Describe(result));
                }

                return Results.Redirect(RolesRoute + "?done=deleted");
            }

            // ---- helpers --------------------------------------------------------------------------------

            private static List<IdentityRole> AllRoles(RoleManager<IdentityRole> roles) => roles.Roles.ToList();

            /// <summary>The roles ticked in the form, limited to roles that exist.</summary>
            private static List<string> SelectedRoles(IFormCollection form, List<IdentityRole> allRoles) =>
                form["roles"].Where(r => !string.IsNullOrEmpty(r))
                    .Select(r => allRoles.FirstOrDefault(a => string.Equals(a.Name, r, StringComparison.OrdinalIgnoreCase))?.Name)
                    .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            private static void AppendRoleBoxes(StringBuilder body, List<IdentityRole> allRoles, List<string> selected)
            {
                if (allRoles.Count == 0)
                {
                    return;
                }

                body.Append("<fieldset class=\"mb-3\"><legend class=\"form-label fs-6\">").Append(Html.Encode(@@UI:idRoles@@)).Append("</legend>");
                var index = 0;
                foreach (var role in allRoles)
                {
                    var name = role.Name ?? string.Empty;
                    var boxId = "role" + index.ToString(CultureInfo.InvariantCulture);
                    index++;
                    var isChecked = selected.Contains(name, StringComparer.OrdinalIgnoreCase) ? " checked" : string.Empty;
                    body.Append("<div class=\"form-check\"><input class=\"form-check-input\" type=\"checkbox\" name=\"roles\" id=\"").Append(boxId)
                        .Append("\" value=\"").Append(Html.Attr(name)).Append('"').Append(isChecked).Append("><label class=\"form-check-label\" for=\"").Append(boxId)
                        .Append("\">").Append(Html.Encode(name)).Append("</label></div>");
                }

                body.Append("</fieldset>");
            }

            private static bool IsLocked(IdentityUser user) => user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

            private static bool IsSelf(HttpContext context, UserManager<IdentityUser> users, IdentityUser user) =>
                string.Equals(users.GetUserId(context.User), user.Id, StringComparison.Ordinal);

            private static List<string> Describe(IdentityResult result) => result.Errors.Select(e => e.Description).ToList();

            private static IResult NotFound() =>
                Results.Content(Html.Document(@@UI:idNotFound@@, "<div class=\"alert alert-warning\">" + Html.Encode(@@UI:idNotFound@@) + "</div>"),
                    "text/html; charset=utf-8", System.Text.Encoding.UTF8, StatusCodes.Status404NotFound);

            private static void AppendDone(StringBuilder body, HttpContext context)
            {
                string? message = context.Request.Query["done"].ToString() switch
                {
                    "created" => @@UI:idCreated@@,
                    "saved" => @@UI:idSaved@@,
                    "deleted" => @@UI:idDeleted@@,
                    "password" => @@UI:idPasswordSet@@,
                    _ => null,
                };
                if (message is not null)
                {
                    body.Append("<div class=\"alert alert-success\">").Append(Html.Encode(message)).Append("</div>");
                }
            }

            private static void AppendErrors(StringBuilder body, List<string> errors)
            {
                if (errors.Count == 0)
                {
                    return;
                }

                body.Append("<div class=\"alert alert-danger\"><ul class=\"mb-0\">");
                foreach (var error in errors)
                {
                    body.Append("<li>").Append(Html.Encode(error)).Append("</li>");
                }

                body.Append("</ul></div>");
            }
        }
        """;
}
