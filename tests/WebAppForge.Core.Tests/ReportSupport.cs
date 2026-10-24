using System.Diagnostics;
using System.Net;
using Microsoft.Data.Sqlite;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Tests;

/// <summary>Sample report projects, and a generated site that runs for the tests of one class.</summary>
internal static class ReportSupport
{
    /// <summary>Path to a Northwind SQLite file, taken from WEBAPPFORGE_NORTHWIND_DB (empty when not set).</summary>
    public static string NorthwindSource { get; } = Environment.GetEnvironmentVariable("WEBAPPFORGE_NORTHWIND_DB") ?? string.Empty;

    public static bool HasNorthwind => NorthwindSource.Length > 0 && File.Exists(NorthwindSource);

    public static ReportGroup Group(string field, DateInterval interval = DateInterval.None, bool descending = false) =>
        new() { Field = field, Interval = interval, Descending = descending };

    public static ReportAggregate Aggregate(AggregateFunction function, string field = "", string caption = "") =>
        new() { Function = function, Field = field, Caption = caption };

    /// <summary>A project on <paramref name="dbPath"/> with every table of the database.</summary>
    public static Project ProjectOn(string name, string dbPath)
    {
        SqliteConnection.ClearAllPools();
        var project = new Project { Name = name, Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath } };
        project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={dbPath}"));
        return project;
    }

    /// <summary>The Northwind project used by the end-to-end tests: one report of each kind and a dashboard.</summary>
    public static Project Northwind(string dbPath)
    {
        var project = ProjectOn("NorthwindReports", dbPath);
        FieldInfo F(string table, string field) => project.Tables.Single(t => t.Name == table).Fields.Single(f => f.Name == field);
        F("Orders", "ShipVia").Ui.Lookup = new FieldLookup { Table = "Shippers", KeyField = "ShipperID", DisplayField = "CompanyName" };
        F("Products", "CategoryID").Ui.Lookup = new FieldLookup { Table = "Categories", KeyField = "CategoryID", DisplayField = "CategoryName" };

        var sales = new ReportInfo { Name = "sales-by-country", Caption = "Sales by country", SourceTable = "Orders", Description = "Freight per ship country and year." };
        sales.Groups.Add(Group("ShipCountry"));
        sales.Groups.Add(Group("OrderDate", DateInterval.Year));
        sales.Aggregates.Add(Aggregate(AggregateFunction.Count));
        sales.Aggregates.Add(Aggregate(AggregateFunction.Sum, "Freight"));
        sales.Aggregates.Add(Aggregate(AggregateFunction.Average, "Freight", "Average freight"));
        sales.DateFilterField = "OrderDate";
        sales.FilterFields.Add("ShipVia");
        project.Reports.Add(sales);

        var stock = new ReportInfo { Name = "stock", Caption = "Stock by category", SourceTable = "Products", ShowDetails = true, MenuGroup = "Stock" };
        stock.Groups.Add(Group("CategoryID"));
        stock.Aggregates.Add(Aggregate(AggregateFunction.Sum, "UnitsInStock"));
        stock.Aggregates.Add(Aggregate(AggregateFunction.Max, "UnitPrice"));
        stock.DetailColumns.AddRange(["ProductName", "UnitsInStock", "UnitPrice"]);
        project.Reports.Add(stock);

        project.Reports.Add(new ReportInfo
        {
            Name = "orders-by-year",
            Caption = "Orders by year",
            Kind = ReportKind.Crosstab,
            SourceTable = "Orders",
            RowField = "ShipCountry",
            ColumnField = "OrderDate",
            ColumnInterval = DateInterval.Year,
        });

        project.Reports.Add(new ReportInfo
        {
            Name = "freight-2016",
            Caption = "Freight in 2016",
            Kind = ReportKind.Chart,
            ChartType = ChartType.Line,
            SourceTable = "Orders",
            Filter = "OrderDate >= '2016-01-01' AND OrderDate < '2017-01-01'",
            RowField = "OrderDate",
            RowInterval = DateInterval.Month,
            ValueField = "Freight",
            Function = AggregateFunction.Sum,
        });

        project.Reports.Add(new ReportInfo
        {
            Name = "top-countries",
            Caption = "Top countries",
            Kind = ReportKind.Chart,
            ChartType = ChartType.HorizontalBar,
            SourceTable = "Orders",
            RowField = "ShipCountry",
            ChartOrder = ChartOrder.ValueDescending,
            TopCount = 5,
        });

        project.Reports.Add(new ReportInfo
        {
            Name = "shippers",
            Caption = "Orders per shipper",
            Kind = ReportKind.Chart,
            ChartType = ChartType.Pie,
            SourceTable = "Orders",
            RowField = "ShipVia",
            ShowDataTable = false,
        });

        project.Reports.Add(new ReportInfo
        {
            Name = "order-calendar",
            Caption = "Order calendar",
            Kind = ReportKind.Calendar,
            SourceTable = "Orders",
            DateField = "OrderDate",
            TitleField = "ShipName",
        });

        var dashboard = new ReportInfo { Name = "overview", Caption = "Overview", Kind = ReportKind.Dashboard };
        dashboard.Items.Add(new DashboardItem { Report = "top-countries", Width = 6 });
        dashboard.Items.Add(new DashboardItem { Report = "shippers", Width = 6 });
        dashboard.Items.Add(new DashboardItem { Report = "orders-by-year", Width = 12 });
        project.Reports.Add(dashboard);
        return project;
    }
}

