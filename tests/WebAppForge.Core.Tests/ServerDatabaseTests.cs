using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Tests;

/// <summary>
/// Server databases for the end-to-end tests: the admin connection strings come from WEBAPPFORGE_SQLSERVER and WEBAPPFORGE_MYSQL.
/// Each fixture creates its own database and drops it afterwards. Without the variable the tests of that server return early.
/// </summary>
internal static class ServerDatabases
{
    public static string? Admin(string provider) => Environment.GetEnvironmentVariable(
        DatabaseProviders.IsSqlServer(provider) ? "WEBAPPFORGE_SQLSERVER" : "WEBAPPFORGE_MYSQL") is { Length: > 0 } value ? value : null;

    /// <summary>Creates an empty database and returns the connection string of it.</summary>
    public static string Create(string provider, string admin, string name)
    {
        using var connection = DatabaseConnections.Create(provider, admin);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = DatabaseProviders.IsSqlServer(provider) ? $"CREATE DATABASE [{name}]" : $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4";
        command.ExecuteNonQuery();
        if (DatabaseProviders.IsSqlServer(provider))
        {
            return new SqlConnectionStringBuilder(admin) { InitialCatalog = name }.ConnectionString;
        }

        return new MySqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
    }

    public static void Drop(string provider, string admin, string name)
    {
        try
        {
            SqlConnection.ClearAllPools();
            MySqlConnection.ClearAllPools();
            using var connection = DatabaseConnections.Create(provider, admin);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = DatabaseProviders.IsSqlServer(provider)
                ? $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]"
                : $"DROP DATABASE `{name}`";
            command.ExecuteNonQuery();
        }
        catch (DbException)
        {
            // The database may be in use for a moment longer; a leftover test database is harmless.
        }
    }

    public static void Exec(string provider, string connectionString, string script)
    {
        using var connection = DatabaseConnections.Create(provider, connectionString);
        connection.Open();
        foreach (var statement in script.Split(";\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement.TrimEnd(';');
            command.ExecuteNonQuery();
        }
    }
}

/// <summary>
/// A secured shop on a server database: users with levels and registration, regions, sales owned by their user (with a Unix time
/// column and a picture), and orders with order lines deleted in cascade. Same data and reports as <see cref="SecureReportSite"/>.
/// </summary>
public abstract class ServerSite : ReportSiteBase
{
    public const string Password = "Correct-horse-42";

    private string? _admin;

    private readonly string _database = "wf_" + Guid.NewGuid().ToString("N")[..12];

    public abstract string Provider { get; }

    public string ConnectionString { get; private set; } = string.Empty;

    public IReadOnlyList<TableInfo> SchemaTables { get; private set; } = [];

    /// <summary>The project the site was generated from.</summary>
    public Project? Project { get; private set; }

    protected abstract string Schema { get; }

    /// <summary>The site keeps an audit trail of orders, order lines and sales.</summary>
    protected virtual bool Audited => false;

    private const string Data = """
        INSERT INTO regions (code, title) VALUES ('N', 'North'), ('S', 'South');
        INSERT INTO sales (owner, region, day, amount, paid, note, stamp) VALUES ('ann', 'N', '2024-05-02', 10.50, 1, 'a1', 1714651200);
        INSERT INTO sales (owner, region, day, amount, paid, note, stamp) VALUES ('ann', 'N', '2024-05-02', 4.25, 0, 'a2', 1714651200);
        INSERT INTO sales (owner, region, day, amount, paid, note, stamp) VALUES ('ann', 'S', '2024-05-20', 100, 1, 'a3', 1716206400);
        INSERT INTO sales (owner, region, day, amount, paid, note, stamp) VALUES ('ann', NULL, '2024-06-01', 7, 1, 'a4 <b>', 1717243200);
        INSERT INTO sales (owner, region, day, amount, paid, note, stamp) VALUES ('bob', 'N', '2024-05-03', 1000, 1, 'b1', 1714737600);
        INSERT INTO sales (owner, region, day, amount, paid, note, stamp) VALUES ('bob', 'S', '2024-05-31', 2000, 0, 'b2', 1717156800);
        INSERT INTO orders (customer, placed) VALUES ('Acme', '2024-05-02 10:30:00');
        INSERT INTO orders (customer, placed) VALUES ('Zeta', '2024-06-01 08:00:00');
        INSERT INTO order_lines (order_id, product, qty) VALUES (1, 'Hammer', 2);
        INSERT INTO order_lines (order_id, product, qty) VALUES (1, 'Nails', 100);
        INSERT INTO order_lines (order_id, product, qty) VALUES (1, 'Saw', 1);
        INSERT INTO order_lines (order_id, product, qty) VALUES (2, 'Kite', 3)
        """;

