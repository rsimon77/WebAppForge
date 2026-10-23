using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class ConnectTests
{
    [NorthwindFact]
    public async Task ConnectLoadsAllThirteenNorthwindTables()
    {
        using var folder = new TempFolder();

        var main = await Harness.NorthwindProjectAsync(folder);

        Assert.Equal("Connected: 13 table(s), 13 new.", main.Status);
        Assert.Equal(13, main.Current.Tables.Count);
        Assert.Equal(13, main.Tables.Items.Count);
        var names = main.Current.Tables.Select(t => t.Name).ToHashSet();
        Assert.Contains("Order Details", names);
        Assert.Contains("Regions", names);
        Assert.Contains("CustomerCustomerDemo", names);
    }

    [NorthwindFact]
    public async Task ReconnectKeepsExistingTableSettingsAndReportsAddedAndRemoved()
    {
        using var folder = new TempFolder();
        NorthwindDb.CopyInto(folder.Path);
        var main = await Harness.SavedProjectAsync(folder);
        main.Database.DatabasePath = "northwind.db";

        // Settings the user already chose: one table that exists in Northwind, one that does not.
        var customers = new TableInfo { Name = "Customers", Caption = "Customer list", Generate = false, Filter = "Country = 'UK'" };
        customers.Pages.PageSize = 50;
        customers.Fields.Add(new FieldInfo { Name = "CompanyName", Caption = "Company", ShowInList = false });
        main.Current.Tables.Add(customers);
        main.Current.Tables.Add(new TableInfo { Name = "Obsolete", Caption = "Obsolete" });

        await main.Database.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Connected: 13 table(s), 12 new, removed 1 no longer in the database (Obsolete).", main.Status);
        Assert.Equal(13, main.Current.Tables.Count);
        Assert.DoesNotContain(main.Current.Tables, t => t.Name == "Obsolete");

        var kept = main.Current.Tables.Single(t => t.Name == "Customers");
        Assert.Same(customers, kept);
        Assert.Equal("Customer list", kept.Caption);
        Assert.False(kept.Generate);
        Assert.Equal("Country = 'UK'", kept.Filter);
        Assert.Equal(50, kept.Pages.PageSize);

        var company = kept.Fields.Single(f => f.Name == "CompanyName");
        Assert.Equal("Company", company.Caption);
        Assert.False(company.ShowInList);
        Assert.True(kept.Fields.Count > 1, "Fields the project did not have yet should be added from the database.");
    }

    [NorthwindFact]
    public async Task ConnectWithoutSavedProjectAndRelativePathReportsWhy()
    {
        var main = new MainViewModel(new FakeDialogService());
        main.Database.DatabasePath = "northwind.db";

        await main.Database.ConnectCommand.ExecuteAsync(null);

        Assert.Equal("Save the project first. Relative database paths are resolved against the project folder.", main.Status);
        Assert.Empty(main.Current.Tables);
    }

    [NorthwindFact]
    public async Task ConnectReportsMissingDatabaseFileWithoutCreatingIt()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Database.DatabasePath = "missing.db";

        await main.Database.ConnectCommand.ExecuteAsync(null);

        Assert.StartsWith("Database file not found: ", main.Status);
        Assert.False(File.Exists(folder.File("missing.db")));
    }
}
