using Microsoft.Data.Sqlite;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Tests;

/// <summary>A small orders database and a project over it for the report tests.</summary>
public static class ReportSupport
{
    public sealed record Row(long Id, string? Customer, string? Region, string? Date, double? Amount, long? Qty, string Owner);

    public const string Schema = """
        CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, customer TEXT, region TEXT, odate DATE, amount REAL, qty INTEGER, owner TEXT);
        """;

    public static List<Row> Rows()
    {
        var rows = new List<Row>();
        var customers = new[] { "Ann", "Bob", "-Cy", "=Dee", null };
        var regions = new[] { "N", "S", "E" };
        for (var i = 1; i <= 40; i++)
        {
            var date = new DateTime(2023, 1, 1).AddDays(i * 17 % 500);
            rows.Add(new Row(i, customers[i % 5], i % 7 == 0 ? null : regions[i % 3],
                i % 11 == 0 ? null : date.ToString("yyyy-MM-dd"),
                i % 9 == 0 ? null : Math.Round(i * 3.7 + 1, 2), i % 6 == 0 ? null : i % 13 + 1, i % 2 == 0 ? "alice" : "bob"));
        }

        return rows;
    }

    public static string TempFolder() => Path.Combine(Path.GetTempPath(), $"rep-{Guid.NewGuid():N}");

    public static Project CreateProject(string root, bool seed = true)
    {
        Directory.CreateDirectory(root);
        var db = Path.Combine(root, "r.db");
        using (var c = new SqliteConnection($"Data Source={db}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = Schema;
            cmd.ExecuteNonQuery();
            if (seed)
            {
                foreach (var r in Rows())
                {
                    cmd.CommandText = "INSERT INTO orders (id, customer, region, odate, amount, qty, owner) VALUES ($i,$c,$r,$d,$a,$q,$o)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("$i", r.Id);
                    cmd.Parameters.AddWithValue("$c", (object?)r.Customer ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$r", (object?)r.Region ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$d", (object?)r.Date ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$a", (object?)r.Amount ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$q", (object?)r.Qty ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$o", r.Owner);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        SqliteConnection.ClearAllPools();
        var project = new Project { Name = "RepShop", Database = new DatabaseSettings { Provider = "sqlite", Path = db } };
        project.Tables.AddRange(new SqliteSchemaReader().ReadTables($"Data Source={db}"));
        return project;
    }

    public static string Conn(Project p) => $"Data Source={p.Database.Path}";

    public static ReportDef Summary(string name = "sum")
    {
        var r = new ReportDef { Name = name, Caption = "Sum", Table = "orders", Type = ReportType.Summary };
        r.Summary.Groups.Add(new ReportField { Field = "region" });
        r.Summary.Aggregates.Add(new ReportAggregate { Function = AggregateFunction.Count });
        return r;
    }

    public static ReportDef Crosstab(string name = "xt")
    {
        var r = new ReportDef { Name = name, Caption = "Xt", Table = "orders", Type = ReportType.Crosstab };
        r.Crosstab.Rows = new ReportField { Field = "region" };
        r.Crosstab.Columns = new ReportField { Field = "customer" };
        return r;
    }
}
