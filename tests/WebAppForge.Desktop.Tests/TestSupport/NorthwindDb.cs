using System.IO;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>
/// Locates the Northwind SQLite sample. The file is never committed; tests copy it before use so the original is never modified.
/// </summary>
public static class NorthwindDb
{
    /// <summary>Path to a Northwind SQLite file, taken from the WEBAPPFORGE_NORTHWIND_DB environment variable (empty when not set).</summary>
    public static string SourcePath { get; } = Environment.GetEnvironmentVariable("WEBAPPFORGE_NORTHWIND_DB") ?? string.Empty;

    public static bool Exists => SourcePath.Length > 0 && File.Exists(SourcePath);

    /// <summary>Copies Northwind into <paramref name="folder"/> under the name northwind.db and returns the copy's path.</summary>
    public static string CopyInto(string folder)
    {
        var target = Path.Combine(folder, "northwind.db");
        File.Copy(SourcePath, target, overwrite: true);
        return target;
    }
}
