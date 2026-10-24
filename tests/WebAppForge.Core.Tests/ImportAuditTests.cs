using WebAppForge.Core.Model;
using static WebAppForge.Core.Tests.ImportExportSiteTests;

namespace WebAppForge.Core.Tests;

/// <summary>The exchange shop with an audit trail on items and parts.</summary>
public sealed class AuditedExchangeSite : ExchangeSite
{
    protected override Project? Create()
    {
        var project = base.Create()!;
        project.Audit.Enabled = true;
        foreach (var name in new[] { "items", "parts" })
        {
            project.Tables.Single(t => t.Name == name).Audit.Enabled = true;
        }

        return project;
    }
}

/// <summary>An import into an audited table writes its entries in the import transaction.</summary>
public class ImportAuditTests : IClassFixture<AuditedExchangeSite>
{
    private readonly AuditedExchangeSite _site;

    public ImportAuditTests(AuditedExchangeSite site) => _site = site;

    private Task<long> Entries(string where) => _site.Count("SELECT COUNT(*) FROM audit_trail WHERE " + where);

    [Fact]
    public async Task AnImportWritesAnEntryForEachAddedAndUpdatedRow()
    {
        using var bob = await _site.SignInAsync("bob", 9);

        var parts = Page(await UploadAsync(bob, "/parts", "parts.csv", Csv("sku,label,stock\r\nA-1,Alpha,31\r\nG-7,Golf,2\r\n")));
        Assert.Contains("1 rows added, 1 rows updated, 0 rows skipped.", Text(await ApplyAsync(bob, "/parts", parts, "import")));
        Assert.Equal(1, await Entries("table_name = 'parts' AND action = 'insert' AND record_key = 'G-7' AND user_name = 'bob' AND changes LIKE '%Golf%'"));
        Assert.Equal(1, await Entries("table_name = 'parts' AND action = 'update' AND record_key = 'A-1' AND changes LIKE '%31%'"));

        // An auto-increment key is read back, so the entry names the new row.
        var items = Page(await UploadAsync(bob, "/items", "items.csv", Csv("name,qty\r\nAudited,2\r\n")));
        Assert.Contains("1 rows added", Text(await ApplyAsync(bob, "/items", items, "import")));
        var id = await _site.Count("SELECT id FROM items WHERE name = 'Audited'");
        Assert.Equal(1, await Entries($"table_name = 'items' AND action = 'insert' AND record_key = '{id}' AND changes LIKE '%Audited%'"));
    }

    [Fact]
    public async Task ARolledBackImportWritesNoEntry()
    {
        using var bob = await _site.SignInAsync("bob", 9);
        var before = await Entries("table_name = 'parts'");

        // Row 4 repeats the unique label Alpha, after row 2 updated Beta and row 3 added Charlie.
        var preview = Page(await UploadAsync(bob, "/parts", "parts.csv", Csv("sku,label,stock\r\nB-2,Beta,40\r\nC-3,Charlie,5\r\nD-4,Alpha,6\r\n")));
        Assert.Contains("rolled back", Text(await ApplyAsync(bob, "/parts", preview, "import")));

        Assert.Equal(before, await Entries("table_name = 'parts'"));
    }
}
