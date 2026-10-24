using Microsoft.Data.SqlClient;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Schema;

/// <summary>
/// Reads tables, columns, keys and foreign keys of a SQL Server or Azure SQL database from the sys catalog views. Tables in the
/// dbo schema keep their plain name; tables in other schemas are named "schema.table". Computed columns, rowversion columns and
/// types the generator cannot edit (sql_variant, spatial types, hierarchyid) are left out.
/// </summary>
public sealed class SqlServerSchemaReader : ISchemaReader
{
    private static readonly HashSet<string> SkippedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "timestamp", "rowversion", "sql_variant", "geography", "geometry", "hierarchyid",
    };

    public IReadOnlyList<TableInfo> ReadTables(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        var objects = new List<(int Id, string Name)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT t.object_id, s.name, t.name
                FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY CASE WHEN s.name = 'dbo' THEN 0 ELSE 1 END, s.name, t.name
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                objects.Add((reader.GetInt32(0), TableName(reader.GetString(1), reader.GetString(2))));
            }
        }

        var tables = new List<TableInfo>();
        foreach (var (id, name) in objects)
        {
            tables.Add(ReadTable(connection, id, name));
        }

        ReadForeignKeys(connection, objects, tables);
        return tables;
    }

    /// <summary>The name of a table as the generator uses it: plain in dbo, "schema.table" elsewhere.</summary>
    internal static string TableName(string schema, string table) =>
        string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase) ? table : schema + "." + table;

    private static TableInfo ReadTable(SqlConnection connection, int objectId, string tableName)
    {
        var table = new TableInfo { Name = tableName, Caption = tableName };
        var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.name, ic.key_ordinal
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = @id AND i.is_primary_key = 1
                """;
            command.Parameters.AddWithValue("@id", objectId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                keys[reader.GetString(0)] = reader.GetByte(1);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.name, TYPE_NAME(c.system_type_id), c.is_nullable, c.is_identity, c.is_computed,
                    CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END
                FROM sys.columns c
                WHERE c.object_id = @id
                ORDER BY c.column_id
                """;
            command.Parameters.AddWithValue("@id", objectId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var type = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (reader.GetBoolean(4) || SkippedTypes.Contains(type))
                {
                    continue;
                }

                var position = keys.GetValueOrDefault(name);
                table.Fields.Add(new FieldInfo
                {
                    Name = name,
                    Caption = name,
                    Type = MapType(type),
                    IsRequired = !reader.GetBoolean(2) || position > 0,
                    HasDefault = reader.GetInt32(5) != 0,
                    IsPrimaryKey = position > 0,
                    KeyPosition = position,
                    IsAutoIncrement = reader.GetBoolean(3),
                });
            }
        }

        return table;
    }

    private static void ReadForeignKeys(SqlConnection connection, List<(int Id, string Name)> objects, List<TableInfo> tables)
    {
        var rows = new List<(int Parent, int Key, int Ordinal, string RefSchema, string RefTable, string From, string To, string OnUpdate, string OnDelete)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT fk.parent_object_id, fk.object_id, fkc.constraint_column_id,
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id), OBJECT_NAME(fk.referenced_object_id), pc.name, rc.name,
                    fk.update_referential_action_desc, fk.delete_referential_action_desc
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8)));
            }
        }

        var names = objects.ToDictionary(o => o.Id, o => o.Name);
        foreach (var group in rows.GroupBy(r => (r.Parent, r.Key)).OrderBy(g => g.Key.Key))
        {
            if (!names.TryGetValue(group.Key.Parent, out var parentName) || tables.FirstOrDefault(t => t.Name == parentName) is not { } table)
            {
                continue;
            }

            var ordered = group.OrderBy(r => r.Ordinal).ToList();
            var fk = new ForeignKeyInfo
            {
                ReferencedTable = TableName(ordered[0].RefSchema, ordered[0].RefTable),
                OnUpdate = Action(ordered[0].OnUpdate),
                OnDelete = Action(ordered[0].OnDelete),
            };
            foreach (var row in ordered)
            {
                fk.Columns.Add(row.From);
                fk.ReferencedColumns.Add(row.To);
            }

            table.ForeignKeys.Add(fk);
        }
    }

    /// <summary>NO_ACTION, CASCADE, SET_NULL and SET_DEFAULT in the words SQLite reports: NO ACTION, CASCADE, SET NULL, SET DEFAULT.</summary>
    private static string Action(string description) => description.Replace('_', ' ');

    /// <summary>Maps a SQL Server system type name to a generator field type.</summary>
    internal static FieldType MapType(string type) => type.ToLowerInvariant() switch
    {
        "bigint" or "int" or "smallint" or "tinyint" => FieldType.Integer,
        "bit" => FieldType.Boolean,
        "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => FieldType.Decimal,
        "date" => FieldType.Date,
        "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => FieldType.DateTime,
        "binary" or "varbinary" or "image" => FieldType.Binary,
        _ => FieldType.Text,
    };
}
