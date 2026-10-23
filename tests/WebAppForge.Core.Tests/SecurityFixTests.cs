using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class SecurityFixTests
{
    [Fact]
    public void LoginPageCarriesReturnUrlInAHiddenInput()
    {
        Generate(LevelSecureProject(), (output, _) =>
        {
            var auth = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));

            // The GET route reads the query string; the form posts the value back as a hidden input escaped with Html.Attr.
            Assert.Contains("Query[\"ReturnUrl\"]", auth);
            Assert.Contains("name=\\\"ReturnUrl\\\"", auth);
            Assert.Contains("Html.Attr(returnUrl)", auth);
            Assert.Contains("var returnUrl = form[\"ReturnUrl\"].ToString();", auth);
        });
    }

    [Fact]
    public void LoginPostRedirectsOnlyToLocalPaths()
    {
        Generate(LevelSecureProject(), (output, _) =>
        {
            var auth = File.ReadAllText(Path.Combine(output, "Pages", "AuthPages.cs"));
            var loginPost = auth.IndexOf("private static async Task<IResult> LoginPost", StringComparison.Ordinal);
            var redirect = auth.IndexOf("return Results.Redirect(IsLocalPath(returnUrl) ? returnUrl : \"/\");", StringComparison.Ordinal);

            Assert.True(loginPost >= 0, "LoginPost not found in generated AuthPages");
            Assert.True(redirect > loginPost, "LoginPost does not redirect through IsLocalPath");

            // The local-path check itself: the "//" and "/\" prefixes, a scheme (':') and a backslash are refused.
            Assert.Contains("private static bool IsLocalPath(string url)", auth);
            Assert.Contains("url.StartsWith(\"/\", StringComparison.Ordinal)", auth);
            Assert.Contains("!url.StartsWith(\"//\", StringComparison.Ordinal)", auth);
            Assert.Contains("!url.StartsWith(\"/\\\\\", StringComparison.Ordinal)", auth);
            Assert.Contains("url.IndexOf(':') < 0", auth);
            Assert.Contains("url.IndexOf('\\\\') < 0", auth);
        });
    }

    [Fact]
    public void UsersPagesNeverShowTheLevelFieldWhenLevelIsSet()
    {
        var project = LevelSecureProject();

        Generate(project, (output, _) =>
        {
            var lines = File.ReadAllLines(Path.Combine(output, "Pages", "UsersPages.cs"));

            // Inputs, checkboxes, list cells and view rows must not reference the level property or column.
            Assert.DoesNotContain(lines, line =>
                (line.Contains("Html.Input(", StringComparison.Ordinal) || line.Contains("Html.Checkbox(", StringComparison.Ordinal)
                    || line.Contains("Append(", StringComparison.Ordinal))
                && (line.Contains("\"level\"", StringComparison.Ordinal) || Regex.IsMatch(line, @"item\.Level\b")));

            // The other columns still get their inputs.
            var text = string.Join('\n', lines);
            Assert.Contains("Html.Input(\"username\", ", text);
            Assert.Contains("Html.Input(\"level_name\", ", text);
        });
    }

    [Fact]
    public void ProjectModelKeepsLevelFieldVisibleAndGeneratedModelUnchanged()
    {
        var project = LevelSecureProject();

        Generate(project, (output, _) =>
        {
            // The project model still has the level field visible, so saving the project changes nothing.
            var level = project.Tables.Single(t => t.Name == "users").Fields.Single(f => f.Name == "level");
            Assert.True(level.ShowInEdit);
            Assert.True(level.ShowInList);
            Assert.True(level.Searchable);

            // The generated model keeps the property, so the JSON API shape is unchanged.
            var model = File.ReadAllText(Path.Combine(output, "Models", "Users.cs"));
            Assert.Matches(@"public \S+ Level \{ get; set; \}", model);
        });
    }

    private static Project LevelSecureProject()
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
                UserLevelField = "level",
                TrackFailedAttempts = true,
                MaxFailedAttempts = 5,
                FailedAttemptsWindowMinutes = 10,
            },
        };

        var users = new TableInfo { Name = "users" };
        users.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        users.Fields.Add(Field("username", FieldType.Text, required: true));
        users.Fields.Add(Field("password_hash", FieldType.Text, required: true));
        users.Fields.Add(Field("level", FieldType.Integer, required: true));
        users.Fields.Add(Field("level_name", FieldType.Text));
        project.Tables.Add(users);
        return project;
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