    protected override Project? Create()
    {
        _admin = ServerDatabases.Admin(Provider);
        if (_admin is null)
        {
            return null;
        }

        ConnectionString = ServerDatabases.Create(Provider, _admin, _database);
        ServerDatabases.Exec(Provider, ConnectionString, Schema);
        ServerDatabases.Exec(Provider, ConnectionString, Data);

        var project = new Project
        {
            Name = "ServerShop",
            Database = new DatabaseSettings { Provider = Provider, ConnectionString = ConnectionString },
        };
        SchemaTables = DatabaseConnections.SchemaReader(Provider).ReadTables(ConnectionString);
        project.Tables.AddRange(DatabaseConnections.SchemaReader(Provider).ReadTables(ConnectionString));
        project.Security = new SecuritySettings
        {
            Enabled = true,
            UserTable = "users",
            UserNameField = "name",
            PasswordField = "pass",
            UserLevelField = "level",
            AllowRegistration = true,
            TrackFailedAttempts = true,
            MaxFailedAttempts = 3,
        };

        TableInfo T(string name) => project.Tables.Single(t => t.Name == name);
        var sales = T("sales");
        sales.OwnerField = "owner";
        sales.Pages.PageSize = 3;
        sales.Fields.Single(f => f.Name == "region").Ui.Lookup = new FieldLookup { Table = "regions", KeyField = "code", DisplayField = "title" };
        sales.Fields.Single(f => f.Name == "stamp").DateStorage = DateStorage.UnixSeconds;
        sales.Designs.List.FilterBuilder = true;
        sales.Designs.List.QuickFilter = true;
        sales.Designs.List.ResetButton = true;

        // After a new order is saved its edit page opens, so the redirect shows which identity value the site read.
        var lines = new DetailLink { DetailTable = "order_lines", OnDelete = DeleteRule.Cascade, FilterAndSort = true, ShowOnAdd = true };
        lines.Links.Add(new KeyLink { MasterField = "id", DetailField = "order_id" });
        T("orders").Details.Add(lines);

        var byRegion = new ReportInfo { Name = "by-region", SourceTable = "sales", DateFilterField = "day", DrillDown = true };
        byRegion.Groups.Add(ReportSupport.Group("region"));
        byRegion.Groups.Add(ReportSupport.Group("paid"));
        byRegion.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Count));
        byRegion.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, "amount"));
        byRegion.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Average, "amount"));
        byRegion.FilterFields.AddRange(["paid", "region"]);
        project.Reports.Add(byRegion);
        var byMonth = new ReportInfo { Name = "by-month", SourceTable = "sales" };
        byMonth.Groups.Add(ReportSupport.Group("stamp", DateInterval.Month));
        byMonth.Groups.Add(ReportSupport.Group("day", DateInterval.Quarter));
        byMonth.Aggregates.Add(ReportSupport.Aggregate(AggregateFunction.Sum, "amount"));
        project.Reports.Add(byMonth);
        var details = new ReportInfo { Name = "details", SourceTable = "sales", ShowDetails = true };
        details.Groups.Add(ReportSupport.Group("region"));
        details.DetailColumns.AddRange(["note", "amount"]);
        project.Reports.Add(details);
        project.Reports.Add(new ReportInfo { Name = "grid", Kind = ReportKind.Crosstab, SourceTable = "sales", RowField = "region", ColumnField = "paid", ValueField = "amount", Function = AggregateFunction.Sum, DrillDown = true });
        project.Reports.Add(new ReportInfo { Name = "chart", Kind = ReportKind.Chart, ChartType = ChartType.Bar, SourceTable = "sales", RowField = "day", RowInterval = DateInterval.Month, ValueField = "amount", Function = AggregateFunction.Sum });
        project.Reports.Add(new ReportInfo { Name = "days", Kind = ReportKind.Calendar, SourceTable = "sales", DateField = "day", TitleField = "note" });
        if (Audited)
        {
            project.Audit.Enabled = true;
            foreach (var name in new[] { "orders", "order_lines", "sales" })
            {
                T(name).Audit.Enabled = true;
            }
        }

        Project = project;
        return project;
    }

    protected override DbConnection CreateConnection() => DatabaseConnections.Create(Provider, ConnectionString);

    protected override void CleanUp()
    {
        if (_admin is not null)
        {
            ServerDatabases.Drop(Provider, _admin, _database);
        }
    }

    private async Task<string> TokenAsync(HttpClient client, string url)
    {
        var page = await client.GetStringAsync(url);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "no anti-forgery token on " + url);
        return match.Groups[1].Value;
    }

    public HttpClient NewClient() =>
        new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = Client.BaseAddress };

    public async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string formUrl, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenAsync(client, formUrl);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    /// <summary>A new client signed in as <paramref name="user"/>, registered through the site first when needed.</summary>
    public async Task<HttpClient> SignInAsync(string user, int level)
    {
        var client = NewClient();
        if (Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM users WHERE name = '{user}'")) == 0)
        {
            using var registered = await PostFormAsync(client, "/register", "/register",
                new() { ["username"] = user, ["password"] = Password, ["confirm"] = Password });
            Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        }

        await Scalar($"UPDATE users SET level = {level} WHERE name = '{user}'");
        using var login = await PostFormAsync(client, "/login", "/login", new() { ["username"] = user, ["password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location?.OriginalString);
        return client;
    }
}

public class SqlServerSite : ServerSite
{
    public override string Provider => DatabaseProviders.SqlServer;

    protected override string Schema => """
        CREATE TABLE users (id int IDENTITY(1,1) PRIMARY KEY, name nvarchar(100) NOT NULL UNIQUE, pass nvarchar(200) NOT NULL, level int NULL);
        CREATE TABLE regions (code nvarchar(10) NOT NULL PRIMARY KEY, title nvarchar(50) NOT NULL);
        CREATE TABLE sales (id int IDENTITY(1,1) PRIMARY KEY, owner nvarchar(100) NOT NULL, region nvarchar(10) NULL REFERENCES regions (code),
            day date NULL, amount decimal(10,2) NULL, paid bit NULL, note nvarchar(200) NULL, stamp bigint NULL, photo varbinary(max) NULL,
            version rowversion, total AS (amount * 2));
        CREATE TABLE orders (id int IDENTITY(1,1) PRIMARY KEY, customer nvarchar(100) NOT NULL, placed datetime2 NULL);
        CREATE TABLE order_lines (id int IDENTITY(1,1) PRIMARY KEY, order_id int NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
            product nvarchar(100) NOT NULL, qty int NOT NULL DEFAULT 1);
        CREATE TABLE audit (id int IDENTITY(1000,1) PRIMARY KEY, what nvarchar(50) NOT NULL);
        CREATE TRIGGER orders_audit ON orders AFTER INSERT AS INSERT INTO audit (what) SELECT 'order' FROM inserted
        """;
}

public class MySqlSite : ServerSite
{
    public override string Provider => DatabaseProviders.MySql;

    protected override string Schema => """
        CREATE TABLE users (id INT AUTO_INCREMENT PRIMARY KEY, name VARCHAR(100) NOT NULL UNIQUE, pass VARCHAR(200) NOT NULL, level INT NULL);
        CREATE TABLE regions (code VARCHAR(10) NOT NULL PRIMARY KEY, title VARCHAR(50) NOT NULL);
        CREATE TABLE sales (id INT AUTO_INCREMENT PRIMARY KEY, owner VARCHAR(100) NOT NULL, region VARCHAR(10) NULL, day DATE NULL,
            amount DECIMAL(10,2) NULL, paid TINYINT(1) NULL, note VARCHAR(200) NULL, stamp BIGINT NULL, photo LONGBLOB NULL,
            total DECIMAL(12,2) GENERATED ALWAYS AS (amount * 2) VIRTUAL, FOREIGN KEY (region) REFERENCES regions (code));
        CREATE TABLE orders (id INT AUTO_INCREMENT PRIMARY KEY, customer VARCHAR(100) NOT NULL, placed DATETIME NULL);
        CREATE TABLE order_lines (id INT AUTO_INCREMENT PRIMARY KEY, order_id INT NOT NULL, product VARCHAR(100) NOT NULL, qty INT NOT NULL DEFAULT 1,
            FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE);
        CREATE TABLE audit (id INT AUTO_INCREMENT PRIMARY KEY, what VARCHAR(50) NOT NULL) AUTO_INCREMENT = 1000;
        CREATE TRIGGER orders_audit AFTER INSERT ON orders FOR EACH ROW INSERT INTO audit (what) VALUES ('order')
        """;
}

public sealed class SqlServerAuditSite : SqlServerSite
{
    protected override bool Audited => true;
}

public sealed class MySqlAuditSite : MySqlSite
{
    protected override bool Audited => true;
}

/// <summary>The same checks on every server: schema reading, login and lockout, lists, grid tools, the JSON API, reports and master/detail.</summary>
public abstract class ServerSiteTests<TSite> where TSite : ServerSite
{
    private readonly TSite _site;

    protected ServerSiteTests(TSite site) => _site = site;

    private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<svg.*?</svg>", string.Empty, RegexOptions.Singleline), "<[^>]+>", " ")), "\\s+", " ");

    [Fact]
    public void TheSchemaReaderFindsTypesKeysIdentitiesAndForeignKeys()
    {
        if (!_site.Ready)
        {
            return;
        }

        var tables = _site.SchemaTables;
        Assert.Equal(["audit", "order_lines", "orders", "regions", "sales", "users"], tables.Select(t => t.Name).Order(StringComparer.Ordinal));
        var sales = tables.Single(t => t.Name == "sales");
        Assert.Equal(["id", "owner", "region", "day", "amount", "paid", "note", "stamp", "photo"], sales.Fields.Select(f => f.Name));
        FieldType TypeOf(string name) => sales.Fields.Single(f => f.Name == name).Type;
        Assert.Equal(FieldType.Integer, TypeOf("id"));
        Assert.Equal(FieldType.Date, TypeOf("day"));
        Assert.Equal(FieldType.Decimal, TypeOf("amount"));
        Assert.Equal(FieldType.Boolean, TypeOf("paid"));
        Assert.Equal(FieldType.Binary, TypeOf("photo"));
        Assert.Equal(FieldType.Integer, TypeOf("stamp"));
        var id = sales.Fields[0];
        Assert.True(id is { IsPrimaryKey: true, IsAutoIncrement: true, KeyPosition: 1, IsRequired: true });
        Assert.True(sales.Fields.Single(f => f.Name == "owner").IsRequired);
        Assert.False(sales.Fields.Single(f => f.Name == "note").IsRequired);
        Assert.True(tables.Single(t => t.Name == "order_lines").Fields.Single(f => f.Name == "qty").HasDefault);
        Assert.Equal(FieldType.DateTime, tables.Single(t => t.Name == "orders").Fields.Single(f => f.Name == "placed").Type);

        var fk = tables.Single(t => t.Name == "order_lines").ForeignKeys.Single();
        Assert.Equal("orders", fk.ReferencedTable);
        Assert.Equal(["order_id"], fk.Columns);
        Assert.Equal(["id"], fk.ReferencedColumns);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Equal("regions", sales.ForeignKeys.Single().ReferencedTable);
    }

    [Fact]
    public async Task RegistrationLoginAndLockoutUseTheServer()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        Assert.Contains("Log out", await ann.GetStringAsync("/"));

        // A second registration of the same name is refused.
        using var again = _site.NewClient();
        using var twice = await _site.PostFormAsync(again, "/register", "/register",
            new() { ["username"] = "ann", ["password"] = ServerSite.Password, ["confirm"] = ServerSite.Password });
        Assert.Contains("That user name is already taken.", await twice.Content.ReadAsStringAsync());

        // Three wrong passwords lock the account, so even the right one is refused; the count is in login_attempts.
        await _site.SignInAsync("carl", 1);
        using var guesser = _site.NewClient();
        for (var i = 0; i < 3; i++)
        {
            using var wrong = await _site.PostFormAsync(guesser, "/login", "/login", new() { ["username"] = "carl", ["password"] = "nope" + i });
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        }

        Assert.Equal(3L, Convert.ToInt64(await _site.Scalar("SELECT failures FROM login_attempts WHERE username = 'carl'")));
        using var locked = await _site.PostFormAsync(guesser, "/login", "/login", new() { ["username"] = "carl", ["password"] = ServerSite.Password });
        Assert.Contains("Too many failed attempts", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListsPageSortSearchAndShowOnlyTheOwnersRows()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        var first = Text(await ann.GetStringAsync("/sales"));
        Assert.Contains("a1", first);
        Assert.Contains("a3", first);
        Assert.DoesNotContain("a4", first);
        Assert.DoesNotContain("b1", first);
        Assert.Contains("a4 <b>", Text(await ann.GetStringAsync("/sales?page=2")));
        Assert.Contains("North", first);

        var sorted = Text(await ann.GetStringAsync("/sales?sort=amount&dir=desc"));
        Assert.True(sorted.IndexOf("a3", StringComparison.Ordinal) < sorted.IndexOf("a1", StringComparison.Ordinal));
        var search = Text(await ann.GetStringAsync("/sales?q=a2"));
        Assert.Contains("a2", search);
        Assert.DoesNotContain("a1", search);
        var field = Text(await ann.GetStringAsync("/sales?f_note=a3"));
        Assert.Contains("a3", field);
        Assert.DoesNotContain("a2", field);
    }

    [Fact]
    public async Task GridToolsFilterOnTheServer()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        async Task<string[]> Notes(string query)
        {
            var html = await ann.GetStringAsync("/sales" + query + (query.Contains('?') ? "&" : "?") + "pageSize=3");
            var all = new List<string>();
            for (var page = 1; page <= 2; page++)
            {
                var text = page == 1 ? html : await ann.GetStringAsync("/sales" + query + (query.Contains('?') ? "&" : "?") + "page=2");
                all.AddRange(new[] { "a1", "a2", "a3", "a4" }.Where(n => text.Contains(">" + n, StringComparison.Ordinal)));
            }

            return all.Distinct().Order().ToArray();
        }

        Assert.Equal(["a1", "a3"], await Notes("?ff0=amount&fo0=ge&fv0=10"));
        Assert.Equal(["a1", "a2"], await Notes("?ff0=region&fo0=eq&fv0=north"));
        Assert.Equal(["a1", "a2", "a3"], await Notes("?ff0=day&fo0=bt&fv0=2024-05-01&fx0=2024-05-31"));
        Assert.Equal(["a3"], await Notes("?ff0=stamp&fo0=eq&fv0=2024-05-20"));
        Assert.Equal(["a2"], await Notes("?ff0=paid&fo0=no"));
        Assert.Equal(["a4"], await Notes("?ff0=region&fo0=null"));
        Assert.Equal(["a4"], await Notes("?ff0=note&fo0=has&fv0=%3Cb"));
        Assert.Equal(["a1", "a2", "a4"], await Notes("?fm=any&ff0=region&fo0=sw&fv0=no&ff1=note&fo1=ew&fv1=%3E"));
        Assert.Equal(["a1"], await Notes("?qf=north%20-a2"));
        Assert.Equal(["a2", "a3"], await Notes("?qf=-a1%20-a4"));
        Assert.Equal(["a1", "a2", "a3", "a4"], await Notes("?s0=paid&d0=desc&s1=amount"));
        Assert.Empty(await Notes("?ff0=note&fo0=has&fv0=%25"));
    }

    [Fact]
    public async Task TheApiCreatesReadsUpdatesAndDeletesWithIdentityKeys()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        using var created = await ann.PostAsJsonAsync("/api/sales", new { owner = "someone else", region = "S", day = "2024-07-04", amount = 12.5m, paid = true, note = "api", stamp = 1720094400, photo = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = item.GetProperty("id").GetInt64();
        Assert.True(id > 6, "identity " + id);
        Assert.Equal("ann", Convert.ToString(await _site.Scalar($"SELECT owner FROM sales WHERE id = {id}")));

        var read = await ann.GetFromJsonAsync<JsonElement>($"/api/sales/{id}");
        Assert.Equal("api", read.GetProperty("note").GetString());
        Assert.True(read.GetProperty("paid").GetBoolean());
        Assert.Equal(12.5m, read.GetProperty("amount").GetDecimal());
        Assert.StartsWith("2024-07-04", read.GetProperty("day").GetString());

        using var updated = await ann.PutAsJsonAsync($"/api/sales/{id}", new { owner = "ann", region = (string?)null, day = (string?)null, amount = 1m, paid = false, note = "changed", stamp = (long?)null, photo = Convert.ToBase64String([1, 2, 3]) });
        Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
        Assert.Equal("changed", Convert.ToString(await _site.Scalar($"SELECT note FROM sales WHERE id = {id}")));
        Assert.Equal(3L, Convert.ToInt64(await _site.Scalar($"SELECT {(_site.Provider == DatabaseProviders.MySql ? "LENGTH" : "DATALENGTH")}(photo) FROM sales WHERE id = {id}")));

        using var deleted = await ann.DeleteAsync($"/api/sales/{id}");
        Assert.True(deleted.IsSuccessStatusCode);
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar($"SELECT COUNT(*) FROM sales WHERE id = {id}")));
    }

    [Fact]
    public async Task TheAddAndEditPagesSaveThroughTheForms()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        using var added = await _site.PostFormAsync(ann, "/orders/add", "/orders/add", new() { ["customer"] = "Form & Co", ["placed"] = "2024-08-01T09:15" });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        var id = Convert.ToInt64(await _site.Scalar("SELECT MAX(id) FROM orders"));

        // The insert trigger also creates an audit row (identity from 1000); the site must read the order's own identity.
        Assert.True(Convert.ToInt64(await _site.Scalar("SELECT MAX(id) FROM audit")) >= 1000);
        Assert.StartsWith($"/orders/edit/{id}", added.Headers.Location?.OriginalString);
        using var api = await ann.PostAsJsonAsync("/api/orders", new { customer = "Api order", placed = (string?)null });
        Assert.Equal(HttpStatusCode.Created, api.StatusCode);
        var apiId = (await api.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(Convert.ToInt64(await _site.Scalar("SELECT MAX(id) FROM orders")), apiId);
        Assert.Equal("Api order", Convert.ToString(await _site.Scalar($"SELECT customer FROM orders WHERE id = {apiId}")));
        Assert.Equal("Form & Co", Convert.ToString(await _site.Scalar($"SELECT customer FROM orders WHERE id = {id}")));
        Assert.Contains("Form &amp; Co", await ann.GetStringAsync($"/orders/view/{id}"));

        using var edited = await _site.PostFormAsync(ann, $"/orders/edit/{id}", $"/orders/edit/{id}", new() { ["customer"] = "Renamed", ["placed"] = "" });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal("Renamed", Convert.ToString(await _site.Scalar($"SELECT customer FROM orders WHERE id = {id}")));
        Assert.Null(await _site.Scalar($"SELECT placed FROM orders WHERE id = {id} AND placed IS NOT NULL"));
    }

    [Fact]
    public async Task ReportsGroupFilterAndDrillDownOnTheServer()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        var summary = Text(await ann.GetStringAsync("/reports/by-region"));
        Assert.Contains("(empty) Yes 1 7.00 7.00 Total (empty) 1 7.00 7.00", summary);
        Assert.Contains("North No 1 4.25 4.25 Yes 1 10.50 10.50 Total North 2 14.75 7.38", summary);
        Assert.Contains("South Yes 1 100.00 100.00 Total South 1 100.00 100.00", summary);
        Assert.Contains("Grand total 4 121.75 30.44", summary);

        var filtered = Text(await ann.GetStringAsync("/reports/by-region?from=2024-05-10&f_paid=1"));
        Assert.True(filtered.Contains("Grand total 2 107.00 53.50", StringComparison.Ordinal), filtered);
        var byRegion = Text(await ann.GetStringAsync("/reports/by-region?f_region=N"));
        Assert.Contains("Grand total 2 14.75 7.38", byRegion);

        var months = Text(await ann.GetStringAsync("/reports/by-month"));
        Assert.True(months.Contains("2024-05 2024-Q2 114.75", StringComparison.Ordinal), months);
        Assert.Contains("2024-05 2024-Q2 114.75", months);
        Assert.Contains("2024-06 2024-Q2 7.00", months);

        var details = Text(await ann.GetStringAsync("/reports/details"));
        Assert.Contains("a4 <b>", details);
        Assert.Contains("North", details);

        var grid = await ann.GetStringAsync("/reports/grid");
        Assert.Contains("121.75", Text(grid));
        var drill = Regex.Match(grid, "href=\"(/reports/grid/rows\\?[^\"]+)\"");
        Assert.True(drill.Success, "no drill-down link");
        using var rows = await ann.GetAsync(WebUtility.HtmlDecode(drill.Groups[1].Value));
        var rowsText = await rows.Content.ReadAsStringAsync();
        Assert.True(rows.IsSuccessStatusCode, rowsText[..Math.Min(3000, rowsText.Length)]);
        Assert.Contains("1 rows", Text(rowsText));
        Assert.Contains("a4 <b>", Text(rowsText));

        Assert.Contains("114.75", Text(await ann.GetStringAsync("/reports/chart")));
        var calendar = await ann.GetStringAsync("/reports/days?month=2024-05");
        Assert.Matches("data-day=\"2024-05-02\"><div class=\"small fw-semibold\">2</div><a [^>]+>a1</a><a [^>]+>a2</a></td>", calendar);
        Assert.DoesNotContain(">b1<", calendar);
    }

    [Fact]
    public void TheEditorsReportPreviewRunsOnTheServer()
    {
        if (!_site.Ready)
        {
            return;
        }

        var project = _site.Project!;
        string[][] Rows(string name)
        {
            var result = Generation.ReportPreview.Run(project, project.Reports.Single(r => r.Name == name), _site.ConnectionString);
            Assert.True(result.Headers.Count > 0, result.Message);
            return result.Rows.ToArray();
        }

        // The preview reads every row (no signed-in user), so bob's sales count too.
        var summary = Rows("by-region");
        Assert.Contains(summary, r => r.SequenceEqual(new[] { "North", "Yes", "2", "1,010.5", "505.25" }));
        Assert.Contains(Rows("by-month"), r => r.SequenceEqual(new[] { "2024-05", "2024-Q2", "3,114.75" }));
        Assert.Contains(Rows("grid"), r => r[0] == "South");
        Assert.Equal(["2024-05", "2024-06"], Rows("chart").Select(r => r[0]));
        Assert.Equal("2024-06-01", Rows("days")[0][0]);
    }

    [Fact]
    public async Task DetailGridsFilterSortAndDeleteInCascade()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        var view = Text(await ann.GetStringAsync("/orders/view/1"));
        Assert.Contains("Hammer", view);
        Assert.Contains("Nails", view);
        var filtered = Text(await ann.GetStringAsync("/orders/view/1?dq0=-nails"));
        Assert.Contains("Hammer", filtered);
        Assert.DoesNotContain("Nails", filtered);
        var sorted = Text(await ann.GetStringAsync("/orders/view/1?ds0=qty&dd0=desc"));
        Assert.True(sorted.IndexOf("Nails", StringComparison.Ordinal) < sorted.IndexOf("Saw", StringComparison.Ordinal));

        using var deleted = await _site.PostFormAsync(ann, "/orders/delete/2", "/orders/delete/2", []);
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM order_lines WHERE order_id = 2")));
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM orders WHERE id = 2")));
    }
}

