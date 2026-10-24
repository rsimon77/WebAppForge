using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Tests;

/// <summary>The ASP.NET Core Identity user store: settings, rules, schema and the generated files.</summary>
public class IdentityTests
{
    /// <summary>A shop with an owned table, a table for level 5 and up, and an AspNetUsers table left over in the database.</summary>
    internal const string Schema = """
        CREATE TABLE sales (id INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL, note TEXT);
        CREATE TABLE secrets (id INTEGER PRIMARY KEY AUTOINCREMENT, body TEXT);
        INSERT INTO secrets (body) VALUES ('the plan');
        """;

    internal static Project Sample(string dbPath)
    {
        MdSupport.Exec(dbPath, Schema);
        var project = ReportSupport.ProjectOn("IdentityShop", dbPath);
        project.Security = new SecuritySettings
        {
            Enabled = true,
            UserStore = UserStore.AspNetIdentity,
            AllowRegistration = true,
            MaxFailedAttempts = 3,
        };
        project.Tables.Single(t => t.Name == "sales").OwnerField = "owner";
        project.Tables.Single(t => t.Name == "secrets").ViewMinLevel = 5;
        return project;
    }

    private static Dictionary<string, string> Generate(Action<Project>? configure = null)
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var project = Sample(Path.Combine(root, "identity.db"));
            configure?.Invoke(project);
            return MdSupport.GenerateText(project, root);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void TheIdentitySettingsSurviveTheProjectFileAndDefaultsAreNotWritten()
    {
        var project = new Project();
        Assert.DoesNotContain("userStore", ProjectFile.Write(project).ToString());
        Assert.DoesNotContain("<identity", ProjectFile.Write(project).ToString());

        project.Security.Enabled = true;
        project.Security.UserStore = UserStore.AspNetIdentity;
        project.Security.Identity = new IdentitySettings
        {
            CreateTablesAtStartup = false, TablePrefix = "Site", MinPasswordLength = 12, RequireDigit = true, RequireUppercase = true,
            RequireLowercase = true, RequireNonAlphanumeric = true, RequireConfirmedEmail = true, AdminUserName = "root",
            AdminPasswordVariable = "ROOT_PW", AdminLevel = 50, RegisteredUserLevel = 2,
        };

        var read = ProjectFile.Read(ProjectFile.Write(project));

        Assert.Equal(UserStore.AspNetIdentity, read.Security.UserStore);
        var identity = read.Security.Identity;
        Assert.False(identity.CreateTablesAtStartup);
        Assert.Equal(("Site", 12, "root", "ROOT_PW", 50, 2), (identity.TablePrefix, identity.MinPasswordLength, identity.AdminUserName, identity.AdminPasswordVariable, identity.AdminLevel, identity.RegisteredUserLevel));
        Assert.True(identity.RequireDigit && identity.RequireUppercase && identity.RequireLowercase && identity.RequireNonAlphanumeric && identity.RequireConfirmedEmail);
        Assert.True(read.Security.UsesIdentity && read.Security.HasUserLevels);
    }

    [Fact]
    public void TheRulesReportBadSettingsOnlyForTheIdentityStore()
    {
        var project = new Project();
        project.Security.Identity = new IdentitySettings { TablePrefix = "1 bad", AdminUserName = " ", AdminPasswordVariable = "NO-DASH", AdminLevel = 0 };
        Assert.Empty(IdentityRules.Problems(project));

        project.Security.Enabled = true;
        project.Security.UserStore = UserStore.AspNetIdentity;
        project.Security.UserTable = "users";
        var problems = IdentityRules.Problems(project);

        Assert.Contains(problems, p => p.Contains("prefix '1 bad'"));
        Assert.Contains(problems, p => p.Contains("administrator the site creates"));
        Assert.Contains(problems, p => p.Contains("'NO-DASH'"));
        Assert.Contains(problems, p => p.Contains("administrator level must be at least 1"));
        Assert.Contains(problems, p => p.Contains("sign up"));
        Assert.Contains(IdentityRules.Notes(project), n => n.Contains("not used"));
        Assert.Throws<InvalidOperationException>(() => IdentityRules.Validate(project));

        project.Security.Identity = new IdentitySettings();
        Assert.Empty(IdentityRules.Problems(project));
    }

