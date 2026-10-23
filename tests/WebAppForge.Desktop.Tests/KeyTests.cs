using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;

namespace WebAppForge.Desktop.Tests;

public class KeyTests
{
    [NorthwindFact]
    public async Task CompositeKeyKeepsKeyOrderAndIsMarkedOnFields()
    {
        using var folder = new TempFolder();
        var main = await Harness.NorthwindProjectAsync(folder);
        var orderDetails = main.Current.Tables.Single(t => t.Name == "Order Details");

        Assert.Equal(new[] { "OrderID", "ProductID" }, orderDetails.PrimaryKeys.Select(f => f.Name));
        Assert.Equal(new[] { 1, 2 }, orderDetails.PrimaryKeys.Select(f => f.KeyPosition));
        Assert.Equal("OrderID", orderDetails.PrimaryKey!.Name);
        Assert.DoesNotContain(orderDetails.PrimaryKeys, f => f.IsAutoIncrement);

        var unitPrice = orderDetails.Fields.Single(f => f.Name == "UnitPrice");
        Assert.False(unitPrice.IsPrimaryKey);

        // The desktop Fields grid shows the same flags.
        main.Tables.SelectedTable = orderDetails;
        Assert.Equal(new[] { "OrderID", "ProductID" }, main.Tables.Fields.Where(f => f.IsPrimaryKey).Select(f => f.Name));
    }

    [NorthwindFact]
    public async Task CompositeKeyOrderSurvivesSaveAndLoad()
    {
        using var folder = new TempFolder();
        var main = await Harness.NorthwindProjectAsync(folder);
        await main.SaveProjectCommand.ExecuteAsync(null);

        var loaded = ProjectFile.Load(folder.File("project.xml"));
        var orderDetails = loaded.Tables.Single(t => t.Name == "Order Details");

        Assert.Equal(new[] { "OrderID", "ProductID" }, orderDetails.PrimaryKeys.Select(f => f.Name));
    }

    [Fact]
    public void ReconnectTakesKeyOrderFromDatabaseNotFromStaleProjectSettings()
    {
        // A saved project whose key positions no longer match the database key (OrderID, ProductID).
        var project = new Project();
        var orderDetails = new TableInfo { Name = "Order Details", Caption = "Order details" };
        orderDetails.Fields.Add(new FieldInfo { Name = "OrderID", IsPrimaryKey = true, KeyPosition = 2 });
        orderDetails.Fields.Add(new FieldInfo { Name = "ProductID", IsPrimaryKey = true, KeyPosition = 1 });
        project.Tables.Add(orderDetails);

        var discovered = new List<TableInfo>
        {
            new()
            {
                Name = "Order Details",
                Fields =
                {
                    new FieldInfo { Name = "OrderID", IsPrimaryKey = true, KeyPosition = 1, IsRequired = true },
                    new FieldInfo { Name = "ProductID", IsPrimaryKey = true, KeyPosition = 2, IsRequired = true },
                    new FieldInfo { Name = "Quantity", Type = FieldType.Integer },
                },
            },
        };

        SchemaMerger.Merge(project, discovered);

        var merged = project.Tables.Single();
        Assert.Equal(new[] { "OrderID", "ProductID" }, merged.PrimaryKeys.Select(f => f.Name));
        Assert.Equal(new[] { 1, 2 }, merged.PrimaryKeys.Select(f => f.KeyPosition));
    }
}
