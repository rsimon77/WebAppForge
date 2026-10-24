using System.Net;
using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class ReportModelTests
{
    private static Project Sample()
    {
        var project = new Project { Name = "R" };
        var orders = new TableInfo { Name = "orders" };
        orders.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true });
        orders.Fields.Add(new FieldInfo { Name = "country", Type = FieldType.Text });
        orders.Fields.Add(new FieldInfo { Name = "placed", Type = FieldType.Date });
        orders.Fields.Add(new FieldInfo { Name = "freight", Type = FieldType.Decimal });
        orders.Fields.Add(new FieldInfo { Name = "photo", Type = FieldType.Binary });
        project.Tables.Add(orders);
        return project;
    }

    [Fact]
    public void ReportsRoundTripThroughTheProjectFile()
    {
        var project = Sample();
        var summary = new ReportInfo { Name = "sales", Caption = "Sales", SourceTable = "orders", Filter = "freight > 0", Description = "d", MinLevel = 3, ShowInMenu = false, MenuGroup = "G", ShowDetails = true, ShowGrandTotal = false };
        summary.Groups.Add(ReportSupport.Group("placed", DateInterval.Quarter, descending: true));
        summary.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Average, "freight", "Avg"));
        summary.DetailColumns.Add("country");
        summary.DateFilterField = "placed";
        summary.FilterFields.AddRange(["country", "freight"]);
        summary.DrillDown = false;
        project.Tables[0].Fields.Single(f => f.Name == "freight").DateStorage = DateStorage.JulianDay;
        project.Reports.Add(summary);
        project.Reports.Add(new ReportInfo
        {
            Name = "chart", Kind = ReportKind.Chart, Generate = false, SourceTable = "orders", RowField = "placed", RowInterval = DateInterval.Month,
            ColumnField = "country", ValueField = "freight", Function = AggregateFunction.Max, MaxColumns = 7, ChartType = ChartType.Area,
            ChartOrder = ChartOrder.ValueDescending, TopCount = 4, ShowDataTable = false,
        });
        project.Reports.Add(new ReportInfo { Name = "cal", Kind = ReportKind.Calendar, SourceTable = "orders", DateField = "placed", EndDateField = "placed", TitleField = "country", LinkToView = false, WeekStartsMonday = false });
        var board = new ReportInfo { Name = "board", Kind = ReportKind.Dashboard };
        board.Items.Add(new DashboardItem { Report = "sales", Width = 4 });
        project.Reports.Add(board);

        var loaded = ProjectFile.Read(ProjectFile.Write(project));
        Assert.Equal(ProjectFile.Write(project).ToString(), ProjectFile.Write(loaded).ToString());
        var s = loaded.Reports[0];
        Assert.Equal(("sales", "Sales", "orders", "freight > 0", "d", 3, false, "G", true, false), (s.Name, s.Caption, s.SourceTable, s.Filter, s.Description, s.MinLevel, s.ShowInMenu, s.MenuGroup, s.ShowDetails, s.ShowGrandTotal));
        Assert.Equal(("placed", DateInterval.Quarter, true), (s.Groups[0].Field, s.Groups[0].Interval, s.Groups[0].Descending));
        Assert.Equal(("placed", false), (s.DateFilterField, s.DrillDown));
        Assert.Equal(["country", "freight"], s.FilterFields);
        Assert.Equal(DateStorage.JulianDay, loaded.Tables[0].Fields.Single(f => f.Name == "freight").DateStorage);
        Assert.Equal(DateStorage.Text, loaded.Tables[0].Fields.Single(f => f.Name == "country").DateStorage);
        Assert.Equal(("freight", AggregateFunction.Average, "Avg"), (s.Aggregates[0].Field, s.Aggregates[0].Function, s.Aggregates[0].Caption));
        var c = loaded.Reports[1];
        Assert.Equal((ReportKind.Chart, false, DateInterval.Month, "country", AggregateFunction.Max, 7, ChartType.Area, ChartOrder.ValueDescending, 4, false),
            (c.Kind, c.Generate, c.RowInterval, c.ColumnField, c.Function, c.MaxColumns, c.ChartType, c.ChartOrder, c.TopCount, c.ShowDataTable));
        Assert.Equal(("placed", "placed", "country", false, false), (loaded.Reports[2].DateField, loaded.Reports[2].EndDateField, loaded.Reports[2].TitleField, loaded.Reports[2].LinkToView, loaded.Reports[2].WeekStartsMonday));
        Assert.Equal(("sales", 4), (loaded.Reports[3].Items[0].Report, loaded.Reports[3].Items[0].Width));
    }

    [Fact]
    public void AProjectWithoutReportsWritesNoReportsElement()
    {
        Assert.Null(ProjectFile.Write(Sample()).Root!.Element("reports"));
    }

    [Fact]
    public void TheValidatorNamesEveryProblem()
    {
        var project = Sample();
        var orders = project.Tables[0];
        void Add(ReportInfo r) => project.Reports.Add(r);
        Add(new ReportInfo { Name = "!!", SourceTable = "orders" });
        Add(new ReportInfo { Name = "a b", SourceTable = "orders" });
        Add(new ReportInfo { Name = "a-b", SourceTable = "orders" });
        Add(new ReportInfo { Name = "nosource" });
        Add(new ReportInfo { Name = "missing", SourceTable = "nope" });
        var sum = new ReportInfo { Name = "sumtext", SourceTable = "orders" };
        sum.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, "country"));
        sum.Groups.Add(ReportSupport.Group("country", DateInterval.Year));
        sum.Groups.Add(ReportSupport.Group("photo"));
        Add(sum);
        Add(new ReportInfo { Name = "grid", Kind = ReportKind.Crosstab, SourceTable = "orders", RowField = "country" });
        Add(new ReportInfo { Name = "pie", Kind = ReportKind.Chart, ChartType = ChartType.Pie, SourceTable = "orders", RowField = "country", ColumnField = "placed" });
        Add(new ReportInfo { Name = "maxdate", Kind = ReportKind.Chart, SourceTable = "orders", RowField = "country", ValueField = "placed", Function = AggregateFunction.Max });
        Add(new ReportInfo { Name = "cal", Kind = ReportKind.Calendar, SourceTable = "orders", DateField = "country", DateFilterField = "placed" });
        var filtered = new ReportInfo { Name = "filtered", SourceTable = "orders", DateFilterField = "country" };
        filtered.FilterFields.AddRange(["country", "Country", "nope"]);
        Add(filtered);
        var numberDates = new ReportInfo { Name = "numberdates", SourceTable = "orders" };
        numberDates.Groups.Add(ReportSupport.Group("id", DateInterval.Year));
        Add(numberDates);
        var board = new ReportInfo { Name = "board", Kind = ReportKind.Dashboard };
        board.Items.Add(new DashboardItem { Report = "board", Width = 13 });
        board.Items.Add(new DashboardItem { Report = "ghost" });
        Add(board);
        Add(new ReportInfo { Name = "off", Generate = false });

        var problems = ReportValidator.Problems(project.Reports, project.Tables);
        void Has(string text) => Assert.Contains(problems, p => p.Contains(text, StringComparison.Ordinal));
        Has("Report !!: give the report a name");
        Has("Report a-b: another report has the same name or URL.");
        Has("Report nosource: choose the table it reads.");
        Has("Report missing: the table 'nope' is not one of the generated tables.");
        Has("Report sumtext: sum needs a number field, but 'country' is Text.");
        Has("Report sumtext: the group field 'country' is not a date, so it cannot be grouped by year.");
        Has("Report sumtext: the group field 'photo' holds binary data");
        Has("Report grid: choose the column field.");
        Has("Report pie: a pie or doughnut chart has one series");
        Has("Report maxdate: a chart plots numbers");
        Has("Report cal: the date field 'country' is not a date.");
        Has("Report cal: a calendar moves by month; clear the date range field.");
        Has("Report filtered: the date range field 'country' is not a date.");
        Has("Report filtered: the filter field 'country' is listed twice.");
        Has("Report filtered: the filter field 'nope' is not a field of 'orders'.");
        Has("Report numberdates: the group field 'id' is not a date, so it cannot be grouped by year.");
        orders.Fields.Single(f => f.Name == "id").DateStorage = DateStorage.UnixSeconds;
        Assert.DoesNotContain(ReportValidator.Problems(project.Reports, project.Tables), p => p.StartsWith("Report numberdates", StringComparison.Ordinal));
        Has("Report board: a dashboard cannot show another dashboard ('board').");
        Has("Report board: the width of 'board' must be between 1 and 12 columns.");
        Has("Report board: the dashboard shows 'ghost', which is not a generated report.");
        Assert.DoesNotContain(problems, p => p.StartsWith("Report off", StringComparison.Ordinal));

        var root = MdSupport.TempFolder();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root));
            Assert.Equal(problems[0], error.Message);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void SecretFieldsOfTheUserTableCannotBeReported()
    {
        var project = Sample();
        var users = new TableInfo { Name = "users" };
        users.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true });
        users.Fields.Add(new FieldInfo { Name = "name", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "hash", Type = FieldType.Text });
        project.Tables.Add(users);
        project.Security = new SecuritySettings { Enabled = true, UserTable = "users", UserNameField = "name", PasswordField = "hash" };
        var leak = new ReportInfo { Name = "leak", SourceTable = "users" };
        leak.Groups.Add(ReportSupport.Group("hash"));
        project.Reports.Add(leak);

        var root = MdSupport.TempFolder();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(project, Path.Combine(root, "site"), root));
            Assert.Equal("Report leak: the group field 'hash' is not a field of 'users'.", error.Message);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void LocalizationListsTheReportTextsOnlyWhenAReportIsGenerated()
    {
        var project = Sample();
        Assert.DoesNotContain(LocalizationKeys.Enumerate(project), s => s.Key.StartsWith("report.", StringComparison.Ordinal) || s.Key == "ui.reportTotal");

        var summary = new ReportInfo { Name = "sales", Caption = "Sales", SourceTable = "orders" };
        summary.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, "freight", "Freight total"));
        project.Reports.Add(summary);
        var keys = LocalizationKeys.Enumerate(project).ToDictionary(s => s.Key, s => s.Text);
        Assert.Equal("Sales", keys["report.sales"]);
        Assert.Equal("Freight total", keys["report.sales.m0"]);
        Assert.Equal("Grand total", keys["ui.reportGrandTotal"]);
    }
}

