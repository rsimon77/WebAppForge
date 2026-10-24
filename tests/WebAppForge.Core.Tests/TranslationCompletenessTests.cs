using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>
/// Every text a visitor reads goes through the translation store. The master/detail sample, with page designs, field settings,
/// menu groups, a multi-page form, a report, the audit trail and import/export, runs with a pseudo-language ("qps") whose
/// translation of every listed text is the English text in ⟦ ⟧. The pages are requested in that language; any English word of
/// the listed texts left outside the brackets is a text that bypasses the store. A Hungarian visitor gets the Hungarian texts.
/// </summary>
public sealed class TranslationCompletenessTests : IAsyncLifetime
{
    private const string Pseudo = "qps";

    /// <summary>Hungarian texts set by hand for the texts the Hungarian test reads.</summary>
    private static readonly Dictionary<string, string> Hungarian = new()
    {
        [LocalizationKeys.SiteTitle] = "Rendelések",
        [LocalizationKeys.Table("customers")] = "Ügyfelek",
        [LocalizationKeys.Field("customers", "name")] = "Név",
        [LocalizationKeys.FieldUi("customers", "name", "help")] = "Az ügyfél teljes neve.",
        [LocalizationKeys.FieldOption("customers", "region", "N")] = "Észak",
        [LocalizationKeys.MenuGroup("Sales")] = "Értékesítés",
        [LocalizationKeys.Page("customers", "list", "title")] = "Ügyfeleink",
        [LocalizationKeys.SiteFooter] = "Minden jog fenntartva.",
        ["ui.add"] = "Új",
        ["ui.save"] = "Mentés",
    };

