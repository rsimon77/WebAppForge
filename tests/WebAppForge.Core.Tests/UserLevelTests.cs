using System.Xml.Linq;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class UserLevelTests
{
    private const string ViewDenied = "You do not have permission to view this table.";
    private const string EditDenied = "You do not have permission to change this table.";

    [Fact]
    public void LevelSettingsRoundTripThroughProjectFile()
    {
        var project = LevelProject(viewMin: 1, editMin: 2);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            ProjectFile.Save(project, path);
            var loaded = ProjectFile.Load(path);

            Assert.Equal("level", loaded.Security.UserLevelField);
            var reports = loaded.Tables.Single(t => t.Name == "reports");
            Assert.Equal(1, reports.ViewMinLevel);
            Assert.Equal(2, reports.EditMinLevel);
            Assert.Equal(0, loaded.Tables.Single(t => t.Name == "notes").ViewMinLevel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OldXmlWithoutLevelAttributesReadsDefaults()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <database provider="sqlite" path="shop.db" />
              <security enabled="true" userTable="users" userNameField="username" passwordField="password_hash" />
              <table name="reports" caption="" generate="true" filter="" list="true" view="true" add="true" copy="true" edit="true" delete="true" search="true" pageSize="20">
                <field name="id" caption="" type="Integer" pk="true" autoinc="true" required="true" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true" />
              </table>
            </project>
            """);

        var project = ProjectFile.Read(document);
        var table = Assert.Single(project.Tables);

        Assert.Equal(string.Empty, project.Security.UserLevelField);
        Assert.Equal(0, table.ViewMinLevel);
        Assert.Equal(0, table.EditMinLevel);
    }

    [Fact]
    public void NonIntegerUserLevelFieldIsRejectedByName()
    {
        var project = LevelProject();
        project.Security.UserLevelField = "level_name";

        AssertGenerationRejected(project, ex =>
        {
            Assert.Contains("level_name", ex.Message);
            Assert.Contains("integer", ex.Message);
        });
    }

    [Fact]
    public void MissingUserLevelFieldIsRejectedByName()
    {
        var project = LevelProject();
        project.Security.UserLevelField = "missing_level";

        AssertGenerationRejected(project, ex => Assert.Contains("missing_level", ex.Message));
    }

    [Fact]
    public void LevelFieldIsIgnoredWhenSecurityIsDisabled()
    {
        var project = LevelProject();
        project.Security.Enabled = false;
        project.Security.UserLevelField = "missing_level";
        project.Tables.Remove(project.Tables.Single(t => t.Name == "users"));

        Generate(project, (output, files) =>
        {
            Assert.DoesNotContain(files, f => f.StartsWith("Security/", StringComparison.Ordinal));
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "ReportsPages.cs"));
            Assert.DoesNotContain(".RequireLevel", pages);
            Assert.DoesNotContain(EditDenied, pages);
        });
    }

    [Fact]
    public void DeniedMessagesAppearOnlyInTablesWithALevelLimit()
    {
        var project = LevelProject(viewMin: 0, editMin: 2);

        Generate(project, (output, _) =>
        {
            var reports = File.ReadAllText(Path.Combine(output, "Pages", "ReportsPages.cs"));
            Assert.Contains(EditDenied, reports);
            Assert.DoesNotContain(ViewDenied, reports);

            var notes = File.ReadAllText(Path.Combine(output, "Pages", "NotesPages.cs"));
            Assert.DoesNotContain(ViewDenied, notes);
            Assert.DoesNotContain(EditDenied, notes);
            Assert.DoesNotContain(".RequireLevel", notes);

            var notesApi = File.ReadAllText(Path.Combine(output, "Endpoints", "NotesEndpoints.cs"));
            Assert.DoesNotContain(".RequireLevel", notesApi);
        });
    }

    [Fact]
    public void ViewAndEditLimitsGuardTheMatchingRoutes()
    {
        var project = LevelProject(viewMin: 1, editMin: 2);

        Generate(project, (output, _) =>
        {
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "ReportsPages.cs"));
            // List, search and view use the view limit.
            Assert.Equal(3, CountOf(pages, ".RequireLevel(1, \"" + ViewDenied + "\")"));
            // Add, copy, edit and delete (GET and POST) use the edit limit.
            Assert.Equal(8, CountOf(pages, ".RequireLevel(2, \"" + EditDenied + "\")"));

            var api = File.ReadAllText(Path.Combine(output, "Endpoints", "ReportsEndpoints.cs"));
            // API GET (list and by key) uses the view limit; POST, PUT and DELETE use the edit limit.
            Assert.Equal(2, CountOf(api, ".RequireLevel(1, \"" + ViewDenied + "\")"));
            Assert.Equal(3, CountOf(api, ".RequireLevel(2, \"" + EditDenied + "\")"));
        });
    }

    [Fact]
    public void PermissionsHelperIsWrittenOnlyWhenLevelFieldIsSet()
    {
        Generate(LevelProject(), (output, files) =>
        {
            Assert.Contains("Security/Permissions.cs", files);
            var helper = File.ReadAllText(Path.Combine(output, "Security", "Permissions.cs"));
            Assert.Contains("public static bool Allowed(ClaimsPrincipal user, int minLevel)", helper);
            Assert.Contains("\"user_level\"", helper);
        });

        Generate(LevelProject(levelField: false), (_, files) =>
        {
            Assert.DoesNotContain("Security/Permissions.cs", files);
        });
    }

    [Fact]
    public void LoginSqlReadsLevelColumnWhenConfigured()
    {
        Generate(LevelProject(), (output, _) =>
        {
            var auth = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));

            Assert.Contains("SELECT \\\"password_hash\\\", \\\"level\\\" FROM \\\"users\\\" WHERE \\\"username\\\" = @username", auth);
            Assert.Contains("var (storedHash, level) = await connection.QueryFirstOrDefaultAsync<(string? Hash, long? Level)>(LookupSql, new { username = userName });", auth);
            Assert.Contains("var userLevel = (int)(level ?? 0);", auth);
            Assert.Contains("claims.Add(new(Permissions.LevelClaimType, userLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)));", auth);
        });
    }

    [Fact]
    public void LoginKeepsOriginalLookupWithoutLevelField()
    {
        Generate(LevelProject(levelField: false), (output, _) =>
        {
            var auth = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));

            Assert.Contains("SELECT \\\"password_hash\\\" FROM \\\"users\\\" WHERE \\\"username\\\" = @username", auth);
            Assert.Contains("var storedHash = await connection.ExecuteScalarAsync<string?>(LookupSql, new { username = userName });", auth);
            Assert.DoesNotContain("LevelClaimType", auth);
        });
    }

    [Fact]
    public void GeneratedFilesContainNoTokens()
    {
        Generate(LevelProject(viewMin: 1, editMin: 2), (output, files) =>
        {
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                Assert.DoesNotContain("@@", File.ReadAllText(Path.Combine(output, file)));
            }
        });
    }

    /// <summary>A secured project: users (with an integer level column) and two tables, reports and notes.</summary>
    private static Project LevelProject(int viewMin = 0, int editMin = 0, bool levelField = true)
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
                UserLevelField = levelField ? "level" : string.Empty,
            },
        };

        var users = new TableInfo { Name = "users" };
        users.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        users.Fields.Add(Field("username", FieldType.Text, required: true));
        users.Fields.Add(Field("password_hash", FieldType.Text, required: true));
        users.Fields.Add(Field("level", FieldType.Integer, required: true));
        users.Fields.Add(Field("level_name", FieldType.Text));
        project.Tables.Add(users);

        var reports = new TableInfo { Name = "reports", ViewMinLevel = viewMin, EditMinLevel = editMin };
        reports.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        reports.Fields.Add(Field("title", FieldType.Text));
        project.Tables.Add(reports);

        var notes = new TableInfo { Name = "notes" };
        notes.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        notes.Fields.Add(Field("body", FieldType.Text));
        project.Tables.Add(notes);
        return project;
    }

    private static int CountOf(string text, string value) => text.Split(value).Length - 1;

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

    private static void AssertGenerationRejected(Project project, Action<InvalidOperationException> verify)
    {
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");
        var ex = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, output, output));
        verify(ex);
        Assert.False(Directory.Exists(output));
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