/// <summary>Generated report sources, and building them in the three sample projects.</summary>
public class ReportGenerationTests
{
    private static Project WithReports(string sample)
    {
        var project = CustomizationSamples.Create(sample);
        var table = project.Tables.First(t => t.Generate && t.Name != project.Security.UserTable && t.OwnerField.Length == 0
            && t.Fields.Any(f => f.Type == FieldType.Text && !f.IsPrimaryKey) && t.PrimaryKeys.Count == 1);
        var text = table.Fields.First(f => f.Type == FieldType.Text && !f.IsPrimaryKey);
        var number = table.Fields.FirstOrDefault(f => f.Type is FieldType.Integer or FieldType.Decimal && !f.IsPrimaryKey) ?? table.PrimaryKey!;
        var date = table.Fields.FirstOrDefault(f => f.Type is FieldType.Date or FieldType.DateTime);
        if (date is null)
        {
            date = new FieldInfo { Name = "when_at", Type = FieldType.DateTime };
            table.Fields.Add(date);
        }

        var summary = new ReportInfo { Name = "summary", Caption = "Summary <&>", SourceTable = table.Name, Description = "About \"it\"", MenuGroup = "Reports" };
        summary.Groups.Add(ReportSupport.Group(text.Name));
        summary.Groups.Add(ReportSupport.Group(date.Name, DateInterval.Quarter, descending: true));
        summary.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Count));
        summary.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, number.Name, "Total"));
        summary.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Max, text.Name));
        project.Reports.Add(summary);
        var details = new ReportInfo { Name = "details", SourceTable = table.Name, ShowDetails = true };
        details.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Average, number.Name));
        project.Reports.Add(details);
        project.Reports.Add(new ReportInfo { Name = "cross", Kind = ReportKind.Crosstab, SourceTable = table.Name, RowField = text.Name, ColumnField = date.Name, ColumnInterval = DateInterval.Year, ValueField = number.Name, Function = AggregateFunction.Average, MinLevel = 2 });
        foreach (var type in Enum.GetValues<ChartType>())
        {
            project.Reports.Add(new ReportInfo { Name = "chart-" + type, Kind = ReportKind.Chart, ChartType = type, SourceTable = table.Name, RowField = date.Name, RowInterval = DateInterval.Day, ValueField = number.Name, Function = AggregateFunction.Sum });
        }

        project.Reports.Add(new ReportInfo { Name = "calendar", Kind = ReportKind.Calendar, SourceTable = table.Name, DateField = date.Name, EndDateField = date.Name });
        var board = new ReportInfo { Name = "board", Kind = ReportKind.Dashboard };
        board.Items.Add(new DashboardItem { Report = "cross", Width = 6 });
        board.Items.Add(new DashboardItem { Report = "chart-Pie", Width = 6 });
        board.Items.Add(new DashboardItem { Report = "calendar", Width = 12 });
        project.Reports.Add(board);
        return project;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("secure")]
    [InlineData("localized")]
    public async Task SitesWithEveryReportKindBuildWithoutWarnings(string sample)
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var files = MdSupport.GenerateText(WithReports(sample), root);
            Assert.Contains("Web/ReportKit.cs", files.Keys);
            Assert.Contains("Reports/SummaryReport.cs", files.Keys);
            Assert.Contains("Reports/BoardReport.cs", files.Keys);
            Assert.Contains("app.MapGet(Route, async (HttpRequest request)", files["Reports/SummaryReport.cs"]);
            Assert.Contains("MapBoardReport(connectionString);", files["Program.cs"]);
            Assert.Contains("\"/reports/chart-horizontalbar\"", files["Program.cs"]);
            if (sample == "localized")
            {
                Assert.Contains("Strings.T(\"report.summary\", \"Summary <&>\")", files["Reports/SummaryReport.cs"]);
                Assert.Contains("Strings.T(\"report.summary.m1\", \"Total\")", files["Reports/SummaryReport.cs"]);
                Assert.Contains("Strings.T(\"ui.reportGrandTotal\", \"Grand total\")", files["Web/ReportKit.cs"]);
                Assert.Contains("\"ui.reportGrandTotal\"", files["Localization/strings.en.json"]);
            }

            var build = await MdSupport.RunDotnetAsync(Path.Combine(root, "site"), "build --nologo -v q -nodeReuse:false -warnaserror");
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void TheCrosstabRequiresTheLevelOnlyWhenUserLevelSecurityIsOn()
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var plain = MdSupport.GenerateText(WithReports("plain"), root);
            Assert.Contains("public const int MinLevel = 0;", plain["Reports/CrossReport.cs"]);
            Assert.DoesNotContain("RequireLevel", plain["Reports/CrossReport.cs"]);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void ReportsAreOffTheMenuWhenHiddenAndGroupedWhenTheyNameAGroup()
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var project = WithReports("plain");
            project.Reports.Single(r => r.Name == "details").ShowInMenu = false;
            var program = MdSupport.GenerateText(project, root)["Program.cs"];
            Assert.Contains("Html.GroupedMenu(", program);
            Assert.Contains("(\"Reports\", \"Summary <&>\", \"/reports/summary\", \"\"),", program);
            Assert.DoesNotContain("\"/reports/details\"),", program);
            Assert.Contains("app.MapDetailsReport(connectionString);", program);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}

