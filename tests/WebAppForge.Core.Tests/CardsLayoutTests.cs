using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>Cards list layout: row styles, sort bar, and the rule for when sorting UI exists.</summary>
public class CardsLayoutTests
{
    private static Project Shop(Action<TableInfo> configure)
    {
        var project = new Project { Name = "shop", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        project.Database.Path = "shop.db";
        var table = new TableInfo { Name = "items" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "name", Type = FieldType.Text });
        table.Fields.Add(new FieldInfo { Name = "price", Type = FieldType.Decimal });
        table.Fields.Add(new FieldInfo { Name = "blob", Type = FieldType.Binary });
        table.Fields.Add(new FieldInfo { Name = "flag", Type = FieldType.Boolean });
        configure(table);
        project.Tables.Add(table);
        return project;
    }

    private static Dictionary<string, string> Generate(Project project)
    {
        var output = Path.Combine(Path.GetTempPath(), $"cards-{Guid.NewGuid():N}");
        try
        {
            new ProjectGenerator().Generate(project, output, "C:/pf");
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace(Path.DirectorySeparatorChar, '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            try { Directory.Delete(output, true); } catch (IOException) { }
        }
    }

    private static void Cards(TableInfo t)
    {
        t.Designs.List.ListLayout = ListLayout.Cards;
        t.Ui.RowStyles.Add(new RowStyleRule { Field = "flag", Operator = ConditionOperator.Equals, Value = "true", Style = "danger" });
    }

    [Fact]
    public void ACardOfAMatchingRecordGetsTheContextualBorderAndBackground()
    {
        var pages = Generate(Shop(Cards))["Pages/ItemsPages.cs"];

        Assert.Contains("return \" border-danger bg-danger-subtle\";", pages);
        Assert.Contains("card h-100\").Append(RowClass(item)).Append(\"\\\"><div class=\\\"card-body", pages);
        Assert.DoesNotContain("table-danger", pages);
    }

    [Fact]
    public void TheTableLayoutKeepsItsTableRowClass()
    {
        var pages = Generate(Shop(t => { Cards(t); t.Designs.List.ListLayout = ListLayout.Table; }))["Pages/ItemsPages.cs"];

        Assert.Contains("class=\\\"table-danger\\\"", pages);
        Assert.DoesNotContain("bg-danger-subtle", pages);
    }

    [Fact]
    public void CardsWithoutRowStylesGetThePlainCard()
    {
        var pages = Generate(Shop(t => t.Designs.List.ListLayout = ListLayout.Cards))["Pages/ItemsPages.cs"];

        Assert.Contains("card h-100\\\"><div class=\\\"card-body", pages);
        Assert.DoesNotContain("RowClass", pages);
    }

    [Fact]
    public void CardsGetASortBarWithEverySortableListColumn()
    {
        var pages = Generate(Shop(t =>
        {
            Cards(t);
            t.Fields.First(f => f.Name == "price").Ui.Sortable = false;
        }))["Pages/ItemsPages.cs"];

        var line = pages.Split('\n').Single(l => l.Contains("FieldView.SortBar("));
        Assert.Contains("(\"id\",", line);
        Assert.Contains("(\"name\",", line);
        Assert.Contains("(\"flag\",", line);
        Assert.DoesNotContain("\"price\"", line);   // not sortable
        Assert.DoesNotContain("\"blob\"", line);    // binary
        Assert.Contains("private static readonly string[] SortFields", pages);
    }

    [Fact]
    public void TheSortBarKeepsOtherQueryParametersAndTogglesTheDirection()
    {
        var ui = Generate(Shop(Cards))["Web/Ui.cs"];

        var bar = ui[ui.IndexOf("public static string SortBar", StringComparison.Ordinal)..];
        Assert.Contains("pair.Key is \"sort\" or \"dir\" or \"page\"", bar);
        Assert.Contains("var next = active && !descending ? \"desc\" : \"asc\";", bar);
        Assert.Contains("Html.Attr(route + \"?sort=\"", bar);
    }

    [Fact]
    public void ATableLayoutDoesNotGetASortBar()
    {
        var pages = Generate(Shop(t => { t.Designs.List.ListLayout = ListLayout.Table; t.Designs.List.Title = "Items"; }))["Pages/ItemsPages.cs"];

        Assert.DoesNotContain("SortBar", pages);
        Assert.Contains("FieldView.SortHeader(", pages);
    }

    [Fact]
    public void ATableWithOnlyAListDesignGetsSorting()
    {
        var files = Generate(Shop(t => t.Designs.List.Title = "Items"));

        Assert.Contains("Web/Ui.cs", files.Keys);
        Assert.Contains("FieldView.SortHeader(", files["Pages/ItemsPages.cs"]);
    }

    [Fact]
    public void ATableWithNoCustomizationAtAllHasNoSortingAndNoRuntime()
    {
        var files = Generate(Shop(_ => { }));

        Assert.DoesNotContain("Web/Ui.cs", files.Keys);
        Assert.DoesNotContain("Sort", files["Pages/ItemsPages.cs"]);
    }

    [Fact]
    public void AnAddDesignAloneDoesNotChangeTheListPageSorting()
    {
        var files = Generate(Shop(t => t.Designs.Add.Title = "New"));

        Assert.DoesNotContain("SortHeader", files["Pages/ItemsPages.cs"]);
    }
}
