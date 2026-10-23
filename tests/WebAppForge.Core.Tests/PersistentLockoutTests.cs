using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>The failed-login lockout is stored in the login_attempts table, not in memory.</summary>
public class PersistentLockoutTests
{
    [Fact]
    public void GeneratedCodeCreatesLoginAttemptsTableIfMissing()
    {
        Generate(SecureProject(), (output, _) =>
        {
            var lockout = File.ReadAllText(Path.Combine(output, "Security", "LoginAttempts.cs"));
            Assert.Contains("CREATE TABLE IF NOT EXISTS login_attempts", lockout);
            Assert.Contains("username TEXT PRIMARY KEY, failures INTEGER NOT NULL, window_start TEXT NOT NULL", lockout);
        });
    }

    [Fact]
    public void ProgramCreatesTheTableBeforeTheAppRuns()
    {
        Generate(SecureProject(), (output, _) =>
        {
            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            var ensure = program.IndexOf("await LoginAttempts.EnsureTableAsync(connectionString);", StringComparison.Ordinal);
            var run = program.IndexOf("app.Run();", StringComparison.Ordinal);

            Assert.True(ensure >= 0, "EnsureTableAsync call not found in Program.cs");
            Assert.True(ensure < run, "EnsureTableAsync must run before app.Run()");
            Assert.Contains("using Demo.Security;", program);
        });
    }

    [Fact]
    public void LoginAttemptsHelperHoldsNoDictionaryState()
    {
        Generate(SecureProject(), (output, _) =>
        {
            var lockout = File.ReadAllText(Path.Combine(output, "Security", "LoginAttempts.cs"));

            Assert.DoesNotContain("Dictionary", lockout);
            Assert.DoesNotContain("_windows", lockout);
            Assert.DoesNotContain("lock (", lockout);
        });
    }

    [Fact]
    public void LoginAttemptsUsesParameterisedSqlAndOneTransactionOnFailure()
    {
        Generate(SecureProject(), (output, _) =>
        {
            var lockout = File.ReadAllText(Path.Combine(output, "Security", "LoginAttempts.cs"));

            Assert.Contains("@username", lockout);
            Assert.Contains("@failures", lockout);
            Assert.Contains("@windowStart", lockout);
            Assert.Contains("ON CONFLICT(username) DO UPDATE", lockout);
            Assert.Contains("BEGIN IMMEDIATE", lockout);
            Assert.Contains("SQLite serialises", lockout);
            Assert.Contains("DateTime.UtcNow", lockout);
            Assert.Contains("CultureInfo.InvariantCulture", lockout);
        });
    }

    [Fact]
    public void LoginPagesKeepTheSameMessageAndCallTheDatabaseHelper()
    {
        Generate(SecureProject(), (output, _) =>
        {
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));

            Assert.Contains("Too many failed attempts. Try again later.", pages);
            Assert.Contains("if (await attempts.IsLocked(userName))", pages);
            Assert.Contains("await attempts.RecordFailure(userName);", pages);
            Assert.Contains("await attempts.Reset(userName);", pages);
            Assert.DoesNotContain("private static readonly LoginAttempts", pages);
        });
    }

    [Fact]
    public void WithoutTrackingNoLoginAttemptsCodeIsGenerated()
    {
        var project = SecureProject();
        project.Security.TrackFailedAttempts = false;

        Generate(project, (output, files) =>
        {
            Assert.DoesNotContain("Security/LoginAttempts.cs", files);

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.DoesNotContain("EnsureTableAsync", program);
            Assert.DoesNotContain("using Demo.Security;", program);

            var pages = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));
            Assert.DoesNotContain("LockedMessage", pages);
            Assert.DoesNotContain("attempts", pages);

            foreach (var file in Directory.EnumerateFiles(output, "*.cs", SearchOption.AllDirectories))
            {
                Assert.DoesNotContain("login_attempts", File.ReadAllText(file));
            }
        });
    }

    private static Project SecureProject()
    {
        var project = new Project
        {
            Name = "demo",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "demo.db" },
            Security = new SecuritySettings
            {
                Enabled = true,
                UserTable = "users",
                UserNameField = "username",
                PasswordField = "password_hash",
                TrackFailedAttempts = true,
                MaxFailedAttempts = 5,
                FailedAttemptsWindowMinutes = 10,
            },
        };
        project.Tables.Add(UsersTable());
        return project;
    }

    private static TableInfo UsersTable()
    {
        var table = new TableInfo { Name = "users" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "username", Type = FieldType.Text, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "password_hash", Type = FieldType.Text, IsRequired = true });
        return table;
    }

    /// <summary>Generates into a fresh temp folder, runs the checks, and always deletes the folder.</summary>
    private static void Generate(Project project, Action<string, IReadOnlyList<string>> verify)
    {
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");
        try
        {
            var files = new ProjectGenerator().Generate(project, output, output);
            verify(output, files);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }
}