/// <summary>The reports of a running site with login, User Level security, record ownership and a lookup.</summary>
public class SecureReportTests : IClassFixture<SecureReportSite>
{
    private readonly SecureReportSite _site;

    public SecureReportTests(SecureReportSite site) => _site = site;

    private static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<svg.*?</svg>", string.Empty, RegexOptions.Singleline), "<[^>]+>", " "));

    private static string Squash(string text) => Regex.Replace(text, "\\s+", " ");

    [Fact]
    public async Task ReportsNeedASignedInUser()
    {
        using var response = await _site.Client.GetAsync("/reports/by-region");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task TheSummaryTotalsOnlyTheRowsOfTheSignedInUserAndShowsTheLookupText()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var page = Squash(Text(await ann.GetStringAsync("/reports/by-region")));
        // Groups: region (lookup title, empty first), then paid (Yes/No). The amount is a decimal column, so it has two decimals.
        Assert.Contains("(empty) Yes 1 7.00 7.00 Total (empty) 1 7.00 7.00", page);
        Assert.Contains("North No 1 4.25 4.25 Yes 1 10.50 10.50 Total North 2 14.75 7.38", page);
        Assert.Contains("South Yes 1 100.00 100.00 Total South 1 100.00 100.00", page);
        Assert.Contains("Grand total 4 121.75 30.44", page);
        Assert.DoesNotContain("1,000", page);

        using var bob = await _site.SignInAsync("bob", 9);
        var bobs = Squash(Text(await bob.GetStringAsync("/reports/by-region")));
        Assert.Contains("Grand total 2 3,000.00 1,500.00", bobs);
    }

