using System.Runtime.CompilerServices;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>A Fact that is reported as skipped, with the reason, when the Northwind sample is not on this machine.</summary>
public sealed class NorthwindFactAttribute : FactAttribute
{
    public NorthwindFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!NorthwindDb.Exists)
        {
            Skip = "Northwind SQLite not found. Set WEBAPPFORGE_NORTHWIND_DB to the path of a Northwind SQLite file.";
        }
    }
}
