using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Tests;

public class ForeignKeyReaderTests
{
    [Fact]
    public void ASingleForeignKeyIsRead()
    {
        var tables = Read("""
            CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER REFERENCES customers (id) ON DELETE CASCADE ON UPDATE SET NULL);
            """);

        var fk = Assert.Single(tables.Single(t => t.Name == "orders").ForeignKeys);
        Assert.Equal("customers", fk.ReferencedTable);
        Assert.Equal(["customer_id"], fk.Columns);
        Assert.Equal(["id"], fk.ReferencedColumns);
        Assert.Equal("CASCADE", fk.OnDelete);
        Assert.Equal("SET NULL", fk.OnUpdate);
        Assert.Empty(tables.Single(t => t.Name == "customers").ForeignKeys);
    }

    [Fact]
    public void ACompositeKeyKeepsTheColumnOrderAndIsOneKey()
    {
        var tables = Read("""
            CREATE TABLE regions (country TEXT, code TEXT, PRIMARY KEY (country, code));
            CREATE TABLE shops (
                id INTEGER PRIMARY KEY, code TEXT, country TEXT, other INTEGER,
                FOREIGN KEY (country, code) REFERENCES regions (country, code) ON DELETE RESTRICT
            );
            """);

        var fk = Assert.Single(tables.Single(t => t.Name == "shops").ForeignKeys);
        Assert.Equal(["country", "code"], fk.Columns);
        Assert.Equal(["country", "code"], fk.ReferencedColumns);
        Assert.Equal("RESTRICT", fk.OnDelete);
        Assert.Equal("NO ACTION", fk.OnUpdate);
    }

    [Fact]
    public void ACompositeKeyWrittenInADifferentOrderMapsEachColumnToItsTarget()
    {
        var tables = Read("""
            CREATE TABLE regions (country TEXT, code TEXT, PRIMARY KEY (country, code));
            CREATE TABLE shops (a TEXT, b TEXT, FOREIGN KEY (b, a) REFERENCES regions (code, country));
            """);

        var fk = Assert.Single(tables.Single(t => t.Name == "shops").ForeignKeys);
        Assert.Equal(["b", "a"], fk.Columns);
        Assert.Equal(["code", "country"], fk.ReferencedColumns);
    }

    [Fact]
    public void SeveralKeysOfOneTableAreSeparate()
    {
        var tables = Read("""
            CREATE TABLE people (id INTEGER PRIMARY KEY);
            CREATE TABLE places (id INTEGER PRIMARY KEY);
            CREATE TABLE trips (id INTEGER PRIMARY KEY, who INTEGER REFERENCES people, "from" INTEGER REFERENCES places (id), "to" INTEGER REFERENCES places (id));
            """);

        var keys = tables.Single(t => t.Name == "trips").ForeignKeys;
        Assert.Equal(3, keys.Count);
        Assert.Equal(["people", "places", "places"], keys.Select(k => k.ReferencedTable).Order());
        Assert.All(keys, k => Assert.Single(k.Columns));
        Assert.Equal(["from", "to", "who"], keys.Select(k => k.Columns[0]).Order());
    }

    [Fact]
    public void ASelfReferenceIsRead()
    {
        var tables = Read("CREATE TABLE employees (id INTEGER PRIMARY KEY, boss_id INTEGER REFERENCES employees (id));");

        var fk = Assert.Single(tables.Single().ForeignKeys);
        Assert.Equal("employees", fk.ReferencedTable);
        Assert.Equal(["boss_id"], fk.Columns);
        Assert.Equal(["id"], fk.ReferencedColumns);
    }

    [Fact]
    public void AKeyWithoutTargetColumnsReferencesThePrimaryKeyOfTheOtherTable()
    {
        // Created in the other order too: the referenced table is read after the table that references it.
        var tables = Read("""
            CREATE TABLE a_orders (id INTEGER PRIMARY KEY, customer INTEGER REFERENCES z_customers);
            CREATE TABLE z_customers (cid INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE b_lines (o1 TEXT, o2 TEXT, FOREIGN KEY (o1, o2) REFERENCES y_composite);
            CREATE TABLE y_composite (k2 TEXT, k1 TEXT, PRIMARY KEY (k1, k2));
            """);

        var simple = Assert.Single(tables.Single(t => t.Name == "a_orders").ForeignKeys);
        Assert.Equal(["customer"], simple.Columns);
        Assert.Equal(["cid"], simple.ReferencedColumns);

        var composite = Assert.Single(tables.Single(t => t.Name == "b_lines").ForeignKeys);
        Assert.Equal(["o1", "o2"], composite.Columns);
        Assert.Equal(["k1", "k2"], composite.ReferencedColumns);
    }

    [Fact]
    public void TablesWithoutKeysHaveNone()
    {
        var tables = Read("CREATE TABLE notes (id INTEGER PRIMARY KEY, body TEXT); CREATE TABLE logs (msg TEXT);");

        Assert.All(tables, t => Assert.Empty(t.ForeignKeys));
    }

    [Fact]
    public void ATableNameWithAQuoteIsQuotedSafely()
    {
        var tables = Read("""
            CREATE TABLE "pa""rent" (id INTEGER PRIMARY KEY);
            CREATE TABLE "chi""ld" (id INTEGER PRIMARY KEY, p INTEGER REFERENCES "pa""rent" (id));
            """);

        var fk = Assert.Single(tables.Single(t => t.Name == "chi\"ld").ForeignKeys);
        Assert.Equal("pa\"rent", fk.ReferencedTable);
    }

    [Fact]
    public void ReadKeysSurviveTheProjectFile()
    {
        var tables = Read("""
            CREATE TABLE customers (id INTEGER PRIMARY KEY);
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER REFERENCES customers (id) ON DELETE CASCADE);
            """);
        var project = new Project { Name = "x", ProjectId = Guid.NewGuid() };
        project.Tables.AddRange(tables);

        var loaded = ProjectFile.Read(ProjectFile.Write(project)).Tables.Single(t => t.Name == "orders");

        var fk = Assert.Single(loaded.ForeignKeys);
        Assert.Equal("customers", fk.ReferencedTable);
        Assert.Equal("CASCADE", fk.OnDelete);
    }

    private static IReadOnlyList<TableInfo> Read(string sql)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            return new SqliteSchemaReader().ReadTables(connectionString);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
