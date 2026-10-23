using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Tests;

public class SqliteSchemaReaderTests
{
    [Fact]
    public void ReadsTablesColumnsKeysAndTypes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE orders (
                        order_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        customer TEXT NOT NULL,
                        total REAL,
                        placed_at DATETIME,
                        paid BOOLEAN,
                        receipt BLOB
                    );
                    """;
                command.ExecuteNonQuery();
            }

            var tables = new SqliteSchemaReader().ReadTables(connectionString);

            var orders = tables.Single(t => t.Name == "orders");
            var key = orders.PrimaryKey!;
            Assert.Equal("order_id", key.Name);
            Assert.True(key.IsAutoIncrement);
            Assert.Equal(FieldType.Integer, key.Type);

            var customer = orders.Fields.Single(f => f.Name == "customer");
            Assert.Equal(FieldType.Text, customer.Type);
            Assert.True(customer.IsRequired);

            Assert.Equal(FieldType.Decimal, orders.Fields.Single(f => f.Name == "total").Type);
            Assert.Equal(FieldType.DateTime, orders.Fields.Single(f => f.Name == "placed_at").Type);
            Assert.Equal(FieldType.Boolean, orders.Fields.Single(f => f.Name == "paid").Type);
            Assert.Equal(FieldType.Binary, orders.Fields.Single(f => f.Name == "receipt").Type);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
