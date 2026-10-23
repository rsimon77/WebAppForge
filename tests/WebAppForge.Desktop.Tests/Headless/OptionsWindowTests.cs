using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>The Application Options dialog and the File > Exit and Tools > Options menu entries on the real windows.</summary>
public class OptionsWindowTests
{
    private static readonly string[] CategoryTitles =
        ["General", "Appearance", "Generate", "Tools and paths", "Translation and AI", "Network", "Advanced"];

    [AvaloniaFact]
    public void OptionsDialogListsTheCategoriesAndHasOkCancelApply()
    {
        var applied = new List<AppSettings>();
        var window = ShowOptions(applied);

        var list = ById<ListBox>(window, "CategoryList");
        var titles = list.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var title in CategoryTitles)
        {
            Assert.Contains(title, titles);
        }

        var ok = ById<Button>(window, "OkButton");
        Assert.Equal("OK", ok.Content);
        Assert.Equal("Cancel", ById<Button>(window, "CancelButton").Content);
        Assert.Equal("Apply", ById<Button>(window, "ApplyButton").Content);
        Assert.True(ok.IsEnabled);
        Assert.Equal("General", ((OptionsViewModel)window.DataContext!).SelectedCategory.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectingACategoryShowsItsPageAndItsControls()
    {
        var window = ShowOptions([]);
        var options = (OptionsViewModel)window.DataContext!;

        foreach (var (id, pageId, controlId) in new[]
        {
            (OptionsCategory.General, "GeneralPage", "AutosaveMinutesBox"),
            (OptionsCategory.Appearance, "AppearancePage", "AppearanceCombo"),
            (OptionsCategory.Generate, "GeneratePage", "ClearOutputBeforeGenerateCheckBox"),
            (OptionsCategory.Tools, "ToolsPage", "DetectButton"),
            (OptionsCategory.Translation, "TranslationPage", "ClaudeModelIdBox"),
            (OptionsCategory.Network, "NetworkPage", "HttpProxyUrlBox"),
            (OptionsCategory.Advanced, "AdvancedPage", "ResetAllButton"),
        })
        {
            options.SelectedCategory = options.Categories.Single(c => c.Id == id);
            Settle(window);
            Assert.True(ById<Control>(window, pageId).IsEffectivelyVisible, $"{pageId} is not visible.");
            Assert.True(ById<Control>(window, controlId).IsEffectivelyVisible, $"{controlId} is not visible.");
        }

        window.Close();
    }

    [AvaloniaFact]
    public void InvalidFontSizeShowsAMessageAndDisablesOk()
    {
        var window = ShowOptions([]);
        var options = (OptionsViewModel)window.DataContext!;
        options.SelectedCategory = options.Categories.Single(c => c.Id == OptionsCategory.Appearance);

        options.UiFontSizeText = "99";
        Settle(window);

        Assert.False(ById<Button>(window, "OkButton").IsEnabled);
        Assert.False(ById<Button>(window, "ApplyButton").IsEnabled);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text is not null && t.Text.StartsWith("UI font size must be", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void OkAppliesTheEditedSettingsAndClosesTheDialog()
    {
        var applied = new List<AppSettings>();
        var window = ShowOptions(applied);
        var options = (OptionsViewModel)window.DataContext!;
        var closed = false;
        window.Closed += (_, _) => closed = true;

        options.Appearance = AppearanceMode.Dark;
        Settle(window);
        ById<Button>(window, "ApplyButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Single(applied);
        Assert.False(closed);

        ById<Button>(window, "OkButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Assert.Equal(2, applied.Count);
        Assert.Equal(AppearanceMode.Dark, applied[^1].Appearance);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void FileMenuHasExitWithCtrlQAndToolsMenuHasOptionsWithCtrlComma()
    {
        var dialogs = new FakeDialogService();
        var main = new MainViewModel(dialogs);
        var window = new MainWindow { DataContext = main };
        window.Show();
        Settle(window);

        var items = window.GetLogicalDescendants().OfType<MenuItem>().ToList();
        var exit = items.Single(i => i.Header as string == "Exit");
        var options = items.Single(i => i.Header as string == "Options...");
        Assert.Same(main.ExitCommand, exit.Command);
        Assert.Same(main.ShowOptionsCommand, options.Command);
        Assert.NotNull(exit.Icon);
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Control), exit.InputGesture);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Control), options.InputGesture);
        Assert.DoesNotContain(items, i => i.Header as string == "Settings...");

        Assert.Contains(window.KeyBindings, k => k.Gesture == new KeyGesture(Key.Q, KeyModifiers.Control));
        Assert.Contains(window.KeyBindings, k => k.Gesture == new KeyGesture(Key.OemComma, KeyModifiers.Control));
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlQOnTheMainWindowAsksAboutUnsavedChangesAndCancelKeepsTheWindow()
    {
        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Cancel };
        var main = new MainViewModel(dialogs);
        main.ProjectName = "Edited";
        var window = new MainWindow { DataContext = main };
        window.Show();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Control);
        Settle(window);

        Assert.Equal(1, dialogs.UnsavedPromptCount);
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ExistingControlsHaveAutomationIds()
    {
        var main = new MainViewModel(new FakeDialogService());
        var window = new MainWindow { DataContext = main };
        window.Show();
        Settle(window);
        Assert.IsType<TextBox>(ById<Control>(window, "ProjectNameBox"));
        Assert.IsType<TextBlock>(ById<Control>(window, "BreadcrumbText"));
        Assert.IsType<TextBox>(ById<Control>(window, "SqlitePathBox"));

        SelectTab(window, 1);
        Assert.IsType<ComboBox>(ById<Control>(window, "PageSizeCombo"));

        SelectTab(window, 4);
        Assert.IsType<CheckBox>(ById<Control>(window, "SignInCheckBox"));
        window.Close();
    }

    [AvaloniaFact]
    public void FontSizeAndCompactTablesAreAppliedAndCanBeRestored()
    {
        var app = Application.Current!;
        var appearance = new AvaloniaAppearance(app);
        try
        {
            Assert.Null(appearance.Apply(new AppSettings { UiFontSize = 18, CompactTables = true }));

            Assert.True(app.TryGetResource(AvaloniaAppearance.FontSizeKey, null, out var size));
            Assert.Equal(18d, size);
            Assert.Contains(app.Styles.OfType<Avalonia.Styling.Style>(), s => s.Setters.OfType<Avalonia.Styling.Setter>().Any(x => x.Property == DataGrid.RowHeightProperty));
        }
        finally
        {
            appearance.Apply(new AppSettings());
        }

        Assert.True(app.TryGetResource(AvaloniaAppearance.FontSizeKey, null, out var restored));
        Assert.Equal(14d, restored);
        Assert.DoesNotContain(app.Styles.OfType<Avalonia.Styling.Style>(), s => s.Setters.OfType<Avalonia.Styling.Setter>().Any(x => x.Property == DataGrid.RowHeightProperty));
    }

    private static OptionsWindow ShowOptions(List<AppSettings> applied)
    {
        var options = new OptionsViewModel(
            new AppSettings(),
            applied.Add,
            new FakeDialogService(),
            new OptionsEnvironment { GetVariable = _ => null, OpenFolder = _ => { } });
        var window = new OptionsWindow { DataContext = options };
        window.Show();
        Settle(window);
        return window;
    }

    private static T ById<T>(Window window, string id, bool requireVisible = true)
        where T : Control
    {
        var found = window.GetVisualDescendants().OfType<T>()
            .Where(c => AutomationProperties.GetAutomationId(c) == id)
            .ToList();
        var control = Assert.Single(found);
        if (requireVisible)
        {
            Assert.True(control.IsEffectivelyVisible, $"{id} is not visible.");
        }

        return control;
    }

    private static void SelectTab(Window window, int index)
    {
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = index;
        Settle(window);
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
}