public sealed class SqlServerSiteTests(SqlServerSite site) : ServerSiteTests<SqlServerSite>(site), IClassFixture<SqlServerSite>;

public sealed class MySqlSiteTests(MySqlSite site) : ServerSiteTests<MySqlSite>(site), IClassFixture<MySqlSite>;

/// <summary>The audit trail on a server: entries for pages, the API and cascades, written in the change's transaction, and the log page.</summary>
public abstract class ServerAuditTests<TSite> where TSite : ServerSite
{
    private readonly TSite _site;

    protected ServerAuditTests(TSite site) => _site = site;

    private async Task<long> Count(string where) => Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM audit_trail WHERE " + where));

    private async Task<string> Latest(string column, string table, string action) => Convert.ToString(await _site.Scalar(
        $"SELECT {column} FROM audit_trail WHERE id = (SELECT MAX(id) FROM audit_trail WHERE table_name = '{table}' AND action = '{action}')")) ?? string.Empty;

    [Fact]
    public async Task PagesWriteEntriesWithTheUserAndTheValues()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        using var added = await _site.PostFormAsync(ann, "/orders/add", "/orders/add", new() { ["customer"] = "Audit Co", ["placed"] = "2024-09-01T10:00" });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        var id = Convert.ToInt64(await _site.Scalar("SELECT MAX(id) FROM orders"));