    /// <summary>Words a page may show outside the brackets: data values of the sample, the language codes and the file formats.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ann", "Bob", "Cy", "Boss", "Sub", "Sub2", "Tools", "Empty", "Hammer", "Saw", "Drill",
        "en", "qps", "hu", "CSV", "Excel", "JSON", "XML", "Word",
    };

    private readonly string _root = MdSupport.TempFolder();
    private Process? _site;
    private HttpClient _client = null!;
    private Project _project = null!;

    private string DbPath => Path.Combine(_root, "md.db");

    internal static Project RichProject(string dbPath)
    {
        var project = MdSupport.SampleProject(dbPath);
        project.AspNet.Title = "Order desk";
        project.Meta.FooterText = "All rights reserved.";
        project.Localization.Enabled = true;
        project.Localization.DefaultLanguage = "en";
        project.Localization.Languages.AddRange([Pseudo, "hu"]);

        var customers = project.Tables.Single(t => t.Name == "customers");
        customers.Ui.MenuGroup = "Sales";
        project.Tables.Single(t => t.Name == "orders").Ui.MenuGroup = "Sales";
        var name = customers.Fields.Single(f => f.Name == "name");
        name.Ui.HelpText = "The full name of the customer.";
        name.Ui.Placeholder = "First and last name";
        name.Ui.Tooltip = "Customer name";
        name.Ui.RequiredMessage = "Please enter a name.";
        name.Ui.MaxLength = 40;
        name.Ui.Pattern = "[A-Za-z ]+";
        name.Ui.PatternMessage = "Letters and spaces only.";
        name.Ui.Group = "Identity";
        name.Ui.Sortable = true;
        name.Ui.FormPage = "Main";
        var region = customers.Fields.Single(f => f.Name == "region");
        region.Ui.Control = EditControl.Select;
        region.Ui.Options.Add(new FieldOption { Value = "N", Text = "North" });
        region.Ui.Options.Add(new FieldOption { Value = "S", Text = "South" });
        region.Ui.BadgeRules.Add(new ValueRule { Value = "S", Style = "info", Text = "Southern" });
        region.Ui.Group = "Location";
        region.Ui.FormPage = "More";
        customers.FormPages.Add(new FormPageDef { Name = "Main", Caption = "Main data", Description = "Who the customer is." });
        customers.FormPages.Add(new FormPageDef { Name = "More", Caption = "Where", Description = "Where the customer lives." });

        var list = customers.Designs.List;
        list.Title = "Our customers";
        list.Subtitle = "Everyone who ordered";
        list.HeaderHtml = "<p>Customer list header</p>";
        list.FooterHtml = "<p>Customer list footer</p>";
        list.AddButtonText = "New customer";
        list.EmptyText = "Nobody yet.";
        list.PageSizeOptions.AddRange([10, 20]);
        var add = customers.Designs.Add;
        add.Title = "Add a customer";
        add.SubmitText = "Create";
        add.CancelText = "Back";
        add.SuccessMessage = "Customer created.";
        add.MultiPage = MultiPageMode.Wizard;
        customers.Designs.Edit.ConfirmBeforeSave = true;
        customers.Designs.Edit.SuccessMessage = "Customer saved.";

        project.Tables.Single(t => t.Name == "categories").Details[0].Caption = "Products of this category";

        project.Audit.Enabled = true;
        customers.Audit.Enabled = true;
        project.ImportExport.Enabled = true;

        project.Reports.Add(new ReportInfo
        {
            Name = "orders_by_customer",
            Caption = "Orders by customer",
            Description = "How many orders each customer placed.",
            MenuGroup = "Sales",
            SourceTable = "orders",
            Groups = { new ReportGroup { Field = "customer_name" } },
            Aggregates = { new ReportAggregate { Function = AggregateFunction.Count, Caption = "Number of orders" } },
        });
        return project;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _project = RichProject(DbPath);
        foreach (var source in LocalizationKeys.Enumerate(_project))
        {
            // The header and footer HTML of a page get their text parts bracketed, so the tags stay tags.
            var html = source.Key.StartsWith("page.", StringComparison.Ordinal) && (source.Key.EndsWith(".header", StringComparison.Ordinal) || source.Key.EndsWith(".footer", StringComparison.Ordinal));
            _project.Localization.SetManual(source.Key, Pseudo, html
                ? Regex.Replace(source.Text, "(^|>)([^<>]+)(?=<|$)", m => m.Groups[1].Value + "⟦" + m.Groups[2].Value + "⟧")
                : "⟦" + source.Text + "⟧");
        }

        foreach (var (key, text) in Hungarian)
        {
            _project.Localization.SetManual(key, "hu", text);
        }

        MdSupport.GenerateText(_project, _root);
        MdSupport.Exec(DbPath, MdSupport.Seed);
        var siteFolder = Path.Combine(_root, "site");
        var build = await MdSupport.RunDotnetAsync(siteFolder, "build --nologo -v q -nodeReuse:false -warnaserror");
        Assert.True(build.ExitCode == 0, build.Output);

        var port = MdSupport.FreePort();
        _site = Process.Start(new ProcessStartInfo("dotnet", "run --no-build")
        {
            WorkingDirectory = siteFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}" },
        })!;
        _ = _site.StandardOutput.ReadToEndAsync();
        _ = _site.StandardError.ReadToEndAsync();
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                await _client.GetStringAsync("/customers");
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(500);
            }
        }

        throw new TimeoutException("The generated site did not start.");
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        if (_site is { HasExited: false })
        {
            _site.Kill(entireProcessTree: true);
            _site.WaitForExit(10000);
        }

        MdSupport.TryDelete(_root);
        return Task.CompletedTask;
    }

    private async Task<string> Get(string url, string language)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept-Language", language);
        using var response = await _client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>The texts a visitor reads: text between tags and the title, placeholder, aria-label, alt and data-confirm attributes.</summary>
    private static IEnumerable<string> VisibleTexts(string html)
    {
        html = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match m in Regex.Matches(html, "\\s(?:title|placeholder|aria-label|alt|data-confirm|data-pk-step)=\"([^\"]*)\""))
        {
            yield return WebUtility.HtmlDecode(m.Groups[1].Value);
        }

        foreach (Match m in Regex.Matches(html, "<input[^>]*type=\"(?:submit|button)\"[^>]*value=\"([^\"]*)\""))
        {
            yield return WebUtility.HtmlDecode(m.Groups[1].Value);
        }

        foreach (Match m in Regex.Matches(html, ">([^<]+)<"))
        {
            yield return WebUtility.HtmlDecode(m.Groups[1].Value);
        }
    }

    /// <summary>Removes the bracketed (translated) parts, innermost first, so a caption inside a message is removed with it.</summary>
    private static string WithoutTranslated(string text)
    {
        string previous;
        do
        {
            previous = text;
            text = Regex.Replace(text, "⟦[^⟦⟧]*⟧", " ");
        }
        while (text != previous);
        return text;
    }

    [Fact]
    public async Task EveryTextOfThePagesComesFromTheTranslationStore()
    {
        var english = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in LocalizationKeys.Enumerate(_project))
        {
            foreach (Match word in Regex.Matches(Regex.Replace(source.Text, "<[^>]*>|\\{\\d+\\}", " "), "[A-Za-z]{2,}"))
            {
                english.Add(word.Value);
            }
        }

        english.ExceptWith(Allowed);
        var pages = new[]
        {
            "/", "/customers", "/customers/add", "/customers/edit/1", "/customers/view/1", "/customers/search", "/customers/delete/1",
            "/orders", "/orders/view/1", "/categories/view/1", "/categories/delete/1", "/reports/orders-by-customer", "/audit-trail",
            "/customers/import",
        };
        var leaks = new List<string>();
        foreach (var page in pages)
        {
            var html = await Get(page, Pseudo);
            Assert.True(html.Contains('⟦'), $"{page} shows no translated text.");
            foreach (var text in VisibleTexts(html))
            {
                var rest = WithoutTranslated(text);
                var words = Regex.Matches(rest, "[A-Za-z]{2,}").Select(m => m.Value).Where(english.Contains).ToList();
                if (words.Count > 0)
                {
                    leaks.Add($"{page}: \"{text.Trim()}\" ({string.Join(", ", words)})");
                }
            }
        }

        Assert.True(leaks.Count == 0, "Texts outside the translation store:\n" + string.Join("\n", leaks.Distinct()));
    }

    [Fact]
    public async Task AHungarianVisitorReadsTheHungarianTexts()
    {
        var home = WebUtility.HtmlDecode(await Get("/", "hu"));
        Assert.Contains("Rendelések", home);
        Assert.Contains("Értékesítés", home);
        Assert.Contains("Minden jog fenntartva.", home);
        var list = WebUtility.HtmlDecode(await Get("/customers", "hu"));
        Assert.Contains("Ügyfeleink", list);
        Assert.Contains(">Név<", list);
        var add = WebUtility.HtmlDecode(await Get("/customers/add", "hu"));
        Assert.Contains(">Észak<", add);
        Assert.Contains("Az ügyfél teljes neve.", add);

        // A text without a Hungarian translation falls back to the default language.
        Assert.Contains("First and last name", add);
        Assert.Contains("Our customers", WebUtility.HtmlDecode(await Get("/customers", "en")));
    }

    [Fact]
    public void EveryProjectTextIsListedOnceUnderItsKey()
    {
        var keys = LocalizationKeys.ProjectTexts(_project).Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var expected in new[]
        {
            LocalizationKeys.SiteTitle, LocalizationKeys.SiteFooter, LocalizationKeys.MenuGroup("Sales"),
            LocalizationKeys.Page("customers", "list", "title"), LocalizationKeys.Page("customers", "list", "header"),
            LocalizationKeys.Page("customers", "list", "addButton"), LocalizationKeys.Page("customers", "list", "empty"),
            LocalizationKeys.Page("customers", "add", "submit"), LocalizationKeys.Page("customers", "add", "success"),
            LocalizationKeys.FieldUi("customers", "name", "help"), LocalizationKeys.FieldUi("customers", "name", "placeholder"),
            LocalizationKeys.FieldUi("customers", "name", "tooltip"), LocalizationKeys.FieldUi("customers", "name", "required"),
            LocalizationKeys.FieldUi("customers", "name", "pattern"), LocalizationKeys.FieldOption("customers", "region", "S"),
            LocalizationKeys.FieldBadge("customers", "region", 0), LocalizationKeys.FieldGroup("customers", "Identity"),
            LocalizationKeys.FormPage("customers", "Main", "caption"), LocalizationKeys.FormPage("customers", "Main", "description"),
            LocalizationKeys.Detail("categories", 0), LocalizationKeys.ReportDescription("orders_by_customer"),
        })
        {
            Assert.Contains(expected, keys);
        }
    }
}

/// <summary>The same project with ASP.NET Core Identity sign-in builds, and its project texts reach the Hungarian store.</summary>
public class TranslationCompletenessIdentityTests
{
    [Fact]
    public async Task TheRichProjectWithIdentityBuildsAndListsItsTexts()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var project = TranslationCompletenessTests.RichProject(Path.Combine(root, "md.db"));
            project.Security.Enabled = true;
            project.Security.UserStore = UserStore.AspNetIdentity;
            project.Localization.SetManual(LocalizationKeys.Page("customers", "list", "title"), "hu", "Ügyfeleink");
            var files = MdSupport.GenerateText(project, root);

            Assert.Contains("\"page.customers.list.title\": \"Ügyfeleink\"", files["Localization/strings.hu.json"]);
            Assert.Contains("\"ui.idUsers\"", files["Localization/strings.en.json"]);
            var build = await MdSupport.RunDotnetAsync(Path.Combine(root, "site"), "build --nologo -v q -nodeReuse:false -warnaserror");
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}
