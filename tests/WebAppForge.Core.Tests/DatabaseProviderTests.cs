using WebAppForge.Core.Build;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Tests;

/// <summary>Provider keys, server connection settings in the project file, their resolution, Docker output and type mapping.</summary>
public class DatabaseProviderTests
{
    [Fact]
    public void ProviderKeysAreNormalizedAndNamed()
    {
        Assert.Equal(["sqlite", "sqlserver", "azuresql", "mysql"], DatabaseProviders.All);
        Assert.True(DatabaseProviders.IsKnown(" MySQL "));
        Assert.True(DatabaseProviders.IsKnown(""));
        Assert.False(DatabaseProviders.IsKnown("oracle"));
        Assert.True(DatabaseProviders.IsSqlServer("AzureSql"));
        Assert.False(DatabaseProviders.IsServer("sqlite"));
        Assert.Equal("Azure SQL", DatabaseProviders.DisplayName("azuresql"));
        Assert.Contains("sqlite, sqlserver, azuresql, mysql", ProjectGenerator.UnsupportedProviderMessage("oracle"));
    }

    [Fact]
    public void ServerSettingsRoundTripAndSqliteProjectsWriteOnlyProviderAndPath()
    {
        var project = new Project { Name = "srv" };
        project.Database = new DatabaseSettings { Provider = "mysql", ConnectionString = "Server=db;Database=shop", ConnectionStringVariable = "SHOP_DB" };
        var loaded = ProjectFile.Read(ProjectFile.Write(project));
        Assert.Equal("mysql", loaded.Database.Provider);
        Assert.Equal("Server=db;Database=shop", loaded.Database.ConnectionString);
        Assert.Equal("SHOP_DB", loaded.Database.ConnectionStringVariable);

        var sqlite = new Project { Name = "file" };
        sqlite.Database.Path = "data/app.db";
        var element = ProjectFile.Write(sqlite).Root!.Element("database")!;
        Assert.Equal(["provider", "path"], element.Attributes().Select(a => a.Name.LocalName));
    }

    [Fact]
    public void AServerConnectionComesFromTheVariableWhenOneIsNamed()
    {
        var name = "WEBAPPFORGE_TEST_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var database = new DatabaseSettings { Provider = "sqlserver", ConnectionString = "Server=fallback" };
        Assert.Equal("Server=fallback", database.ResolveConnectionString("/p"));
        Assert.Equal("Server=fallback", database.GeneratedConnectionString("/p"));

        database.ConnectionStringVariable = name;
        Assert.Equal(string.Empty, database.ResolveConnectionString("/p"));
        Assert.Equal(string.Empty, database.GeneratedConnectionString("/p"));
        Environment.SetEnvironmentVariable(name, "Server=fromvariable");
        try
        {
            Assert.Equal("Server=fromvariable", database.ResolveConnectionString("/p"));
            Assert.Equal(string.Empty, database.GeneratedConnectionString("/p"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        var file = new DatabaseSettings { Path = "data/app.db" };
        Assert.Equal("Data Source=" + Path.GetFullPath("/p/data/app.db"), file.GeneratedConnectionString("/p"));
    }

    [Theory]
    [InlineData("", "ConnectionStrings__Default")]
    [InlineData("SHOP_DB", "SHOP_DB")]
    public void DockerFilesOfAServerSiteKeepTheConnectionOutOfTheImage(string variable, string expected)
    {
        var project = new Project { Name = "Server Shop" };
        project.Database = new DatabaseSettings { Provider = "azuresql", ConnectionString = "Server=tcp:x;Password=secret", ConnectionStringVariable = variable };
        var dockerfile = DockerGenerator.Dockerfile(project);
        Assert.DoesNotContain("secret", dockerfile);
        Assert.DoesNotContain("VOLUME", dockerfile);
        Assert.DoesNotContain("/data", dockerfile);
        Assert.Contains($"#   docker run -e {expected}=\"...\"", dockerfile);

        var compose = DockerGenerator.Compose(project);
        Assert.Contains($"      {expected}: \"${{{expected}}}\"\n", compose);
        Assert.DoesNotContain("volumes:", compose);
        Assert.DoesNotContain("secret", compose);
    }

    [Theory]
    [InlineData("bigint", FieldType.Integer)]
    [InlineData("tinyint", FieldType.Integer)]
    [InlineData("bit", FieldType.Boolean)]
    [InlineData("money", FieldType.Decimal)]
    [InlineData("float", FieldType.Decimal)]
    [InlineData("date", FieldType.Date)]
    [InlineData("datetimeoffset", FieldType.DateTime)]
    [InlineData("smalldatetime", FieldType.DateTime)]
    [InlineData("varbinary", FieldType.Binary)]
    [InlineData("uniqueidentifier", FieldType.Text)]
    [InlineData("nvarchar", FieldType.Text)]
    [InlineData("time", FieldType.Text)]
    public void SqlServerTypesMapToFieldTypes(string type, FieldType expected) =>
        Assert.Equal(expected, SqlServerSchemaReader.MapType(type));

    [Theory]
    [InlineData("tinyint", "tinyint(1)", FieldType.Boolean)]
    [InlineData("bit", "bit(1)", FieldType.Boolean)]
    [InlineData("tinyint", "tinyint(4)", FieldType.Integer)]
    [InlineData("bigint", "bigint unsigned", FieldType.Integer)]
    [InlineData("year", "year", FieldType.Integer)]
    [InlineData("double", "double", FieldType.Decimal)]
    [InlineData("datetime", "datetime(6)", FieldType.DateTime)]
    [InlineData("timestamp", "timestamp", FieldType.DateTime)]
    [InlineData("longblob", "longblob", FieldType.Binary)]
    [InlineData("enum", "enum('a','b')", FieldType.Text)]
    [InlineData("json", "json", FieldType.Text)]
    public void MySqlTypesMapToFieldTypes(string dataType, string columnType, FieldType expected) =>
        Assert.Equal(expected, MySqlSchemaReader.MapType(dataType, columnType));

    [Fact]
    public void TablesOutsideDboAreNamedWithTheirSchemaAndQuotedInParts()
    {
        Assert.Equal("orders", SqlServerSchemaReader.TableName("dbo", "orders"));
        Assert.Equal("Sales.Orders", SqlServerSchemaReader.TableName("Sales", "Orders"));
        Assert.Equal("[Sales].[Orders]", SqlDialect.SqlServer.Quote("Sales.Orders"));
        Assert.Equal("[order id]", SqlDialect.SqlServer.Quote("order id"));
        Assert.Equal("`order id`", SqlDialect.MySql.Quote("order id"));
        Assert.Throws<NotSupportedException>(() => SqlDialect.SqlServer.Quote("bad]name"));
        Assert.Throws<NotSupportedException>(() => SqlDialect.MySql.Quote("bad`name"));
        Assert.Equal("'a\\\\b''c'", SqlDialect.MySql.Text("a\\b'c"));
        Assert.Equal("N'a''b'", SqlDialect.SqlServer.Text("a'b"));
    }
}
