using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class ProjectGeneratorTests
{
    private static Project SampleProject()
    {
        var project = new Project
        {
            Name = "shop",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "shop.db" },
        };
        project.Tables.Add(new TableInfo
        {
            Name = "order_items",
            Fields =
            {
                new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true },
                new FieldInfo { Name = "sku", Type = FieldType.Text, IsRequired = true },
                new FieldInfo { Name = "qty", Type = FieldType.Integer, IsRequired = true },
                new FieldInfo { Name = "note", Type = FieldType.Text },
                new FieldInfo { Name = "shipped", Type = FieldType.Boolean },
            },
        });
        return project;
    }

    [Fact]
    public void GeneratesProgramModelsAndEndpointsForEachTable()
    {
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");
        try
        {
            var files = new ProjectGenerator().Generate(SampleProject(), output, output);

            Assert.Contains("Shop.csproj", files);
            Assert.Contains("Program.cs", files);
            Assert.Contains("appsettings.json", files);
            Assert.Contains("Models/OrderItems.cs", files);
            Assert.Contains("Endpoints/OrderItemsEndpoints.cs", files);

            var program = File.ReadAllText(Path.Combine(output, "Program.cs"));
            Assert.Contains("app.MapOrderItemsEndpoints(connectionString);", program);

            var endpoints = File.ReadAllText(Path.Combine(output, "Endpoints", "OrderItemsEndpoints.cs"));
            Assert.Contains("group.MapGet(\"/{id}\"", endpoints);
            Assert.Contains("group.MapPost(\"/\"", endpoints);
            Assert.Contains("group.MapPut(\"/{id}\"", endpoints);
            Assert.Contains("group.MapDelete(\"/{id}\"", endpoints);
            Assert.Contains("item.Id = await connection.ExecuteScalarAsync<long>(\"INSERT INTO \\\"order_items\\\"", endpoints);
            Assert.Contains("; SELECT last_insert_rowid()\", args);", endpoints);
            Assert.Contains("FROM \\\"order_items\\\"", endpoints);

            var model = File.ReadAllText(Path.Combine(output, "Models", "OrderItems.cs"));
            Assert.Contains("public long Id { get; set; }", model);
            Assert.Contains("public string Sku { get; set; } = string.Empty;", model);
            Assert.Contains("public string? Note { get; set; }", model);
            Assert.Contains("public bool? Shipped { get; set; }", model);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void RejectsUnsupportedProvider()
    {
        var project = SampleProject();
        project.Database.Provider = "oracle";
        var output = Path.Combine(Path.GetTempPath(), $"gen-{Guid.NewGuid():N}");

        Assert.Throws<NotSupportedException>(() => new ProjectGenerator().Generate(project, output, output));
    }

    [Theory]
    [InlineData("order_items", "OrderItems")]
    [InlineData("customers", "Customers")]
    [InlineData("2fa codes", "_2faCodes")]
    public void PascalCaseIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, Identifier.Pascal(input));
    }
}
