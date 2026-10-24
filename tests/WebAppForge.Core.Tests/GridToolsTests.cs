using System.Text.RegularExpressions;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>Items in two categories with text, lookup, decimal, date, Unix time and boolean columns. The list has every grid tool.</summary>
public sealed class GridSite : ReportSiteBase
{
    public const string Schema = """
        CREATE TABLE categories (id INTEGER PRIMARY KEY, title TEXT NOT NULL);
        CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, category INTEGER, price DECIMAL(10,2), added DATE, stamp INTEGER, active BOOLEAN, note TEXT);
        INSERT INTO categories (id, title) VALUES (1, 'Tools'), (2, 'Toys');
        INSERT INTO items (name, category, price, added, active, note) VALUES
            ('Red hammer', 1, 12.50, '2024-01-05', 1, 'steel'),
            ('Blue hammer', 1, 15, '2024-02-10', 0, 'wood 100%'),
            ('Red ball', 2, 3.20, '2024-02-11 08:30:00', 1, NULL),
            ('Green kite', 2, 22, '2024-03-01', 1, 'big_kite'),
            ('Saw', 1, 30, NULL, 0, '');
        UPDATE items SET stamp = CAST(strftime('%s', added) AS INTEGER) WHERE added IS NOT NULL;
        """;

    public static readonly string[] Names = ["Red hammer", "Blue hammer", "Red ball", "Green kite", "Saw"];

    protected override Project? Create()
    {
        MdSupport.Exec(DbPath, Schema);
        var project = ReportSupport.ProjectOn("GridShop", DbPath);
        var items = project.Tables.Single(t => t.Name == "items");
        items.Fields.Single(f => f.Name == "category").Ui.Lookup = new FieldLookup { Table = "categories", KeyField = "id", DisplayField = "title" };
        items.Fields.Single(f => f.Name == "stamp").DateStorage = DateStorage.UnixSeconds;
        items.Designs.List.FilterBuilder = true;
        items.Designs.List.QuickFilter = true;
        items.Designs.List.ResetButton = true;

        // The view page of a category lists its items with the detail grid's quick filter and header sort.
        var link = MdSupport.Link("items", ("id", "category"));
        link.FilterAndSort = true;
        project.Tables.Single(t => t.Name == "categories").Details.Add(link);
        return project;
    }
}

public class GridToolsGenerationTests
{
    private static Project Sample(string sample, Action<PageDesign>? design = null)
    {
        var project = CustomizationSamples.Create(sample);
        var table = project.Tables.First(t => t.Generate && t.Name != project.Security.UserTable && t.PrimaryKeys.Count == 1);
        design?.Invoke(table.Designs.List);
        return project;
    }

    [Fact]
    public void TheGridToolsAreOffByDefaultAndMakeTheListDesigned()
    {
        var design = new PageDesign();
        Assert.False(design.FilterBuilder || design.QuickFilter || design.ResetButton);
        Assert.True(design.IsDefaultExceptMultiPage());
        design.QuickFilter = true;
        Assert.False(design.IsDefaultExceptMultiPage());
    }

    [Fact]
    public void GridKitIsWrittenOnlyForSitesWhoseListUsesIt()
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var plain = MdSupport.GenerateText(Sample("plain"), root);
            Assert.DoesNotContain("Web/GridKit.cs", plain.Keys);
            Assert.DoesNotContain(plain, f => f.Value.Contains("GridKit.", StringComparison.Ordinal));

            var quick = MdSupport.GenerateText(Sample("plain", d => d.QuickFilter = true), root);
            Assert.Contains("Web/GridKit.cs", quick.Keys);
            var pages = quick.Single(f => f.Value.Contains("GridKit.Panel(", StringComparison.Ordinal)).Value;
            Assert.Contains("GridKit.Panel(request, Route, GridColumns, GridSorts, false, true, false)", pages);
            Assert.Contains("GridKit.Where(request, GridColumns, parameters, false, true)", pages);
            Assert.DoesNotContain("GridKit.Order(", pages);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void LocalizationListsTheGridTextsOnlyWhenAListUsesThem()
    {
        var plain = LocalizationKeys.Enumerate(Sample("localized")).Select(s => s.Key).ToList();
        Assert.DoesNotContain("ui.gridFilter", plain);
        var used = LocalizationKeys.Enumerate(Sample("localized", d => d.FilterBuilder = true)).Select(s => s.Key).ToList();
        Assert.All(UiStrings.Grid, s => Assert.Contains(s.Key, used));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("localized")]
    [InlineData("secure")]
    public async Task SitesWithEveryGridToolBuildWithoutWarningsInTableAndCardLayouts(string sample)
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var project = Sample(sample, d =>
            {
                d.FilterBuilder = true;
                d.QuickFilter = true;
                d.ResetButton = true;
            });
            var second = project.Tables.Where(t => t.Generate && t.Name != project.Security.UserTable).Skip(1).FirstOrDefault();
            if (second is not null)
            {
                second.Designs.List.ListLayout = ListLayout.Cards;
                second.Designs.List.FilterBuilder = true;
                second.Designs.List.PageSizeOptions.AddRange([5, 10]);
            }

