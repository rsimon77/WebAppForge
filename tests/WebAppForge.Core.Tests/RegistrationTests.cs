using System.Xml.Linq;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class RegistrationTests
{
    [Fact]
    public void RegistrationSettingsRoundTripThroughProjectFile()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = true;
        project.Security.AllowChangePassword = false;

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            ProjectFile.Save(project, path);
            var loaded = ProjectFile.Load(path).Security;

            Assert.True(loaded.AllowRegistration);
            Assert.False(loaded.AllowChangePassword);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OldSecurityXmlReadsRegistrationDefaults()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <database provider="sqlite" path="shop.db" />
              <security enabled="true" userTable="users" userNameField="username" passwordField="password" />
            </project>
            """);

        var security = ProjectFile.Read(document).Security;

        Assert.True(security.Enabled);
        Assert.False(security.AllowRegistration);
        Assert.True(security.AllowChangePassword);
    }

    [Fact]
    public void RegistrationRoutesExistOnlyWhenAllowRegistration()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = true;
        project.Security.AllowChangePassword = false;

        Generate(project, (output, files) =>
        {
            Assert.Contains("Pages/UserAccountPages.cs", files);
            var accounts = File.ReadAllText(Path.Combine(output, "Pages", "UserAccountPages.cs"));
            Assert.Contains("app.MapGet(\"/register\"", accounts);
            Assert.Contains("app.MapPost(\"/register\"", accounts);
            Assert.DoesNotContain("changepassword", accounts, StringComparison.OrdinalIgnoreCase);

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.Contains("app.MapRegistrationPages(connectionString);", program);
            Assert.DoesNotContain("MapChangePasswordPages", program);

            // Sign-up is anonymous: it is mapped on app, before the authorized group is created.
            Assert.True(program.IndexOf("app.MapRegistrationPages(", StringComparison.Ordinal)
                < program.IndexOf("var secured = app.MapGroup", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void NoRegistrationRoutesWhenAllowRegistrationIsOff()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = false;
        project.Security.AllowChangePassword = true;

        Generate(project, (output, files) =>
        {
            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(output, file));
                Assert.DoesNotContain("\"/register\"", content);
                Assert.DoesNotContain("MapRegistrationPages", content);
            }

            var login = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));
            Assert.DoesNotContain("Register", login);
        });
    }

    [Fact]
    public void ChangePasswordRouteIsUnderTheAuthorizedGroup()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = false;
        project.Security.AllowChangePassword = true;

        Generate(project, (output, _) =>
        {
            var accounts = File.ReadAllText(Path.Combine(output, "Pages", "UserAccountPages.cs"));
            Assert.Contains("app.MapGet(\"/changepassword\"", accounts);
            Assert.Contains("app.MapPost(\"/changepassword\"", accounts);

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            var authorized = program.IndexOf("var secured = app.MapGroup(\"\").RequireAuthorization();", StringComparison.Ordinal);
            var mapped = program.IndexOf("secured.MapChangePasswordPages(connectionString);", StringComparison.Ordinal);
            Assert.True(authorized >= 0, "the authorized group is missing");
            Assert.True(mapped > authorized, "change password must be mapped on the authorized group");
            Assert.DoesNotContain("app.MapChangePasswordPages", program);

            // The home page is in the same group, so the link only shows to signed-in users.
            Assert.Contains("Html.Link(\"/changepassword\", \"Change password\")", program);
        });
    }

    [Fact]
    public void ChangePasswordRoutesExistOnlyWhenAllowChangePassword()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = false;
        project.Security.AllowChangePassword = false;

        Generate(project, (output, files) =>
        {
            Assert.DoesNotContain("Pages/UserAccountPages.cs", files);
            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(output, file));
                Assert.DoesNotContain("changepassword", content, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("MapChangePasswordPages", content);
            }
        });
    }

    [Fact]
    public void DisabledSecurityWritesNoAccountPagesEvenWithOptionsSet()
    {
        var project = SecureProject();
        project.Security.Enabled = false;
        project.Security.AllowRegistration = true;
        project.Security.AllowChangePassword = true;

        Generate(project, (output, files) =>
        {
            Assert.DoesNotContain("Pages/UserAccountPages.cs", files);
            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.DoesNotContain("MapRegistrationPages", program);
            Assert.DoesNotContain("MapChangePasswordPages", program);
        });
    }

    [Fact]
    public void NonTextPasswordColumnIsRejectedByName()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = true;
        project.Tables[0].Fields.Single(f => f.Name == "password_hash").Type = FieldType.Integer;
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");

        var ex = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, output, output));

        Assert.Contains("password_hash", ex.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void SignUpSaysManualSignUpWhenAnotherRequiredColumnHasNoDefault()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = true;
        project.Tables[0].Fields.Add(new FieldInfo { Name = "tenant", Type = FieldType.Text, IsRequired = true, HasDefault = false });

        Generate(project, (output, _) =>
        {
            var accounts = File.ReadAllText(Path.Combine(output, "Pages", "UserAccountPages.cs"));
            Assert.Contains("This site needs a manual sign-up for this table.", accounts);
            Assert.DoesNotContain("INSERT INTO", accounts);
        });
    }

    [Fact]
    public void SignUpInsertsWhenOtherRequiredColumnsHaveDefaults()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = true;
        project.Tables[0].Fields.Add(new FieldInfo { Name = "tenant", Type = FieldType.Text, IsRequired = true, HasDefault = true });

        Generate(project, (output, _) =>
        {
            var accounts = File.ReadAllText(Path.Combine(output, "Pages", "UserAccountPages.cs"));
            Assert.DoesNotContain("manual sign-up", accounts);
            Assert.Contains("INSERT INTO", accounts);
        });
    }

    [Fact]
    public void NoGeneratedFileContainsTokensWithBothOptionsOn()
    {
        var project = SecureProject();
        project.Security.AllowRegistration = true;
        project.Security.AllowChangePassword = true;
        project.Tables.Add(OrdersTable());

        Generate(project, (output, files) =>
        {
            Assert.Contains("Pages/UserAccountPages.cs", files);
            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(output, file));
                Assert.DoesNotContain("@@", content);
            }
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
