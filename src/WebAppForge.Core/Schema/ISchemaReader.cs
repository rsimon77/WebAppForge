using WebAppForge.Core.Model;

namespace WebAppForge.Core.Schema;

/// <summary>Reads table and column metadata from a database. One implementation per provider.</summary>
public interface ISchemaReader
{
    IReadOnlyList<TableInfo> ReadTables(string connectionString);
}
