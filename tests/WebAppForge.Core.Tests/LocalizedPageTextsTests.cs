using System.Diagnostics;
using System.Net;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>
/// The fixed texts of designed pages, multi-page forms, detail grids and delete notes follow the visitor's language. The master/detail
/// sample runs with English and German; German texts are set by hand for every text this test reads.
/// </summary>
public sealed class LocalizedPageTextsTests : IAsyncLifetime
{
    private static readonly Dictionary<string, string> German = new()
    {
        ["ui.pageConfirmSave"] = "Änderungen speichern?",
        ["ui.pageClose"] = "Schließen",
        ["ui.pageRowsPerPage"] = "Zeilen pro Seite",
        ["ui.formPrevious"] = "Zurück",
        ["ui.formNext"] = "Weiter",
        ["ui.formStep"] = "Schritt {0} von {1}",
        ["ui.formProgress"] = "Fortschritt",
        ["ui.detailNoRecords"] = "Keine Datensätze.",
        ["ui.detailBlocked"] = "Löschen nicht möglich: {0} verknüpfte Datensätze in {1}.",
        ["ui.detailAlsoDeletes"] = "Dabei werden auch {0} Datensätze in {1} gelöscht.",
    };

    private readonly string _root = MdSupport.TempFolder();
    private Process? _site;
    private HttpClient _client = null!;

    private string DbPath => Path.Combine(_root, "md.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var project = MdSupport.SampleProject(DbPath);
        project.Localization.Enabled = true;
        project.Localization.DefaultLanguage = "en";
        project.Localization.Languages.Add("de");
        foreach (var (key, text) in German)
        {
            project.Localization.SetManual(key, "de", text);
        }

        var customers = project.Tables.Single(t => t.Name == "customers");
        customers.FormPages.Add(new FormPageDef { Name = "Main", Caption = "Main" });
        customers.FormPages.Add(new FormPageDef { Name = "More", Caption = "More" });
        customers.Fields.Single(f => f.Name == "name").Ui.FormPage = "Main";
        customers.Fields.Single(f => f.Name == "region").Ui.FormPage = "More";
        customers.Designs.Add.MultiPage = MultiPageMode.Wizard;
        customers.Designs.Edit.ConfirmBeforeSave = true;
        customers.Designs.List.PageSizeOptions.AddRange([10, 20]);

        MdSupport.GenerateText(project, _root);
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheFixedTextsOfThePagesAreInTheVisitorsLanguage()
    {
        var list = await Get("/customers", "de");
        Assert.Contains("Zeilen pro Seite", list);
        var categories = await Get("/categories", "de");
        Assert.Contains("aria-label=\"Schließen\"", categories);

        var add = await Get("/customers/add", "de");
        Assert.Contains(">Zurück<", add);
        Assert.Contains(">Weiter<", add);
        Assert.Contains("data-pk-step=\"Schritt {0} von {1}\"", add);
        Assert.Contains("aria-label=\"Fortschritt\"", add);
        Assert.Contains("data-confirm=\"Änderungen speichern?\"", await Get("/customers/edit/1", "de"));

        Assert.Contains("Keine Datensätze.", await Get("/categories/view/2", "de"));
        Assert.Contains("Löschen nicht möglich: 3 verknüpfte Datensätze in products.", await Get("/categories/delete/1", "de"));
        Assert.Contains("Dabei werden auch 2 Datensätze in order_lines gelöscht.", await Get("/orders/delete/1", "de"));
    }

    [Fact]
    public async Task EnglishVisitorsSeeTheEnglishTexts()
    {
        Assert.Contains("Rows per page", await Get("/customers", "en"));
        var add = await Get("/customers/add", "en");
        Assert.Contains(">Previous<", add);
        Assert.Contains("data-pk-step=\"Step {0} of {1}\"", add);
        Assert.Contains("data-confirm=\"Save changes?\"", await Get("/customers/edit/1", "en"));
        Assert.Contains("No records.", await Get("/categories/view/2", "en"));
        Assert.Contains("Cannot delete: 3 related products record(s) exist.", await Get("/categories/delete/1", "en"));
        Assert.Contains("This will also delete 2 record(s) in order_lines.", await Get("/orders/delete/1", "en"));
    }
}

/// <summary>The "no permission" answers of the level checks are translatable; a site without localization keeps its English constants.</summary>
public class LocalizedDeniedTextsTests
{
    private static Dictionary<string, string> Generate(Project project)
    {
        var output = Path.Combine(Path.GetTempPath(), $"loc-{Guid.NewGuid():N}");
        try
        {
            new WebAppForge.Core.Generation.ProjectGenerator().Generate(project, output, "C:/pf");
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), File.ReadAllText);
        }
        finally
        {
            MdSupport.TryDelete(output);
        }
    }

    [Fact]
    public void LocalizedLevelChecksAnswerInTheVisitorsLanguage()
    {
        var files = Generate(CustomizationSamples.Create("localized"));

        var permissions = files["Security/Permissions.cs"];
        Assert.Contains("return Results.Text(Translate(deniedMessage), ", permissions);
        Assert.Contains("\"You do not have permission to view this table.\" => Strings.T(\"ui.deniedView\", message),", permissions);
        Assert.Contains("\"ui.deniedEdit\": \"You do not have permission to change this table.\"", files["Localization/strings.en.json"]);
    }

    [Fact]
    public void ASiteWithoutLocalizationKeepsTheEnglishAnswer()
    {
        var files = Generate(CustomizationSamples.Create("secure"));

        Assert.Contains("return Results.Text(deniedMessage, ", files["Security/Permissions.cs"]);
        Assert.DoesNotContain(files.Values, f => f.Contains("ui.denied", StringComparison.Ordinal));
    }
}
