using System.Collections.Generic;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Desktop.Services;

/// <summary>
/// Reads and creates the ASP.NET Core Identity tables in the project database. The Security tab calls it; tests replace it
/// with a fake so they need no database.
/// </summary>
public interface IIdentityTableService
{
    /// <summary>The identity tables with <paramref name="prefix"/> that the database does not have yet.</summary>
    Task<IReadOnlyList<string>> MissingTablesAsync(string provider, string connectionString, string prefix);

    /// <summary>Creates the missing identity tables (idempotent). Returns the tables that were missing.</summary>
    Task<IReadOnlyList<string>> CreateMissingTablesAsync(string provider, string connectionString, string prefix);

    /// <summary>Reads the tables of the database, like the Database tab's Connect.</summary>
    Task<IReadOnlyList<TableInfo>> ReadTablesAsync(string provider, string connectionString);
}

/// <summary>The real service: <see cref="IdentitySchema"/> over a connection from <see cref="DatabaseConnections"/>.</summary>
public sealed class CoreIdentityTableService : IIdentityTableService
{
    public async Task<IReadOnlyList<string>> MissingTablesAsync(string provider, string connectionString, string prefix)
    {
        await using var connection = DatabaseConnections.Create(provider, connectionString);
        await connection.OpenAsync();
        return await IdentitySchema.MissingTablesAsync(connection, provider, prefix);
    }

    public async Task<IReadOnlyList<string>> CreateMissingTablesAsync(string provider, string connectionString, string prefix)
    {
        await using var connection = DatabaseConnections.Create(provider, connectionString);
        await connection.OpenAsync();
        return await IdentitySchema.CreateMissingTablesAsync(connection, provider, prefix);
    }

    public Task<IReadOnlyList<TableInfo>> ReadTablesAsync(string provider, string connectionString) =>
        Task.Run(() => DatabaseConnections.SchemaReader(provider).ReadTables(connectionString));
}
