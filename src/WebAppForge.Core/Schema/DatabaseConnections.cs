using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Schema;

/// <summary>Opens connections and picks the schema reader for a provider key of <see cref="DatabaseProviders"/>.</summary>
public static class DatabaseConnections
{
    public static DbConnection Create(string provider, string connectionString) => DatabaseProviders.Normalize(provider) switch
    {
        DatabaseProviders.Sqlite => new SqliteConnection(connectionString),
        DatabaseProviders.SqlServer or DatabaseProviders.AzureSql => new SqlConnection(connectionString),
        DatabaseProviders.MySql => new MySqlConnection(connectionString),
        var other => throw new NotSupportedException($"Provider '{other}' is not supported."),
    };

    public static ISchemaReader SchemaReader(string provider) => DatabaseProviders.Normalize(provider) switch
    {
        DatabaseProviders.Sqlite => new SqliteSchemaReader(),
        DatabaseProviders.SqlServer or DatabaseProviders.AzureSql => new SqlServerSchemaReader(),
        DatabaseProviders.MySql => new MySqlSchemaReader(),
        var other => throw new NotSupportedException($"Provider '{other}' is not supported."),
    };

    /// <summary>Opens and closes a connection. Returns null when it works, else the driver's message.</summary>
    public static async Task<string?> TestAsync(string provider, string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = Create(provider, connectionString);
            await connection.OpenAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is DbException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}
