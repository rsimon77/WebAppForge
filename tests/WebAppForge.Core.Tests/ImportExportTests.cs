using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class ImportExportModelTests
{
    private static string Roundtrip(Project project, out Project loaded)
    {
        var xml = ProjectFile.Write(project).ToString();
        loaded = ProjectFile.Read(System.Xml.Linq.XDocument.Parse(xml));
        return xml;
    }

    private static Project WithTable()
    {
        var project = new Project { Name = "Shop" };
        var table = new TableInfo { Name = "items" };
        table.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        table.Fields.Add(new FieldInfo { Name = "name", Type = FieldType.Text });
        project.Tables.Add(table);
        return project;
    }

    [Fact]
    public void DefaultSettingsLeaveNoTraceInTheProjectFile()
    {
        var project = WithTable();
        var xml = Roundtrip(project, out var loaded);

        Assert.DoesNotContain("importExport", xml);
        Assert.False(loaded.ImportExport.Enabled);
        Assert.True(loaded.ImportExport.IsDefault());
        Assert.True(loaded.Tables[0].ImportExport.IsDefault());
    }

    [Fact]
    public void EverySettingRoundTrips()
    {
        var project = WithTable();
        project.ImportExport = new ImportExportSettings
        {
            Enabled = true,
            ExportFormats = DataFormats.Csv | DataFormats.Word,
            ImportFormats = DataFormats.Xlsx,
            MaxUploadMb = 25,
            CsvDelimiter = ";",
            HeaderStyle = HeaderStyle.FieldName,
            IncludeHeader = false,
            DateFormat = "dd/MM/yyyy",
            ExportFileNamePattern = "{table}-{datetime}",
            ExportMinLevel = 2,
            ImportMinLevel = 7,
        };
        var table = project.Tables[0].ImportExport;
        table.Export = false;
        table.Import = false;
        table.ImportMode = ImportMode.Upsert;
        table.ExportMaxRows = 500;
        table.ExcludedFields.AddRange(["name", "id"]);

        Roundtrip(project, out var loaded);
        var s = loaded.ImportExport;

        Assert.True(s.Enabled);
        Assert.Equal(DataFormats.Csv | DataFormats.Word, s.ExportFormats);
        Assert.Equal(DataFormats.Xlsx, s.ImportFormats);
        Assert.Equal(25, s.MaxUploadMb);
        Assert.Equal(";", s.CsvDelimiter);
        Assert.Equal(HeaderStyle.FieldName, s.HeaderStyle);
        Assert.False(s.IncludeHeader);
        Assert.Equal("dd/MM/yyyy", s.DateFormat);
        Assert.Equal("{table}-{datetime}", s.ExportFileNamePattern);
        Assert.Equal(2, s.ExportMinLevel);
        Assert.Equal(7, s.ImportMinLevel);
        var t = loaded.Tables[0].ImportExport;
        Assert.False(t.Export);
        Assert.False(t.Import);
        Assert.Equal(ImportMode.Upsert, t.ImportMode);
        Assert.Equal(500, t.ExportMaxRows);
        Assert.Equal(["name", "id"], t.ExcludedFields);
    }

    [Fact]
    public void AnOlderFileOrABadValueKeepsTheDefaults()
    {
        var document = XDocument.Parse("""
            <project name="shop" id="095F6728-DF53-4763-A372-D8F2EAC959F2">
              <table name="items"><field name="id" type="Integer" pk="true" /></table>
              <importExport enabled="true" exportFormats="Nonsense" maxUploadMb="x" headerStyle="Wrong" />
            </project>
            """);
        var project = ProjectFile.Read(document);
        var d = new ImportExportSettings();

        Assert.True(project.ImportExport.Enabled);
        Assert.Equal(d.ExportFormats, project.ImportExport.ExportFormats);
        Assert.Equal(d.MaxUploadMb, project.ImportExport.MaxUploadMb);
        Assert.Equal(HeaderStyle.Caption, project.ImportExport.HeaderStyle);
        Assert.True(project.Tables[0].ImportExport.IsDefault());
    }
}

public class ImportExportRulesTests
{
    private static Project Sample()
    {
        var project = CustomizationSamples.Create("secure");
        project.ImportExport.Enabled = true;
        return project;
    }

    private static List<string> Problems(Project project) => ImportExportRules.Problems(project);

    [Fact]
    public void ThereAreNoProblemsWhileItIsOffOrWithTheDefaults()
    {
        var project = Sample();
        project.ImportExport.Enabled = false;
        project.ImportExport.CsvDelimiter = "abc";
        Assert.Empty(Problems(project));

        project.ImportExport.Enabled = true;
        project.ImportExport.CsvDelimiter = ",";
        Assert.Empty(Problems(project));
    }