/// <summary>Generates, builds and runs a site; the tests of a class share one instance.</summary>
public abstract class ReportSiteBase : IAsyncLifetime
{
    private Process? _site;

    public string Root { get; } = MdSupport.TempFolder();

    public string DbPath => Path.Combine(Root, "reports.db");

    public HttpClient Client { get; private set; } = null!;

    public Dictionary<string, string> Files { get; private set; } = [];

    /// <summary>False when the site cannot be set up here (no Northwind file); the tests then return early.</summary>
    public bool Ready { get; private set; }

    /// <summary>Creates the database and returns the project, or null to skip.</summary>
    protected abstract Project? Create();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        var project = Create();
        if (project is null)
        {
            return;
        }

        Files = MdSupport.GenerateText(project, Root);
        var siteFolder = Path.Combine(Root, "site");
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
        _site.OutputDataReceived += (_, e) => Log(e.Data);
        _site.ErrorDataReceived += (_, e) => Log(e.Data);
        _site.BeginOutputReadLine();
        _site.BeginErrorReadLine();
        Client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        for (var attempt = 0; attempt < 80; attempt++)
        {
            try
            {
                using var response = await Client.GetAsync("/");
                Ready = true;
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
        Client?.Dispose();
        if (_site is { HasExited: false })
        {
            _site.Kill(entireProcessTree: true);
            _site.WaitForExit(10000);
        }

        MdSupport.TryDelete(Root);
        CleanUp();
        return Task.CompletedTask;
    }

    private readonly System.Text.StringBuilder _output = new();

    /// <summary>The last part of what the site wrote to its console, such as the exception behind a 500.</summary>
    public string SiteOutput
    {
        get
        {
            lock (_output)
            {
                return _output.Length > 4000 ? _output.ToString(_output.Length - 4000, 4000) : _output.ToString();
            }
        }
    }

    private void Log(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }

    /// <summary>Runs after the site has stopped, for example to drop a server database.</summary>
    protected virtual void CleanUp()
    {
    }

    /// <summary>A connection to the site's database (the SQLite file at <see cref="DbPath"/>), not yet opened.</summary>
    protected virtual System.Data.Common.DbConnection CreateConnection() => new SqliteConnection($"Data Source={DbPath}");

    public async Task<string> Get(string url)
    {
        using var response = await Client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {text}\n--- site output ---\n{SiteOutput}");
        return text;
    }

    public async Task<object?> Scalar(string sql) => await Task.Run(() =>
    {
        using var connection = CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    });
}

/// <summary>The Northwind site with the reports of <see cref="ReportSupport.Northwind"/>. Skipped without a Northwind file.</summary>
public sealed class NorthwindReportSite : ReportSiteBase
{
    protected override Project? Create()
    {
        if (!ReportSupport.HasNorthwind)
        {
            return null;
        }

        File.Copy(ReportSupport.NorthwindSource, DbPath);
        return ReportSupport.Northwind(DbPath);
    }
}

/// <summary>A Fact that is reported as skipped, with the reason, when the Northwind sample is not on this machine.</summary>
public sealed class NorthwindFactAttribute : FactAttribute
{
    public NorthwindFactAttribute()
    {
        if (!ReportSupport.HasNorthwind)
        {
            Skip = "Northwind SQLite not found. Set WEBAPPFORGE_NORTHWIND_DB to the path of a Northwind SQLite file.";
        }
    }
}

/// <summary>
/// A small secured shop with every report kind: login with registration, User Level security, and sales rows owned by their user.
/// Users ann (level 1) and bob (level 9) are registered through the site, so the tests also cover the real login.
/// </summary>
public sealed class SecureReportSite : ReportSiteBase
{
    public const string Password = "Correct-horse-42";