            var files = MdSupport.GenerateText(project, root);
            if (sample == "localized")
            {
                Assert.Contains("\"gridFilter\" => Strings.T(\"ui.gridFilter\", \"Filter and sort\"),", files["Web/GridKit.cs"]);
                Assert.Contains("\"ui.gridReset\"", files["Localization/strings.en.json"]);
            }
            else
            {
                Assert.Contains("\"gridFilter\" => \"Filter and sort\",", files["Web/GridKit.cs"]);
            }

            var build = await MdSupport.RunDotnetAsync(Path.Combine(root, "site"), "build --nologo -v q -nodeReuse:false -warnaserror");
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}

/// <summary>The filter builder, quick filter, sort levels and reset button of a running site.</summary>
public class GridToolsSiteTests : IClassFixture<GridSite>
{
    private readonly GridSite _site;

    public GridToolsSiteTests(GridSite site) => _site = site;

    /// <summary>The item names listed by /items with <paramref name="query"/>, in page order.</summary>
    private async Task<List<string>> Listed(string query)
    {
        var html = await _site.Get("/items" + query);
        return GridSite.Names.Select(n => (Name: n, At: html.IndexOf(">" + n + "<", StringComparison.Ordinal)))
            .Where(p => p.At >= 0).OrderBy(p => p.At).Select(p => p.Name).ToList();
    }

    private async Task AssertListed(string query, params string[] expected)
        => Assert.Equal(expected.Order(), (await Listed(query)).Order());

    [Fact]
    public async Task TheListShowsTheToolbarAndEveryRowWithoutAFilter()
    {
        var html = await _site.Get("/items");
        Assert.Contains("<input type=\"search\" name=\"qf\"", html);
        Assert.Contains("<details class=\"gk-builder position-relative\">", html);
        Assert.Contains("<option value=\"category\" data-kind=\"0\">category</option>", html);
        Assert.Contains("<option value=\"stamp\" data-kind=\"2\">stamp</option>", html);
        Assert.Contains("<option value=\"active\" data-kind=\"3\">active</option>", html);
        Assert.Contains("gk-reset disabled", html);
        Assert.Equal(GridSite.Names.Order(), (await Listed(string.Empty)).Order());
    }

    [Fact]
    public async Task TextConditionsMatchIgnoringCaseAndTheLookupTextIsFiltered()
    {
        await AssertListed("?ff0=name&fo0=has&fv0=HAMMER", "Red hammer", "Blue hammer");
        await AssertListed("?ff0=name&fo0=nhas&fv0=hammer", "Red ball", "Green kite", "Saw");
        await AssertListed("?ff0=name&fo0=sw&fv0=red", "Red hammer", "Red ball");
        await AssertListed("?ff0=name&fo0=ew&fv0=ball", "Red ball");
        await AssertListed("?ff0=category&fo0=eq&fv0=toys", "Red ball", "Green kite");
        await AssertListed("?ff0=category&fo0=ne&fv0=Toys", "Red hammer", "Blue hammer", "Saw");
    }

    [Fact]
    public async Task ConditionsAreJoinedWithAllOrAny()
    {
        await AssertListed("?ff0=name&fo0=has&fv0=red&ff1=price&fo1=gt&fv1=10", "Red hammer");
        await AssertListed("?fm=any&ff0=name&fo0=has&fv0=red&ff1=price&fo1=gt&fv1=20", "Red hammer", "Red ball", "Green kite", "Saw");
    }

    [Fact]
    public async Task NumbersDatesAndUnixTimesCompareAsValues()
    {
        await AssertListed("?ff0=price&fo0=bt&fv0=20&fx0=10", "Red hammer", "Blue hammer");
        await AssertListed("?ff0=price&fo0=le&fv0=3.2", "Red ball");
        await AssertListed("?ff0=added&fo0=bt&fv0=2024-02-01&fx0=2024-02-28", "Blue hammer", "Red ball");
        await AssertListed("?ff0=added&fo0=eq&fv0=2024-02-11", "Red ball");
        await AssertListed("?ff0=stamp&fo0=ge&fv0=2024-02-11", "Red ball", "Green kite");
        await AssertListed("?ff0=added&fo0=null", "Saw");
    }