    [Fact]
    public async Task DetailRowsAreEncodedAndListedUnderTheirGroups()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var html = await ann.GetStringAsync("/reports/detail");
        Assert.Contains("a4 &lt;b&gt;", html);
        var page = Squash(Text(html));
        Assert.Contains("day: 2024-05 a1 10.5 Yes a2 4.25 No a3 100 Yes Total 2024-05 · Amount: 114.75", page);
        Assert.Contains("day: 2024-06 a4 <b> 7 Yes Total 2024-06 · Amount: 7.00", page);
        Assert.Contains("Grand total · Amount: 121.75", page);
    }

    [Fact]
    public async Task TheCrosstabHasTotalsPerRowAndColumn()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        var page = Squash(Text(await bob.GetStringAsync("/reports/grid")));
        Assert.Contains("region No Yes Total", page);
        // Bob has no unpaid sale in the north, so that cell stays empty.
        Assert.Contains("North 1,000.00 1,000.00", page);
        Assert.Contains("South 2,000.00 2,000.00", page);
        Assert.Contains("Total 2,000.00 1,000.00 3,000.00", page);
    }

    [Fact]
    public async Task TheChartDrawsOneSeriesPerRegionAndListsItsData()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var html = await ann.GetStringAsync("/reports/chart");
        Assert.Contains("<svg", html);
        Assert.Equal(3, Regex.Matches(html, "<rect x=\"[^\"]+\" y=\"[^\"]+\" width=\"[^\"]+\" height=\"[^\"]+\" fill=").Count);
        Assert.Contains("<title>2024-05 / North: 14.75</title>", html);
        var page = Squash(Text(html));
        Assert.Contains("day (empty) North South", page);
        Assert.Contains("2024-05 14.75 100.00", page);
    }

    [Fact]
    public async Task TheCalendarPlacesTheRowsOnTheirDaysAndLinksToTheViewPage()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var html = await ann.GetStringAsync("/reports/days?month=2024-05");
        Assert.Contains("May 2024", html);
        Assert.Contains("href=\"/reports/days?month=2024-04\"", html);
        Assert.Matches("data-day=\"2024-05-02\"><div class=\"small fw-semibold\">2</div><a class=\"d-block small text-truncate report-entry\" title=\"a1\" href=\"/sales/view/1\">a1</a><a [^>]+>a2</a></td>", html);
        Assert.Contains("data-day=\"2024-04-29\"", html);
        Assert.DoesNotContain(">b1<", html);
        Assert.Contains(">Mon</th>", html);
    }

    [Fact]
    public async Task TheDateRangeAndThePickListsFilterTheSummary()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var form = await ann.GetStringAsync("/reports/by-region");
        Assert.Contains("<form method=\"get\" action=\"/reports/by-region\"", form);
        Assert.Contains("name=\"from\"", form);
        // The choices are the values of the user's own rows, with their lookup or Yes/No text.
        Assert.Contains("<option value=\"N\">North</option><option value=\"S\">South</option>", form);
        Assert.Contains("<option value=\"0\">No</option><option value=\"1\">Yes</option>", form);

        var range = Squash(Text(await ann.GetStringAsync("/reports/by-region?from=2024-05-10&to=2024-05-31")));
        Assert.Contains("Grand total 1 100.00 100.00", range);
        var unpaid = await ann.GetStringAsync("/reports/by-region?f_paid=0");
        Assert.Contains("Grand total 1 4.25 4.25", Squash(Text(unpaid)));
        Assert.Contains("<option value=\"0\" selected>No</option>", unpaid);
        var both = Squash(Text(await ann.GetStringAsync("/reports/by-region?f_region=N&from=2024-01-01&to=not-a-date")));
        Assert.Contains("Grand total 2 14.75 7.38", both);
    }

    [Fact]
    public async Task NumbersLinkToTheRowsBehindThemAndTheRowsPageKeepsOwnerAndFilters()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var summary = await ann.GetStringAsync("/reports/by-region?f_paid=1");
        Assert.Contains("href=\"/reports/by-region/rows?f_paid=1&amp;k0=s%3ANorth&amp;k1=i%3A1\"", summary);
        Assert.Contains("href=\"/reports/by-region/rows?f_paid=1&amp;n0=1&amp;k1=i%3A1\"", summary);
        Assert.Contains("href=\"/reports/by-region/rows?f_paid=1\"", summary);

        var north = await ann.GetStringAsync("/reports/by-region/rows?k0=s%3ANorth");
        var text = Squash(Text(north));
        Assert.Contains("region: North", text);
        Assert.Contains("2 rows", text);
        Assert.Contains("<a class=\"link-underline-opacity-0\" href=\"/sales/view/1\">", north);
        Assert.DoesNotContain(">b1<", north);
        Assert.Contains("href=\"/reports/by-region\"", north);

        Assert.Contains("1 rows", Squash(Text(await ann.GetStringAsync("/reports/by-region/rows?k0=s%3ANorth&f_paid=1"))));
        Assert.Contains("a4", Squash(Text(await ann.GetStringAsync("/reports/by-region/rows?n0=1"))));
        Assert.Contains("0 rows", Squash(Text(await ann.GetStringAsync("/reports/by-region/rows?k0=North' OR 1=1"))));

        using var bob = await _site.SignInAsync("bob", 9);
        var bobs = Squash(Text(await bob.GetStringAsync("/reports/by-region/rows?k0=s%3ANorth")));
        Assert.Contains("1 rows", bobs);
        Assert.Contains("b1", bobs);

        var grid = await bob.GetStringAsync("/reports/grid");
        Assert.Contains("href=\"/reports/grid/rows?k0=s%3ASouth&amp;k1=i%3A0\"", grid);
        Assert.Contains("href=\"/reports/grid/rows?k1=i%3A1\"", grid);
        var chart = await ann.GetStringAsync("/reports/chart");
        Assert.Contains("<a href=\"/reports/chart/rows?k0=s%3A2024-05&amp;k1=s%3ANorth\"><rect", chart);
        Assert.Contains("2 rows", Squash(Text(await ann.GetStringAsync("/reports/chart/rows?k0=s%3A2024-05&k1=s%3ANorth"))));
    }

    [Fact]
    public async Task UnixAndJulianDatesAreGroupedFilteredAndPlacedOnTheCalendar()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        var page = Squash(Text(await ann.GetStringAsync("/reports/by-stamp")));
        Assert.Contains("2024-05 2024-05-02 2 2024-05-20 1 Total 2024-05 3", page);
        Assert.Contains("2024-06 2024-06-01 1", page);
        Assert.Contains("Grand total 3", Squash(Text(await ann.GetStringAsync("/reports/by-stamp?to=2024-05-31"))));

        var calendar = await ann.GetStringAsync("/reports/stamps?month=2024-05");
        Assert.Matches("data-day=\"2024-05-20\"><div class=\"small fw-semibold\">20</div><a [^>]+>a3</a></td>", calendar);
    }

    [Fact]
    public async Task UsersBelowTheLevelOfAReportAreRefusedAndTheDashboardLeavesItOut()
    {
        using var ann = await _site.SignInAsync("ann", 1);
        using var denied = await ann.GetAsync("/reports/secret");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var board = await ann.GetStringAsync("/reports/board");
        Assert.Contains("href=\"/reports/by-region\"", board);
        Assert.DoesNotContain("/reports/secret", board);

        using var bob = await _site.SignInAsync("bob", 9);
        Assert.Contains("href=\"/reports/secret\"", await bob.GetStringAsync("/reports/board"));
        var home = await bob.GetStringAsync("/");
        Assert.Contains("/reports/by-region", home);
        Assert.DoesNotContain("/reports/secret", home);
    }
}