    public const string Schema = """
        CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, pass TEXT NOT NULL, level INTEGER);
        CREATE TABLE regions (code TEXT PRIMARY KEY, title TEXT NOT NULL);
        CREATE TABLE sales (id INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL, region TEXT, day DATE, amount DECIMAL(10,2), paid BOOLEAN, note TEXT,
            stamp INTEGER, jd REAL);
        INSERT INTO regions (code, title) VALUES ('N', 'North'), ('S', 'South');
        INSERT INTO sales (owner, region, day, amount, paid, note) VALUES
            ('ann', 'N', '2024-05-02', 10.50, 1, 'a1'),
            ('ann', 'N', '2024-05-02', 4.25, 0, 'a2'),
            ('ann', 'S', '2024-05-20', 100, 1, 'a3'),
            ('ann', NULL, '2024-06-01', 7, 1, 'a4 <b>'),
            ('bob', 'N', '2024-05-03', 1000, 1, 'b1'),
            ('bob', 'S', '2024-05-31', 2000, 0, 'b2');
        UPDATE sales SET stamp = CAST(strftime('%s', day) AS INTEGER) + 3600, jd = julianday(day) + 0.25;
        """;

    protected override Project? Create()
    {
        MdSupport.Exec(DbPath, Schema);
        var project = ReportSupport.ProjectOn("SecureReports", DbPath);
        project.Security = new SecuritySettings
        {
            Enabled = true,
            UserTable = "users",
            UserNameField = "name",
            PasswordField = "pass",
            UserLevelField = "level",
            AllowRegistration = true,
        };
        var sales = project.Tables.Single(t => t.Name == "sales");
        sales.OwnerField = "owner";
        sales.Fields.Single(f => f.Name == "region").Ui.Lookup = new FieldLookup { Table = "regions", KeyField = "code", DisplayField = "title" };
        sales.Fields.Single(f => f.Name == "stamp").DateStorage = DateStorage.UnixSeconds;
        sales.Fields.Single(f => f.Name == "jd").DateStorage = DateStorage.JulianDay;

        var byRegion = new ReportInfo { Name = "by-region", Caption = "Sales by region", SourceTable = "sales" };
        byRegion.Groups.Add(ReportSupport.Group("region"));
        byRegion.Groups.Add(ReportSupport.Group("paid"));
        byRegion.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Count));
        byRegion.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, "amount"));
        byRegion.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Average, "amount"));
        byRegion.DateFilterField = "day";
        byRegion.FilterFields.AddRange(["paid", "region"]);
        project.Reports.Add(byRegion);

        // Dates stored as Unix seconds and as Julian day numbers.
        var byStamp = new ReportInfo { Name = "by-stamp", SourceTable = "sales", DateFilterField = "stamp" };
        byStamp.Groups.Add(ReportSupport.Group("stamp", DateInterval.Month));
        byStamp.Groups.Add(ReportSupport.Group("jd", DateInterval.Day));
        project.Reports.Add(byStamp);
        project.Reports.Add(new ReportInfo { Name = "stamps", Kind = ReportKind.Calendar, SourceTable = "sales", DateField = "jd", TitleField = "note" });

        var detail = new ReportInfo { Name = "detail", SourceTable = "sales", ShowDetails = true };
        detail.Groups.Add(ReportSupport.Group("day", DateInterval.Month));
        detail.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, "amount", "Amount"));
        detail.DetailColumns.AddRange(["note", "amount", "paid"]);
        project.Reports.Add(detail);

        project.Reports.Add(new ReportInfo { Name = "grid", Kind = ReportKind.Crosstab, SourceTable = "sales", RowField = "region", ColumnField = "paid", ValueField = "amount", Function = AggregateFunction.Sum });
        project.Reports.Add(new ReportInfo { Name = "chart", Kind = ReportKind.Chart, ChartType = ChartType.Bar, SourceTable = "sales", RowField = "day", RowInterval = DateInterval.Month, ColumnField = "region", ValueField = "amount", Function = AggregateFunction.Sum });
        project.Reports.Add(new ReportInfo { Name = "days", Kind = ReportKind.Calendar, SourceTable = "sales", DateField = "day", TitleField = "note" });
        project.Reports.Add(new ReportInfo { Name = "secret", Caption = "Secret", Kind = ReportKind.Summary, SourceTable = "sales", MinLevel = 5, ShowInMenu = false });
        var board = new ReportInfo { Name = "board", Kind = ReportKind.Dashboard };
        board.Items.Add(new DashboardItem { Report = "by-region", Width = 12 });
        board.Items.Add(new DashboardItem { Report = "secret", Width = 6 });
        project.Reports.Add(board);
        return project;
    }

    private async Task<string> TokenAsync(HttpClient client, string url)
    {
        var page = await client.GetStringAsync(url);
        var match = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
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
        Assert.Equal("/", login.Headers.Location?.OriginalString);
        return client;
    }
}
