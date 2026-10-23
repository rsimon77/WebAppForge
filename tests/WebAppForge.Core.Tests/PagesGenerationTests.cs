using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class PagesGenerationTests
{
    [Fact]
    public void TableWithoutPrimaryKeyGetsOnlyListAndSearchPages()
    {
        var project = ProjectWith(NoKeyTable());

        Generate(project, (output, _) =>
        {
            var pages = ReadPages(output, "AuditLog");

            Assert.Contains("ListPage(request, connectionString)", pages);
            Assert.Contains("SearchPage()", pages);
            Assert.DoesNotContain("ViewPage", pages);
            Assert.DoesNotContain("AddPost", pages);
            Assert.DoesNotContain("EditPost", pages);
            Assert.DoesNotContain("DeletePost", pages);
            Assert.DoesNotContain("app.MapPost(Route + \"/add\"", pages);
        });
    }

    [Fact]
    public void TextPrimaryKeyRoutesTakeStringIdAndAddFormIncludesKey()
    {
        var project = ProjectWith(TextKeyTable());

        Generate(project, (output, _) =>
        {
            var pages = ReadPages(output, "Codes");

            Assert.Contains("app.MapGet(Route + \"/view/{id}\", (string id) => ViewPage(id, connectionString));", pages);
            Assert.Contains("app.MapGet(Route + \"/edit/{id}\", (string id, HttpContext context, IAntiforgery antiforgery) => EditForm(id, context, antiforgery, connectionString));", pages);
            Assert.Contains("if (!edit) body.Append(Html.Input(\"code\", \"code\", \"text\", item.Code, true));", pages);
        });
    }

    [Fact]
    public void AutoIncrementKeyIsExcludedFromInsertColumnsAndAddForm()
    {
        var project = ProjectWith(AutoKeyTable());

        Generate(project, (output, _) =>
        {
            var pages = ReadPages(output, "OrderItems");
            var endpoints = File.ReadAllText(Path.Combine(output, "Endpoints", "OrderItemsEndpoints.cs"));
            const string insert = """INSERT INTO \"order_items\" (\"sku\", \"qty\", \"note\", \"shipped\") VALUES (@p0, @p1, @p2, @p3)""";

            Assert.Contains(insert, pages);
            Assert.Contains(insert, endpoints);
            Assert.DoesNotContain("Html.Input(\"id\"", pages);
        });
    }

    [Fact]
    public void EditNeverBindsTheIntegerPrimaryKey()
    {
        var project = ProjectWith(AutoKeyTable());

        Generate(project, (output, _) =>
        {
            var bindForm = BindFormSection(ReadPages(output, "OrderItems"));

            Assert.DoesNotContain("item.Id =", bindForm);
        });
    }

    [Fact]
    public void EditNeverBindsTheTextPrimaryKey()
    {
        var project = ProjectWith(TextKeyTable());

        Generate(project, (output, _) =>
        {
            var pages = ReadPages(output, "Codes");
            var bindForm = BindFormSection(pages);

            // The key is bound only when adding or copying, never when editing.
            Assert.Equal("if (!edit)", EnclosingGuard(bindForm, "item.Code = value;"));
            Assert.Contains("UPDATE \\\"codes\\\" SET \\\"label\\\" = @p0 WHERE \\\"code\\\" = @id", pages);
        });
    }

    [Fact]
    public void BooleanFieldBindsFromFormContainsKey()
    {
        var project = ProjectWith(AutoKeyTable());

        Generate(project, (output, _) =>
        {
            var bindForm = BindFormSection(ReadPages(output, "OrderItems"));

            Assert.Contains("item.Shipped = form.ContainsKey(\"shipped\");", bindForm);
        });
    }

    [Fact]
    public void CompositeKeyRoutesTakeOneSegmentPerKeyColumn()
    {
        var table = Table("order_lines", Key("order_id", FieldType.Integer), Key("line_no", FieldType.Integer));
        var project = ProjectWith(table);

        Generate(project, (output, _) =>
        {
            var pages = ReadPages(output, "OrderLines");

            Assert.Contains("app.MapGet(Route + \"/view/{k0}/{k1}\", (long k0, long k1) => ViewPage(k0, k1, connectionString));", pages);
        });
    }

    [Fact]
    public void CompositeKeyWithAutoIncrementColumnIsRejected()
    {
        var table = Table("order_lines", Key("order_id", FieldType.Integer, autoIncrement: true), Key("line_no", FieldType.Integer));
        var project = ProjectWith(table);
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");

        var ex = Assert.Throws<NotSupportedException>(() => new ProjectGenerator().Generate(project, output, output));

        Assert.Contains("composite key with an auto-increment", ex.Message);
    }

    [Theory]
    [InlineData("View", "/view/", "ViewPage")]
    [InlineData("Add", "/add", "AddForm")]
    [InlineData("Copy", "/copy/", "CopyForm")]
    [InlineData("Edit", "/edit/", "EditForm")]
    [InlineData("Delete", "/delete/", "DeleteConfirm")]
    [InlineData("Search", "/search", "SearchPage")]
    public void DisabledPageFlagRemovesItsRouteAndHandler(string flag, string routeSegment, string handler)
    {
        var table = AutoKeyTable();
        typeof(PageSettings).GetProperty(flag)!.SetValue(table.Pages, false);
        var project = ProjectWith(table);

        Generate(project, (output, _) =>
        {
            var pages = ReadPages(output, "OrderItems");

            Assert.Contains("ListPage(request, connectionString)", pages);
            Assert.DoesNotContain(routeSegment, pages);
            Assert.DoesNotContain(handler, pages);
        });
    }

    [Fact]
    public void TableWithGenerateFalseIsNotWritten()
    {
        var hidden = Table("hidden_notes", Key("id", FieldType.Integer));
        hidden.Generate = false;
        var project = ProjectWith(AutoKeyTable(), hidden);

        Generate(project, (output, files) =>
        {
            Assert.DoesNotContain(files, f => f.Contains("HiddenNotes"));
            Assert.False(File.Exists(Path.Combine(output, "Models", "HiddenNotes.cs")));
            Assert.False(File.Exists(Path.Combine(output, "Endpoints", "HiddenNotesEndpoints.cs")));
            Assert.False(File.Exists(Path.Combine(output, "Pages", "HiddenNotesPages.cs")));

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.DoesNotContain("HiddenNotes", program);
            Assert.DoesNotContain("hidden_notes", program);
        });
    }

    [Fact]
    public void NoGeneratedFileContainsNamespaceOrOtherTokens()
    {
        var project = ProjectWith(AutoKeyTable(), TextKeyTable(), NoKeyTable());

        Generate(project, (output, files) =>
        {
            Assert.Contains("namespace Shop.Web;", File.ReadAllText(Path.Combine(output, "Web", "Html.cs")));
            Assert.Contains("Web/Html.cs", files);

            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(output, file));
                Assert.DoesNotContain("@@NS@@", content);
                Assert.DoesNotContain("@@", content);
            }
        });
    }

    private static Project ProjectWith(params TableInfo[] tables)
    {
        var project = new Project
        {
            Name = "shop",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "shop.db" },
        };
        project.Tables.AddRange(tables);
        return project;
    }

    private static TableInfo Table(string name, params FieldInfo[] fields)
    {
        var table = new TableInfo { Name = name };
        table.Fields.AddRange(fields);
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

    private static TableInfo NoKeyTable() => Table(
        "audit_log",
        Field("message", FieldType.Text),
        Field("logged_on", FieldType.Date));

    private static TableInfo TextKeyTable() => Table(
        "codes",
        Key("code", FieldType.Text),
        Field("label", FieldType.Text));

    private static TableInfo AutoKeyTable() => Table(
        "order_items",
        Key("id", FieldType.Integer, autoIncrement: true),
        Field("sku", FieldType.Text, required: true),
        Field("qty", FieldType.Integer, required: true),
        Field("note", FieldType.Text),
        Field("shipped", FieldType.Boolean));

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

    private static string ReadPages(string output, string className) =>
        File.ReadAllText(Path.Combine(output, "Pages", $"{className}Pages.cs"));

    /// <summary>The generated BindForm method, from its signature up to the next helper.</summary>
    private static string BindFormSection(string pages)
    {
        var start = pages.IndexOf("private static void BindForm(", StringComparison.Ordinal);
        var end = pages.IndexOf("private static void BindParameters(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "BindForm method not found.");
        return pages[start..end];
    }

    /// <summary>
    /// The top-level guard (method-body indent) that encloses the line containing <paramref name="assignment"/>.
    /// Nested checks such as required-field validation are indented deeper and are skipped.
    /// </summary>
    private static string EnclosingGuard(string section, string assignment)
    {
        var lines = section.Split('\n');
        var index = Array.FindIndex(lines, line => line.Contains(assignment, StringComparison.Ordinal));
        Assert.True(index >= 0, $"'{assignment}' not found.");

        for (var i = index; i >= 0; i--)
        {
            if (lines[i].StartsWith("        if (", StringComparison.Ordinal))
            {
                return lines[i].Trim();
            }
        }

        return string.Empty;
    }
}