/// <summary>Every report kind on a site generated from Northwind, checked against the database. Skipped without Northwind.</summary>
public class NorthwindReportTests : IClassFixture<NorthwindReportSite>
{
    private readonly NorthwindReportSite _site;

    public NorthwindReportTests(NorthwindReportSite site) => _site = site;

    private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<svg.*?</svg>", string.Empty, RegexOptions.Singleline), "<[^>]+>", " ")), "\\s+", " ");

    private static string N0(object? value) => Convert.ToDecimal(value).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

    private static string N2(object? value) => Convert.ToDecimal(value).ToString("#,0.00", System.Globalization.CultureInfo.InvariantCulture);

    [NorthwindFact]
    public async Task TheSummaryMatchesTheDatabase()
    {
        var page = Text(await _site.Get("/reports/sales-by-country"));
        var count = await _site.Scalar("SELECT COUNT(*) FROM Orders WHERE ShipCountry = 'Germany'");
        var freight = await _site.Scalar("SELECT SUM(Freight) FROM Orders WHERE ShipCountry = 'Germany'");
        var average = await _site.Scalar("SELECT AVG(Freight) FROM Orders WHERE ShipCountry = 'Germany'");
        Assert.Contains($"Total Germany {N0(count)} {N2(freight)} {N2(average)}", page);
        var all = await _site.Scalar("SELECT COUNT(*) FROM Orders");
        Assert.Contains($"Grand total {N0(all)} {N2(await _site.Scalar("SELECT SUM(Freight) FROM Orders"))}", page);
        Assert.Contains("Freight per ship country and year.", page);
    }

