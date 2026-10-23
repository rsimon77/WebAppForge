using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Tests;

public class MultiPageFormTests
{
    // ---- unused = unchanged ---------------------------------------------------------------------

    [Fact]
    public void NoPagesOrModeNoneGivesByteIdenticalOutput()
    {
        // A designed table (so it takes the designed form code) without anything multi-page.
        var baseline = Generate(People(t => t.Designs.Add.Title = "New person"));

        // Pages defined and fields assigned, but every mode is None.
        var pagesOnly = Generate(People(t =>
        {
            t.Designs.Add.Title = "New person";
            AddPages(t);
        }));
        // A mode set but no pages defined.
        var modeOnly = Generate(People(t =>
        {
            t.Designs.Add.Title = "New person";
            t.Designs.Add.MultiPage = MultiPageMode.Tabs;
            t.Designs.Edit.MultiPage = MultiPageMode.Wizard;
            t.Designs.View.MultiPage = MultiPageMode.Accordion;
        }));

        AssertSame(baseline, pagesOnly);
        AssertSame(baseline, modeOnly);
        Assert.DoesNotContain("Web/FormPages.cs", pagesOnly.Keys);
        Assert.DoesNotContain("FormPages", pagesOnly["Pages/PeoplePages.cs"]);
        Assert.DoesNotContain("bootstrap-icons", pagesOnly["Web/Html.cs"]);

        // The same for a table that is not designed at all.
        AssertSame(Generate(People(_ => { })), Generate(People(t => AddPages(t))));
    }

    [Fact]
    public void AMultiPageTableDoesNotChangeOtherTables()
    {
        var plain = Generate(TwoTables(paged: false));
        var paged = Generate(TwoTables(paged: true));

        Assert.Equal(plain["Pages/OthersPages.cs"], paged["Pages/OthersPages.cs"]);
        Assert.Equal(plain["Endpoints/OthersEndpoints.cs"], paged["Endpoints/OthersEndpoints.cs"]);
        Assert.NotEqual(plain["Pages/PeoplePages.cs"], paged["Pages/PeoplePages.cs"]);
        Assert.Contains("Web/FormPages.cs", paged.Keys);
    }