    [Fact]
    public void AnIdentitySiteGetsTheStoresAndPagesAndNoUserTableLogin()
    {
        var files = Generate();

        foreach (var path in new[] { "Security/SiteIdentity.cs", "Security/IdentityStores.cs", "Security/Permissions.cs", "Pages/AuthPages.cs", "Pages/UserAccountPages.cs", "Pages/UserAdminPages.cs" })
        {
            Assert.Contains(path, files.Keys);
        }

        Assert.DoesNotContain("Security/Passwords.cs", files.Keys);
        Assert.DoesNotContain("Security/LoginAttempts.cs", files.Keys);
        Assert.Contains("builder.Services.AddSiteIdentity(connectionString);", files["Program.cs"]);
        Assert.Contains("await SiteIdentity.InitializeAsync(app.Services, connectionString);", files["Program.cs"]);
        Assert.Contains("secured.MapUserAdminPages(connectionString);", files["Program.cs"]);
        Assert.DoesNotContain("CookieAuthenticationDefaults", files["Program.cs"]);
        // Levels come from the claims of the user and their roles; the highest counts.
        Assert.Contains("user.FindAll(LevelClaimType)", files["Security/Permissions.cs"]);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \\\"AspNetUsers\\\"", files["Security/SiteIdentity.cs"]);
        Assert.Contains("lockoutOnFailure: true", files["Pages/AuthPages.cs"]);
        Assert.Contains(".RequireLevel(5,", files["Pages/SecretsPages.cs"]);
    }

    [Fact]
    public void IdentityTablesInTheProjectAreNotGeneratedAsPages()
    {
        var files = Generate(p => p.Tables.Add(new TableInfo { Name = "AspNetUsers", Fields = { new FieldInfo { Name = "Id", Type = FieldType.Text, IsPrimaryKey = true } } }));

        Assert.DoesNotContain("Pages/AspNetUsersPages.cs", files.Keys);
        Assert.DoesNotContain("AspNetUsers", files["Program.cs"]);
    }

    [Fact]
    public void WithoutStartupCreationTheSiteHasNoCreateStatements()
    {
        var files = Generate(p => p.Security.Identity.CreateTablesAtStartup = false);

        Assert.DoesNotContain("CREATE TABLE", files["Security/SiteIdentity.cs"]);
        Assert.DoesNotContain("CreateStatements", files["Security/SiteIdentity.cs"]);
    }

    [Fact]
    public async Task TheSchemaCreatesTheMissingTablesOnceAndTheSchemaReaderSeesThem()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "schema.db");
        try
        {
            var project = Sample(dbPath);
            Assert.Equal(7, IdentitySchema.TableNames("AspNet").Count);

            var created = await IdentitySchema.CreateMissingTablesAsync(project, root);
            Assert.Equal(IdentitySchema.TableNames("AspNet"), created);
            Assert.Empty(await IdentitySchema.CreateMissingTablesAsync(project, root));

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                Assert.Equal(7, (await IdentitySchema.ExistingTablesAsync(connection, "sqlite", "AspNet")).Count);
                Assert.Empty(await IdentitySchema.MissingTablesAsync(connection, "sqlite", "AspNet"));
                Assert.Equal(7, (await IdentitySchema.MissingTablesAsync(connection, "sqlite", "Other")).Count);
            }

            SqliteConnection.ClearAllPools();
            var tables = new SqliteSchemaReader().ReadTables($"Data Source={dbPath}");
            var users = tables.Single(t => t.Name == "AspNetUsers");
            Assert.Equal("Id", users.PrimaryKeys.Single().Name);
            Assert.Contains(users.Fields, f => f.Name == "NormalizedUserName");
            Assert.True(IdentitySchema.IsIdentityTable(project.Security, "aspnetuserroles"));
            Assert.False(IdentitySchema.IsIdentityTable(project.Security, "sales"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            MdSupport.TryDelete(root);
        }
    }

    /// <summary>The other branches: a localized site, another table prefix, confirmed e-mail, a level for sign-ups and no lockout.</summary>
    [Fact]
    public async Task ALocalizedSiteWithEveryOptionBuilds()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var project = Sample(Path.Combine(root, "identity.db"));
            project.Localization.Enabled = true;
            project.Localization.Languages.Add("de");
            project.Security.TrackFailedAttempts = false;
            project.Security.AllowChangePassword = false;
            project.Security.Identity = new IdentitySettings
            {
                TablePrefix = "Site", RequireConfirmedEmail = true, RegisteredUserLevel = 1, RequireDigit = true, AdminPasswordVariable = "SHOP_ADMIN",
            };
            var files = MdSupport.GenerateText(project, root);

            Assert.Contains("Strings.T(\"ui.idUsers\", \"Users\")", files["Pages/UserAdminPages.cs"]);
            Assert.Contains("\\\"SiteUsers\\\"", files["Security/IdentityStores.cs"]);
            Assert.Contains("SiteIdentity.SetLevelAsync(users, user, SiteIdentity.RegisteredUserLevel)", files["Pages/UserAccountPages.cs"]);
            Assert.DoesNotContain("MapChangePasswordPages", files["Pages/UserAccountPages.cs"]);
            Assert.Contains("lockoutOnFailure: false", files["Pages/AuthPages.cs"]);
            Assert.Contains("ui.idRegistrationPending", files["Pages/AuthPages.cs"]);
            Assert.Contains(Localization.LocalizationKeys.Enumerate(project), t => t.Key == "ui.idRoles");

            var build = await MdSupport.RunDotnetAsync(Path.Combine(root, "site"), "build --nologo -v q -nodeReuse:false -warnaserror");
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void TheServerStatementsAreGuardedAndUseTheServerTypes()
    {
        var sqlServer = string.Join("\n", IdentitySchema.CreateStatements(DatabaseProviders.SqlServer, "AspNet"));
        Assert.Contains("IF OBJECT_ID(N'AspNetUsers', N'U') IS NULL CREATE TABLE [AspNetUsers]", sqlServer);
        Assert.Contains("[LockoutEnd] datetimeoffset NULL", sqlServer);
        Assert.Contains("CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL", sqlServer);
        Assert.Contains("[Id] int IDENTITY(1,1) NOT NULL", sqlServer);

        var mySql = string.Join("\n", IdentitySchema.CreateStatements(DatabaseProviders.MySql, "AspNet"));
        Assert.Contains("CREATE TABLE IF NOT EXISTS `AspNetUsers`", mySql);
        Assert.Contains("UNIQUE KEY `UserNameIndex` (`NormalizedUserName`)", mySql);
        Assert.Contains("`Id` varchar(255) NOT NULL", mySql);
        Assert.Throws<ArgumentException>(() => IdentitySchema.CreateStatements(DatabaseProviders.MySql, "bad prefix"));
    }
}

