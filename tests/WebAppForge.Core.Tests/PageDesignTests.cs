using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using WebAppForge.Core.Build;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Tests;

public class PageDesignTests
{
    // ---- defaults are unchanged -----------------------------------------------------------------

    [Fact]
    public void ProjectsWithoutDesignsGetNoPageRuntimeAndTheOriginalPages()
    {
        var files = Generate(OneTable(_ => { }));

        Assert.DoesNotContain("Web/PageKit.cs", files.Keys);
        Assert.DoesNotContain("Properties/launchSettings.json", files.Keys);
        var pages = files["Pages/ItemsPages.cs"];
        Assert.Contains("table table-striped table-hover\\\"><thead><tr>", pages);
        Assert.Contains("return Html.Page(Caption, body.ToString());", pages);
        Assert.Contains("body.Append(\"<th></th></tr></thead><tbody>\");", pages);
        Assert.DoesNotContain("PageKit", pages);
        Assert.DoesNotContain("pageSize", pages);
    }

    [Fact]
    public void ADesignedTableDoesNotChangeTheOtherTables()
    {
        var plain = Generate(TwoTables(designed: false));
        var designed = Generate(TwoTables(designed: true));

        Assert.Equal(plain["Pages/OthersPages.cs"], designed["Pages/OthersPages.cs"]);
        Assert.NotEqual(plain["Pages/ItemsPages.cs"], designed["Pages/ItemsPages.cs"]);
        Assert.Contains("Web/PageKit.cs", designed.Keys);
    }

    [Fact]
    public void AnEnvironmentOfDevelopmentChangesNothing()
    {
        var project = OneTable(_ => { });
        project.Meta.Environment = "development";
        project.Build.GenerateDockerfile = true;

        Assert.DoesNotContain("Properties/launchSettings.json", Generate(project).Keys);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", DockerGenerator.Dockerfile(project));
    }

    [Fact]
    public void ProductionSetsTheEnvironmentInLaunchSettingsAndDockerfile()
    {
        var project = OneTable(_ => { });
        project.Meta.Environment = "Production";
        project.Meta.HttpPort = 5123;

        var launch = Generate(project)["Properties/launchSettings.json"];
        Assert.Contains("\"ASPNETCORE_ENVIRONMENT\": \"Production\"", launch);
        Assert.Contains("\"applicationUrl\": \"http://localhost:5123\"", launch);
        Assert.Contains("ENV ASPNETCORE_ENVIRONMENT=Production\n", DockerGenerator.Dockerfile(project));
    }

    [Fact]
    public void TheEnvironmentIsReducedToASafeToken()
    {
        var project = OneTable(_ => { });
        project.Meta.Environment = "Prod\"\n;rm";

        Assert.Contains("\"ASPNETCORE_ENVIRONMENT\": \"Prodrm\"", Generate(project)["Properties/launchSettings.json"]);
    }

    // ---- title, header, footer, css, script -----------------------------------------------------

    [Fact]
    public void TextsAreOptionsOfThePageAndRawFieldsAreEmittedAsWritten()
    {
        var project = OneTable(t =>
        {
            t.Designs.List.Title = "Cat <script>alert(\"x\")</script>";
            t.Designs.List.Subtitle = "Sub \"quoted\" & <b>";
            t.Designs.List.HeaderHtml = "<div class=\"hdr\">Head</div>";
            t.Designs.List.FooterHtml = "<p id='foot'>Foot</p>";
            t.Designs.List.CssClass = "my-list \"x\"";
            t.Designs.List.CustomCss = ".a > .b { color: red; }";
            t.Designs.List.CustomScript = "console.log(\"hi\");";
        });

        var files = Generate(project);
        var pages = files["Pages/ItemsPages.cs"];

        // The values are C# literals of the options; PageKit encodes the texts and writes the raw fields as they are.
        Assert.Contains("Title = \"Cat <script>alert(\\\"x\\\")</script>\"", pages);
        Assert.Contains("Subtitle = \"Sub \\\"quoted\\\" & <b>\"", pages);
        Assert.Contains("HeaderHtml = \"<div class=\\\"hdr\\\">Head</div>\"", pages);
        Assert.Contains("FooterHtml = \"<p id='foot'>Foot</p>\"", pages);
        Assert.Contains("CustomCss = \".a > .b { color: red; }\"", pages);
        Assert.Contains("CustomScript = \"console.log(\\\"hi\\\");\"", pages);
        Assert.Contains("return PageKit.Page(request, Caption, body.ToString(), DesignList);", pages);

        var kit = files["Web/PageKit.cs"];
        Assert.Contains("Html.Encode(o.Subtitle.Trim())", kit);
        Assert.Contains("Html.Encode(heading)", kit);
        Assert.Contains("Html.Attr(o.CssClass.Trim())", kit);
        Assert.Contains("\"<style>\" + o.CustomCss + \"</style>\"", kit);
        Assert.Contains("\"<script>\" + o.CustomScript + \"</script>\"", kit);
        Assert.Contains("o.HeaderHtml", kit);
        Assert.Contains("o.FooterHtml", kit);
    }

