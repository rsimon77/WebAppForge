using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>
/// The shell (app bar, sidebar, status bar) and every section rendered in light and dark at the default window size, with
/// Inter, the font the app uses. Set WEBAPPFORGE_SCREENSHOTS to a folder to keep a PNG of each.
/// </summary>
public class ShellLayoutTests
{
    private static readonly string? ShotFolder = Environment.GetEnvironmentVariable("WEBAPPFORGE_SCREENSHOTS");

    private static Window Show(MainViewModel main, double width = 1100, double height = 720)
    {
        FontManager.Current.AddFontCollection(new InterFontCollection());
        var window = new MainWindow { DataContext = main, FontFamily = new FontFamily("fonts:Inter#Inter"), Width = width, Height = height };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static T? Find<T>(Window window, string automationId) where T : Control
        => window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);

    [AvaloniaFact]
    public void TheSidebarListsTheSectionsInWorkflowOrderUnderTheirStagesAndFitsTheWindow()
    {
        var (main, _) = CustomizationTests.Project();
        var window = Show(main);
        var nav = Find<TabControl>(window, "SectionNav")!;
        var items = nav.Items.OfType<TabItem>().ToList();

        Assert.Equal(MainViewModel.TabNames.ToArray(), items.Select(t => t.Header?.ToString() ?? string.Empty).ToArray());
        Assert.Equal(MainViewModel.Stages.Select(s => s.Stage).ToArray(), items.Where(t => t.Tag is not null).Select(t => t.Tag!.ToString()).ToArray());
        Assert.Equal(MainViewModel.Stages.Select(s => s.FirstSection).ToArray(), items.Where(t => t.Tag is not null).Select(t => t.Header!.ToString()).ToArray());

        // Every section is visible without scrolling the sidebar at the default window size.
        foreach (var item in items)
        {
            var bottom = item.TranslatePoint(new Point(0, item.Bounds.Height), window)!.Value.Y;
            Assert.True(bottom <= window.Bounds.Height - 26, $"{item.Header} ends at {bottom}");
        }

        Assert.All(items, t => Assert.NotNull(NavIconConverter.Instance.Convert(t.Header, typeof(Geometry), null, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [AvaloniaFact]
    public void TheAppBarShowsTheProjectItsSaveStateAndFindsPlacesBySearch()
    {
        var (main, _) = CustomizationTests.Project();
        var window = Show(main);
        Assert.Equal("Not saved yet", Find<TextBlock>(window, "SaveStateText")!.Text);
        Assert.NotNull(Find<Button>(window, "AppBarGenerateButton"));

        var entries = main.SearchEntries();
        Assert.Contains(entries, e => e is { Kind: "Section", Text: "Home page" });
        main.GoTo(entries.Single(e => e is { Kind: "Field", Text: "Orders.Total" }));
        Settle(window);
        Assert.Equal("Fields", MainViewModel.TabNames[main.ActiveTabIndex]);
        Assert.Equal("Total", main.Tables.SelectedField?.Name);
        Assert.EndsWith("> Fields > Orders > Total", Find<TextBlock>(window, "BreadcrumbText")!.Text);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void EverySectionRendersInLightAndDark(string variant)
    {
        var application = Application.Current!;
        var previous = application.RequestedThemeVariant;
        application.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var (main, _) = CustomizationTests.Project();
            main.Tables.SelectedTable = main.Current.Tables[0];
            main.Tables.SelectedField = main.Current.Tables[0].Fields[1];
            main.Reports.AddReportCommand.Execute(null);
            var window = Show(main);
            foreach (var section in MainViewModel.TabNames)
            {
                main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf(section);
                Settle(window);
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (!string.IsNullOrEmpty(ShotFolder))
                {
                    Directory.CreateDirectory(ShotFolder);
                    using var stream = File.Create(Path.Combine(ShotFolder, $"{variant}-{section.Replace(' ', '-')}.png"));
#pragma warning disable CS0618 // The BitmapEncoderOptions overload is not available for streams in this Avalonia version.
                    frame!.Save(stream);
#pragma warning restore CS0618
                }
            }
        }
        finally
        {
            application.RequestedThemeVariant = previous;
        }
    }
}