/// <summary>The identity sample, running on SQLite with an administrator from an environment variable.</summary>
public sealed class IdentitySite : ReportSiteBase
{
    public const string AdminPassword = "Admin-pass-42";

    public const string Password = "Some-pass-42";

    protected override IReadOnlyDictionary<string, string> SiteEnvironment =>
        new Dictionary<string, string> { [IdentitySettings.DefaultAdminPasswordVariable] = AdminPassword };

    protected override Project? Create() => IdentityTests.Sample(DbPath);

    public HttpClient NewClient() =>
        new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = Client.BaseAddress };

    public static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var page = await client.GetStringAsync(url);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "no anti-forgery token on " + url);
        return match.Groups[1].Value;
    }

    /// <summary>Posts a form of <paramref name="page"/> (the page that carries the token) to <paramref name="url"/>.</summary>
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string url, Dictionary<string, string> fields)
    {
        var token = await TokenAsync(client, page);
        var data = new List<KeyValuePair<string, string>>(fields) { new("__RequestVerificationToken", token) };
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    /// <summary>Logs in; returns the client and the login response (a redirect when it worked, the form with a message when not).</summary>
    public async Task<(HttpClient Client, HttpResponseMessage Response)> LogInAsync(string user, string password)
    {
        var client = NewClient();
        var response = await PostAsync(client, "/login", "/login", new() { ["username"] = user, ["password"] = password });
        return (client, response);
    }

    public async Task<HttpClient> SignedInAsync(string user, string password)
    {
        var (client, response) = await LogInAsync(user, password);
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"login of {user} failed: {await response.Content.ReadAsStringAsync()}\n{SiteOutput}");
        response.Dispose();
        return client;
    }

    /// <summary>The id of a user, from the identity table.</summary>
    public async Task<string> UserId(string user) =>
        Convert.ToString(await Scalar($"SELECT Id FROM AspNetUsers WHERE NormalizedUserName = '{user.ToUpperInvariant()}'")) ?? string.Empty;
}