    [Fact]
    public void AModeOfNoneForOneKindKeepsTheFlatFormForThatKind()
    {
        var pages = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Edit.MultiPage = MultiPageMode.Wizard;
        }))["Pages/PeoplePages.cs"];

        Assert.Contains("var mp = kind == \"add\" ? \"None\" : kind == \"copy\" ? \"None\" : \"Wizard\";", pages);
        Assert.Contains("if (mp == \"None\")", pages);
        // The view page is not split.
        Assert.DoesNotContain("var mp = \"", pages);
    }

    // ---- grouping -------------------------------------------------------------------------------

    [Fact]
    public void FieldsAreGroupedByPageAndUnknownOrEmptyGoesToTheFirstPage()
    {
        var pages = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Add.MultiPage = MultiPageMode.Tabs;
        }))["Pages/PeoplePages.cs"];

        var form = Between(pages, "private static IResult RenderForm(", "private static void BindForm(");
        var personal = form.IndexOf("new FormPages.Page(0, \"Personal\"", StringComparison.Ordinal);
        var work = form.IndexOf("new FormPages.Page(1, \"Work\"", StringComparison.Ordinal);
        var notes = form.IndexOf("new FormPages.Page(2, \"Notes\"", StringComparison.Ordinal);
        Assert.True(personal > 0 && personal < work && work < notes, "pages are in order");

        // Segment of each page: the inputs before its pages.Add line.
        var inPersonal = form[..personal];
        var inWork = form[personal..work];
        var inNotes = form[work..notes];
        foreach (var name in new[] { "first", "last", "birth", "city", "stray", "unknown" })
        {
            Assert.Contains($"Html.Input(\"{name}\"", inPersonal);
        }

        Assert.Contains("Html.Input(\"title\"", inWork);
        Assert.Contains("Html.Input(\"hired\"", inWork);
        Assert.Contains("Html.Input(\"notes\"", inNotes);
        // A page without fields and a binary field are skipped.
        Assert.DoesNotContain("\"Empty\"", form);
        Assert.DoesNotContain("photo", form);
        Assert.Equal(3, Count(form, "pages.Add("));
    }

    [Fact]
    public void FieldGroupsInsideAPageAreStillHonored()
    {
        var pages = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Add.MultiPage = MultiPageMode.Pills;
            t.Fields.Single(f => f.Name == "city").Ui.Group = "Address";
            t.Fields.Single(f => f.Name == "stray").Ui.Group = "Address";
        }))["Pages/PeoplePages.cs"];

        var form = Between(pages, "private static IResult RenderForm(", "private static void BindForm(");
        var personal = form[..form.IndexOf("new FormPages.Page(0,", StringComparison.Ordinal)];
        Assert.Contains("FieldEditor.GroupStart(\"Address\")", personal);
        Assert.Contains("FieldEditor.GroupEnd", personal);
        // The group is closed at the end of its page, not carried into the next one.
        var groupEnd = personal.LastIndexOf("FieldEditor.GroupEnd", StringComparison.Ordinal);
        Assert.True(groupEnd > personal.LastIndexOf("Html.Input(\"stray\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePageNameMatchIgnoresCaseAndSpaces()
    {
        var pages = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Add.MultiPage = MultiPageMode.Tabs;
            t.Fields.Single(f => f.Name == "title").Ui.FormPage = "  NOTES ";
        }))["Pages/PeoplePages.cs"];

        var form = Between(pages, "private static IResult RenderForm(", "private static void BindForm(");
        var work = form.IndexOf("new FormPages.Page(1, \"Work\"", StringComparison.Ordinal);
        var notes = form.IndexOf("new FormPages.Page(2, \"Notes\"", StringComparison.Ordinal);
        Assert.Contains("Html.Input(\"title\"", form[work..notes]);
    }

    // ---- modes ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(MultiPageMode.Tabs)]
    [InlineData(MultiPageMode.Pills)]
    [InlineData(MultiPageMode.Accordion)]
    [InlineData(MultiPageMode.Wizard)]
    public void EachModeIsRenderedByTheRuntimeForAddCopyAndEdit(MultiPageMode mode)
    {
        var files = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Add.MultiPage = mode;
            t.Designs.Copy.MultiPage = mode;
            t.Designs.Edit.MultiPage = mode;
        }));

        var pages = files["Pages/PeoplePages.cs"];
        Assert.Contains($"var mp = kind == \"add\" ? \"{mode}\" : kind == \"copy\" ? \"{mode}\" : \"{mode}\";", pages);
        Assert.Contains("FormPages.Render(mp, pages, errors, errMap, true)", pages);
        Assert.Contains("FormPages.ErrorAlert(errors, errMap)", pages);
        Assert.Contains("Web/FormPages.cs", files.Keys);
        var runtime = files["Web/FormPages.cs"];
        Assert.Contains("kind == \"accordion\"", runtime);
        Assert.Contains("nav nav-tabs", runtime);
        Assert.Contains("nav nav-pills", runtime);
        Assert.Contains("data-pk-prev", runtime);
        Assert.Contains("progress-bar", runtime);
        Assert.Contains("accordion-button", runtime);
    }

    [Fact]
    public void TheRuntimeDescribesTheWizardAndTheNoScriptFallback()
    {
        var runtime = Generate(People(t => { AddPages(t); t.Designs.Edit.MultiPage = MultiPageMode.Wizard; }))["Web/FormPages.cs"];

        // Browser validity before moving on, Save only on the last step, and Enter on an earlier step moves on.
        Assert.Contains("checkValidity()", runtime);
        Assert.Contains("reportValidity()", runtime);
        Assert.Contains("b.classList.toggle('d-none',i!==n-1)", runtime);
        Assert.Contains("if(cur<n-1){e.preventDefault();", runtime);
        // An invalid control on another page makes that page visible, so the browser can focus it.
        Assert.Contains("form.addEventListener('invalid'", runtime);
        // Without JavaScript nothing is hidden: panes only hide under .pk-js.
        Assert.Contains(".pk-pages.pk-js .pk-pane{display:none}", runtime);
        Assert.Contains(".pk-pages:not(.pk-js) .pk-js-only{display:none!important}", runtime);
        Assert.Contains(".pk-pages:not(.pk-js) .collapse{display:block}", runtime);
        // Fragments: a mutation observer calls pkPages after the insert.
        Assert.Contains("window.pkPages=function(root)", runtime);
        Assert.Contains("new MutationObserver", runtime);
        // English strings, not localized.
        Assert.Contains("\"Previous\"", runtime);
        Assert.Contains("\"Next\"", runtime);
        Assert.Contains("\"Step {0} of {1}\"", runtime);
        Assert.DoesNotContain("Strings.", runtime);
    }

    [Fact]
    public void TheViewPageRendersTheSamePagesReadOnly()
    {
        var pages = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.View.MultiPage = MultiPageMode.Accordion;
        }))["Pages/PeoplePages.cs"];

        var view = Between(pages, "private static async Task<IResult> ViewPage(", "private static");
        Assert.Contains("var mp = \"Accordion\";", view);
        Assert.Contains("FormPages.Render(mp, pages, new List<string>(), Array.Empty<int>(), false)", view);
        Assert.Contains("new FormPages.Page(0, \"Personal\"", view);
        Assert.Contains("new FormPages.Page(1, \"Work\"", view);
        Assert.Contains("new FormPages.Page(2, \"Notes\"", view);
        Assert.DoesNotContain("\"Empty\"", view);
        Assert.Contains("<dl class=\\\"row\\\">", view);
        Assert.Contains("Back to list", view);
        // The photo is binary but shown on the view page (as before): view pages list ShowInView fields, forms list editable ones.
        Assert.Contains("Html.Encode(\"photo\")", view);
    }

    // ---- errors ---------------------------------------------------------------------------------

    [Fact]
    public void BindFormRemembersWhichPageEachFieldErrorBelongsTo()
    {
        var pages = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Edit.MultiPage = MultiPageMode.Wizard;
        }))["Pages/PeoplePages.cs"];

        var bind = Between(pages, "private static void BindForm(", "private static");
        var tags = Regex.Matches(bind, @"FormPages\.Tag\(errors, pk_e(\d+), (\d+)\);").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        // first, last, birth, city, title, hired, notes, stray, unknown: every bound text, number and date field.
        Assert.Equal(9, tags.Count);
        Assert.Equal(["0", "0", "0", "0", "1", "1", "2", "0", "0"], tags.Select(t => t.Item2));
        Assert.Equal(9, Count(bind, "var pk_e"));
    }

    [Fact]
    public void TablesWithoutPagesDoNotTagErrors()
    {
        var pages = Generate(People(t => t.Designs.Add.Title = "x"))["Pages/PeoplePages.cs"];

        Assert.DoesNotContain("pk_e", pages);
    }

    // ---- encoding, icons, containers ------------------------------------------------------------

    [Fact]
    public void CaptionsAndDescriptionsAreCodeLiteralsAndTheIconIsReducedToAToken()
    {
        var files = Generate(People(t =>
        {
            AddPages(t);
            t.Designs.Add.MultiPage = MultiPageMode.Tabs;
            t.FormPages[0].Caption = "Per\"son\\al <b>1</b>";
            t.FormPages[0].Description = "Line\nTwo \"q\"";
            t.FormPages[0].Icon = "person\"><script>";
        }));

        var pages = files["Pages/PeoplePages.cs"];
        Assert.Contains("new FormPages.Page(0, \"Personal\", \"Per\\\"son\\\\al <b>1</b>\", \"personscript\", \"Line\\nTwo \\\"q\\\"\",", pages);
        // The runtime encodes captions and descriptions when it writes them.
        var runtime = files["Web/FormPages.cs"];
        Assert.Contains("Html.Encode(p.Caption)", runtime);
        Assert.Contains("Html.Encode(page.Description)", runtime);
        Assert.Contains("Html.Attr(icon)", runtime);
    }

    [Fact]
    public void TheIconFontIsLinkedOnlyWhenAPageUsesAnIcon()
    {
        string Html(Action<TableInfo> configure) => Generate(People(configure))["Web/Html.cs"];

        Assert.DoesNotContain("bootstrap-icons", Html(t => { AddPages(t); t.Designs.Add.MultiPage = MultiPageMode.Tabs; t.FormPages.ForEach(p => p.Icon = string.Empty); }));
        Assert.Contains("bootstrap-icons", Html(t => { AddPages(t); t.Designs.Add.MultiPage = MultiPageMode.Tabs; t.FormPages[1].Icon = "briefcase"; }));
        // An icon on pages that are not split does not add the font.
        Assert.DoesNotContain("bootstrap-icons", Html(t => { AddPages(t); t.FormPages[1].Icon = "briefcase"; }));
    }

    [Fact]
    public void TheListPageBootsTheRuntimeOnlyForPagesOpenedInAContainer()
    {
        string List(Action<TableInfo> configure) => Generate(People(configure))["Pages/PeoplePages.cs"];

        var modal = List(t => { AddPages(t); t.Designs.Add.MultiPage = MultiPageMode.Tabs; t.Designs.Add.Container = PageContainer.Modal; });
        Assert.Contains("PageKit.Containers(Route, true, false)", modal);
        Assert.Contains("body.Append(FormPages.Boot);", modal);

        var offcanvas = List(t => { AddPages(t); t.Designs.View.MultiPage = MultiPageMode.Accordion; t.Designs.View.Container = PageContainer.Offcanvas; });
        Assert.Contains("body.Append(FormPages.Boot);", offcanvas);

        var page = List(t => { AddPages(t); t.Designs.Add.MultiPage = MultiPageMode.Tabs; });
        Assert.DoesNotContain("FormPages.Boot", page);

        // A modal page that is not split does not need it.
        var plainModal = List(t => { AddPages(t); t.Designs.Edit.Container = PageContainer.Modal; });
        Assert.DoesNotContain("FormPages.Boot", plainModal);
    }

    [Fact]
    public void LocalizationLeavesPageTextsAsProjectTextAndAddsNoUiStrings()
    {
        var plain = Generate(People(t => { AddPages(t); t.Designs.Add.MultiPage = MultiPageMode.Tabs; }));
        var project = People(t => { AddPages(t); t.Designs.Add.MultiPage = MultiPageMode.Tabs; });
        project.Localization.Enabled = true;
        project.Localization.DefaultLanguage = "en";
        project.Localization.Languages.Add("de");
        var localized = Generate(project);

        Assert.Contains("new FormPages.Page(0, \"Personal\", \"Personal\"", localized["Pages/PeoplePages.cs"]);
        Assert.DoesNotContain("Previous", localized["Localization/strings.en.json"]);
        Assert.DoesNotContain("Step {0}", localized["Localization/strings.en.json"]);
        Assert.Equal(plain["Web/FormPages.cs"], localized["Web/FormPages.cs"]);
    }

    // ---- the generated site compiles and behaves ------------------------------------------------

    [Theory]
    [InlineData("plain")]
    [InlineData("secure")]
    [InlineData("localized")]
    public async Task ASiteWithEveryModeBuildsWithoutWarnings(string sample)
    {
        var project = CustomizationSamples.Create(sample);
        project.Name = "Paged" + sample;
        CustomizationSamples.Customize(project);
        var products = project.Tables.Single(t => t.Name == "products");
        SplitIntoPages(products);
        products.Designs.Add.MultiPage = MultiPageMode.Tabs;
        products.Designs.Add.Container = PageContainer.Modal;
        products.Designs.Add.FormLayout = FormLayout.TwoColumns;
        products.Designs.Copy.MultiPage = MultiPageMode.Pills;
        products.Designs.Copy.Container = PageContainer.Offcanvas;
        products.Designs.Edit.MultiPage = MultiPageMode.Wizard;
        products.Designs.Edit.FormLayout = FormLayout.Horizontal;
        products.Designs.Edit.Buttons = ButtonPlacement.Both;
        products.Designs.Edit.ConfirmBeforeSave = true;
        products.Designs.View.MultiPage = MultiPageMode.Accordion;
        products.Designs.View.Container = PageContainer.Modal;

        var categories = project.Tables.Single(t => t.Name == "categories");
        SplitIntoPages(categories);
        categories.Designs.Add.MultiPage = MultiPageMode.Wizard;
        categories.Designs.Edit.MultiPage = MultiPageMode.Accordion;
        categories.Designs.View.MultiPage = MultiPageMode.Tabs;

        // A composite key table and a keyless table.
        foreach (var name in new[] { "order_lines", "codes" })
        {
            var table = project.Tables.Single(t => t.Name == name);
            SplitIntoPages(table);
            table.Designs.Add.MultiPage = MultiPageMode.Pills;
            table.Designs.Edit.MultiPage = MultiPageMode.Tabs;
            table.Designs.Copy.MultiPage = MultiPageMode.Wizard;
            table.Designs.View.MultiPage = MultiPageMode.Wizard;
        }

        var root = Path.Combine(Path.GetTempPath(), $"paged-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task ARunningSiteRendersTheModesAndMarksThePagesWithErrors()
    {
        var root = Path.Combine(Path.GetTempPath(), $"paged-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(root, "people.db");
        Directory.CreateDirectory(root);
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE people (id INTEGER PRIMARY KEY AUTOINCREMENT, first TEXT NOT NULL, last TEXT NOT NULL, birth DATE, city TEXT,
                    title TEXT NOT NULL, hired DATE, notes TEXT, photo BLOB, stray TEXT, "unknown" TEXT);
                INSERT INTO people (first, last, title, notes) VALUES ('Ann', 'Lee', 'Boss', 'Hello');
                """;
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var project = new Project { Name = "PagedRun", Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath } };
        var people = new TableInfo { Name = "people", Caption = "People" };
        people.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        foreach (var (name, type, required) in new (string, FieldType, bool)[]
        {
            ("first", FieldType.Text, true), ("last", FieldType.Text, true), ("birth", FieldType.Date, false), ("city", FieldType.Text, false),
            ("title", FieldType.Text, true), ("hired", FieldType.Date, false), ("notes", FieldType.Text, false), ("photo", FieldType.Binary, false),
            ("stray", FieldType.Text, false), ("unknown", FieldType.Text, false),
        })
        {
            people.Fields.Add(new FieldInfo { Name = name, Type = type, IsRequired = required });
        }

        AddPages(people);
        people.FormPages[0].Caption = "Per<son>al";
        people.FormPages[0].Description = "Who \"they\" are";
        people.FormPages[1].Icon = "briefcase";
        people.Designs.Add.MultiPage = MultiPageMode.Tabs;
        people.Designs.Add.Container = PageContainer.Modal;
        people.Designs.Copy.MultiPage = MultiPageMode.Pills;
        people.Designs.Edit.MultiPage = MultiPageMode.Wizard;
        people.Designs.View.MultiPage = MultiPageMode.Accordion;
        project.Tables.Add(people);

        Process? site = null;
        try
        {
            new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root);
            var build = Process.Start(new ProcessStartInfo("dotnet", "build --nologo -v q -nodeReuse:false")
            {
                WorkingDirectory = Path.Combine(root, "site"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var buildOutput = build.StandardOutput.ReadToEndAsync();
            await build.WaitForExitAsync();
            Assert.True(build.ExitCode == 0, await buildOutput);

            var port = FreePort();
            site = Process.Start(new ProcessStartInfo("dotnet", "run --no-build")
            {
                WorkingDirectory = Path.Combine(root, "site"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}" },
            })!;
            _ = site.StandardOutput.ReadToEndAsync();
            _ = site.StandardError.ReadToEndAsync();

            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            };
            var list = string.Empty;
            for (var attempt = 0; attempt < 60 && list.Length == 0; attempt++)
            {
                try
                {
                    list = await client.GetStringAsync("/people");
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(500);
                }
            }

            Assert.NotEqual(string.Empty, list);
            Assert.Contains("window.pkPages", list);

            // Add: tabs. Captions are encoded, the icon font is linked, panes without fields are not there.
            var add = await client.GetStringAsync("/people/add");
            Assert.Contains("data-pk-pages=\"tabs\"", add);
            Assert.Equal(3, Count(add, "role=\"tabpanel\""));
            Assert.Contains("Per&lt;son&gt;al", add);
            Assert.Contains("Who &quot;they&quot; are", add);
            Assert.Contains("bi bi-briefcase", add);
            Assert.Contains("bootstrap-icons", add);
            Assert.DoesNotContain("Empty", add);
            Assert.Contains("nav nav-tabs", add);
            Assert.Equal(1, Count(add, "<form method=\"post\""));
            Assert.Contains("data-pk-active=\"0\"", add);

            // The same page as a fragment for a modal.
            var partial = await client.GetStringAsync("/people/add?partial=1");
            Assert.StartsWith("<div data-partial=\"1\">", partial);
            Assert.Contains("data-pk-pages=\"tabs\"", partial);
            Assert.DoesNotContain("<html", partial);

            // Copy: pills. Edit: wizard with buttons and the step text. View: accordion with the first page open.
            Assert.Contains("nav nav-pills mb-3", await client.GetStringAsync("/people/copy/1"));
            var edit = await client.GetStringAsync("/people/edit/1");
            Assert.Contains("data-pk-pages=\"wizard\"", edit);
            Assert.Contains("data-pk-prev", edit);
            Assert.Contains("data-pk-next", edit);
            Assert.Contains("progress-bar", edit);
            Assert.Contains("data-pk-step=\"Step {0} of {1}\"", edit);
            Assert.Contains("value=\"Ann\"", edit);
            var view = await client.GetStringAsync("/people/view/1");
            Assert.Contains("data-pk-pages=\"accordion\"", view);
            Assert.Equal(3, Count(view, "class=\"accordion-item\""));
            Assert.Equal(1, Count(view, "accordion-collapse collapse pk-acc show"));
            Assert.Contains("Boss", view);

            // A server validation error on the second page: that page is shown first and marked, the first page is not.
            var token = Regex.Match(add, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
            var onWork = await Post(client, "/people/add", token, new() { ["first"] = "Bob", ["last"] = "Ray", ["title"] = "" });
            Assert.Contains("data-pk-active=\"1\"", onWork);
            Assert.Equal(1, Count(onWork, "badge text-bg-danger"));
            Assert.Equal(1, Count(onWork, "pk-page-errors"));
            Assert.Contains("title is required", onWork);
            Assert.DoesNotContain("alert alert-danger\"><ul", onWork.Replace("alert alert-danger pk-page-errors\"><ul", string.Empty));
            Assert.Contains("value=\"Bob\"", onWork);

            // Errors on two pages: the page of the first error is shown (field order), both are marked.
            var both = await Post(client, "/people/add", token, new() { ["first"] = "", ["last"] = "Ray", ["title"] = "", ["hired"] = "not a date" });
            Assert.Contains("data-pk-active=\"0\"", both);
            Assert.Equal(2, Count(both, "badge text-bg-danger"));
            Assert.Equal(2, Count(both, "pk-page-errors"));

            // A valid post saves exactly as before.
            var saved = await Post(client, "/people/add", token, new() { ["first"] = "Cy", ["last"] = "Doe", ["title"] = "Dev" });
            Assert.Equal(string.Empty, saved);
            Assert.Contains("Cy", await client.GetStringAsync("/people?q=Cy"));
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

    /// <summary>Posts a form; returns the page when the server answered with one (a validation error) and an empty string for a redirect.</summary>
    private static async Task<string> Post(HttpClient client, string url, string token, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = token;
        var response = await client.PostAsync(url, new FormUrlEncodedContent(values));
        return response.StatusCode == HttpStatusCode.Redirect ? string.Empty : await response.Content.ReadAsStringAsync();
    }

    /// <summary>Gives the first fields of a table to three pages (and the rest to none, so they land on the first page).</summary>
    private static void SplitIntoPages(TableInfo table)
    {
        table.FormPages.Clear();
        table.FormPages.Add(new FormPageDef { Name = "One", Caption = "Page one", Icon = "star", Description = "First" });
        table.FormPages.Add(new FormPageDef { Name = "Two", Caption = "Page \"two\"" });
        table.FormPages.Add(new FormPageDef { Name = "Three" });
        var i = 0;
        foreach (var field in table.Fields)
        {
            field.Ui.FormPage = (i++ % 4) switch { 1 => "Two", 2 => "Three", 3 => "Unknown", _ => string.Empty };
        }
    }

    private static void AddPages(TableInfo table)
    {
        table.FormPages.Add(new FormPageDef { Name = "Personal", Caption = "Personal" });
        table.FormPages.Add(new FormPageDef { Name = "Work" });
        table.FormPages.Add(new FormPageDef { Name = "Notes", Description = "Free text" });
        table.FormPages.Add(new FormPageDef { Name = "Empty" });
        foreach (var field in table.Fields)
        {
            field.Ui.FormPage = field.Name switch
            {
                "first" or "last" or "birth" or "city" => "Personal",
                "title" or "hired" => "Work",
                "notes" or "photo" => "Notes",
                "unknown" => "No such page",
                _ => string.Empty,
            };
        }
    }

    private static Project People(Action<TableInfo> configure)
    {
        var project = new Project { Name = "hr", ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2") };
        project.Database.Path = "hr.db";
        project.Tables.Add(PeopleTable(configure));
        return project;
    }

    private static TableInfo PeopleTable(Action<TableInfo> configure)
    {
        var table = new TableInfo { Name = "people" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "first", Type = FieldType.Text, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "last", Type = FieldType.Text, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "birth", Type = FieldType.Date });
        table.Fields.Add(new FieldInfo { Name = "city", Type = FieldType.Text });
        table.Fields.Add(new FieldInfo { Name = "title", Type = FieldType.Text, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "hired", Type = FieldType.Date });
        table.Fields.Add(new FieldInfo { Name = "notes", Type = FieldType.Text });
        table.Fields.Add(new FieldInfo { Name = "photo", Type = FieldType.Binary });
        table.Fields.Add(new FieldInfo { Name = "stray", Type = FieldType.Text });
        table.Fields.Add(new FieldInfo { Name = "unknown", Type = FieldType.Text });
        configure(table);
        return table;
    }

    private static Project TwoTables(bool paged)
    {
        var project = People(t =>
        {
            t.Designs.Add.Title = "New person";
            if (paged)
            {
                AddPages(t);
                t.Designs.Add.MultiPage = MultiPageMode.Tabs;
            }
        });
        var others = new TableInfo { Name = "others" };
        others.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        others.Fields.Add(new FieldInfo { Name = "label", Type = FieldType.Text });
        project.Tables.Add(others);
        return project;
    }

    private static void AssertSame(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (name, text) in expected)
        {
            Assert.True(text == actual[name], $"{name} differs");
        }
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "missing " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        return to < 0 ? text[from..] : text[from..to];
    }

    private static int Count(string text, string part) => text.Split(part).Length - 1;

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
        var output = Path.Combine(Path.GetTempPath(), $"paged-{Guid.NewGuid():N}");
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
}
