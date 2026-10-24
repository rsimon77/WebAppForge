using Microsoft.Data.Sqlite;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;

namespace WebAppForge.Desktop.Tests;

public class CoreReportServicesTests
{
    [Fact]
    public void ValidationMapsCoreIssuesToEditorIssues()
    {
        var project = OrdersProject(Path.Combine(Path.GetTempPath(), "unused.db"));
        project.Reports.Add(new ReportDef { Name = "broken", Type = ReportType.Summary, Table = "missing" });

        var issues = new CoreReportValidationService().Validate(project);

        var error = Assert.Single(issues, i => i.IsError && i.Message.Contains("missing"));
        Assert.Equal("broken", error.ReportName);
        // The editor adds the report name itself, so the message must not repeat it.
        Assert.StartsWith("the table 'missing'", error.Message);
        Assert.Equal("Error: broken: the table 'missing' does not exist.", error.Text);
    }

    [Fact]
    public async Task PreviewRunsTheSummaryGroupQuery()
    {
        using var temp = new TempDb();
        var project = OrdersProject(temp.Path);
        var report = SummaryReport();
        project.Reports.Add(report);

        var result = await new CoreReportPreviewService(() => null).RunAsync(project, report, CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, result.Rows.Length);
        // One row per country; the sums are 30 (DE: 10 + 20) and 5 (HU).
        var sums = result.Rows.Select(r => Convert.ToDecimal(r[^1])).OrderBy(x => x).ToArray();
        Assert.Equal([5m, 30m], sums);
    }

    [Fact]
    public async Task PreviewRefusesDashboards()
    {
        var project = OrdersProject(Path.Combine(Path.GetTempPath(), "unused.db"));
        var dashboard = new ReportDef { Name = "home", Type = ReportType.Dashboard };
        project.Reports.Add(dashboard);

        var result = await new CoreReportPreviewService(() => null).RunAsync(project, dashboard, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("dashboard", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewNeedsASavedProjectForRelativeDatabasePaths()
    {
        var project = OrdersProject("data/orders.db");
        var report = SummaryReport();
        project.Reports.Add(report);

        var result = await new CoreReportPreviewService(() => null).RunAsync(project, report, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Save the project first", result.Error);
    }

    [Fact]
    public async Task PreviewReportsAMissingDatabaseFile()
    {
        var project = OrdersProject(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        var report = SummaryReport();
        project.Reports.Add(report);

        var result = await new CoreReportPreviewService(() => null).RunAsync(project, report, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task PreviewExplainsWhyOwnedTablesCannotBePreviewed()
    {
        using var temp = new TempDb();
        var project = OrdersProject(temp.Path);
        project.Security = new SecuritySettings
        {
            Enabled = true,
            UserTable = "users",
            UserNameField = "name",
            PasswordField = "password",
        };
        project.Tables.Add(UsersTable());
        project.Tables[0].OwnerField = "country";
        var report = SummaryReport();
        project.Reports.Add(report);

        var result = await new CoreReportPreviewService(() => null).RunAsync(project, report, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("owner", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static ReportDef SummaryReport()
    {
        var report = new ReportDef { Name = "by_country", Caption = "By country", Type = ReportType.Summary, Table = "orders" };
        report.Summary.Groups.Add(new ReportField { Field = "country" });
        report.Summary.Aggregates.Add(new ReportAggregate { Field = "total", Function = AggregateFunction.Sum });
        return report;
    }

    private static Project OrdersProject(string databasePath)
    {
        var project = new Project { Name = "shop" };
        project.Database.Path = databasePath;
        var orders = new TableInfo { Name = "orders" };
        orders.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true });
        orders.Fields.Add(new FieldInfo { Name = "country", Type = FieldType.Text, IsRequired = true });
        orders.Fields.Add(new FieldInfo { Name = "total", Type = FieldType.Decimal });
        project.Tables.Add(orders);
        return project;
    }

    private static TableInfo UsersTable()
    {
        var users = new TableInfo { Name = "users" };
        users.Fields.Add(new FieldInfo { Name = "name", Type = FieldType.Text, IsPrimaryKey = true, IsRequired = true });
        users.Fields.Add(new FieldInfo { Name = "password", Type = FieldType.Text, IsRequired = true });
        return users;
    }

    private sealed class TempDb : IDisposable
    {
        public TempDb()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            using var connection = new SqliteConnection($"Data Source={Path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, country TEXT NOT NULL, total REAL);
                INSERT INTO orders (country, total) VALUES ('DE', 10), ('DE', 20), ('HU', 5);
                """;
            command.ExecuteNonQuery();
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A pooled handle may still be closing; the file is in the temp folder.
            }
        }
    }
}
