using System.Xml.Linq;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class AuthenticationTests
{
    [Fact]
    public void SecuritySettingsRoundTripThroughProjectFile()
    {
        var project = new Project
        {
            Name = "shop",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "shop.db" },
            Security = new SecuritySettings
            {
                Enabled = true,
                UserTable = "members",
                UserNameField = "login",
                PasswordField = "pwd_hash",
                TrackFailedAttempts = false,
                MaxFailedAttempts = 3,
                FailedAttemptsWindowMinutes = 15,
            },
        };

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            ProjectFile.Save(project, path);
            var loaded = ProjectFile.Load(path).Security;

            Assert.True(loaded.Enabled);
            Assert.Equal("members", loaded.UserTable);
            Assert.Equal("login", loaded.UserNameField);
            Assert.Equal("pwd_hash", loaded.PasswordField);
            Assert.False(loaded.TrackFailedAttempts);
            Assert.Equal(3, loaded.MaxFailedAttempts);
            Assert.Equal(15, loaded.FailedAttemptsWindowMinutes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProjectWithoutSecurityElementReadsDisabledWithDefaults()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <database provider="sqlite" path="shop.db" />
            </project>
            """);

        var security = ProjectFile.Read(document).Security;

        Assert.False(security.Enabled);
        Assert.Equal(string.Empty, security.UserTable);
        Assert.Equal(string.Empty, security.UserNameField);
        Assert.Equal(string.Empty, security.PasswordField);
        Assert.True(security.TrackFailedAttempts);
        Assert.Equal(5, security.MaxFailedAttempts);
        Assert.Equal(10, security.FailedAttemptsWindowMinutes);
    }

    [Fact]
    public void DisabledSecurityWritesNoLoginFilesAndNoAuthorization()
    {
        var project = SecureProject();
        project.Security.Enabled = false;

        Generate(project, (output, files) =>
        {
            Assert.DoesNotContain("Pages/AuthPages.cs", files);
            Assert.False(File.Exists(Path.Combine(output, "Pages", "AuthPages.cs")));
            Assert.False(Directory.Exists(Path.Combine(output, "Security")));
            Assert.DoesNotContain(files, f => f.StartsWith("Security/", StringComparison.Ordinal));

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.DoesNotContain("RequireAuthorization", program);
        });
    }

    [Fact]
    public void EnabledSecurityWithTrackingWritesAllLoginFiles()
    {
        var project = SecureProject();

        Generate(project, (output, files) =>
        {
            Assert.Contains("Pages/AuthPages.cs", files);
            Assert.Contains("Security/Passwords.cs", files);
            Assert.Contains("Security/LoginAttempts.cs", files);
            Assert.True(File.Exists(Path.Combine(output, "Pages", "AuthPages.cs")));
            Assert.True(File.Exists(Path.Combine(output, "Security", "Passwords.cs")));
            Assert.True(File.Exists(Path.Combine(output, "Security", "LoginAttempts.cs")));

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.Contains("RequireAuthorization", program);
        });
    }

    [Fact]
    public void EnabledSecurityWithoutTrackingSkipsLoginAttempts()
    {
        var project = SecureProject();
        project.Security.TrackFailedAttempts = false;

        Generate(project, (output, files) =>
        {
            Assert.Contains("Security/Passwords.cs", files);
            Assert.Contains("Pages/AuthPages.cs", files);
            Assert.DoesNotContain("Security/LoginAttempts.cs", files);
            Assert.False(File.Exists(Path.Combine(output, "Security", "LoginAttempts.cs")));
        });
    }

    [Fact]
    public void MissingUserTableIsReportedByName()
    {
        var project = SecureProject();
        project.Security.UserTable = "accounts";
        project.Tables.Clear();
        project.Tables.Add(OrdersTable());
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");

        var ex = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, output, output));

        Assert.Contains("accounts", ex.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void UsersPagesNeverShowThePasswordColumn()
    {
        var project = SecureProject();

        Generate(project, (output, _) =>
        {
            var lines = File.ReadAllLines(Path.Combine(output, "Pages", "UsersPages.cs"));

            // Form inputs and checkboxes for the password column (or its search filter) must not exist.
            Assert.DoesNotContain(lines, line =>
                (line.Contains("Html.Input(", StringComparison.Ordinal) || line.Contains("Html.Checkbox(", StringComparison.Ordinal))
                && line.Contains("password_hash", StringComparison.Ordinal));

            // List cells and view rows must not render the password property.
            Assert.DoesNotContain(lines, line =>
                line.Contains("Append(", StringComparison.Ordinal) && line.Contains("item.PasswordHash", StringComparison.Ordinal));

            // The other columns still get their inputs.
            var text = string.Join('\n', lines);
            Assert.Contains("Html.Input(\"username\", ", text);
            Assert.Contains("Html.Input(\"email\", ", text);
            Assert.Contains("Html.Checkbox(\"active\", ", text);
        });
    }

    [Fact]
    public void NoGeneratedFileContainsTokensWhenSecurityIsEnabled()
    {
        var project = SecureProject();
        project.Tables.Add(OrdersTable());

        Generate(project, (output, files) =>
        {
            Assert.Contains("Pages/AuthPages.cs", files);
            Assert.Contains("Security/Passwords.cs", files);
            Assert.Contains("Security/LoginAttempts.cs", files);

            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(output, file));
                Assert.DoesNotContain("@@", content);
            }
        });
    }

    [Fact]
    public void UsersModelMarksPasswordPropertyAsJsonIgnored()
    {
        var project = SecureProject();

        Generate(project, (output, _) =>
        {
            var lines = File.ReadAllLines(Path.Combine(output, "Models", "Users.cs"));
            var index = Array.FindIndex(lines, line => line.Contains("public string PasswordHash", StringComparison.Ordinal));

            Assert.True(index > 0, "password property not found in generated model");
            Assert.Equal("    [JsonIgnore]", lines[index - 1]);
            Assert.Contains("using System.Text.Json.Serialization;", lines);
        });
    }

    private static Project SecureProject()
    {
        var project = new Project
        {
            Name = "shop",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "shop.db" },
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
        table.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        table.Fields.Add(Field("username", FieldType.Text, required: true));
        table.Fields.Add(Field("password_hash", FieldType.Text, required: true));
        table.Fields.Add(Field("email", FieldType.Text));
        table.Fields.Add(Field("active", FieldType.Boolean));
        return table;
    }

    private static TableInfo OrdersTable()
    {
        var table = new TableInfo { Name = "orders" };
        table.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        table.Fields.Add(Field("total", FieldType.Decimal));
        return table;
    }

    private static FieldInfo Key(string name, FieldType type, bool autoIncrement = false) => new()
    {
        Name = name,
        Type = type,
        IsPrimaryKey = true,
        IsAutoIncrement = autoIncrement,
        IsRequired = true,
    };

    private static FieldInfo Field(string name, FieldType type, bool required = false) => new()
    {
        Name = name,
        Type = type,
        IsRequired = required,
    };

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
