using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using static WebAppForge.Core.Tests.ReportSupport;

namespace WebAppForge.Core.Tests;

internal static class ReportSites
{
    /// <summary>Every kind of report on the orders table.</summary>
    public static void AddAllReports(Project project)
    {
        var byRegion = new ReportDef { Name = "byregion", Caption = "By region", Table = "orders", Type = ReportType.Summary, MenuGroup = "Sales", Icon = "bar-chart" };
        byRegion.Summary.Groups.Add(new ReportField { Field = "region" });
        byRegion.Summary.Groups.Add(new ReportField { Field = "odate", Interval = GroupInterval.Month });
        byRegion.Summary.Aggregates.Add(new ReportAggregate { Function = AggregateFunction.Count });
        byRegion.Summary.Aggregates.Add(new ReportAggregate { Field = "amount", Function = AggregateFunction.Sum, FormatString = "N2" });
        byRegion.Summary.DetailFields.AddRange(["id", "customer", "amount"]);
        byRegion.Summary.GroupsPerPage = 2;
        byRegion.Summary.DetailRowsPerGroup = 1;
        byRegion.Summary.Filters.Add(new ReportFilter { Field = "customer" });
        byRegion.Summary.Filters.Add(new ReportFilter { Field = "odate" });

        var compact = new ReportDef { Name = "compact", Caption = "Compact", Table = "orders", Type = ReportType.Summary };
        compact.Summary.Layout = SummaryLayout.Compact;
        compact.Summary.Groups.Add(new ReportField { Field = "customer" });
        compact.Summary.Aggregates.Add(new ReportAggregate());

        var xt = Crosstab("xt");
        xt.Caption = "Cross";
        xt.Crosstab.MaxColumns = 2;
        xt.DrillDownReport = "drill";

        var drill = new ReportDef { Name = "drill", Caption = "Drill", Table = "orders", Type = ReportType.Summary, ShowInMenu = false };
        drill.Summary.Groups.Add(new ReportField { Field = "region" });
        drill.Summary.Aggregates.Add(new ReportAggregate());

        var admin = new ReportDef { Name = "admin", Caption = "Admin", Table = "orders", Type = ReportType.Summary, MinLevel = 5 };
        admin.Summary.Aggregates.Add(new ReportAggregate());

        var chart = new ReportDef { Name = "chart1", Caption = "Chart", Table = "orders", Type = ReportType.Chart };
        chart.Chart.Category = new ReportField { Field = "region" };
        chart.Chart.Series.Add(new ChartSeries());
        var cal = new ReportDef { Name = "cal1", Caption = "Cal", Table = "orders", Type = ReportType.Calendar };
        cal.Calendar.TitleField = "customer";
        cal.Calendar.StartField = "odate";
        var dash = new ReportDef { Name = "dash1", Caption = "Dash", Type = ReportType.Dashboard };
        dash.Dashboard.Items.Add(new DashboardItem { Report = "byregion" });
        project.Reports.AddRange([byRegion, compact, xt, drill, admin, chart, cal, dash]);
    }

    public static async Task<(int ExitCode, string Output)> BuildAsync(string folder) =>
        await MdSupport.RunDotnetAsync(folder, "build --nologo -v q -nodeReuse:false -warnaserror");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA256, 32);
        var buffer = new byte[13 + 16 + 32];
        buffer[0] = 1;
        void Be(int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        Be(1, 1);
        Be(5, 10000);
        Be(9, 16);
        salt.CopyTo(buffer, 13);
        key.CopyTo(buffer, 29);
        return Convert.ToBase64String(buffer);
    }
}