    [Fact]
    public async Task BooleansAndEmptyValuesNeedNoValue()
    {
        await AssertListed("?ff0=active&fo0=no", "Blue hammer", "Saw");
        await AssertListed("?ff0=active&fo0=yes&ff1=note&fo1=null", "Red ball");
        await AssertListed("?ff0=note&fo0=nnull", "Red hammer", "Blue hammer", "Green kite");
    }

    [Fact]
    public async Task WildcardsInValuesMatchThemselves()
    {
        await AssertListed("?ff0=note&fo0=has&fv0=100%25", "Blue hammer");
        await AssertListed("?ff0=note&fo0=has&fv0=_", "Green kite");
        await AssertListed("?qf=%25", "Blue hammer");
    }

    [Fact]
    public async Task InvalidValuesAreIgnoredAndShownAndRequestTextNeverReachesTheSql()
    {
        var html = await _site.Get("/items?ff0=price&fo0=gt&fv0=abc");
        Assert.Contains("<details class=\"gk-builder position-relative\" open>", html);
        Assert.Contains("gk-value is-invalid", html);
        Assert.Equal(5, (await Listed("?ff0=price&fo0=gt&fv0=abc")).Count);

        // Unknown fields and operators, and operators of another kind, are left out.
        Assert.Equal(5, (await Listed("?ff0=name%22)%20OR%201%3D1--&fo0=has&fv0=x")).Count);
        Assert.Equal(5, (await Listed("?ff0=name&fo0=%3D1%20OR&fv0=x")).Count);
        Assert.Equal(5, (await Listed("?ff0=active&fo0=has&fv0=x")).Count);
        Assert.Empty(await Listed("?ff0=name&fo0=eq&fv0=%27%20OR%201%3D1%20--"));
        Assert.Empty(await Listed("?qf=%27)%20OR%20(%271%27%3D%271"));
        Assert.Equal(5L, await _site.Scalar("SELECT COUNT(*) FROM items"));
    }

    [Fact]
    public async Task TheQuickFilterUnderstandsWordsPhrasesColumnsAndExclusions()
    {
        await AssertListed("?qf=red", "Red hammer", "Red ball");
        await AssertListed("?qf=red%20hammer", "Red hammer");
        await AssertListed("?qf=red%20-ball", "Red hammer");
        await AssertListed("?qf=%22green%20kite%22", "Green kite");
        await AssertListed("?qf=category:tools%20-hammer", "Saw");
        await AssertListed("?qf=name:%22red%20b%22", "Red ball");
        await AssertListed("?qf=2024-02", "Blue hammer", "Red ball");
        Assert.Empty(await Listed("?qf=nothing:red"));
    }

    [Fact]
    public async Task SortLevelsOrderTheListAndAColumnHeaderTakesOver()
    {
        Assert.Equal(["Red ball", "Green kite", "Red hammer", "Blue hammer", "Saw"], await Listed("?s0=category&d0=desc&s1=price&d1=asc"));
        Assert.Equal(["Saw", "Green kite", "Blue hammer", "Red hammer", "Red ball"], await Listed("?s0=price&d0=desc&s1=price"));
        Assert.Equal(["Blue hammer", "Green kite", "Red ball", "Red hammer", "Saw"], await Listed("?s0=price&d0=desc&sort=name&dir=asc"));
        Assert.Equal(5, (await Listed("?s0=nothing&s1=%22name%22")).Count);
    }

    [Fact]
    public async Task ActiveConditionsShowAsChipsThatRemoveThemselves()
    {
        var html = await _site.Get("/items?qf=x&ff0=name&fo0=has&fv0=red&ff3=price&fo3=bt&fv3=1&fx3=20&s0=price&d0=desc&page=2");
        Assert.Contains("name contains red<a href=\"/items?qf=x&amp;ff3=price&amp;fo3=bt&amp;fv3=1&amp;fx3=20&amp;s0=price&amp;d0=desc\"", html);
        Assert.Contains("price between 1 – 20<a href=", html);
        Assert.Contains("Sort by price ↓<a href=\"/items?qf=x&amp;ff0=name&amp;fo0=has&amp;fv0=red&amp;ff3=price&amp;fo3=bt&amp;fv3=1&amp;fx3=20\"", html);
        Assert.Contains("<span class=\"badge text-bg-primary\">3</span>", html);
        Assert.Contains("<span class=\"text-muted small\">and</span>", html);

        // The quick filter form keeps the builder, the builder form keeps the quick filter; neither keeps the page.
        Assert.Contains("<input type=\"hidden\" name=\"ff0\" value=\"name\">", html);
        Assert.Contains("<input type=\"hidden\" name=\"qf\" value=\"x\">", html);
        Assert.DoesNotContain("name=\"page\"", html);
    }

