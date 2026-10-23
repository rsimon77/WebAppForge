using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;

// Registers the Avalonia application that the Avalonia.Headless.XUnit runner starts for [AvaloniaFact] tests.
[assembly: AvaloniaTestApplication(typeof(WebAppForge.Desktop.Tests.TestSupport.HeadlessTestApp))]

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>Builds the real application on the headless platform, so views are created exactly as in the desktop app.</summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<WebAppForge.Desktop.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