        // The insert trigger of the SQL Server and MySQL schemas writes to the audit table too; the entry must carry the order's own key.
        Assert.Equal(id.ToString(), await Latest("record_key", "orders", "insert"));
        Assert.Equal("ann", await Latest("user_name", "orders", "insert"));
        Assert.Contains("Audit Co", await Latest("changes", "orders", "insert"));

        using var edited = await _site.PostFormAsync(ann, $"/orders/edit/{id}", $"/orders/edit/{id}", new() { ["customer"] = "Audit Renamed", ["placed"] = "2024-09-01T10:00" });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal(1L, await Count($"table_name = 'orders' AND action = 'update' AND record_key = '{id}'"));
        var update = await Latest("changes", "orders", "update");
        Assert.Contains("Audit Co", update);
        Assert.Contains("Audit Renamed", update);

        using var deleted = await _site.PostFormAsync(ann, $"/orders/delete/{id}", $"/orders/delete/{id}", []);
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(1L, await Count($"table_name = 'orders' AND action = 'delete' AND record_key = '{id}'"));
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar($"SELECT COUNT(*) FROM orders WHERE id = {id}")));
    }

    [Fact]
    public async Task TheApiWritesEntriesForPostPutAndDelete()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        using var created = await ann.PostAsJsonAsync("/api/sales", new { owner = "x", region = "S", day = "2024-07-04", amount = 12.5m, paid = true, note = "audited", stamp = 1720094400, photo = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(1L, await Count($"table_name = 'sales' AND action = 'insert' AND record_key = '{id}' AND user_name = 'ann'"));

        using var updated = await ann.PutAsJsonAsync($"/api/sales/{id}", new { owner = "ann", region = "S", day = "2024-07-04", amount = 99m, paid = true, note = "audited", stamp = 1720094400, photo = (string?)null });
        Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
        Assert.Equal(1L, await Count($"table_name = 'sales' AND action = 'update' AND record_key = '{id}'"));
        Assert.Contains("amount", await Latest("changes", "sales", "update"));

        using var deleted = await ann.DeleteAsync($"/api/sales/{id}");
        Assert.True(deleted.IsSuccessStatusCode);
        Assert.Equal(1L, await Count($"table_name = 'sales' AND action = 'delete' AND record_key = '{id}'"));
    }

    [Fact]
    public async Task ACascadeDeleteRecordsTheOrderAndItsLines()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        using var deleted = await _site.PostFormAsync(ann, "/orders/delete/1", "/orders/delete/1", []);
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        Assert.Equal(1L, await Count("table_name = 'orders' AND action = 'delete' AND record_key = '1'"));
        Assert.Equal(3L, await Count("table_name = 'order_lines' AND action = 'delete'"));
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM order_lines WHERE order_id = 1")));
    }

    [Fact]
    public async Task TheLogPageListsAndFiltersTheEntries()
    {
        if (!_site.Ready)
        {
            return;
        }

        using var ann = await _site.SignInAsync("ann", 1);
        using var created = await ann.PostAsJsonAsync("/api/sales", new { owner = "ann", region = "N", day = "2024-07-05", amount = 1m, paid = false, note = "for the page", stamp = (long?)null, photo = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var all = await ann.GetStringAsync("/audit-trail");
        Assert.Contains("ann", all);
        Assert.Contains("for the page", all);

        var inserts = await ann.GetStringAsync("/audit-trail?t=sales&a=insert&u=ann");
        Assert.Contains("for the page", inserts);
        Assert.Contains("text-bg-success", inserts);
        Assert.DoesNotContain("text-bg-danger", inserts);

        var none = await ann.GetStringAsync("/audit-trail?t=sales&u=nobody-like-this");
        Assert.DoesNotContain("for the page", none);
        Assert.Contains("<select", await ann.GetStringAsync("/audit-trail?page=99"));
    }
}

public sealed class SqlServerAuditTests(SqlServerAuditSite site) : ServerAuditTests<SqlServerAuditSite>(site), IClassFixture<SqlServerAuditSite>;

public sealed class MySqlAuditTests(MySqlAuditSite site) : ServerAuditTests<MySqlAuditSite>(site), IClassFixture<MySqlAuditSite>;