    [Fact]
    public void EveryPageTypeUsesItsOwnDesign()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text));
            t.Designs.View.Title = "V";
            t.Designs.Add.Title = "A";
            t.Designs.Copy.Title = "C";
            t.Designs.Edit.Title = "E";
            t.Designs.Delete.Title = "D";
            t.Designs.Search.Title = "S";
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        foreach (var (name, title) in new[] { ("View", "V"), ("Add", "A"), ("Copy", "C"), ("Edit", "E"), ("Delete", "D"), ("Search", "S") })
        {
            Assert.Contains($"PageKit.Options Design{name} = new() {{ Title = \"{title}\" }};", pages);
        }

        Assert.Contains("PageKit.Page(context.Request, Caption, body.ToString(), DesignView)", pages);
        Assert.Contains("PageKit.Page(request, \"Search \" + Caption, body.ToString(), o)", pages);
        Assert.Contains("PageKit.Page(context.Request, \"Delete \" + Caption, body, o)", pages);
        Assert.Contains("kind == \"add\" ? DesignAdd : kind == \"copy\" ? DesignCopy : DesignEdit", pages);
        Assert.Contains("RenderForm(\"Add \" + Caption, Route + \"/add\"", pages);
        Assert.Contains("antiforgery, \"add\")", pages);
        Assert.Contains("antiforgery, \"copy\")", pages);
        Assert.Contains("antiforgery, \"edit\")", pages);
    }

    // ---- forms ----------------------------------------------------------------------------------

    [Fact]
    public void FormSettingsAreOptionsOfTheForm()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text, true));
            t.Designs.Add.FormLayout = FormLayout.TwoColumns;
            t.Designs.Add.SubmitText = "Save <it>";
            t.Designs.Add.CancelText = "Back";
            t.Designs.Add.ShowCancel = false;
            t.Designs.Add.SubmitCssClass = "btn-success";
            t.Designs.Add.Buttons = ButtonPlacement.Both;
            t.Designs.Add.ConfirmBeforeSave = true;
            t.Designs.Edit.FormLayout = FormLayout.Horizontal;
            t.Designs.Copy.FormLayout = FormLayout.ThreeColumns;
        });

        var files = Generate(project);
        var pages = files["Pages/ItemsPages.cs"];

        Assert.Contains("DesignAdd = new() { Layout = \"TwoColumns\", SubmitText = \"Save <it>\", CancelText = \"Back\", ShowCancel = false, SubmitCssClass = \"btn-success\", Buttons = \"Both\", Confirm = \"Save changes?\" };", pages);
        Assert.Contains("DesignEdit = new() { Layout = \"Horizontal\" };", pages);
        Assert.Contains("DesignCopy = new() { Layout = \"ThreeColumns\" };", pages);
        Assert.Contains("PageKit.TopButtons(o,", pages);
        Assert.Contains("PageKit.BottomButtons(o,", pages);
        Assert.Contains("PageKit.Wrap(body, start, o);", pages);
        Assert.Contains("PageKit.FormAttributes(o)", pages);

        var kit = files["Web/PageKit.cs"];
        Assert.Contains("col-md-6", kit);
        Assert.Contains("col-md-4", kit);
        Assert.Contains("col-sm-3 col-form-label", kit);
        Assert.Contains("onsubmit=\\\"return confirm(this.dataset.confirm)\\\"", kit);
        Assert.Contains("Html.Encode(text)", kit);
    }

    [Fact]
    public void SuccessMessagesAreKeptServerSideAndTheCookieHoldsOnlyAKey()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text, true));
            t.Designs.Add.SuccessMessage = "Saved <b>\"it\"</b>";
            t.Designs.Delete.SuccessMessage = "Gone.";
        });

        var files = Generate(project);
        var pages = files["Pages/ItemsPages.cs"];

        Assert.Contains("FlashMessages = [new(\"add\", \"Saved <b>\\\"it\\\"</b>\"), new(\"delete\", \"Gone.\")]", pages);
        Assert.Contains("FlashCookie = \"flash_items\"", pages);
        Assert.Contains("return PageKit.Done(context, Route, FlashCookie, \"add\");", pages);
        Assert.Contains("return PageKit.Done(context, Route, FlashCookie, \"delete\");", pages);
        Assert.Contains("return PageKit.Done(context, Route, FlashCookie, \"\");", pages);
        Assert.Contains("body.Append(PageKit.TakeFlash(request.HttpContext, FlashCookie, FlashMessages));", pages);
        var kit = files["Web/PageKit.cs"];
        Assert.Contains("Html.Encode(message.Value)", kit);
        Assert.Contains("HttpOnly = true", kit);
        Assert.Contains("Delete(cookie", kit);
    }

    // ---- containers -----------------------------------------------------------------------------

    [Fact]
    public void ModalAndOffcanvasPagesOpenFromTheListOnly()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text, true));
            t.Designs.Add.Container = PageContainer.Modal;
            t.Designs.Delete.Container = PageContainer.Offcanvas;
        });

        var files = Generate(project);
        var pages = files["Pages/ItemsPages.cs"];

        Assert.Contains("PageKit.OpenLink(Route + \"/add\", \"Add\", \"btn btn-sm btn-primary\", \"modal\")", pages);
        Assert.Contains("PageKit.OpenLink(Route + \"/delete/\" + Uri.EscapeDataString(item.Id.ToString()), \"Delete\", \"btn btn-sm btn-outline-danger\", \"offcanvas\")", pages);
        Assert.Contains("Html.Link(Route + \"/edit/\"", pages);
        Assert.Contains("body.Append(PageKit.Containers(Route, true, true));", pages);
        Assert.DoesNotContain("bootstrap.bundle", pages);
        Assert.DoesNotContain("bootstrap.bundle", files["Web/Html.cs"]);

        var kit = files["Web/PageKit.cs"];
        Assert.Contains("partial=1", kit);
        Assert.Contains("X-Requested-With", kit);
        Assert.Contains("id=\\\"pk-modal\\\"", kit);
        Assert.Contains("id=\\\"pk-offcanvas\\\"", kit);
        Assert.Contains("bootstrap.bundle.min.js", kit);
        Assert.Contains("new FormData(f)", kit);
        Assert.Contains("location.reload()", kit);
    }

    [Fact]
    public void OnlyTheUsedContainerKindIsWrittenAndPagesWithoutContainersHaveNone()
    {
        var modalOnly = Generate(OneTable(t => { t.Fields.Add(Field("name", FieldType.Text)); t.Designs.Edit.Container = PageContainer.Modal; }))["Pages/ItemsPages.cs"];
        Assert.Contains("PageKit.Containers(Route, true, false)", modalOnly);

        var offcanvasOnly = Generate(OneTable(t => t.Designs.Search.Container = PageContainer.Offcanvas))["Pages/ItemsPages.cs"];
        Assert.Contains("PageKit.Containers(Route, false, true)", offcanvasOnly);

        var none = Generate(OneTable(t => t.Designs.List.Title = "x"))["Pages/ItemsPages.cs"];
        Assert.DoesNotContain("PageKit.Containers", none);
        Assert.DoesNotContain("PageKit.OpenLink", none);
    }

    [Fact]
    public void AContainerOfAPageThatIsNotGeneratedAddsNoLink()
    {
        var project = OneTable(t =>
        {
            t.Pages.Add = false;
            t.Designs.Add.Container = PageContainer.Modal;
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        Assert.DoesNotContain("PageKit.Containers", pages);
    }

    [Fact]
    public void PartialAndFullResponsesBothGoThroughTheSameHandlers()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text, true));
            t.Designs.Add.Container = PageContainer.Modal;
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        // One handler per page; the partial switch lives in PageKit.Page and PageKit.Done, which every designed handler returns through.
        Assert.Equal(1, Count(pages, "private static IResult AddForm("));
        Assert.Equal(1, Count(pages, "private static async Task<IResult> AddPost("));
        Assert.Contains("Html.IsAntiforgeryValidAsync(context, antiforgery)", pages);
        Assert.DoesNotContain("partial", pages);
    }

    // ---- list page ------------------------------------------------------------------------------

    [Fact]
    public void CardsShowTheColumnsAsLabelAndValueAndTheActionsInTheFooter()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text, true));
            t.Fields.Add(Field("price", FieldType.Decimal));
            t.Designs.List.ListLayout = ListLayout.Cards;
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        Assert.Contains("card h-100", pages);
        Assert.Contains("card-footer", pages);
        Assert.Contains("<dt class=\\\"col-5 text-muted\\\">\").Append(Html.Encode(\"name\"))", pages);
        Assert.Contains("Append(Html.Encode(item.Name))", pages);
        Assert.DoesNotContain("<table", pages);
    }

    [Fact]
    public void CompactStripedHoverAndBorderedChooseTheTableClasses()
    {
        string Classes(Action<PageDesign> configure)
        {
            var pages = Generate(OneTable(t => configure(t.Designs.List)))["Pages/ItemsPages.cs"];
            return Regex.Match(pages, "<table class=\\\\\"([^\\\\]*)\\\\\"").Groups[1].Value;
        }

        Assert.Equal("table table-striped table-hover", Classes(_ => { }));
        Assert.Equal("table table-striped table-hover table-sm", Classes(d => d.ListLayout = ListLayout.Compact));
        Assert.Equal("table table-hover table-bordered", Classes(d => { d.Striped = false; d.Bordered = true; }));
        Assert.Equal("table", Classes(d => { d.Striped = false; d.Hover = false; }));
    }

    [Fact]
    public void StickyHeaderGetsAScrollContainerAndStickyCells()
    {
        var pages = Generate(OneTable(t => t.Designs.List.StickyHeader = true))["Pages/ItemsPages.cs"];

        Assert.Contains("table-responsive", pages);
        Assert.Contains("max-height:70vh", pages);
        Assert.Contains("position:sticky;top:0", pages);
        Assert.Contains("table table-striped table-hover table-sticky", pages);
        Assert.Contains("</tbody></table></div>", pages);
    }

    [Fact]
    public void ColumnsAreShownInTheOrderGivenWithTheirWidthAndUnknownNamesAreIgnored()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text));
            t.Fields.Add(Field("price", FieldType.Decimal));
            t.Fields.Add(Field("stock", FieldType.Integer));
            t.Fields.Add(Field("hidden_in_list", FieldType.Text));
            t.Fields.Last().ShowInList = false;
            t.Designs.List.Columns.Add(new ListColumn { Field = "STOCK" });
            t.Designs.List.Columns.Add(new ListColumn { Field = "nothing_like_it" });
            t.Designs.List.Columns.Add(new ListColumn { Field = "name", Width = "30%" });
            t.Designs.List.Columns.Add(new ListColumn { Field = "price", Visible = false });
            t.Designs.List.Columns.Add(new ListColumn { Field = "hidden_in_list" });
            t.Designs.List.Columns.Add(new ListColumn { Field = "stock" });
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];
        var header = pages[pages.IndexOf("<thead><tr>", StringComparison.Ordinal)..pages.IndexOf("foreach (var item in items)", StringComparison.Ordinal)];

        Assert.True(header.IndexOf("\"stock\"", StringComparison.Ordinal) < header.IndexOf("\"name\"", StringComparison.Ordinal));
        Assert.Contains("style=\\\"width:30%\\\"", header);
        Assert.DoesNotContain("price", header);
        Assert.DoesNotContain("hidden_in_list", header);
        Assert.DoesNotContain("nothing_like_it", header);
        // A list design makes the table sortable, so the header names the field twice (sort key and caption).
        Assert.Equal(1, Count(header, "SortHeader(request, Route, \"stock\""));
    }

    [Fact]
    public void ColumnsThatNameNoKnownFieldFallBackToTheStandardColumns()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text));
            t.Designs.List.Columns.Add(new ListColumn { Field = "nope" });
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        Assert.Contains("Html.Encode(\"name\")", pages);
        Assert.Contains("Html.Encode(\"id\")", pages);
    }

    [Fact]
    public void ColumnsKeepTheSortLinksAndWidthsOfACustomizedTable()
    {
        var project = OneTable(t =>
        {
            var name = Field("name", FieldType.Text);
            name.Ui.Width = "10%";
            t.Fields.Add(name);
            t.Designs.List.Columns.Add(new ListColumn { Field = "name", Width = "25%" });
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        Assert.Contains("FieldView.SortHeader(request, Route, \"name\", \"name\", \" style=\\\"width:25%\\\"\")", pages);
    }

    [Fact]
    public void ARowNumberColumnAndActionsFirstChangeTheOrderOfTheCells()
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text));
            t.Designs.List.ShowRowNumbers = true;
            t.Designs.List.ShowActionsFirst = true;
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];
        var headerStart = pages.IndexOf("<thead><tr>", StringComparison.Ordinal);

        var number = pages.IndexOf("<th>#</th>", headerStart, StringComparison.Ordinal);
        var actions = pages.IndexOf("<th></th>", headerStart, StringComparison.Ordinal);
        var first = pages.IndexOf("Html.Encode(\"id\")", headerStart, StringComparison.Ordinal);
        Assert.True(headerStart < number && number < actions && actions < first);
        Assert.Contains("var rowNo = (page - 1) * PageSize;", pages);
        Assert.Contains("Append(++rowNo)", pages);
        Assert.DoesNotContain("<th></th></tr></thead>", pages);
        Assert.Contains("\"</tr></thead><tbody>\"", pages);
    }

    [Fact]
    public void HidingThePagerAndSettingTextsChangeTheListPage()
    {
        var project = OneTable(t =>
        {
            t.Designs.List.ShowPager = false;
            t.Designs.List.AddButtonText = "New <thing>";
            t.Designs.List.EmptyText = "Nothing here";
        });

        var pages = Generate(project)["Pages/ItemsPages.cs"];

        Assert.DoesNotContain("Html.PageLinks", pages);
        Assert.Contains("\"New <thing>\"", pages);
        Assert.Contains("if (items.Count == 0) body.Append(\"<tr><td colspan=\\\"2\\\" class=\\\"text-muted\\\">\").Append(Html.Encode(\"Nothing here\"))", pages);
    }

    [Fact]
    public void ThePagerStaysForDefaultsAndPageSizeOptionsAreWhitelisted()
    {
        Assert.Contains("Html.PageLinks", Generate(OneTable(t => t.Designs.List.Title = "x"))["Pages/ItemsPages.cs"]);

        var project = OneTable(t =>
        {
            t.Pages.PageSize = 15;
            t.Designs.List.PageSizeOptions.AddRange([10, 20, 20, 0, -5, 50]);
        });
        var pages = Generate(project)["Pages/ItemsPages.cs"];

        Assert.Contains("private static readonly int[] PageSizeOptions = [10, 20, 50];", pages);
        Assert.Contains("var pageSize = PageKit.PageSize(request, PageSize, PageSizeOptions);", pages);
        Assert.Contains("Math.Ceiling(total / (double)pageSize)", pages);
        Assert.Contains("AddWithValue(\"@limit\", pageSize)", pages);
        Assert.Contains("PageKit.PageSizeSelector(request, Route, pageSize, PageSizeOptions)", pages);
        Assert.DoesNotContain("AddWithValue(\"@limit\", PageSize)", pages);
    }

    [Fact]
    public void WithoutPageSizeOptionsThereIsNoSelector()
    {
        var pages = Generate(OneTable(t => t.Designs.List.Striped = false))["Pages/ItemsPages.cs"];

        Assert.Contains("AddWithValue(\"@limit\", PageSize)", pages);
        Assert.DoesNotContain("PageSizeSelector", pages);
        Assert.DoesNotContain("PageSizeOptions", pages);
    }

    // ---- every option compiles ------------------------------------------------------------------

    [Theory]
    [InlineData("plain")]
    [InlineData("secure")]
    [InlineData("localized")]
    public async Task ASiteWithEveryDesignOptionSetBuildsWithoutWarnings(string sample)
    {
        var project = CustomizationSamples.Create(sample);
        project.Name = "Designed" + sample;
        CustomizationSamples.Customize(project);
        project.Meta.Environment = "Production";
        foreach (var table in project.Tables.Where(t => t.Name is "products" or "categories" or "order_items" or "codes" or "order_lines" or "audit_log"))
        {
            Design(table, table.Name == "products" ? EveryOption : Mixed(table.Name));
        }

        var root = Path.Combine(Path.GetTempPath(), $"design-{Guid.NewGuid():N}");
        try
        {
            new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root);

            var info = new ProcessStartInfo("dotnet", "build --nologo -v q -nodeReuse:false -warnaserror")
            {
                WorkingDirectory = Path.Combine(root, "site"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
            var text = await output + await error;
            Assert.True(process.ExitCode == 0, text);
        }
        finally
        {
            TryDelete(root);
        }
    }

    // ---- the generated site really behaves ------------------------------------------------------

    [Fact]
    public async Task ADesignedSiteEncodesTextsServesPartialsAndWhitelistsPageSizes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"design-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(root, "shop.db");
        Directory.CreateDirectory(root);
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, price REAL);"
                + string.Concat(Enumerable.Range(1, 30).Select(i => $"INSERT INTO items (name, price) VALUES ('Item {i:00}', {i});"));
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var project = new Project { Name = "DesignRun", Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath } };
        var items = new TableInfo { Name = "items", Caption = "Items" };
        items.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        items.Fields.Add(new FieldInfo { Name = "name", Type = FieldType.Text, IsRequired = true });
        items.Fields.Add(new FieldInfo { Name = "price", Type = FieldType.Decimal });
        items.Pages.PageSize = 5;
        items.Designs.List.Title = "Cat <script>alert(1)</script> \"q\"";
        items.Designs.List.Subtitle = "Sub <i>x</i>";
        items.Designs.List.HeaderHtml = "<div id=\"raw-header\">H</div>";
        items.Designs.List.PageSizeOptions.AddRange([10, 20]);
        items.Designs.List.ListLayout = ListLayout.Cards;
        items.Designs.List.Columns.Add(new ListColumn { Field = "price" });
        items.Designs.List.Columns.Add(new ListColumn { Field = "name" });
        items.Designs.Add.Container = PageContainer.Modal;
        items.Designs.Add.FormLayout = FormLayout.TwoColumns;
        items.Designs.Add.SuccessMessage = "Saved <ok>.";
        items.Designs.Add.SubmitText = "Store it";
        project.Tables.Add(items);

        Process? site = null;
        try
        {
            new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root);
            var port = FreePort();
            var build = Process.Start(new ProcessStartInfo("dotnet", "build --nologo -v q -nodeReuse:false")
            {
                WorkingDirectory = Path.Combine(root, "site"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var buildOutput = build.StandardOutput.ReadToEndAsync();
            await build.WaitForExitAsync();
            Assert.True(build.ExitCode == 0, await buildOutput);

            site = Process.Start(new ProcessStartInfo("dotnet", "run --no-build")
            {
                WorkingDirectory = Path.Combine(root, "site"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}" },
            })!;
            _ = site.StandardOutput.ReadToEndAsync();
            _ = site.StandardError.ReadToEndAsync();

            var cookies = new CookieContainer();
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            };
            string list = string.Empty;
            for (var attempt = 0; attempt < 60 && list.Length == 0; attempt++)
            {
                try
                {
                    list = await client.GetStringAsync("/items");
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(500);
                }
            }

            Assert.NotEqual(string.Empty, list);

            // Texts are encoded, raw header is not.
            Assert.Contains("<title>Cat &lt;script&gt;alert(1)&lt;/script&gt; &quot;q&quot;</title>", list);
            Assert.DoesNotContain("<script>alert(1)</script>", list);
            Assert.Contains("Sub &lt;i&gt;x&lt;/i&gt;", list);
            Assert.Contains("<div id=\"raw-header\">H</div>", list);

            // Cards, in the configured column order, and the default page size.
            Assert.True(list.IndexOf(">price<", StringComparison.Ordinal) is var priceAt and > 0 && priceAt < list.IndexOf(">name<", StringComparison.Ordinal));
            Assert.Equal(5, Count(list, "card h-100"));
            Assert.Equal(10, Count(await client.GetStringAsync("/items?pageSize=10"), "card h-100"));
            Assert.Equal(20, Count(await client.GetStringAsync("/items?pageSize=20"), "card h-100"));
            Assert.Equal(5, Count(await client.GetStringAsync("/items?pageSize=7"), "card h-100"));
            Assert.Equal(5, Count(await client.GetStringAsync("/items?pageSize=abc"), "card h-100"));

            // The add page opens in a modal; as a partial it is only the fragment.
            Assert.Contains("data-open=\"modal\"", list);
            Assert.Contains("id=\"pk-modal\"", list);
            var partial = await client.GetStringAsync("/items/add?partial=1");
            Assert.StartsWith("<div data-partial=\"1\">", partial);
            Assert.DoesNotContain("<html", partial);
            Assert.Contains("<form method=\"post\" action=\"/items/add\"", partial);
            Assert.Contains("Store it", partial);
            Assert.Contains("col-md-6", partial);
            var viaHeader = new HttpRequestMessage(HttpMethod.Get, "/items/add");
            viaHeader.Headers.Add("X-Requested-With", "fetch");
            Assert.DoesNotContain("<html", await (await client.SendAsync(viaHeader)).Content.ReadAsStringAsync());
            Assert.Contains("<html", await client.GetStringAsync("/items/add"));

            // A post without a token is refused; with the token it saves and answers with json.
            var rejected = await client.PostAsync("/items/add?partial=1", new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "X" }));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            var token = Regex.Match(partial, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
            var invalid = new HttpRequestMessage(HttpMethod.Post, "/items/add")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["name"] = string.Empty, ["price"] = "1" }),
            };
            invalid.Headers.Add("X-Requested-With", "fetch");
            var invalidText = await (await client.SendAsync(invalid)).Content.ReadAsStringAsync();
            Assert.StartsWith("<div data-partial=\"1\">", invalidText);
            Assert.Contains("alert-danger", invalidText);

            var save = new HttpRequestMessage(HttpMethod.Post, "/items/add")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["name"] = "Fresh", ["price"] = "9" }),
            };
            save.Headers.Add("X-Requested-With", "fetch");
            var saved = await client.SendAsync(save);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal("{\"ok\":true}", await saved.Content.ReadAsStringAsync());

            // The flash message shows once, encoded.
            var flash = await client.GetStringAsync("/items");
            Assert.Contains("Saved &lt;ok&gt;.", flash);
            Assert.DoesNotContain("Saved &lt;ok&gt;.", await client.GetStringAsync("/items"));
            Assert.Contains("Fresh", await client.GetStringAsync("/items?q=Fresh"));
        }
        finally
        {
            if (site is not null && !site.HasExited)
            {
                site.Kill(entireProcessTree: true);
                await site.WaitForExitAsync();
            }

            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static void Design(TableInfo table, Action<PageDesigns> configure) => configure(table.Designs);

    private static void EveryOption(PageDesigns d)
    {
        void Common(PageDesign p, string name)
        {
            p.Title = name + " <b>\"title\"</b>";
            p.Subtitle = "Sub of " + name;
            p.HeaderHtml = "<div class=\"hdr\">h</div>";
            p.FooterHtml = "<div class=\"ftr\">f</div>";
            p.CssClass = "extra-" + name.ToLowerInvariant();
            p.CustomCss = ".x { color: red; }";
            p.CustomScript = "console.log('" + name + "');";
        }

        void Form(PageDesign p, FormLayout layout, ButtonPlacement placement)
        {
            p.FormLayout = layout;
            p.SubmitText = "Go \"now\"";
            p.CancelText = "Never mind";
            p.ShowCancel = placement != ButtonPlacement.Top;
            p.SubmitCssClass = "btn-success";
            p.Buttons = placement;
            p.SuccessMessage = "Done <1>";
            p.ConfirmBeforeSave = true;
        }

        Common(d.List, "List");
        Common(d.View, "View");
        Common(d.Add, "Add");
        Common(d.Copy, "Copy");
        Common(d.Edit, "Edit");
        Common(d.Delete, "Delete");
        Common(d.Search, "Search");
        Form(d.Add, FormLayout.TwoColumns, ButtonPlacement.Both);
        Form(d.Copy, FormLayout.ThreeColumns, ButtonPlacement.Top);
        Form(d.Edit, FormLayout.Horizontal, ButtonPlacement.Bottom);
        Form(d.Search, FormLayout.Vertical, ButtonPlacement.Both);
        Form(d.Delete, FormLayout.Vertical, ButtonPlacement.Top);
        d.Add.Container = PageContainer.Modal;
        d.Copy.Container = PageContainer.Offcanvas;
        d.Edit.Container = PageContainer.Modal;
        d.View.Container = PageContainer.Offcanvas;
        d.Delete.Container = PageContainer.Offcanvas;
        d.Search.Container = PageContainer.Modal;
        d.List.ListLayout = ListLayout.Cards;
        d.List.Columns.Add(new ListColumn { Field = "price", Width = "20%" });
        d.List.Columns.Add(new ListColumn { Field = "name" });
        d.List.Columns.Add(new ListColumn { Field = "nope" });
        d.List.ShowRowNumbers = true;
        d.List.ShowPager = false;
        d.List.PageSizeOptions.AddRange([5, 10, 25]);
        d.List.StickyHeader = true;
        d.List.Striped = false;
        d.List.Hover = false;
        d.List.Bordered = true;
        d.List.ShowActionsFirst = true;
        d.List.AddButtonText = "New one";
        d.List.EmptyText = "Nothing <here>";
    }

    private static Action<PageDesigns> Mixed(string name) => d =>
    {
        switch (name)
        {
            case "categories":
                d.List.ListLayout = ListLayout.Compact;
                d.List.StickyHeader = true;
                d.List.ShowRowNumbers = true;
                d.List.ShowActionsFirst = true;
                d.List.EmptyText = "None";
                break;
            case "order_items":
                d.List.ListLayout = ListLayout.Cards;
                d.List.ShowRowNumbers = true;
                d.List.EmptyText = "None";
                d.Edit.Container = PageContainer.Modal;
                d.Delete.Container = PageContainer.Offcanvas;
                d.Edit.FormLayout = FormLayout.TwoColumns;
                break;
            case "order_lines":
                d.Add.FormLayout = FormLayout.Horizontal;
                d.Add.Container = PageContainer.Modal;
                d.Add.SuccessMessage = "Added";
                break;
            default:
                d.List.PageSizeOptions.AddRange([2, 4]);
                d.View.Title = "Details";
                break;
        }
    };

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static Dictionary<string, string> Generate(Project project)
    {
        var output = Path.Combine(Path.GetTempPath(), $"design-{Guid.NewGuid():N}");
        try
        {
            new ProjectGenerator().Generate(project, output, "C:/pf");
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            TryDelete(output);
        }
    }

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    private static FieldInfo Field(string name, FieldType type, bool required = false) =>
        new() { Name = name, Type = type, IsRequired = required };

    /// <summary>A project "shop" with one table "items" (auto-increment key) configured by the test.</summary>
    private static Project OneTable(Action<TableInfo> configure)
    {
        var project = new Project { Name = "shop", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        project.Database.Path = "shop.db";
        var table = new TableInfo { Name = "items" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        configure(table);
        project.Tables.Add(table);
        return project;
    }

    private static Project TwoTables(bool designed)
    {
        var project = OneTable(t =>
        {
            t.Fields.Add(Field("name", FieldType.Text, true));
            if (designed)
            {
                t.Designs.List.Title = "Designed";
            }
        });
        var others = new TableInfo { Name = "others" };
        others.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        others.Fields.Add(Field("label", FieldType.Text));
        project.Tables.Add(others);
        return project;
    }
}