    [NorthwindFact]
    public async Task TheFiltersOfTheSummaryMatchTheDatabase()
    {
        var html = await _site.Get("/reports/sales-by-country?from=2016-01-01&to=2016-12-31&f_ShipVia=1");
        Assert.Contains("<option value=\"1\" selected>Speedy Express</option>", html);
        var where = "substr(OrderDate, 1, 10) BETWEEN '2016-01-01' AND '2016-12-31' AND ShipVia = 1";
        var count = await _site.Scalar($"SELECT COUNT(*) FROM Orders WHERE {where}");
        var freight = await _site.Scalar($"SELECT SUM(Freight) FROM Orders WHERE {where}");
        Assert.Contains($"Grand total {N0(count)} {N2(freight)}", Text(html));
    }

    [NorthwindFact]
    public async Task ChartBarsDrillDownToTheOrdersOfTheirCountry()
    {
        var chart = await _site.Get("/reports/top-countries");
        Assert.Contains("<a href=\"/reports/top-countries/rows?k0=s%3AUSA\"><rect", chart);
        var rows = await _site.Get("/reports/top-countries/rows?k0=s%3AUSA");
        var usa = await _site.Scalar("SELECT COUNT(*) FROM Orders WHERE ShipCountry = 'USA'");
        Assert.Contains($"{usa} rows", Text(rows));
        Assert.Equal(50, Regex.Matches(rows, "href=\"/orders/view/\\d+\"").Count);
        Assert.Contains("href=\"/reports/top-countries/rows?page=2&k0=s%3AUSA\"", rows);
        var second = await _site.Get("/reports/top-countries/rows?k0=s%3AUSA&page=2");
        Assert.Contains("<li class=\"page-item active\"><a class=\"page-link\" href=\"/reports/top-countries/rows?page=2", second);
    }

