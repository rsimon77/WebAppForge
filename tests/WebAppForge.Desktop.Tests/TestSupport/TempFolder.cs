using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>A unique temp folder that is deleted when disposed.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WebAppForge.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        // Pooled SQLite connections keep the database file open on Windows.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup; the folder is under the user temp directory.
        }
    }
}
