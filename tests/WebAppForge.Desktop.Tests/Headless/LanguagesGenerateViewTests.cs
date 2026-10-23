using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Build;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>Opens the Languages tab and the Generate tab console on the real MainWindow and finds their named controls.</summary>
public class LanguagesGenerateViewTests
{
    [AvaloniaFact]
    public void LanguagesTabShowsItsControlsAndEditsTheProject()
    {
        var rig = LanguagesTests.Setup();
        var window = ShowWindow(rig.Main);
        SelectTab(window, "Languages");

        foreach (var id in new[]
                 {
                     "LocalizationEnabledCheck", "DefaultLanguageCombo", "LanguageList", "NewLanguageBox", "AddLanguageButton", "PickLanguageButton",
                     "AutoTranslateCheck", "ProviderCombo", "ProviderStatusText", "TranslationNotesBox", "LanguageFilterCombo", "OnlyMissingCheck",
                     "OnlyUnreviewedCheck", "OnlyStaleCheck", "TranslationSearchBox", "TranslateMissingButton", "TranslateSelectedButton",
                     "CancelTranslationButton", "ImportCsvButton", "ExportCsvButton", "TranslationsGrid", "TranslationProgress",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.Contains("GOOGLE_TRANSLATE_API_KEY is set", Find<TextBlock>(window, "ProviderStatusText")!.Text);
        Assert.DoesNotContain(FakeApiKeys.SecretValue, Find<TextBlock>(window, "ProviderStatusText")!.Text);
        Assert.False(Find<Button>(window, "CancelTranslationButton")!.IsEffectivelyEnabled);
        Assert.False(Find<ProgressBar>(window, "TranslationProgress")!.IsEffectivelyVisible);

        Find<CheckBox>(window, "LocalizationEnabledCheck")!.IsChecked = false;
        Find<TextBox>(window, "TranslationNotesBox")!.Text = "Use the formal you";
        Settle(window);
        Assert.False(rig.Settings.Enabled);
        Assert.Equal("Use the formal you", rig.Settings.TranslationNotes);

        var list = Find<ListBox>(window, "LanguageList")!;
        Assert.Equal(4, list.ItemsSource!.Cast<object>().Count());

        // One column per language after the key and source columns; adding a language adds a column.
        var grid = Find<DataGrid>(window, "TranslationsGrid")!;
        Assert.Equal(2 + 4, grid.Columns.Count);
        rig.Languages.NewLanguageText = "fr";
        Find<Button>(window, "AddLanguageButton")!.Command!.Execute(null);
        Settle(window);
        Assert.Equal(2 + 5, grid.Columns.Count);
        Assert.Contains("fr", rig.Settings.Languages);

        // A cell edit becomes a manual, reviewed entry.
        var box = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.DataContext is TranslationCell c && c.Language == "de" && c.Row.Key == "ui.add");
        Assert.Equal("TranslationCellBox", AutomationProperties.GetAutomationId(box));
        box.Text = "Hinzufügen";
        Settle(window);
        var entry = rig.Settings.Find("ui.add", "de")!;
        Assert.Equal("Hinzufügen", entry.Text);
        Assert.True(entry.Reviewed);
        var reviewed = window.GetVisualDescendants().OfType<CheckBox>()
            .First(c => c.DataContext is TranslationCell cell && cell.Language == "de" && cell.Row.Key == "ui.add");
        Assert.True(reviewed.IsChecked);
        Assert.True(rig.Main.IsDirty);

        // Filters change the rows and the columns.
        Find<CheckBox>(window, "OnlyMissingCheck")!.IsChecked = true;
        Find<TextBox>(window, "TranslationSearchBox")!.Text = "Customer";
        Settle(window);
        Assert.All(grid.ItemsSource!.Cast<TranslationRow>(), r => Assert.Contains("ustomer", r.Key + r.SourceText));
        rig.Languages.FilterLanguage = "hu";
        Settle(window);
        Assert.Equal(2 + 1, grid.Columns.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void StaleTranslationsAreHighlightedInTheGrid()
    {
        var rig = LanguagesTests.Setup();
        rig.Settings.Entries.Add(new WebAppForge.Core.Model.TranslationEntry
        {
            Key = "ui.add", Language = "de", Text = "Hinzufügen", Source = TranslationSource.Google, SourceHash = "0000000000000000",
        });
        rig.Languages.Reload();
        var window = ShowWindow(rig.Main);
        SelectTab(window, "Languages");

        var stale = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("stale")).ToList();
        Assert.Single(stale);
        Assert.True(((TranslationCell)stale[0].DataContext!).IsStale);
        window.Close();
    }

    [AvaloniaFact]
    public void ProjectTabButtonJumpsToTheLanguagesTab()
    {
        var rig = LanguagesTests.Setup();
        var window = ShowWindow(rig.Main);
        SelectTab(window, "Project");
        foreach (var expander in window.GetVisualDescendants().OfType<Expander>().Where(e => e.IsEffectivelyVisible))
        {
            expander.IsExpanded = true;
        }

        Settle(window);
        Find<Button>(window, "GoToLocalizationButton")!.Command!.Execute(null);
        Settle(window);

        Assert.Equal("Languages", MainViewModel.TabNames[rig.Main.ActiveTabIndex]);
        Assert.True(Find<CheckBox>(window, "LocalizationEnabledCheck") is not null);
        Assert.EndsWith("Languages", rig.Main.Breadcrumb);
        window.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task GenerateTabShowsTheBuildConsole()
    {
        using var folder = new TempFolder();
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, stages, output, _) =>
            {
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Running));
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Succeeded));
                output.Report(FakeBuildRunner.Line("Restoring..."));
                output.Report(FakeBuildRunner.Line(@"C:\site\Pages\Home.cs(10,5): error CS1002: ; expected [C:\site\Site.csproj]"));
                stages.Report(new StageProgress(BuildStage.Build, StageStatus.Failed, "1 error(s)"));
                return System.Threading.Tasks.Task.FromResult(new GenerateAndBuildResult { Succeeded = false, FailedStage = BuildStage.Build, Message = "1 error(s)" });
            },
        };
        var rig = await BuildConsoleTests.SetupAsync(folder, runner: runner);
        var window = ShowWindow(rig.Main);
        SelectTab(window, "Generate");

        foreach (var id in new[]
                 {
                     "GenerateOutputFolderBox", "GenerateButton", "BuildOnlyButton", "PublishOnlyButton", "CancelBuildButton", "RunSiteButton",
                     "StopSiteButton", "OpenInBrowserButton", "OpenOutputFolderButton", "StageStrip", "OutputList", "DiagnosticsGrid",
                     "AutoScrollCheck", "ClearOutputButton", "CopyAllButton", "DockerStatusText", "DotnetStatusText", "NodeStatusText",
                     "BuildSummaryText", "ShowBuildSettingsLink",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.True(Find<Button>(window, "GenerateButton")!.IsEffectivelyEnabled);
        Assert.False(Find<Button>(window, "CancelBuildButton")!.IsEffectivelyEnabled);
        Assert.False(Find<Button>(window, "StopSiteButton")!.IsEffectivelyEnabled);
        Assert.True(Find<CheckBox>(window, "AutoScrollCheck")!.IsChecked);
        Assert.StartsWith("Generate only", Find<TextBlock>(window, "BuildSummaryText")!.Text);
        Assert.Contains("Docker CLI", Find<TextBlock>(window, "DockerStatusText")!.Text);
        Assert.Equal(5, Find<ItemsControl>(window, "StageStrip")!.ItemsSource!.Cast<object>().Count());

        Find<Button>(window, "GenerateButton")!.Command!.Execute(null);
        Settle(window);

        Assert.Equal(3, Find<ListBox>(window, "OutputList")!.ItemsSource!.Cast<object>().Count()); // header line plus two output lines
        Assert.Single(Find<DataGrid>(window, "DiagnosticsGrid")!.ItemsSource!.Cast<object>());
        Assert.Equal("Ok,Pending,Failed,Pending,Pending", string.Join(",", rig.Generate.Stages.Select(s => s.State)));

        Find<Button>(window, "ClearOutputButton")!.Command!.Execute(null);
        Settle(window);
        Assert.Empty(Find<ListBox>(window, "OutputList")!.ItemsSource!.Cast<object>());

        // Run site shows the URL as a link while the site runs.
        rig.CreateSite(built: true);
        Assert.False(Find<Button>(window, "SiteLinkButton")!.IsEffectivelyVisible);
        Find<Button>(window, "RunSiteButton")!.Command!.Execute(null);
        Settle(window);
        Assert.True(Find<Button>(window, "SiteLinkButton")!.IsEffectivelyVisible);
        Assert.True(Find<Button>(window, "StopSiteButton")!.IsEffectivelyEnabled);
        Assert.True(Find<Button>(window, "OpenInBrowserButton")!.IsEffectivelyEnabled);
        Find<Button>(window, "SiteLinkButton")!.Command!.Execute(null);
        Assert.Equal(["http://localhost:5123"], rig.OpenedUrls);
        Find<Button>(window, "StopSiteButton")!.Command!.Execute(null);
        Settle(window);
        Assert.False(Find<Button>(window, "SiteLinkButton")!.IsEffectivelyVisible);

        Find<Button>(window, "ShowBuildSettingsLink")!.Command!.Execute(null);
        Assert.Equal("Project", MainViewModel.TabNames[rig.Main.ActiveTabIndex]);
        window.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task GenerateButtonIsDisabledWhileTheProjectIsInvalidAndShowsWhy()
    {
        using var folder = new TempFolder();
        var rig = await BuildConsoleTests.SetupAsync(folder);
        rig.Main.Security.Enabled = true;
        var window = ShowWindow(rig.Main);
        SelectTab(window, "Generate");
        rig.Main.Security.Refresh();
        Settle(window);

        Assert.False(Find<Button>(window, "GenerateButton")!.IsEffectivelyEnabled);
        Assert.True(Find<TextBlock>(window, "ValidationBlockText")!.IsEffectivelyVisible);
        window.Close();
    }

    private static T? Find<T>(Window window, string automationId) where T : Control
        => window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == automationId);

    private static Window ShowWindow(MainViewModel main)
    {
        var window = new MainWindow { DataContext = main };
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

    private static void SelectTab(Window window, string name)
    {
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = MainViewModel.TabNames.ToList().IndexOf(name);
        Settle(window);
    }
}
