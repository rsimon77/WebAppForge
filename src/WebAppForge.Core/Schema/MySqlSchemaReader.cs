using MySqlConnector;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Schema;

/// <summary>
/// Reads the tables, columns, keys and foreign keys of the current MySQL database from information_schema. Generated columns and
/// spatial types are left out. TINYINT(1), BOOL and BIT(1) columns are read as booleans.
/// </summary>
public sealed class MySqlSchemaReader : ISchemaReader
{
    private static readonly HashSet<string> SkippedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "geometry", "point", "linestring", "polygon", "multipoint", "multilinestring", "multipolygon", "geometrycollection", "geomcollection",
    };

    public IReadOnlyList<TableInfo> ReadTables(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        using var connection = new MySqlConnection(connectionString);
        connection.Open();

        var tables = new List<TableInfo>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TABLE_NAME FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                tables.Add(new TableInfo { Name = name, Caption = name });
            }
        }

        var byName = tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var keys = new Dictionary<(string Table, string Column), int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TABLE_NAME, COLUMN_NAME, ORDINAL_POSITION FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY'
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                keys[(reader.GetString(0).ToLowerInvariant(), reader.GetString(1).ToLowerInvariant())] = Convert.ToInt32(reader.GetValue(2));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                ORDER BY TABLE_NAME, ORDINAL_POSITION
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!byName.TryGetValue(reader.GetString(0), out var table))
                {
                    continue;
                }

                var name = reader.GetString(1);
                var dataType = reader.GetString(2);
                var extra = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                if (SkippedTypes.Contains(dataType) || extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var position = keys.GetValueOrDefault((table.Name.ToLowerInvariant(), name.ToLowerInvariant()));
                table.Fields.Add(new FieldInfo
                {
                    Name = name,
                    Caption = name,
                    Type = MapType(dataType, reader.GetString(3)),
                    IsRequired = reader.GetString(4) == "NO" || position > 0,
                    HasDefault = !reader.IsDBNull(5),
                    IsPrimaryKey = position > 0,
                    KeyPosition = position,
                    IsAutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        ReadForeignKeys(connection, byName);
        return tables;
    }

    private static void ReadForeignKeys(MySqlConnection connection, Dictionary<string, TableInfo> tables)
    {
        var rows = new List<(string Table, string Name, int Ordinal, string RefTable, string From, string To, string OnUpdate, string OnDelete)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT k.TABLE_NAME, k.CONSTRAINT_NAME, k.ORDINAL_POSITION, k.REFERENCED_TABLE_NAME, k.COLUMN_NAME, k.REFERENCED_COLUMN_NAME,
                    r.UPDATE_RULE, r.DELETE_RULE
                FROM information_schema.KEY_COLUMN_USAGE k
                JOIN information_schema.REFERENTIAL_CONSTRAINTS r
                    ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME AND r.TABLE_NAME = k.TABLE_NAME
                WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL AND k.REFERENCED_TABLE_SCHEMA = DATABASE()
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7)));
            }
        }

        foreach (var group in rows.GroupBy(r => (r.Table, r.Name)).OrderBy(g => g.Key.Table).ThenBy(g => g.Key.Name))
        {
            if (!tables.TryGetValue(group.Key.Table, out var table))
            {
                continue;
            }

            var ordered = group.OrderBy(r => r.Ordinal).ToList();
            var fk = new ForeignKeyInfo
            {
                ReferencedTable = tables.TryGetValue(ordered[0].RefTable, out var target) ? target.Name : ordered[0].RefTable,
                OnUpdate = ordered[0].OnUpdate,
                OnDelete = ordered[0].OnDelete,
            };
            foreach (var row in ordered)
            {
                fk.Columns.Add(row.From);
                fk.ReferencedColumns.Add(row.To);
            }

            table.ForeignKeys.Add(fk);
        }
    }

    /// <summary>Maps a MySQL data type (and the full column type, for TINYINT(1) and BIT(1)) to a generator field type.</summary>
    internal static FieldType MapType(string dataType, string columnType)
    {
        var full = columnType.ToLowerInvariant();
        if (full.StartsWith("tinyint(1)", StringComparison.Ordinal) || full == "bit(1)")
        {
            return FieldType.Boolean;
        }

        return dataType.ToLowerInvariant() switch
        {
            "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint" or "year" or "bit" => FieldType.Integer,
            "decimal" or "numeric" or "float" or "double" or "real" => FieldType.Decimal,
            "date" => FieldType.Date,
            "datetime" or "timestamp" => FieldType.DateTime,
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => FieldType.Binary,
            _ => FieldType.Text,
        };
    }
}