    [Fact]
    public void BadSettingsAreReported()
    {
        var project = Sample();
        var s = project.ImportExport;
        s.ExportFormats = DataFormats.None;
        s.ImportFormats = DataFormats.None;
        s.MaxUploadMb = 0;
        s.CsvDelimiter = "ab";
        s.DateFormat = "yyyy";
        s.ExportFileNamePattern = "{nope}";
        s.ImportMinLevel = -1;

        var problems = string.Join("\n", Problems(project));

        Assert.Contains("at least one export or import format", problems);
        Assert.Contains("between 1 and 100 MB", problems);
        Assert.Contains("CSV separator", problems);
        Assert.Contains("date format 'yyyy'", problems);
        Assert.Contains("unknown part {nope}", problems);
        Assert.Contains("cannot be negative", problems);
    }

    [Fact]
    public void FileNamePatternsAreChecked()
    {
        Assert.Null(ImportExportRules.FileNameProblem("{table}-{date}"));
        Assert.Null(ImportExportRules.FileNameProblem("export {datetime}"));
        Assert.NotNull(ImportExportRules.FileNameProblem(""));
        Assert.NotNull(ImportExportRules.FileNameProblem("a/b"));
        Assert.NotNull(ImportExportRules.FileNameProblem("{date"));
        Assert.NotNull(ImportExportRules.FileNameProblem("--"));
        Assert.True(ImportExportRules.IsUsableDateFormat("dd.MM.yyyy"));
        Assert.False(ImportExportRules.IsUsableDateFormat(""));
        Assert.False(ImportExportRules.IsUsableDateFormat("MM"));
    }

    [Fact]
    public void TablesAreCheckedForWhatAnImportNeeds()
    {
        var project = Sample();
        var items = project.Tables.Single(t => t.Name == "order_items");
        items.ImportExport.ExcludedFields.AddRange(["sku", "nothing"]);
        items.ImportExport.ImportMode = ImportMode.Upsert;
        items.ImportExport.ExcludedFields.Add("id");
        items.ImportExport.ExportMaxRows = -1;

        var problems = string.Join("\n", Problems(project));

        Assert.Contains("excluded field 'nothing' does not exist", problems);
        Assert.Contains("required field 'sku' is left out", problems);
        Assert.Contains("key field 'id' is left out", problems);
        Assert.Contains("cannot be negative", problems);

        var codes = project.Tables.Single(t => t.Name == "codes");
        codes.PrimaryKey!.IsPrimaryKey = false;
        codes.ImportExport.ImportMode = ImportMode.Upsert;
        Assert.Contains("needs a primary key", string.Join("\n", Problems(project)));
    }

    [Fact]
    public void TheUsersTableNeverImportsAndNeverExportsItsSecrets()
    {
        var project = Sample();
        var users = project.Tables.Single(t => t.Name == "users");
        var fields = ImportExportRules.Fields(project, users).Select(f => f.Name).ToList();

        Assert.False(ImportExportRules.CanImport(project, users));
        Assert.True(ImportExportRules.CanExport(project, users));
        Assert.Equal(["id", "username"], fields);
        var notes = project.Tables.Single(t => t.Name == "notes");
        Assert.DoesNotContain("owner", ImportExportRules.Fields(project, notes).Select(f => f.Name));
        Assert.Empty(Problems(project));
    }

    [Fact]
    public void OnlyTheFormatsThatNeedThePackageAskForIt()
    {
        var project = Sample();
        project.ImportExport.ExportFormats = DataFormats.Csv | DataFormats.Json;
        project.ImportExport.ImportFormats = DataFormats.Xml;
        Assert.False(ImportExportRules.UsesOpenXml(project));

        project.ImportExport.ImportFormats = DataFormats.Word;
        Assert.True(ImportExportRules.UsesOpenXml(project));

        project.ImportExport.Enabled = false;
        Assert.False(ImportExportRules.UsesOpenXml(project));
        Assert.False(ImportExportRules.IsUsed(project));
    }
}

public class ImportExportGenerationTests
{
    private static Dictionary<string, string> Generate(Project project)
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            return MdSupport.GenerateText(project, root);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    /// <summary>The ExchangeSpec method of a Pages file: the columns of the export and the import.</summary>
    private static string Region(string pages)
    {
        var start = pages.IndexOf("private static DataExchange.Spec ExchangeSpec()", StringComparison.Ordinal);
        return pages[start..pages.IndexOf("private static async Task<IResult> ExportPage(HttpRequest", start, StringComparison.Ordinal)];
    }

    private static Project Sample(string name, Action<ImportExportSettings>? settings = null)
    {
        var project = CustomizationSamples.Create(name);
        project.ImportExport.Enabled = true;
        settings?.Invoke(project.ImportExport);
        return project;
    }

