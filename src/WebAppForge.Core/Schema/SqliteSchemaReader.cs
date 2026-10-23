using WebAppForge.Core.Model;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Schema;

/// <summary>Reads tables and columns from a SQLite database using PRAGMA metadata.</summary>
public sealed class SqliteSchemaReader : ISchemaReader
{
    public IReadOnlyList<TableInfo> ReadTables(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var tableNames = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
                ORDER BY name
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        var tables = new List<TableInfo>();
        foreach (var tableName in tableNames)
        {
            tables.Add(ReadTable(connection, tableName));
        }

        ReadForeignKeys(connection, tables);
        return tables;
    }

    private static TableInfo ReadTable(SqliteConnection connection, string tableName)
    {
        var table = new TableInfo { Name = tableName, Caption = tableName };

        // PRAGMA table_info does not accept bound parameters, so quote the identifier.
        var columns = new List<(string Name, string DeclaredType, bool NotNull, bool HasDefault, int PkOrdinal)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // Columns: cid, name, type, notnull, dflt_value, pk (1-based position within the key, 0 if not in key)
                columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0, !reader.IsDBNull(4), reader.GetInt32(5)));
            }
        }

        var keyColumns = columns.Count(c => c.PkOrdinal > 0);
        foreach (var column in columns)
        {
            var isPk = column.PkOrdinal > 0;
            // A single INTEGER PRIMARY KEY is an alias for the rowid, so SQLite assigns it on insert.
            var isRowidAlias = isPk && keyColumns == 1 && column.DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);
            table.Fields.Add(new FieldInfo
            {
                Name = column.Name,
                Caption = column.Name,
                Type = MapType(column.DeclaredType),
                IsRequired = column.NotNull || isPk,
                HasDefault = column.HasDefault,
                IsPrimaryKey = isPk,
                KeyPosition = column.PkOrdinal,
                IsAutoIncrement = isRowidAlias,
            });
        }

        return table;
    }

    /// <summary>
    /// Reads PRAGMA foreign_key_list for every table: rows are grouped by key id in seq order. A key without explicit
    /// target columns references the primary key of the referenced table, which is resolved from that table's key.
    /// </summary>
    private static void ReadForeignKeys(SqliteConnection connection, List<TableInfo> tables)
    {
        foreach (var table in tables)
        {
            // Columns: id, seq, table, from, to, on_update, on_delete, match
            var rows = new List<(int Id, int Seq, string Table, string From, string? To, string OnUpdate, string OnDelete)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA foreign_key_list(\"{table.Name.Replace("\"", "\"\"")}\")";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6)));
                }
            }

            foreach (var group in rows.GroupBy(r => r.Id).OrderBy(g => g.Key))
            {
                var ordered = group.OrderBy(r => r.Seq).ToList();
                var fk = new ForeignKeyInfo
                {
                    ReferencedTable = ordered[0].Table,
                    OnUpdate = ordered[0].OnUpdate,
                    OnDelete = ordered[0].OnDelete,
                };
                var referenced = tables.FirstOrDefault(t => string.Equals(t.Name, fk.ReferencedTable, StringComparison.OrdinalIgnoreCase));
                var primaryKeys = referenced?.PrimaryKeys ?? [];
                for (var i = 0; i < ordered.Count; i++)
                {
                    fk.Columns.Add(ordered[i].From);
                    fk.ReferencedColumns.Add(ordered[i].To ?? (i < primaryKeys.Count ? primaryKeys[i].Name : string.Empty));
                }

                table.ForeignKeys.Add(fk);
            }
        }
    }

    /// <summary>Maps a declared SQLite column type to a generator field type using SQLite affinity rules.</summary>
    internal static FieldType MapType(string declaredType)
    {
        var type = declaredType.ToUpperInvariant();

        if (type.Contains("INT")) return FieldType.Integer;
        if (type.Contains("BOOL")) return FieldType.Boolean;
        if (type.Contains("DATETIME") || type.Contains("TIMESTAMP")) return FieldType.DateTime;
        if (type == "DATE") return FieldType.Date;
        if (type.Contains("BLOB")) return FieldType.Binary;
        if (type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB")
            || type.Contains("DEC") || type.Contains("NUM")) return FieldType.Decimal;

        return FieldType.Text;
    }
}