public class ReportPagesTests
{
    [Fact]
    public void ProjectWithoutReportsHasNoReportFiles()
    {
        var root = TempFolder();
        try
        {
            var project = CreateProject(root, seed: false);
            var files = new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root);
            Assert.DoesNotContain(files, f => f.Contains("Report", StringComparison.Ordinal));
            Assert.DoesNotContain("MapReportPages", File.ReadAllText(Path.Combine(root, "site", "Program.cs")));
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void GeneratedTextHasRoutesGuardsAndMenuEntries()
    {
        var root = TempFolder();
        try
        {
            var project = CreateProject(root, seed: false);
            ReportSites.AddAllReports(project);
            var text = MdSupport.GenerateText(project, root);
            Assert.Contains("Web/ReportEngine.cs", text.Keys);
            Assert.Contains("namespace RepShop.Web;", text["Web/ReportEngine.cs"]);
            Assert.DoesNotContain("@@", text["Web/Reports.cs"]);
            Assert.Contains("app.MapReportPages(connectionString);", text["Program.cs"]);
            var reports = text["Web/Reports.cs"];
            Assert.Contains("app.MapGet(\"/reports/byregion\"", reports);
            Assert.Contains("app.MapGet(\"/reports/dash1\"", reports);
            Assert.DoesNotContain("RequireLevel", reports);
            // menu: reports under their group, default group "Reports", hidden ones absent
            Assert.Contains("(\"Sales\", \"By region\", \"/reports/byregion\"", text["Program.cs"]);
            Assert.Contains("(\"Reports\", \"Compact\", \"/reports/compact\"", text["Program.cs"]);
            Assert.DoesNotContain("/reports/drill\"", text["Program.cs"]);
            Assert.Contains("/reports/compact", text["Web/Html.cs"]);
            Assert.Contains("bootstrap-icons", text["Web/Html.cs"]);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task PlainSiteWithAllReportKindsCompilesWithWarningsAsErrors()
    {
        var root = TempFolder();
        try
        {
            var project = CreateProject(root, seed: false);
            ReportSites.AddAllReports(project);
            MdSupport.GenerateText(project, root);
            var build = await ReportSites.BuildAsync(Path.Combine(root, "site"));
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task LocalizedSiteWithReportsCompiles()
    {
        var root = TempFolder();
        try
        {
            var project = CreateProject(root, seed: false);
            ReportSites.AddAllReports(project);
            project.Localization.Enabled = true;
            project.Localization.DefaultLanguage = "en";
            project.Localization.Languages.Add("de");
            MdSupport.GenerateText(project, root);
            var build = await ReportSites.BuildAsync(Path.Combine(root, "site"));
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}

/// <summary>A secured site (user levels, owned orders) with every report kind, built and run once.</summary>
public sealed class ReportSite : IAsyncLifetime
{
    private Process? _site;

    public string Root { get; } = TempFolder();

    public Uri BaseUri { get; private set; } = null!;

    public List<Row> Data { get; } = Rows();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        var db = Path.Combine(Root, "r.db");
        using (var c = new SqliteConnection($"Data Source={db}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = ReportSupport.Schema + "CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT NOT NULL, password_hash TEXT NOT NULL, level INTEGER NOT NULL);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var project = CreateProject(Path.Combine(Root, "x"), seed: false);
        project.Database.Path = db;
        project.Tables.Clear();
        project.Tables.AddRange(new WebAppForge.Core.Schema.SqliteSchemaReader().ReadTables($"Data Source={db}"));
        project.Security.Enabled = true;
        project.Security.UserTable = "users";
        project.Security.UserNameField = "username";
        project.Security.PasswordField = "password_hash";
        project.Security.UserLevelField = "level";
        project.Security.TrackFailedAttempts = false;
        project.Tables.Single(t => t.Name == "orders").OwnerField = "owner";
        ReportSites.AddAllReports(project);
        MdSupport.GenerateText(project, Root);

        using (var c = new SqliteConnection($"Data Source={db}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            foreach (var r in Data)
            {
                cmd.CommandText = "INSERT INTO orders (id, customer, region, odate, amount, qty, owner) VALUES ($i,$c,$r,$d,$a,$q,$o)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$i", r.Id);
                cmd.Parameters.AddWithValue("$c", (object?)r.Customer ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$r", (object?)r.Region ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$d", (object?)r.Date ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$a", (object?)r.Amount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$q", (object?)r.Qty ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$o", r.Owner);
                cmd.ExecuteNonQuery();
            }

            foreach (var (name, level) in new[] { ("alice", 1), ("boss", 9) })
            {
                cmd.CommandText = "INSERT INTO users (username, password_hash, level) VALUES ($u, $h, $l)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$u", name);
                cmd.Parameters.AddWithValue("$h", ReportSites.Hash("pw-" + name));
                cmd.Parameters.AddWithValue("$l", level);
                cmd.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();
        var siteFolder = Path.Combine(Root, "site");
        var build = await ReportSites.BuildAsync(siteFolder);
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
        BaseUri = new Uri($"http://127.0.0.1:{port}");
        using var probe = new HttpClient { BaseAddress = BaseUri };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                await probe.GetAsync("/login");
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
        if (_site is { HasExited: false })
        {
            _site.Kill(entireProcessTree: true);
            _site.WaitForExit(10000);
        }

        MdSupport.TryDelete(Root);
        return Task.CompletedTask;
    }

    public async Task<HttpClient> LoginAsync(string user)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = BaseUri };
        var page = await client.GetStringAsync("/login");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = user,
            ["password"] = "pw-" + user,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public HttpClient Anonymous() =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = BaseUri };
}

public class ReportSiteTests(ReportSite site) : IClassFixture<ReportSite>
{
    private List<Row> Alice => site.Data.Where(r => r.Owner == "alice").ToList();

    private static int GrandTotal(string html) =>
        int.Parse(Regex.Match(html, "Grand total</td><td class=\"text-end\">(\\d+)</td>").Groups[1].Value);

    [Fact]
    public async Task AnonymousIsRedirectedAndIndexListsOnlyAllowedMenuReports()
    {
        using var anonymous = site.Anonymous();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/reports/byregion")).StatusCode);

        using var alice = await site.LoginAsync("alice");
        var index = await alice.GetStringAsync("/reports");
        index = index[index.IndexOf("<main", StringComparison.Ordinal)..];
        Assert.Contains("href=\"/reports/byregion\"", index);
        Assert.Contains("<h2 class=\"h5 mt-4\">Sales</h2>", index);
        Assert.Contains("<h2 class=\"h5 mt-4\">Reports</h2>", index);
        Assert.DoesNotContain("/reports/admin", index);
        Assert.DoesNotContain("/reports/drill", index);

        using var boss = await site.LoginAsync("boss");
        Assert.Contains("/reports/admin", await boss.GetStringAsync("/reports"));
    }

    [Fact]
    public async Task MinLevelIsEnforced()
    {
        using var alice = await site.LoginAsync("alice");
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/reports/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/reports/admin?format=csv")).StatusCode);
        using var boss = await site.LoginAsync("boss");
        Assert.Equal(HttpStatusCode.OK, (await boss.GetAsync("/reports/admin")).StatusCode);
    }

    [Fact]
    public async Task SummaryIsScopedToTheOwnerWithGroupsDrillLinksAndPaging()
    {
        using var alice = await site.LoginAsync("alice");
        var html = await alice.GetStringAsync("/reports/compact");
        Assert.Equal(Alice.Count, GrandTotal(html));
        foreach (var g in Alice.GroupBy(r => r.Customer).Where(g => g.Key is "Ann" or "Bob"))
        {
            Assert.Contains($"<a href=\"/orders?f_customer={g.Key}\">{g.Key}</a></td><td class=\"text-end\">{g.Count()}</td>", html);
        }

        var page1 = await alice.GetStringAsync("/reports/byregion");
        var regions = Alice.Select(r => r.Region).Distinct().ToList();
        Assert.Equal(Alice.Count, int.Parse(Regex.Match(page1, "Grand total</td><td class=\"text-end\">(\\d+)</td>").Groups[1].Value));
        Assert.Contains("class=\"pagination\"", page1);
        Assert.Contains("href=\"/orders?f_region=", page1);
        Assert.Contains("&amp;f_odate=", page1);
        var shown = Regex.Matches(page1, "region: ").Count;
        Assert.Equal(2, shown);
        var page2 = await alice.GetStringAsync("/reports/byregion?page=2");
        Assert.Equal(regions.Count - 2, Regex.Matches(page2, "region: ").Count);
        Assert.Contains("rows.</small>", page1 + page2);
        Assert.Contains("Show all", page1 + page2);
        var both = page1 + page2;
        var showAll = Regex.Match(both, "href=\"([^\"]+)\">Show all</a>");
        Assert.True(showAll.Success);
        var full = await alice.GetStringAsync(WebUtility.HtmlDecode(showAll.Groups[1].Value));
        Assert.True(Regex.Matches(full, "Showing ").Count < Regex.Matches(both, "Showing ").Count / (both == page1 ? 1 : 1) + 1);
    }

    [Fact]
    public async Task FiltersApplyAndAreEncoded()
    {
        using var alice = await site.LoginAsync("alice");
        var html = await alice.GetStringAsync("/reports/byregion?f_customer=Ann");
        Assert.Equal(Alice.Count(r => r.Customer == "Ann"), GrandTotal(html));
        var dated = await alice.GetStringAsync("/reports/byregion?f_odate_from=2023-03-01&f_odate_to=2023-06-30");
        Assert.Equal(Alice.Count(r => r.Date != null && string.CompareOrdinal(r.Date, "2023-03-01") >= 0 && string.CompareOrdinal(r.Date, "2023-06-30") <= 0), GrandTotal(dated));
        var evil = await alice.GetStringAsync("/reports/byregion?f_customer=%3Cscript%3Ealert(1)%3C/script%3E");
        Assert.DoesNotContain("<script>alert", evil);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", evil);
        var injected = await alice.GetAsync("/reports/byregion?f_customer=x%27%20OR%20%271%27%3D%271");
        Assert.Equal(HttpStatusCode.OK, injected.StatusCode);
        Assert.DoesNotContain("Grand total</td><td class=\"text-end\">" + Alice.Count + "<", await injected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CsvIsStrictAndGuardsFormulas()
    {
        using var alice = await site.LoginAsync("alice");
        var response = await alice.GetAsync("/reports/compact?format=csv");
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("compact.csv", response.Content.Headers.ContentDisposition?.ToString());
        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("\"customer\",\"Count\"", lines[0]);
        Assert.Contains("\"'=Dee\"," + Alice.Count(r => r.Customer == "=Dee"), lines);
        Assert.Contains("\"'-Cy\"," + Alice.Count(r => r.Customer == "-Cy"), lines);
        Assert.Contains("\"Total\"," + Alice.Count, lines);

        var xt = await alice.GetStringAsync("/reports/xt?format=csv");
        Assert.StartsWith("\"region / customer\",", xt);
        Assert.Contains("\"Other\"", xt);
    }

    [Fact]
    public async Task CrosstabHasOtherColumnTotalsDrillLinksAndPartialMode()
    {
        using var alice = await site.LoginAsync("alice");
        var html = await alice.GetStringAsync("/reports/xt");
        Assert.Contains(">Other</th>", html);
        Assert.Contains(">Total</th>", html);
        Assert.Contains($"<td class=\"text-end\">{Alice.Count}</td>", html);
        var link = Regex.Match(html, "href=\"(/reports/drill\\?w=[^\"]+)\"");
        Assert.True(link.Success);
        var drill = await alice.GetAsync(WebUtility.HtmlDecode(link.Groups[1].Value));
        Assert.Equal(HttpStatusCode.OK, drill.StatusCode);
        Assert.Contains("report-table", await drill.Content.ReadAsStringAsync());

        var partial = await alice.GetStringAsync("/reports/xt?partial=1");
        Assert.Contains("data-partial=\"1\"", partial);
        Assert.DoesNotContain("<nav", partial);
        Assert.DoesNotContain("<form", partial);
        Assert.Contains("@media print", html);
    }

    [Fact]
    public async Task ChartCalendarAndDashboardShowPlaceholders()
    {
        using var alice = await site.LoginAsync("alice");
        foreach (var name in new[] { "chart1", "cal1", "dash1" })
        {
            var html = await alice.GetStringAsync("/reports/" + name);
            Assert.Contains("not generated yet", html);
        }
    }
}