    [Fact]
    public void NothingIsGeneratedWhileItIsOff()
    {
        var files = Generate(CustomizationSamples.Create("secure"));

        Assert.DoesNotContain("Web/DataExchange.cs", files.Keys);
        Assert.DoesNotContain("Web/OpenXmlFormats.cs", files.Keys);
        Assert.DoesNotContain(files, f => f.Value.Contains("DataExchange", StringComparison.Ordinal) || f.Value.Contains("DocumentFormat", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSiteGetsExportRoutesImportRoutesAndAToolbar()
    {
        var files = Generate(Sample("plain"));
        var pages = files["Pages/OrderItemsPages.cs"];

        Assert.Contains("app.MapGet(Route + \"/export\"", pages);
        Assert.Contains("app.MapGet(Route + \"/import\"", pages);
        Assert.Contains("app.MapPost(Route + \"/import\"", pages);
        Assert.Contains("app.MapPost(Route + \"/import/apply\"", pages);
        Assert.Contains("app.MapGet(Route + \"/import/template\"", pages);
        Assert.Contains("body.Append(DataExchange.Toolbar(request, ExchangeSpec(), true, true));", pages);
        // Search, filters and sort of the list: the export builds its WHERE clause with the list's own method.
        Assert.Contains("var where = BuildWhere(request, parameters);", pages);
        Assert.Contains("DataExchange.Export(ExchangeSpec(), format, connection, where, order, Db.Args(parameters))", pages);
        Assert.Contains("Web/DataExchange.cs", files.Keys);
        // The binary column never takes part.
        var spec = Region(pages);
        Assert.DoesNotContain("\"blob\"", spec);
        Assert.Contains("\"sku\"", spec);
    }

    [Fact]
    public void TheOpenXmlPackageComesOnlyWithExcelOrWord()
    {
        var csv = Generate(Sample("plain", s => { s.ExportFormats = DataFormats.Csv | DataFormats.Json; s.ImportFormats = DataFormats.Xml; }));
        Assert.DoesNotContain("DocumentFormat.OpenXml", csv["ShopDemo.csproj"]);
        Assert.Contains("NotSupportedException", csv["Web/OpenXmlFormats.cs"]);
        Assert.DoesNotContain("DocumentFormat", csv["Web/OpenXmlFormats.cs"]);

        var excel = Generate(Sample("plain", s => s.ExportFormats = DataFormats.Xlsx));
        Assert.Contains("DocumentFormat.OpenXml", excel["ShopDemo.csproj"]);
        Assert.Contains("SpreadsheetDocument", excel["Web/OpenXmlFormats.cs"]);
    }

    [Fact]
    public void ATableCanTakeOutOfExportOrImport()
    {
        var project = Sample("plain");
        project.Tables.Single(t => t.Name == "codes").ImportExport.Import = false;
        project.Tables.Single(t => t.Name == "audit_log").ImportExport.Export = false;
        project.Tables.Single(t => t.Name == "audit_log").ImportExport.Import = false;
        var files = Generate(project);

        Assert.DoesNotContain("/import", files["Pages/CodesPages.cs"]);
        Assert.Contains("/export", files["Pages/CodesPages.cs"]);
        Assert.DoesNotContain("DataExchange", files["Pages/AuditLogPages.cs"]);
    }

    [Fact]
    public void TheMostRowsOfAnExportLimitTheQuery()
    {
        var project = Sample("plain");
        project.Tables.Single(t => t.Name == "codes").ImportExport.ExportMaxRows = 100;
        var files = Generate(project);

        Assert.Contains("\" LIMIT 100\"", Region(files["Pages/CodesPages.cs"]));
        Assert.DoesNotContain("LIMIT", Region(files["Pages/OrderItemsPages.cs"]));

        project.Database = new DatabaseSettings { Provider = DatabaseProviders.SqlServer, ConnectionString = "Server=db;Database=shop" };
        var server = Region(Generate(project)["Pages/CodesPages.cs"]);
        Assert.Contains("\"TOP 100 \"", server);
    }

    [Fact]
    public void TheUsersTableHasNoImportAndNoSecretColumns()
    {
        var files = Generate(Sample("secure"));
        var pages = files["Pages/UsersPages.cs"];

        Assert.Contains("/export", pages);
        Assert.DoesNotContain("/import", pages);
        var spec = Region(pages);
        Assert.DoesNotContain("password_hash", spec);
        Assert.DoesNotContain("\"level\"", spec);
        // The owner of a note is set by the site, so it is no column either.
        var notes = files["Pages/NotesPages.cs"];
        var notesSpec = Region(notes);
        Assert.DoesNotContain("\"owner\"", notesSpec);
        Assert.Contains("OwnerColumn,", notesSpec);
    }

    [Fact]
    public void UserLevelsOfTheSettingsAndTheTableGuardTheRoutes()
    {
        var files = Generate(Sample("secure", s => { s.ExportMinLevel = 3; s.ImportMinLevel = 4; }));
        var notes = files["Pages/NotesPages.cs"];

        // notes: view level 1, edit level 2; the project's levels are higher and win.
        Assert.Contains("ExportPage(request, connectionString)).RequireLevel(3,", notes);
        Assert.Contains("DataExchange.Template(ExchangeSpec())).RequireLevel(4,", notes);
        var codes = files["Pages/CodesPages.cs"];
        Assert.Contains("/export\", (HttpRequest request) => ExportPage(request, connectionString)).RequireLevel(3,", codes);
        Assert.Contains("using SecureShop.Security;", codes);

        // Without User Level security the levels do not count.
        var plain = Sample("plain", s => { s.ExportMinLevel = 3; s.ImportMinLevel = 4; });
        Assert.DoesNotContain("RequireLevel", Generate(plain)["Pages/CodesPages.cs"]);
    }

    [Fact]
    public void ALocalizedSiteTranslatesEveryTextOfTheRuntime()
    {
        var project = Sample("localized");
        var files = Generate(project);
        var runtime = files["Web/DataExchange.cs"];

        Assert.Contains("using LocShop.Localization;", runtime);
        // The check of foreign keys of more than one column, and its message, is only in sites that have such a key.
        foreach (var text in UiStrings.ImportExport.Where(t => t.Key != "ui.impReference"))
        {
            Assert.Contains($"\"{text.Key}\"", runtime);
        }

        var keys = LocalizationKeys.Enumerate(project).Select(k => k.Key).ToList();
        Assert.Contains("ui.impImport", keys);
        Assert.Contains("ui.impLookup", keys);

        project.ImportExport.Enabled = false;
        Assert.DoesNotContain("ui.impImport", LocalizationKeys.Enumerate(project).Select(k => k.Key));
    }

    [Fact]
    public void AnInvalidProjectIsRefusedByTheGenerator()
    {
        var project = Sample("plain", s => s.CsvDelimiter = "ab");
        var root = MdSupport.TempFolder();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root));
            Assert.Contains("CSV separator", error.Message);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}

/// <summary>
/// A secured shop that exports and imports: items owned per user with a lookup and a secret column, parts with a text key that update
/// on import and a unique label, and a table without import. Users ann (level 1), bob and cat (level 9) sign in through the site.
/// </summary>
public class ExchangeSite : ReportSiteBase
{
    public const string Password = "Correct-horse-42";

    public const string Schema = """
        CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, pass TEXT NOT NULL, level INTEGER);
        CREATE TABLE regions (code TEXT PRIMARY KEY, title TEXT NOT NULL);
        CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL, name TEXT NOT NULL, region TEXT, qty INTEGER, price DECIMAL(10,2),
            added DATE, active BOOLEAN, note TEXT, token TEXT);
        CREATE TABLE parts (sku TEXT PRIMARY KEY, label TEXT NOT NULL UNIQUE, stock INTEGER);
        CREATE TABLE locked (id INTEGER PRIMARY KEY, v TEXT);
        INSERT INTO regions (code, title) VALUES ('N', 'North'), ('S', 'South');
        INSERT INTO items (owner, name, region, qty, price, added, active, note, token) VALUES
            ('ann', 'Ann one', 'N', 5, 10.50, '2024-05-02', 1, '=1+1', 'secret-a'),
            ('ann', 'Ann two', NULL, NULL, NULL, NULL, 0, 'line1' || char(10) || 'line2, "q"', 'secret-a2'),
            ('bob', 'Bob one', 'N', 7, 1000, '2024-05-03', 1, 'plain', 'secret-b1'),
            ('bob', 'Bob two', 'S', 1, 2.25, '2024-06-01', 0, 'x', 'secret-b2');
        INSERT INTO parts (sku, label, stock) VALUES ('A-1', 'Alpha', 3), ('B-2', 'Beta', 4);
        INSERT INTO locked (id, v) VALUES (1, 'x');
        """;

    protected override Project? Create()
    {
        MdSupport.Exec(DbPath, Schema);
        var project = ReportSupport.ProjectOn("Exchange", DbPath);
        project.Security = new SecuritySettings
        {
            Enabled = true,
            UserTable = "users",
            UserNameField = "name",
            PasswordField = "pass",
            UserLevelField = "level",
            AllowRegistration = true,
        };
        var items = project.Tables.Single(t => t.Name == "items");
        items.OwnerField = "owner";
        items.Fields.Single(f => f.Name == "token").IsSecret = true;
        items.Fields.Single(f => f.Name == "region").Ui.Lookup = new FieldLookup { Table = "regions", KeyField = "code", DisplayField = "title" };
        project.Tables.Single(t => t.Name == "parts").ImportExport.ImportMode = ImportMode.Upsert;
        project.Tables.Single(t => t.Name == "locked").ImportExport.Import = false;
        project.ImportExport = new ImportExportSettings
        {
            Enabled = true,
            ExportFormats = DataFormats.All,
            ImportFormats = DataFormats.All,
            MaxUploadMb = 1,
            ImportMinLevel = 5,
        };
        return project;
    }

    private async Task<string> TokenAsync(HttpClient client, string url)
    {
        var page = await client.GetStringAsync(url);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "no anti-forgery token on " + url);
        return match.Groups[1].Value;
    }

    /// <summary>A new client signed in as <paramref name="user"/>, registered first when needed. The level is set in the database before login.</summary>
    public async Task<HttpClient> SignInAsync(string user, int level)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = Client.BaseAddress };
        if (Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM users WHERE name = '{user}'")) == 0)
        {
            var token = await TokenAsync(client, "/register");
            using var registered = await client.PostAsync("/register", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["username"] = user, ["password"] = Password, ["confirm"] = Password,
            }));
            Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        }

        await Scalar($"UPDATE users SET level = {level} WHERE name = '{user}'");
        var loginToken = await TokenAsync(client, "/login");
        using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken, ["username"] = user, ["password"] = Password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    public async Task<long> Count(string sql) => Convert.ToInt64(await Scalar(sql));
}

