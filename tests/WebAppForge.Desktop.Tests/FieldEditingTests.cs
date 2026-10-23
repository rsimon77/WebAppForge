using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;

namespace WebAppForge.Desktop.Tests;

public class FieldEditingTests
{
    [NorthwindFact]
    public async Task FieldTypeAndDisplayEditsPersistThroughSaveAndLoad()
    {
        using var folder = new TempFolder();
        var main = await Harness.NorthwindProjectAsync(folder);
        main.Tables.SelectedTable = main.Current.Tables.Single(t => t.Name == "Products");

        var field = main.Tables.Fields.Single(f => f.Name == "UnitPrice");
        field.Type = FieldType.Integer;
        field.Caption = "Unit price (USD)";
        field.ShowInList = false;
        field.ShowInAdd = false;
        field.Searchable = false;

        // The grid row and the model object are the same instance.
        Assert.Same(field, main.Current.Tables.Single(t => t.Name == "Products").Fields.Single(f => f.Name == "UnitPrice"));

        await main.SaveProjectCommand.ExecuteAsync(null);
        var loaded = ProjectFile.Load(folder.File("project.xml"));

        var stored = loaded.Tables.Single(t => t.Name == "Products").Fields.Single(f => f.Name == "UnitPrice");
        Assert.Equal(FieldType.Integer, stored.Type);
        Assert.Equal("Unit price (USD)", stored.Caption);
        Assert.False(stored.ShowInList);
        Assert.False(stored.ShowInAdd);
        Assert.False(stored.Searchable);
        Assert.True(stored.ShowInView);
        Assert.True(stored.ShowInEdit);
    }

    [NorthwindFact]
    public async Task TableCaptionAndPageFlagsRoundTripThroughSaveAndLoad()
    {
        using var folder = new TempFolder();
        var main = await Harness.NorthwindProjectAsync(folder);
        var table = main.Current.Tables.Single(t => t.Name == "Shippers");
        table.Caption = "Shipping companies";
        table.Pages.Delete = false;
        table.Pages.PageSize = 5;
        table.Sort.Add(new SortField { Field = "CompanyName", Descending = true });

        await main.SaveProjectCommand.ExecuteAsync(null);
        var loaded = ProjectFile.Load(folder.File("project.xml"));

        var stored = loaded.Tables.Single(t => t.Name == "Shippers");
        Assert.Equal("Shipping companies", stored.Caption);
        Assert.False(stored.Pages.Delete);
        Assert.True(stored.Pages.View);
        Assert.Equal(5, stored.Pages.PageSize);
        var sort = Assert.Single(stored.Sort);
        Assert.Equal("CompanyName", sort.Field);
        Assert.True(sort.Descending);
    }
}