public class IdentitySiteTests : IClassFixture<IdentitySite>
{
    private readonly IdentitySite _site;

    public IdentitySiteTests(IdentitySite site) => _site = site;

    /// <summary>The administrator adds a user with a level and optional roles.</summary>
    private async Task AddUserAsync(HttpClient admin, string user, int level, params string[] roles)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("username", user), new("password", IdentitySite.Password), new("confirm", IdentitySite.Password), new("level", level.ToString()),
        };
        fields.AddRange(roles.Select(r => new KeyValuePair<string, string>("roles", r)));
        var token = await IdentitySite.TokenAsync(admin, "/users/add");
        fields.Add(new("__RequestVerificationToken", token));
        using var response = await admin.PostAsync("/users/add", new FormUrlEncodedContent(fields));
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StartupCreatedTheTablesAndTheAdministratorManagesUsers()
    {
        Assert.Equal(7L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'AspNet%'")));
        using var admin = await _site.SignedInAsync("admin", IdentitySite.AdminPassword);

        var home = await admin.GetStringAsync("/");
        Assert.Contains("href=\"/users\"", home);
        Assert.Contains("admin", await admin.GetStringAsync("/users"));
        Assert.Equal("100", Convert.ToString(await _site.Scalar(
            "SELECT ClaimValue FROM AspNetUserClaims WHERE ClaimType = 'user_level' AND UserId = (SELECT Id FROM AspNetUsers WHERE UserName = 'admin')")));
    }

    [Fact]
    public async Task LevelsComeFromTheUserAndTheirRoles()
    {
        using var admin = await _site.SignedInAsync("admin", IdentitySite.AdminPassword);
        using (var role = await IdentitySite.PostAsync(admin, "/roles", "/roles/add", new() { ["name"] = "Managers", ["level"] = "5" }))
        {
            Assert.Equal(HttpStatusCode.Redirect, role.StatusCode);
        }

        await AddUserAsync(admin, "carol", 1, "Managers");
        await AddUserAsync(admin, "dave", 1);

        using var carol = await _site.SignedInAsync("carol", IdentitySite.Password);
        Assert.Contains("the plan", await carol.GetStringAsync("/secrets"));
        using var dave = await _site.SignedInAsync("dave", IdentitySite.Password);
        using (var denied = await dave.GetAsync("/secrets"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var notAdmin = await dave.GetAsync("/users"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, notAdmin.StatusCode);
        }

        Assert.DoesNotContain("href=\"/users\"", await dave.GetStringAsync("/"));
    }

    [Fact]
    public async Task OwnedRowsBelongToTheIdentityUserName()
    {
        using var admin = await _site.SignedInAsync("admin", IdentitySite.AdminPassword);
        await AddUserAsync(admin, "olga", 0);
        await AddUserAsync(admin, "pete", 0);
        using var olga = await _site.SignedInAsync("olga", IdentitySite.Password);
        using (var created = await olga.PostAsJsonAsync("/api/sales", new { owner = "someone else", note = "olga's note" }))
        {
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        }

        Assert.Equal("olga", Convert.ToString(await _site.Scalar("SELECT owner FROM sales WHERE note = 'olga''s note'")));
        using var pete = await _site.SignedInAsync("pete", IdentitySite.Password);
        Assert.DoesNotContain("olga&#39;s note", await pete.GetStringAsync("/sales"));
        Assert.Contains("olga&#39;s note", await olga.GetStringAsync("/sales"));
    }

    [Fact]
    public async Task ARegisteredUserCanLogInAndChangeThePassword()
    {
        var client = _site.NewClient();
        using (var registered = await IdentitySite.PostAsync(client, "/register", "/register", new()
        {
            ["username"] = "erin", ["email"] = "erin@example.test", ["password"] = IdentitySite.Password, ["confirm"] = IdentitySite.Password,
        }))
        {
            Assert.True(registered.StatusCode == HttpStatusCode.Redirect, await registered.Content.ReadAsStringAsync());
        }

        using (var tooShort = await IdentitySite.PostAsync(client, "/register", "/register", new() { ["username"] = "frank", ["password"] = "short", ["confirm"] = "short" }))
        {
            Assert.Contains("at least 8 characters", await tooShort.Content.ReadAsStringAsync());
        }

        using var erin = await _site.SignedInAsync("erin", IdentitySite.Password);
        using (var denied = await erin.GetAsync("/secrets"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        const string newPassword = "Other-pass-43";
        using (var wrong = await IdentitySite.PostAsync(erin, "/changepassword", "/changepassword", new() { ["current"] = "nope", ["password"] = newPassword, ["confirm"] = newPassword }))
        {
            Assert.Contains("The current password is not correct.", await wrong.Content.ReadAsStringAsync());
        }

        using (var changed = await IdentitySite.PostAsync(erin, "/changepassword", "/changepassword", new() { ["current"] = IdentitySite.Password, ["password"] = newPassword, ["confirm"] = newPassword }))
        {
            Assert.Contains("Your password has been changed.", await changed.Content.ReadAsStringAsync());
        }

        var (_, old) = await _site.LogInAsync("erin", IdentitySite.Password);
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        (await _site.SignedInAsync("erin", newPassword)).Dispose();
    }

    [Fact]
    public async Task FailedLoginsLockTheUserOutUntilTheAdministratorUnlocksThem()
    {
        using var admin = await _site.SignedInAsync("admin", IdentitySite.AdminPassword);
        await AddUserAsync(admin, "gina", 0);
        for (var i = 0; i < 2; i++)
        {
            var (_, failed) = await _site.LogInAsync("gina", "wrong-password");
            Assert.Contains("Invalid username or password.", await failed.Content.ReadAsStringAsync());
        }

        // Identity locks the user out at the third failure, so that attempt already reports the lockout.
        var (_, third) = await _site.LogInAsync("gina", "wrong-password");
        Assert.Contains("Too many failed attempts.", await third.Content.ReadAsStringAsync());
        var (_, locked) = await _site.LogInAsync("gina", IdentitySite.Password);
        Assert.Contains("Too many failed attempts.", await locked.Content.ReadAsStringAsync());

        var id = await _site.UserId("gina");
        using (var unlocked = await IdentitySite.PostAsync(admin, "/users/edit?id=" + id, "/users/unlock", new() { ["id"] = id }))
        {
            Assert.Equal(HttpStatusCode.Redirect, unlocked.StatusCode);
        }

        (await _site.SignedInAsync("gina", IdentitySite.Password)).Dispose();
    }

    [Fact]
    public async Task TheAdministratorCannotDeleteThemselvesButCanDeleteOthers()
    {
        using var admin = await _site.SignedInAsync("admin", IdentitySite.AdminPassword);
        var self = await _site.UserId("admin");
        using (var refused = await IdentitySite.PostAsync(admin, "/users/edit?id=" + self, "/users/delete", new() { ["id"] = self }))
        {
            Assert.Contains("You cannot do this to your own account.", await refused.Content.ReadAsStringAsync());
        }

        await AddUserAsync(admin, "hank", 0);
        var id = await _site.UserId("hank");
        using (var deleted = await IdentitySite.PostAsync(admin, "/users/edit?id=" + id, "/users/delete", new() { ["id"] = id }))
        {
            Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        }

        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE UserName = 'hank'")));
        var (_, gone) = await _site.LogInAsync("hank", IdentitySite.Password);
        Assert.Contains("Invalid username or password.", await gone.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ThePagesNeedAnAntiforgeryToken()
    {
        using var admin = await _site.SignedInAsync("admin", IdentitySite.AdminPassword);
        using var response = await admin.PostAsync("/users/add", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
