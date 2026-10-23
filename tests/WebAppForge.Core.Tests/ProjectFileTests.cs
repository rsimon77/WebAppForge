using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class ProjectFileTests
{
    [Fact]
    public void RoundTripPreservesProjectTablesAndFields()
    {
        var project = new Project
        {
            Name = "shop",
            ProjectId = Guid.Parse("095F6728-DF53-4763-A372-D8F2EAC959F2"),
            Database = new DatabaseSettings { Provider = "sqlite", Path = "shop.db" },
        };
        project.Tables.Add(new TableInfo
        {
            Name = "customers",
            Caption = "Customers",
            Fields =
            {
                new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true },
                new FieldInfo { Name = "name", Caption = "Name", Type = FieldType.Text, IsRequired = true },
                new FieldInfo { Name = "balance", Type = FieldType.Decimal },
            },
        });

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            ProjectFile.Save(project, path);
            var loaded = ProjectFile.Load(path);

            Assert.Equal("shop", loaded.Name);
            Assert.Equal(project.ProjectId, loaded.ProjectId);
            Assert.Equal("sqlite", loaded.Database.Provider);
            Assert.Equal("shop.db", loaded.Database.Path);

            var table = Assert.Single(loaded.Tables);
            Assert.Equal("customers", table.Name);
            Assert.Equal(3, table.Fields.Count);
            Assert.Equal("id", table.PrimaryKey?.Name);
            Assert.True(table.PrimaryKey!.IsAutoIncrement);
            Assert.Equal(FieldType.Decimal, table.Fields[2].Type);
            Assert.False(table.Fields[2].IsRequired);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRejectsWrongRootElement()
    {
        var document = System.Xml.Linq.XDocument.Parse("<other name='x' id='095F6728-DF53-4763-A372-D8F2EAC959F2' />");
        Assert.Throws<InvalidDataException>(() => ProjectFile.Read(document));
    }
}
