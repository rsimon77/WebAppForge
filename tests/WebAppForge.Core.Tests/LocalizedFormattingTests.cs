using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Tests;

/// <summary>
/// Dates and numbers of a localized site follow the visitor's language (English and Hungarian here), while form values, the JSON
/// API and exports stay invariant, and a decimal typed with the visitor's decimal separator is saved as the same number.
/// </summary>
public sealed class LocalizedFormattingTests : IAsyncLifetime
{
    private static readonly CultureInfo Hungarian = CultureInfo.CreateSpecificCulture("hu");
    private static readonly CultureInfo English = CultureInfo.CreateSpecificCulture("en");

    private readonly string _root = MdSupport.TempFolder();
    private Process? _site;
    private HttpClient _client = null!;

    private string DbPath => Path.Combine(_root, "fmt.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        MdSupport.Exec(DbPath, """
            CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, price NUMERIC, cost NUMERIC, due DATE, stamp DATETIME);
            INSERT INTO items (id, name, price, cost, due, stamp) VALUES (1, 'Lamp', 1234.5, 99.5, '2024-07-04', '2024-07-04 10:30:00');
            """);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var project = new Project { Name = "FmtShop", Database = new DatabaseSettings { Provider = "sqlite", Path = DbPath } };
        project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={DbPath}"));
        project.Localization.Enabled = true;
        project.Localization.DefaultLanguage = "en";
        project.Localization.Languages.Add("hu");
        project.ImportExport.Enabled = true;
        var items = project.Tables.Single(t => t.Name == "items");
        items.Fields.Single(f => f.Name == "cost").Ui.Format = ViewFormat.Currency;
        items.Fields.Single(f => f.Name == "due").Ui.Format = ViewFormat.Date;

        MdSupport.GenerateText(project, _root);
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
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                await _client.GetStringAsync("/items");
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

    private async Task<HttpResponseMessage> Save(string language, string price)
    {
        using var page = new HttpRequestMessage(HttpMethod.Get, "/items/edit/1");
        page.Headers.Add("Accept-Language", language);
        using var form = await _client.SendAsync(page);
        var html = await form.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookie = string.Join("; ", form.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var post = new HttpRequestMessage(HttpMethod.Post, "/items/edit/1")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["name"] = "Lamp",
                ["price"] = price,
                ["cost"] = "99.5",
                ["due"] = "2024-07-04",
                ["stamp"] = "2024-07-04T10:30",
            }),
        };
        post.Headers.Add("Accept-Language", language);
        post.Headers.Add("Cookie", cookie);
        return await _client.SendAsync(post);
    }

    private decimal StoredPrice()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(price AS TEXT) FROM items WHERE id = 1";
        return Convert.ToDecimal(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task HungarianVisitorsSeeHungarianDatesAndNumbers()
    {
        var list = await Get("/items", "hu-HU,hu;q=0.9,en;q=0.5");
        Assert.Contains("<html lang=\"hu\"", list);
        Assert.Contains(1234.5m.ToString("#,0.############################", Hungarian), list);
        var currency = (NumberFormatInfo)Hungarian.NumberFormat.Clone();
        currency.CurrencySymbol = English.NumberFormat.CurrencySymbol;
        Assert.Contains(99.5m.ToString("C", currency), list);
        Assert.Contains(new DateTime(2024, 7, 4).ToString(Hungarian.DateTimeFormat.ShortDatePattern, Hungarian), list);

        var view = await Get("/items/view/1", "hu");
        Assert.Contains(new DateTime(2024, 7, 4, 10, 30, 0).ToString(Hungarian.DateTimeFormat.ShortDatePattern + " " + Hungarian.DateTimeFormat.ShortTimePattern, Hungarian), view);

        // The edit form keeps the invariant text the inputs need.
        var edit = await Get("/items/edit/1", "hu");
        Assert.Contains("value=\"1234.5\"", edit);
        Assert.Contains("value=\"2024-07-04\"", edit);
    }

    [Fact]
    public async Task EnglishVisitorsSeeEnglishFormats()
    {
        var list = await Get("/items", "en");
        Assert.Contains("<html lang=\"en\"", list);
        Assert.Contains("1,234.5", list);
        Assert.Contains(99.5m.ToString("C", English), list);
    }

    [Fact]
    public async Task TheApiAndTheCsvExportStayInvariant()
    {
        var json = await Get("/api/items/1", "hu");
        Assert.Contains("1234.5", json);
        Assert.DoesNotContain("1234,5", json);

        var csv = await Get("/items/export?format=csv", "hu");
        Assert.Contains("1234.5", csv);
        Assert.Contains("2024-07-04", csv);
    }

    [Fact]
    public async Task ADecimalIsReadWithTheVisitorsSeparatorAndNoGroupSeparator()
    {
        using (var saved = await Save("hu", "12,5"))
        {
            Assert.True(saved.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther, saved.StatusCode.ToString());
        }

        Assert.Equal(12.5m, StoredPrice());

        using (var saved = await Save("hu", "7.25"))
        {
            Assert.NotEqual(HttpStatusCode.OK, saved.StatusCode);
        }

        Assert.Equal(7.25m, StoredPrice());

        // "1,5" is not a decimal in English and must not be read as 15.
        using (var refused = await Save("en", "1,5"))
        {
            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        }

        Assert.Equal(7.25m, StoredPrice());
    }

    [Fact]
    public async Task AnImportReadsTheVisitorsDecimalCommaAndDatesAfterTheInvariantForms()
    {
        // A Hungarian spreadsheet saved as CSV: semicolons, a decimal comma and the Hungarian date order, next to invariant values.
        using (var hu = Visitor("hu"))
        {
            var csv = ImportExportSiteTests.Csv("name;price;due\nDesk;12,5;2026. 08. 05.\nChair;7.25;2026-08-06\n");
            var preview = ImportExportSiteTests.Page(await ImportExportSiteTests.UploadAsync(hu, "/items", "items.csv", csv));
            await ImportExportSiteTests.ApplyAsync(hu, "/items", preview, "import");
        }

        Assert.Equal(("12.5", "2026-08-05"), Imported("Desk"));
        Assert.Equal(("7.25", "2026-08-06"), Imported("Chair"));

        // In English "1,5" is no decimal (and never 15), so the whole file is refused.
        using (var en = Visitor("en"))
        {
            var csv = ImportExportSiteTests.Csv("name,price\nStool,\"1,5\"\n");
            var preview = ImportExportSiteTests.Page(await ImportExportSiteTests.UploadAsync(en, "/items", "items.csv", csv));
            await ImportExportSiteTests.ApplyAsync(en, "/items", preview, "import");
        }

        Assert.Null(Imported("Stool"));
    }

    private HttpClient Visitor(string language)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = _client.BaseAddress };
        client.DefaultRequestHeaders.Add("Accept-Language", language);
        return client;
    }

    /// <summary>Price and date of the imported row named <paramref name="name"/>, as SQLite stores them, or null when there is none.</summary>
    private (string Price, string Due)? Imported(string name)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(price AS TEXT), substr(due, 1, 10) FROM items WHERE name = $name";
        command.Parameters.AddWithValue("$name", name);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }
}
