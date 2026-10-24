using System.Xml.Linq;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class OwnershipTests
{
    [Fact]
    public void OwnerFieldRoundTripsThroughProjectFile()
    {
        var project = SecureProjectWithNotes();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            ProjectFile.Save(project, path);
            var loaded = ProjectFile.Load(path);

            Assert.Equal("owner", loaded.Tables.Single(t => t.Name == "notes").OwnerField);
            Assert.Equal(string.Empty, loaded.Tables.Single(t => t.Name == "users").OwnerField);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TableWithoutOwnerAttributeReadsEmptyOwnerField()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <database provider="sqlite" path="shop.db" />
              <table name="notes" caption="" generate="true" filter="" list="true" view="true" add="true" copy="true" edit="true" delete="true" search="true" pageSize="20">
                <field name="id" caption="" type="Integer" pk="true" autoinc="true" required="true" inList="true" inView="true" inAdd="true" inEdit="true" searchable="true" />
              </table>
            </project>
            """);

        var table = Assert.Single(ProjectFile.Read(document).Tables);

        Assert.Equal(string.Empty, table.OwnerField);
    }

    [Fact]
    public void ListQueryIncludesOwnerCondition()
    {
        var project = SecureProjectWithNotes();

        Generate(project, (output, _) =>
        {
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "NotesPages.cs"));

            // BuildWhere adds the owner clause, and the count and page queries both use its result.
            Assert.Contains("clauses.Add(OwnerColumn + \" = @owner\");", pages);
            Assert.Contains("parameters.Add(new KeyValuePair<string, object>(\"@owner\", OwnerOf(request.HttpContext)));", pages);
            Assert.Contains("var args = Db.Args(parameters);", pages);
            Assert.Contains("await connection.ExecuteScalarAsync<long>(\"SELECT COUNT(*) FROM \" + Table + where, args);", pages);
            Assert.Contains("await connection.QueryAsync<Notes>(\"SELECT \" + SelectColumns + \" FROM \" + Table + where + Order", pages);

            var api = File.ReadAllText(Path.Combine(output, "Endpoints", "NotesEndpoints.cs"));
            Assert.Contains("SELECT \\\"id\\\" AS \\\"Id\\\", \\\"owner\\\" AS \\\"Owner\\\", \\\"body\\\" AS \\\"Body\\\" FROM \\\"notes\\\" WHERE \\\"owner\\\" = @owner", api);
        });
    }

    [Fact]
    public void InsertSetsOwnerToSignedInUser()
    {
        var project = SecureProjectWithNotes();

        Generate(project, (output, _) =>
        {
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "NotesPages.cs"));
            var api = File.ReadAllText(Path.Combine(output, "Endpoints", "NotesEndpoints.cs"));

            // The owner is assigned on the item before the insert, so a posted owner value is overwritten.
            Assert.Contains("item.Owner = OwnerOf(context);", pages);
            Assert.Contains("item.Owner = user.Identity?.Name ?? string.Empty;", api);
            Assert.Contains("group.MapPost(\"/\", async (Notes item, ClaimsPrincipal user) =>", api);
        });
    }

    [Fact]
    public void EditUpdateStatementDoesNotContainOwnerColumn()
    {
        var project = SecureProjectWithNotes();

        Generate(project, (output, _) =>
        {
            var pages = File.ReadAllLines(Path.Combine(output, "Pages", "NotesPages.cs"));
            var api = File.ReadAllLines(Path.Combine(output, "Endpoints", "NotesEndpoints.cs"));

            var pageUpdate = Assert.Single(pages, line => line.Contains("UPDATE ", StringComparison.Ordinal));
            var apiUpdate = Assert.Single(api, line => line.Contains("UPDATE ", StringComparison.Ordinal));

            Assert.Contains("UPDATE \\\"notes\\\" SET \\\"body\\\" = @p0", pageUpdate);
            Assert.DoesNotContain("owner", pageUpdate);
            Assert.DoesNotContain("owner", apiUpdate);
        });
    }

    [Fact]
    public void MissingOwnerFieldIsReportedByTableAndField()
    {
        var project = SecureProjectWithNotes();
        project.Tables.Single(t => t.Name == "notes").OwnerField = "account";
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");

        var ex = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, output, output));

        Assert.Contains("notes", ex.Message);
        Assert.Contains("account", ex.Message);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void DisabledSecurityProducesNoOwnerCondition()
    {
        var project = SecureProjectWithNotes();
        project.Security.Enabled = false;
        project.Tables.Remove(project.Tables.Single(t => t.Name == "users"));

        Generate(project, (output, files) =>
        {
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "NotesPages.cs"));
            var api = File.ReadAllText(Path.Combine(output, "Endpoints", "NotesEndpoints.cs"));

            Assert.DoesNotContain("OwnerColumn", pages);
            Assert.DoesNotContain("OwnerOf", pages);
            Assert.DoesNotContain("@owner", pages);
            Assert.DoesNotContain("@owner", api);
            Assert.DoesNotContain("ClaimsPrincipal", api);
            Assert.DoesNotContain("Security/", string.Join('\n', files));

            // The owner column stays an ordinary, editable column when security is off.
            Assert.Contains("Html.Input(\"owner\", ", pages);
            Assert.Contains("UPDATE \\\"notes\\\" SET \\\"owner\\\" = @p0, \\\"body\\\" = @p1", api);
        });
    }

    [Fact]
    public void OwnerFieldIsHiddenFromPagesAndForms()
    {
        var project = SecureProjectWithNotes();

        Generate(project, (output, _) =>
        {
            var pages = File.ReadAllText(Path.Combine(output, "Pages", "NotesPages.cs"));

            Assert.DoesNotContain("Html.Input(\"owner\"", pages);
            Assert.DoesNotContain("Html.Checkbox(\"owner\"", pages);
            Assert.DoesNotContain("f_owner", pages);
            Assert.Contains("Html.Input(\"body\", ", pages);
        });
    }

    [Fact]
    public void OwnerHelpersAreOnlyWrittenForOwnedTables()
    {
        var project = SecureProjectWithNotes();

        Generate(project, (output, _) =>
        {
            var users = File.ReadAllText(Path.Combine(output, "Pages", "UsersPages.cs"));
            Assert.DoesNotContain("OwnerOf", users);
        });
    }

    private static Project SecureProjectWithNotes()
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
            },
        };
        project.Tables.Add(UsersTable());

        var notes = new TableInfo { Name = "notes", OwnerField = "owner" };
        notes.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        notes.Fields.Add(Field("owner", FieldType.Text, required: true));
        notes.Fields.Add(Field("body", FieldType.Text));
        project.Tables.Add(notes);
        return project;
    }

    private static TableInfo UsersTable()
    {
        var table = new TableInfo { Name = "users" };
        table.Fields.Add(Key("id", FieldType.Integer, autoIncrement: true));
        table.Fields.Add(Field("username", FieldType.Text, required: true));
        table.Fields.Add(Field("password_hash", FieldType.Text, required: true));
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
