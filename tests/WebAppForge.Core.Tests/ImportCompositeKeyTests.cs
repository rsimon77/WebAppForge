using System.Net;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>Order lines that refer to their order by region and number, a foreign key of two columns, imported on a running site.</summary>
public class CompositeKeySite : ReportSiteBase
{
    protected override Project? Create()
    {
        MdSupport.Exec(DbPath, """
            CREATE TABLE orders (region TEXT NOT NULL, num INTEGER NOT NULL, PRIMARY KEY (region, num));
            CREATE TABLE lines (id INTEGER PRIMARY KEY AUTOINCREMENT, region TEXT, num INTEGER, item TEXT NOT NULL,
                FOREIGN KEY (region, num) REFERENCES orders (region, num));
            INSERT INTO orders (region, num) VALUES ('N', 1), ('S', 2);
            """);
        var project = ReportSupport.ProjectOn("Composite", DbPath);
        project.ImportExport = new ImportExportSettings { Enabled = true, ExportFormats = DataFormats.Csv, ImportFormats = DataFormats.Csv };
        return project;
    }

    public HttpClient Visitor() =>
        new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = Client.BaseAddress };
}

public class ImportCompositeKeyTests : IClassFixture<CompositeKeySite>
{
    private readonly CompositeKeySite _site;

    public ImportCompositeKeyTests(CompositeKeySite site) => _site = site;

    [Fact]
    public async Task ARowWhoseKeyPairIsNotInTheReferencedTableIsRefused()
    {
        Assert.Contains("References = new DataExchange.Reference[]", _site.Files["Pages/LinesPages.cs"]);
        Assert.Contains("ReferenceExists(", _site.Files["Web/DataExchange.cs"]);

        using var client = _site.Visitor();
        // N/1 exists; S/1 mixes the parts of two orders; a line without an order is not checked, like a foreign key in SQL.
        var csv = ImportExportSiteTests.Csv("region,num,item\nN,1,ok\nS,1,mixed\n,,loose\n");
        var preview = ImportExportSiteTests.Page(await ImportExportSiteTests.UploadAsync(client, "/lines", "lines.csv", csv));
        var refused = await ImportExportSiteTests.ApplyAsync(client, "/lines", preview, "import");
        Assert.Contains("Row 3: region, num: no row of orders has these values.", ImportExportSiteTests.Text(refused));
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM lines")));

        var again = ImportExportSiteTests.Page(await ImportExportSiteTests.UploadAsync(client, "/lines", "lines.csv", csv));
        await ImportExportSiteTests.ApplyAsync(client, "/lines", again, "import", skip: true);
        Assert.Equal(1L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM lines WHERE item = 'ok'")));
        Assert.Equal(1L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM lines WHERE item = 'loose'")));
        Assert.Equal(0L, Convert.ToInt64(await _site.Scalar("SELECT COUNT(*) FROM lines WHERE item = 'mixed'")));
    }

    [Fact]
    public void AKeyWithAColumnTheImportLeavesOutIsNotChecked()
    {
        var root = MdSupport.TempFolder();
        try
        {
            var project = ReportSupport.ProjectOn("Composite", _site.DbPath);
            project.ImportExport = new ImportExportSettings { Enabled = true, ExportFormats = DataFormats.Csv, ImportFormats = DataFormats.Csv };
            project.Tables.Single(t => t.Name == "lines").ImportExport.ExcludedFields.Add("num");
            var files = MdSupport.GenerateText(project, root);
            Assert.DoesNotContain("References =", files["Pages/LinesPages.cs"]);
            Assert.DoesNotContain("ReferenceExists(", files["Web/DataExchange.cs"]);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}