    [Fact]
    public async Task TheResetButtonClearsEverythingButThePageSize()
    {
        var html = await _site.Get("/items?q=red&f_name=r&qf=x&ff0=name&fo0=has&fv0=red&s0=price&sort=name&dir=desc&pageSize=10");
        Assert.Contains("<a class=\"btn btn-sm btn-outline-secondary gk-reset\" href=\"/items?pageSize=10\">Reset</a>", html);
        Assert.Contains("<a class=\"btn btn-sm btn-outline-secondary gk-reset\" href=\"/items\">Reset</a>", await _site.Get("/items?sort=name"));
        Assert.Contains("gk-reset disabled", await _site.Get("/items?dir=desc&page=1"));
    }

    [Fact]
    public async Task ValuesAreEncodedInThePanel()
    {
        var html = await _site.Get("/items?ff0=name&fo0=has&fv0=%3Cb%3E%22&qf=%3Cscript%3E");
        Assert.DoesNotContain("<b>\"", html);
        Assert.DoesNotContain("<script>\"", html);
        Assert.Contains("value=\"&lt;b&gt;&quot;\"", html);
        Assert.Contains("value=\"&lt;script&gt;\"", html);
        Assert.Matches(new Regex("name contains &lt;b&gt;&quot;<a "), html);
    }
}

/// <summary>The quick filter, header sort and reset of a detail grid on the master's view page.</summary>
public class DetailGridToolsTests : IClassFixture<GridSite>
{
    private readonly GridSite _site;

    public DetailGridToolsTests(GridSite site) => _site = site;

    /// <summary>The item names in the detail grid of category 1 (Tools) with <paramref name="query"/>, in page order.</summary>
    private async Task<List<string>> Listed(string query, string page = "view")
    {
        var html = await _site.Get("/categories/" + page + "/1" + query);
        return GridSite.Names.Select(n => (Name: n, At: html.IndexOf(">" + n + "<", StringComparison.Ordinal)))
            .Where(p => p.At >= 0).OrderBy(p => p.At).Select(p => p.Name).ToList();
    }

    [Fact]
    public async Task TheViewPageGridHasAQuickFilterSortableHeadersAndAReset()
    {
        var html = await _site.Get("/categories/view/1");
        Assert.Contains("<div class=\"mt-4\" id=\"md-grid-0\" data-md-grid=\"0\">", html);
        Assert.Contains("<form method=\"get\" action=\"/categories/view/1#md-grid-0\" role=\"search\"", html);
        Assert.Contains("name=\"dq0\"", html);
        Assert.Contains("href=\"/categories/view/1?ds0=name&amp;dd0=asc#md-grid-0\">name</a></th>", html);
        Assert.Contains("gk-reset disabled", html);
        Assert.Equal(["Red hammer", "Blue hammer", "Saw"], await Listed(string.Empty));
    }

    [Fact]
    public async Task TheQuickFilterLimitsTheRowsOfTheMaster()
    {
        Assert.Equal(["Red hammer"], await Listed("?dq0=red"));
        Assert.Equal(["Saw"], await Listed("?dq0=-hammer"));
        Assert.Equal(["Blue hammer"], await Listed("?dq0=note:%25"));
        Assert.Empty(await Listed("?dq0=kite"));
    }

    [Fact]
    public async Task HeadersSortTheGridAndResetClearsItsParametersOnly()
    {
        Assert.Equal(["Saw", "Blue hammer", "Red hammer"], await Listed("?ds0=price&dd0=desc"));
        Assert.Equal(["Blue hammer", "Red hammer", "Saw"], await Listed("?ds0=name"));
        Assert.Equal(["Red hammer", "Blue hammer", "Saw"], await Listed("?ds0=nothing"));

        var html = await _site.Get("/categories/view/1?x=1&dq0=a&ds0=price&dd0=desc&dp0=1");
        Assert.Contains("href=\"/categories/view/1?x=1#md-grid-0\">Reset</a>", html);
        Assert.Contains("href=\"/categories/view/1?x=1&amp;dq0=a&amp;ds0=price&amp;dd0=asc#md-grid-0\">price &#9660;</a></th>", html);
        Assert.Contains("<input type=\"hidden\" name=\"ds0\" value=\"price\">", html);
    }

    [Fact]
    public async Task TheEditPageGridHasNoToolsAndIgnoresTheirParameters()
    {
        var html = await _site.Get("/categories/edit/1");
        Assert.DoesNotContain("name=\"dq0\"", html);
        Assert.DoesNotContain("gk-reset", html);
        Assert.DoesNotContain("ds0=", html);
        Assert.Equal(["Red hammer", "Blue hammer", "Saw"], await Listed("?dq0=red&ds0=price&dd0=desc", "edit"));
    }
}
