using System.Text.RegularExpressions;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>
/// The richest sample projects generated for SQL Server and MySQL: every file must be free of SQLite names and SQL, and the site
/// must build without warnings. No database is needed; the running sites are tested in <see cref="ServerSiteTests{TSite}"/>.
/// </summary>
public class DialectBuildTests
{
    private static Project Sample(string sample, string root) => sample switch
    {
        "customized" => Customized(),
        "masterdetail" => MasterDetail(root),
        "audited" => Audited(root),
        "exchange" => Exchange(),
        _ => ReportGenerationTests.WithReports("secure"),
    };

    private static Project Customized()
    {
        var project = CustomizationSamples.Create("localized");
        CustomizationSamples.Customize(project);
        foreach (var table in project.Tables)
        {
            table.Designs.List.FilterBuilder = true;
            table.Designs.List.QuickFilter = true;
            table.Designs.List.ResetButton = true;
        }

        project.ImportExport = new ImportExportSettings { Enabled = true, ExportFormats = DataFormats.All, ImportFormats = DataFormats.All };
        return project;
    }

    /// <summary>Export and import of every table in every format, with an update-by-key import and a lookup to check.</summary>
    private static Project Exchange()
    {
        var project = CustomizationSamples.Create("secure");
        project.ImportExport = new ImportExportSettings { Enabled = true, ExportFormats = DataFormats.All, ImportFormats = DataFormats.All, ImportMinLevel = 2 };
        project.Tables.Single(t => t.Name == "codes").ImportExport.ImportMode = ImportMode.Upsert;
        project.Tables.Single(t => t.Name == "order_items").ImportExport.ExportMaxRows = 100;
        project.Tables.Single(t => t.Name == "notes").Fields.Single(f => f.Name == "body").Ui.Lookup =
            new FieldLookup { Table = "codes", KeyField = "code", DisplayField = "label" };
        return project;
    }

    private static Project MasterDetail(string root)
    {
        var project = MdSupport.SampleProject(Path.Combine(root, "md.db"));
        foreach (var link in project.Tables.SelectMany(t => t.Details))
        {
            link.FilterAndSort = true;
        }

        return project;
    }

    /// <summary>The master/detail sample with an audit trail on the tables of its cascades.</summary>
    private static Project Audited(string root)
    {
        var project = MasterDetail(root);
        project.Audit.Enabled = true;
        project.Audit.RecordIpAddress = true;
        foreach (var name in new[] { "customers", "orders", "order_lines", "line_notes" })
        {
            project.Tables.Single(t => t.Name == name).Audit.Enabled = true;
        }

        return project;
    }

    [Theory]
    [InlineData(DatabaseProviders.SqlServer, "customized")]
    [InlineData(DatabaseProviders.SqlServer, "masterdetail")]
    [InlineData(DatabaseProviders.SqlServer, "reports")]
    [InlineData(DatabaseProviders.SqlServer, "audited")]
    [InlineData(DatabaseProviders.MySql, "audited")]
    [InlineData(DatabaseProviders.SqlServer, "exchange")]
    [InlineData(DatabaseProviders.AzureSql, "reports")]
    [InlineData(DatabaseProviders.MySql, "customized")]
    [InlineData(DatabaseProviders.MySql, "masterdetail")]
    [InlineData(DatabaseProviders.MySql, "reports")]
    [InlineData(DatabaseProviders.MySql, "exchange")]
    public async Task SamplesBuildForEveryServerWithoutSqliteLeftovers(string provider, string sample)
    {
        var root = MdSupport.TempFolder();
        try
        {
            Directory.CreateDirectory(root);
            var project = Sample(sample, root);
            project.Database = new DatabaseSettings { Provider = provider, ConnectionString = "Server=db;Database=shop", ConnectionStringVariable = sample == "reports" ? "SHOP_DB" : string.Empty };
            var files = MdSupport.GenerateText(project, root);

            foreach (var (path, text) in files.Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal)))
            {
                // Comments may still name SQLite; code and SQL may not.
                var code = Regex.Replace(text, "//.*", string.Empty);
                var leftover = Regex.Match(code, "Sqlite|last_insert_rowid|BEGIN IMMEDIATE|COLLATE NOCASE|ON CONFLICT|unixepoch");
                Assert.False(leftover.Success, path + ": " + leftover.Value);
                if (DatabaseProviders.IsSqlServer(provider))
                {
                    var sql = Regex.Match(code, " LIMIT [0-9@]|AS TEXT\\)");
                    Assert.False(sql.Success, path + ": " + sql.Value);
                }
            }

            if (sample == "audited")
            {
                var audit = files["Web/Audit.cs"];
                var page = files["Pages/AuditTrailPages.cs"];
                if (DatabaseProviders.IsSqlServer(provider))
                {
                    Assert.Contains("IF OBJECT_ID(N'audit_trail', N'U') IS NULL CREATE TABLE [audit_trail]", audit);
                    Assert.Contains("BEGIN TRANSACTION", audit);
                    Assert.Contains("OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY", page);
                }
                else
                {
                    Assert.Contains("CREATE TABLE IF NOT EXISTS `audit_trail`", audit);
                    Assert.Contains("START TRANSACTION", audit);
                    Assert.Contains("LIMIT @limit OFFSET @offset", page);
                }
            }

            var csproj = files.Single(f => f.Key.EndsWith(".csproj", StringComparison.Ordinal)).Value;
            Assert.Contains(DatabaseProviders.IsSqlServer(provider) ? "Microsoft.Data.SqlClient" : "MySqlConnector", csproj);
            Assert.DoesNotContain("Microsoft.Data.Sqlite", csproj);
            Assert.Equal(provider == DatabaseProviders.AzureSql, csproj.Contains("Microsoft.Data.SqlClient.Extensions.Azure", StringComparison.Ordinal));
            if (sample == "reports")
            {
                Assert.Contains("\"Default\": \"\"", files["appsettings.json"]);
                Assert.Contains("Environment.GetEnvironmentVariable(\"SHOP_DB\")", files["Program.cs"]);
            }
            else
            {
                Assert.Contains("Server=db;Database=shop", files["appsettings.json"]);
            }

            var build = await MdSupport.RunDotnetAsync(Path.Combine(root, "site"), "build --nologo -v q -nodeReuse:false -warnaserror");
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}