public class ImportExportSiteTests : IClassFixture<ExchangeSite>
{
    private readonly ExchangeSite _site;

    public ImportExportSiteTests(ExchangeSite site) => _site = site;

    private static string Decode(byte[] bytes) => new UTF8Encoding(false).GetString(bytes);

    private static string Entry(byte[] zip, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static async Task<byte[]> ExportAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    internal static string Field(string html, string name) =>
        Regex.Match(html, $"name=\"{name}\" value=\"([^\"]+)\"").Groups[1].Value;

    /// <summary>Uploads a file to the import page of <paramref name="route"/> and returns the page that shows the mapping.</summary>
    internal static async Task<string> UploadAsync(HttpClient client, string route, string fileName, byte[] bytes, bool header = true, bool withToken = true)
    {
        var token = Regex.Match(await client.GetStringAsync(route + "/import"), "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        using var content = new MultipartFormDataContent();
        if (withToken)
        {
            content.Add(new StringContent(token), "__RequestVerificationToken");
        }

        if (header)
        {
            content.Add(new StringContent("true"), "header");
        }

        content.Add(new ByteArrayContent(bytes), "file", fileName);
        using var response = await client.PostAsync(route + "/import", content);
        return (int)response.StatusCode + "|" + await response.Content.ReadAsStringAsync();
    }

    /// <summary>Posts the mapping of a preview page as it is shown (or with <paramref name="map"/> on top) and returns the next page.</summary>
    internal static async Task<string> ApplyAsync(HttpClient client, string route, string preview, string action, bool skip = false, Dictionary<int, int>? map = null)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(preview, "__RequestVerificationToken"),
            ["token"] = Field(preview, "token"),
            ["action"] = action,
        };
        if (skip)
        {
            form["skip"] = "true";
        }

        foreach (Match select in Regex.Matches(preview, "<select class=\"form-select form-select-sm\" name=\"map(\\d+)\">(.*?)</select>", RegexOptions.Singleline))
        {
            var index = int.Parse(select.Groups[1].Value);
            var chosen = Regex.Match(select.Groups[2].Value, "<option value=\"(-?\\d+)\" selected>");
            form["map" + index] = map is not null && map.TryGetValue(index, out var over) ? over.ToString() : chosen.Success ? chosen.Groups[1].Value : "-1";
        }

        using var response = await client.PostAsync(route + "/import/apply", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    internal static string Page(string uploaded)
    {
        Assert.StartsWith("200|", uploaded);
        return uploaded[4..];
    }

    internal static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), "\\s+", " ");

