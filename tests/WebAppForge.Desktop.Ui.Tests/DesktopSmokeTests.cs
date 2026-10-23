using Xunit;

namespace WebAppForge.Desktop.Ui.Tests;

/// <summary>
/// Smoke test of the built desktop app through Windows UI Automation (FlaUI). Windows only; skipped elsewhere and when
/// the Northwind sample is not on this machine.
/// </summary>
[Trait("Platform", "Windows")]
[Collection(DesktopSession.CollectionName)]
public class DesktopSmokeTests
{
    [DesktopUiFact]
    public void Database_ConnectToNorthwind_ReportsThirteenTables()
    {
        using var session = DesktopSession.Launch();
        session.ConnectToNorthwind();
    }
}
