using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Tests;

public class SqliteEndToEndTests
{
    [Fact]
    public void GeneratesProjectFromSqliteSchemaWithoutLeftoverTokens()
    {
        var root = Path.Combine(Path.GetTempPath(), $"e2e-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(root, "store.db");
        var output = Path.Combine(root, "generated");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            Directory.CreateDirectory(root);
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE inventory (
                        item_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL,
                        unit_price REAL,
                        in_stock BOOLEAN NOT NULL,
                        received_at DATETIME,
                        photo BLOB
                    );
                    CREATE TABLE codes (
                        code TEXT PRIMARY KEY,
                        label TEXT
                    );
                    CREATE TABLE audit_log (
                        message TEXT,
                        logged_on DATE
                    );
                    """;
                command.ExecuteNonQuery();
            }

            var tables = new SqliteSchemaReader().ReadTables(connectionString);
            var project = new Project
            {
                Name = "store",
                Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath },
            };
            project.Tables.AddRange(tables);

            var files = new ProjectGenerator().Generate(project, output, root);

            Assert.Contains("Models/Inventory.cs", files);
            Assert.Contains("Pages/InventoryPages.cs", files);
            Assert.Contains("Pages/CodesPages.cs", files);
            Assert.Contains("Pages/AuditLogPages.cs", files);

            foreach (var file in files)
            {
                var content = File.ReadAllText(Path.Combine(output, file));
                Assert.DoesNotContain("@@", content);
            }

            var model = File.ReadAllText(Path.Combine(output, "Models", "Inventory.cs"));
            Assert.Contains("public long ItemId { get; set; }", model);
            Assert.Contains("public bool InStock { get; set; }", model);
            Assert.Contains("public DateTime? ReceivedAt { get; set; }", model);
            Assert.Contains("public byte[]? Photo { get; set; }", model);

            var codesPages = File.ReadAllText(Path.Combine(output, "Pages", "CodesPages.cs"));
            Assert.Contains("app.MapGet(Route + \"/view/{id}\", (string id) => ViewPage(id, connectionString));", codesPages);

            var auditPages = File.ReadAllText(Path.Combine(output, "Pages", "AuditLogPages.cs"));
            Assert.DoesNotContain("ViewPage", auditPages);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