    internal static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    [Theory]
    [InlineData("Csv", ".csv")]
    [InlineData("Json", ".json")]
    [InlineData("Xml", ".xml")]
    [InlineData("Xlsx", ".xlsx")]
    [InlineData("Word", ".docx")]
    public async Task EveryFormatExportsOnlyTheRowsAndFieldsTheUserMaySee(string format, string extension)
    {
        using var ann = await _site.SignInAsync("ann", 1);
        using var response = await ann.GetAsync("/items/export?format=" + format);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"items-{DateTime.Now:yyyyMMdd}{extension}", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        string text;
        switch (format)
        {
            case "Csv":
                Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
                text = Decode(bytes[3..]);
                Assert.StartsWith("id,name,region,qty,price,added,active,note\r\n", text);
                // A cell that would run as a formula in a spreadsheet gets an apostrophe; quotes and line breaks are kept.
                Assert.Contains("'=1+1", text);
                Assert.Contains("\"line1\nline2, \"\"q\"\"\"", text);
                Assert.Contains("Ann one,N,5,10.5,2024-05-02,true,'=1+1", text);
                break;
            case "Json":
                text = Decode(bytes);
                using (var document = JsonDocument.Parse(bytes))
                {
                    var rows = document.RootElement.EnumerateArray().ToList();
                    Assert.Equal(2, rows.Count);
                    Assert.Equal(JsonValueKind.Number, rows[0].GetProperty("qty").ValueKind);
                    Assert.Equal(10.5m, rows[0].GetProperty("price").GetDecimal());
                    Assert.Equal(JsonValueKind.True, rows[0].GetProperty("active").ValueKind);
                    Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("qty").ValueKind);
                    Assert.Equal("2024-05-02", rows[0].GetProperty("added").GetString());
                }

                break;
            case "Xml":
                text = Decode(bytes);
                var xml = XDocument.Parse(text.TrimStart('﻿'));
                Assert.Equal(2, xml.Root!.Elements("row").Count());
                Assert.Equal("Ann one", xml.Root.Elements("row").First().Element("name")!.Value);
                break;
            case "Xlsx":
                Assert.Equal((byte)'P', bytes[0]);
                text = Entry(bytes, "xl/worksheets/sheet1.xml");
                Assert.Contains("Ann one", text);
                break;
            default:
                text = Entry(bytes, "word/document.xml");
                Assert.Contains("Ann one", text);
                break;
        }