    [NorthwindFact]
    public async Task TheDetailSummaryListsProductsUnderTheirCategoryName()
    {
        var page = Text(await _site.Get("/reports/stock"));
        var stock = await _site.Scalar("SELECT SUM(UnitsInStock) FROM Products WHERE CategoryID = 1");
        Assert.Contains("CategoryID: Beverages Chai", page);
        Assert.Contains($"Total Beverages · Sum of UnitsInStock: {N0(stock)}", page);
    }

    [NorthwindFact]
    public async Task TheCrosstabMatchesTheDatabase()
    {
        var page = Text(await _site.Get("/reports/orders-by-year"));
        var years = new List<string>();
        for (var year = 2012; year <= 2023; year++)
        {
            years.Add(N0(await _site.Scalar($"SELECT COUNT(*) FROM Orders WHERE ShipCountry = 'Germany' AND substr(OrderDate, 1, 4) = '{year}'")));
        }

        Assert.Contains("Germany " + string.Join(" ", years) + " " + N0(await _site.Scalar("SELECT COUNT(*) FROM Orders WHERE ShipCountry = 'Germany'")), page);
        Assert.Matches("Total( [0-9,]+){12} " + N0(await _site.Scalar("SELECT COUNT(*) FROM Orders")), page);
    }

    [NorthwindFact]
    public async Task ChartsListTheirDataAndShowLookupNames()
    {
        var top = Text(await _site.Get("/reports/top-countries"));
        var leader = await _site.Scalar("SELECT ShipCountry FROM Orders GROUP BY ShipCountry ORDER BY COUNT(*) DESC LIMIT 1");
        Assert.Contains($"ShipCountry Count {leader} {N0(await _site.Scalar($"SELECT COUNT(*) FROM Orders WHERE ShipCountry = '{leader}'"))}", top);
        Assert.Equal(5, Regex.Matches(await _site.Get("/reports/top-countries"), "<tr><th scope=\"row\">").Count);

        var pie = await _site.Get("/reports/shippers");
        Assert.Contains("Speedy Express (", pie);
        Assert.DoesNotContain("report-data", pie);

        var freight = await _site.Get("/reports/freight-2016");
        Assert.Contains("<polyline", freight);
        Assert.Contains($"2016-07 {N2(await _site.Scalar("SELECT SUM(Freight) FROM Orders WHERE substr(OrderDate, 1, 7) = '2016-07'"))}", Text(freight));
    }

    [NorthwindFact]
    public async Task TheCalendarShowsTheOrdersOfEachDay()
    {
        var html = await _site.Get("/reports/order-calendar?month=2016-07");
        var day = Regex.Match(html, "data-day=\"2016-07-04\">(.*?)</td>").Groups[1].Value;
        var expected = Convert.ToInt32(await _site.Scalar("SELECT COUNT(*) FROM Orders WHERE substr(OrderDate, 1, 10) = '2016-07-04'"));
        Assert.Equal(expected, Regex.Matches(day, "report-entry").Count);
        Assert.Contains("href=\"/orders/view/", day);
    }

    [NorthwindFact]
    public async Task TheDashboardAndTheHomePageShowTheReports()
    {
        var board = await _site.Get("/reports/overview");
        Assert.Equal(3, Regex.Matches(board, "class=\"card h-100\"").Count);
        Assert.Contains("col-lg-12", board);
        var home = await _site.Get("/");
        Assert.Contains("<a href=\"/reports/overview\">Overview</a>", home);
        Assert.Contains("<h2 class=\"h5 mt-4\">Stock</h2>", home);
    }
}