        Assert.Contains("Ann", text);
        Assert.DoesNotContain("Bob", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("owner", text);
    }

    [Fact]
    public async Task AnExportKeepsTheSearchOfTheListAndRefusesWhatIsNotOffered()
    {
        using var ann = await _site.SignInAsync("ann", 1);

        var searched = Decode(await ExportAsync(ann, "/items/export?format=csv&q=one"));
        Assert.Contains("Ann one", searched);
        Assert.DoesNotContain("Ann two", searched);
        var byField = Decode(await ExportAsync(ann, "/items/export?format=csv&f_name=two"));
        Assert.Contains("Ann two", byField);
        Assert.DoesNotContain("Ann one", byField);

        using var wrong = await ann.GetAsync("/items/export?format=exe");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var none = await ann.GetAsync("/items/export");
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);

        // The list page offers the exports with the same search.
        var list = await ann.GetStringAsync("/items?q=one");
        Assert.Contains("/items/export?format=Csv&amp;q=one", list);
        Assert.Contains("href=\"/items/import\"", list);
    }

    [Fact]
    public async Task AnImportNeedsTheLevelAndATableThatHasOne()
    {
        using var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = _site.Client.BaseAddress };
        using var redirected = await anonymous.GetAsync("/items/import");
        Assert.Equal(HttpStatusCode.Redirect, redirected.StatusCode);

        using var ann = await _site.SignInAsync("ann", 1);
        using var denied = await ann.GetAsync("/items/import");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var exports = await ann.GetAsync("/items/export?format=csv");
        Assert.Equal(HttpStatusCode.OK, exports.StatusCode);

        using var bob = await _site.SignInAsync("bob", 9);
        using var allowed = await bob.GetAsync("/items/import");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var noImport = await bob.GetAsync("/locked/import");
        Assert.Equal(HttpStatusCode.NotFound, noImport.StatusCode);
        using var users = await bob.GetAsync("/users/import");
        Assert.Equal(HttpStatusCode.NotFound, users.StatusCode);
        var usersBytes = await ExportAsync(bob, "/users/export?format=csv");
        var usersCsv = Decode(usersBytes[3..]);
        Assert.StartsWith("id,name\r\n", usersCsv);
        Assert.DoesNotContain("pass", usersCsv);
    }

    [Theory]
    [InlineData("Csv", ".csv")]
    [InlineData("Json", ".json")]
    [InlineData("Xml", ".xml")]
    [InlineData("Xlsx", ".xlsx")]
    [InlineData("Word", ".docx")]
    public async Task AnExportedFileImportsBackAsTheRowsOfTheUser(string format, string extension)
    {
        using var bob = await _site.SignInAsync("bob", 9);
        var exported = await ExportAsync(bob, "/items/export?format=" + format);
        await _site.Scalar("DELETE FROM items WHERE owner = 'bob'");

        var preview = Page(await UploadAsync(bob, "/items", "bob-items" + extension, exported));
        Assert.Contains("2 rows are valid and 0 rows have errors.", Text(preview));
        var done = Text(await ApplyAsync(bob, "/items", preview, "import"));

        Assert.Contains("2 rows added, 0 rows updated, 0 rows skipped.", done);
        Assert.Equal(1, await _site.Count("SELECT COUNT(*) FROM items WHERE owner = 'bob' AND name = 'Bob one' AND region = 'N' AND qty = 7 AND price = 1000 AND substr(added, 1, 10) = '2024-05-03' AND active = 1 AND note = 'plain' AND token IS NULL"));
        Assert.Equal(1, await _site.Count("SELECT COUNT(*) FROM items WHERE owner = 'bob' AND name = 'Bob two' AND region = 'S' AND qty = 1 AND price = 2.25 AND substr(added, 1, 10) = '2024-06-01' AND active = 0 AND token IS NULL"));
        Assert.Equal(2, await _site.Count("SELECT COUNT(*) FROM items WHERE owner = 'ann'"));
    }

    [Fact]
    public async Task ARowIsAlwaysOwnedByTheUserWhoImportsIt()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        try
        {
            // The file names an owner and a secret; neither is a field of the import.
            var csv = Csv("name,owner,token,qty\r\nSpoof,ann,hacked,3\r\n");
            var preview = Page(await UploadAsync(bob, "/items", "spoof.csv", csv));
            Assert.DoesNotContain("<td>owner</td>", preview);
            Assert.DoesNotContain("<td>token</td>", preview);
            var done = Text(await ApplyAsync(bob, "/items", preview, "import"));

            Assert.Contains("1 rows added", done);
            Assert.Equal(1, await _site.Count("SELECT COUNT(*) FROM items WHERE name = 'Spoof' AND owner = 'bob' AND token IS NULL AND qty = 3"));
        }
        finally
        {
            await _site.Scalar("DELETE FROM items WHERE name = 'Spoof'");
        }
    }

    [Fact]
    public async Task InvalidRowsAreReportedWithTheirRowAndSkippedOnRequest()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        const string file = "name,region,qty,price,added,active,note\r\n"
            + "Good,N,1,1.5,2024-01-01,yes,ok\r\n"
            + ",N,2,2,2024-01-01,no,missing name\r\n"
            + "Badnum,N,abc,2,2024-01-01,no,x\r\n"
            + "Badlookup,Z,2,2,2024-01-01,no,x\r\n"
            + "Badbool,N,2,2,2024-01-01,maybe,x\r\n"
            + "Baddate,N,2,2,notadate,no,x\r\n"
            + "Display,North,3,3,2024-02-02,1,display text maps to the code\r\n";
        try
        {
            var preview = Page(await UploadAsync(bob, "/items", "mixed.csv", Csv(file)));
            var text = Text(preview);

            Assert.Contains("7 rows found in mixed.csv.", text);
            Assert.Contains("2 rows are valid and 5 rows have errors.", text);
            Assert.Contains("Row 3: name is required.", text);
            Assert.Contains("Row 4: qty must be a number.", text);
            Assert.Contains("Row 5: region: 'Z' is not an allowed value.", text);
            Assert.Contains("Row 6: active must be yes or no.", text);
            Assert.Contains("Row 7: added is not a valid date.", text);

            var refused = Text(await ApplyAsync(bob, "/items", preview, "import"));
            Assert.Contains("Nothing was imported", refused);
            Assert.Equal(0, await _site.Count("SELECT COUNT(*) FROM items WHERE name IN ('Good', 'Display')"));

            var again = Page(await UploadAsync(bob, "/items", "mixed.csv", Csv(file)));
            var done = Text(await ApplyAsync(bob, "/items", again, "import", skip: true));
            Assert.Contains("2 rows added, 0 rows updated, 5 rows skipped.", done);
            Assert.Contains("Row 3:", done);
            Assert.Equal(1, await _site.Count("SELECT COUNT(*) FROM items WHERE name = 'Good' AND region = 'N' AND active = 1 AND owner = 'bob'"));
            Assert.Equal(1, await _site.Count("SELECT COUNT(*) FROM items WHERE name = 'Display' AND region = 'N' AND active = 1"));
        }
        finally
        {
            await _site.Scalar("DELETE FROM items WHERE name IN ('Good', 'Display')");
        }
    }

    [Fact]
    public async Task TheMappingCanBeChangedBeforeTheImport()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        try
        {
            // The headers match nothing, so nothing is mapped until the user maps the columns.
            var preview = Page(await UploadAsync(bob, "/items", "plain.csv", Csv("a,b\r\nMapped,4\r\n")));
            Assert.Contains("Map at least one column.", Text(preview));

            // Fields: id=0, name=1, region=2, qty=3, ...; the file columns: a=0, b=1.
            var checkedPage = Text(await ApplyAsync(bob, "/items", preview, "check", map: new Dictionary<int, int> { [1] = 0, [3] = 1 }));
            Assert.Contains("1 rows are valid and 0 rows have errors.", checkedPage);
            Assert.Equal(0, await _site.Count("SELECT COUNT(*) FROM items WHERE name = 'Mapped'"));
        }
        finally
        {
            await _site.Scalar("DELETE FROM items WHERE name = 'Mapped'");
        }
    }

    [Fact]
    public async Task ADatabaseErrorRollsTheWholeImportBack()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        // Row 4 repeats the unique label Alpha at insert time, after row 2 updated Beta and row 3 added Gamma.
        var csv = Csv("sku,label,stock\r\nB-2,Beta,40\r\nC-3,Gamma,5\r\nD-4,Alpha,6\r\n");
        var preview = Page(await UploadAsync(bob, "/parts", "parts.csv", csv));
        var done = Text(await ApplyAsync(bob, "/parts", preview, "import"));

        Assert.Contains("The import failed at row 4 and was rolled back.", done);
        Assert.Equal(4, await _site.Count("SELECT stock FROM parts WHERE sku = 'B-2'"));
        Assert.Equal(0, await _site.Count("SELECT COUNT(*) FROM parts WHERE sku IN ('C-3', 'D-4')"));
        Assert.Equal(2, await _site.Count("SELECT COUNT(*) FROM parts"));
    }

    [Fact]
    public async Task AnUpsertUpdatesTheRowsWithAKnownKeyAndAddsTheRest()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        try
        {
            var csv = Csv("sku,label,stock\r\nA-1,Alpha,30\r\nE-5,Echo,1\r\nF-6,Foxtrot,1\r\nF-6,Foxtrot,2\r\n");
            var preview = Page(await UploadAsync(bob, "/parts", "parts.csv", csv));
            Assert.Contains("3 rows are valid and 1 rows have errors.", Text(preview));
            Assert.Contains("Row 5: The key appears more than once in the file.", Text(preview));

            var done = Text(await ApplyAsync(bob, "/parts", preview, "import", skip: true));
            Assert.Contains("2 rows added, 1 rows updated, 1 rows skipped.", done);
            Assert.Equal(30, await _site.Count("SELECT stock FROM parts WHERE sku = 'A-1'"));
            Assert.Equal(1, await _site.Count("SELECT stock FROM parts WHERE sku = 'F-6'"));
            Assert.Equal(1, await _site.Count("SELECT COUNT(*) FROM parts WHERE sku = 'E-5'"));

            // Where a table only adds, an auto-increment key of the file is not used: the row gets a new one.
            var existing = Page(await UploadAsync(bob, "/items", "ids.csv", Csv("id,name\r\n1,Clash\r\n")));
            Assert.Contains("1 rows are valid", Text(existing));
        }
        finally
        {
            await _site.Scalar("DELETE FROM parts WHERE sku IN ('E-5', 'F-6')");
            await _site.Scalar("UPDATE parts SET stock = 3 WHERE sku = 'A-1'");
        }
    }

    [Fact]
    public async Task AnUploadIsCheckedForSizeTypeTokenAndUser()
    {
        using var bob = await _site.SignInAsync("bob", 9);

        var large = Page(await UploadAsync(bob, "/items", "big.csv", Csv("name\r\n" + new string('x', 1_200_000))));
        Assert.Contains("larger than 1 MB", Text(large));
        var wrongType = Page(await UploadAsync(bob, "/items", "tool.exe", Csv("name\r\nx\r\n")));
        Assert.Contains("This type of file cannot be imported.", Text(wrongType));
        var empty = Page(await UploadAsync(bob, "/items", "empty.csv", Csv("name\r\n")));
        Assert.Contains("The file has no rows to import.", Text(empty));
        var broken = Page(await UploadAsync(bob, "/items", "broken.xlsx", Csv("this is not a zip")));
        Assert.Contains("could not be read as Excel", Text(broken));
        var bad = Page(await UploadAsync(bob, "/items", "bad.json", Csv("{\"rows\": 1}")));
        Assert.Contains("could not be read as JSON", Text(bad));

        Assert.StartsWith("400|", await UploadAsync(bob, "/items", "x.csv", Csv("name\r\nx\r\n"), withToken: false));

        // The file of an upload is only for the user who uploaded it.
        var preview = Page(await UploadAsync(bob, "/items", "mine.csv", Csv("name,qty\r\nMine,1\r\n")));
        using var cat = await _site.SignInAsync("cat", 9);
        var apply = Page(await UploadAsync(cat, "/items", "cat.csv", Csv("name\r\nCat\r\n")));
        var stolen = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(apply, "__RequestVerificationToken"),
            ["token"] = Field(preview, "token"),
            ["action"] = "import",
        };
        using var response = await cat.PostAsync("/items/import/apply", new FormUrlEncodedContent(stolen));
        Assert.Contains("no longer available", Text(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, await _site.Count("SELECT COUNT(*) FROM items WHERE name = 'Mine'"));
    }

    [Fact]
    public async Task TheTemplateIsACsvFileWithTheImportColumns()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        var bytes = await ExportAsync(bob, "/items/import/template");

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("id,name,region,qty,price,added,active,note\r\n", Decode(bytes[3..]));
    }
}
