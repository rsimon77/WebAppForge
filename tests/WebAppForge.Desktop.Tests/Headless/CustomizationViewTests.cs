using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>Opens the Project tab, the Fields tab and the Pages tab on the real MainWindow and finds their named controls.</summary>
public class CustomizationViewTests
{
    [AvaloniaFact]
    public void ProjectTabShowsItsSectionsAndWritesEditsToTheProject()
    {
        var (main, _) = CustomizationTests.Project();
        var window = ShowWindow(main);
        SelectTab(window, "Project");
        ExpandAll(window);

        foreach (var id in new[]
                 {
                     "ProjectIdentityNameBox", "ProjectIdBox", "ProjectVersionBox", "OutputFolderBox", "RootNamespaceBox", "SiteTitleBox",
                     "FaviconPathBox", "LogoPathBox", "HttpPortBox", "HttpsPortBox", "GlobalCssBox", "GlobalScriptBox", "ProjectDatabasePathBox",
                     "PublishFolderBox", "DockerExposePortBox", "SecuritySummaryText",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.NotNull(Find<Button>(window, "RegenerateProjectIdButton"));
        Assert.NotNull(Find<Button>(window, "BrowseOutputFolderButton"));
        Assert.NotNull(Find<ComboBox>(window, "LogLevelCombo"));
        Assert.NotNull(Find<CheckBox>(window, "UseHttpsCheck"));
        Assert.True(Find<TextBox>(window, "ProjectIdBox")!.IsReadOnly);
        Assert.Equal(main.Current.ProjectId.ToString(), Find<TextBox>(window, "ProjectIdBox")!.Text);

        Find<TextBox>(window, "HttpPortBox")!.Text = "9100";
        Find<TextBox>(window, "GlobalCssBox")!.Text = "h1 { color: red }";
        Find<CheckBox>(window, "UseHttpsCheck")!.IsChecked = true;
        Settle(window);

        Assert.Equal(9100, main.Current.Meta.HttpPort);
        Assert.Equal("h1 { color: red }", main.Current.Meta.GlobalCss);
        Assert.True(main.Current.Meta.UseHttps);
        Assert.True(Find<CheckBox>(window, "EnableHstsCheck")!.IsEffectivelyEnabled);

        Find<TextBox>(window, "HttpPortBox")!.Text = "99999";
        Settle(window);
        Assert.Equal(9100, main.Current.Meta.HttpPort);
        Assert.True(HasVisibleText(window, main.ProjectSettings.Message));

        Find<Button>(window, "GoToSecurityButton")!.Command!.Execute(null);
        Settle(window);
        Assert.Equal("Security", MainViewModel.TabNames[main.ActiveTabIndex]);
    }

    [AvaloniaFact]
    public void FieldsTabShowsTheFieldListAndThePropertyPanel()
    {
        var (main, _) = CustomizationTests.Project();
        var window = ShowWindow(main);
        SelectTab(window, "Fields");

        ExpandAll(window);
        Assert.NotNull(Find<ComboBox>(window, "FieldTablePicker"));
        Assert.NotNull(Find<TextBox>(window, "FieldSearchBox"));
        Assert.NotNull(Find<Button>(window, "MoveFieldUpButton"));
        Assert.NotNull(Find<Button>(window, "CopyFieldSettingsButton"));
        Assert.True(Find<TextBlock>(window, "NoFieldText")!.IsEffectivelyVisible);

        var customers = main.Current.Tables[0];
        main.Tables.SelectedTable = customers;
        main.Tables.SelectedField = customers.Fields.Single(f => f.Name == "Name");
        Settle(window);

        ExpandAll(window);
        var grid = Find<DataGrid>(window, "FieldList")!;
        Assert.Equal(6, grid.ItemsSource!.Cast<object>().Count());
        Assert.False(Find<TextBlock>(window, "NoFieldText")!.IsEffectivelyVisible);

        var caption = Find<TextBox>(window, "FieldCaptionBox")!;
        Assert.Equal("Customer name", caption.Text);
        caption.Text = "Full name";
        Find<TextBox>(window, "FieldHelpTextBox")!.Text = "As on the card";
        Find<ComboBox>(window, "FieldControlCombo")!.SelectedItem = EditControl.TextArea;
        Settle(window);

        var name = customers.Fields.Single(f => f.Name == "Name");
        Assert.Equal("Full name", name.Caption);
        Assert.Equal("As on the card", name.Ui.HelpText);
        Assert.Equal(EditControl.TextArea, name.Ui.Control);
        Assert.True(Find<TextBox>(window, "FieldRowsBox")!.IsEffectivelyVisible);

        Assert.Equal("Sample text", Find<TextBlock>(window, "FormatPreviewText")!.Text);
        Assert.NotNull(Find<TextBox>(window, "PatternTestBox"));
        Find<TextBox>(window, "FieldPatternBox")!.Text = "[a-z]+";
        Find<TextBox>(window, "PatternTestBox")!.Text = "abc";
        Settle(window);
        Assert.Equal("Matches", Find<TextBlock>(window, "PatternTestResultText")!.Text);

        Find<TextBox>(window, "FieldSearchBox")!.Text = "mail";
        Settle(window);
        Assert.Single(grid.ItemsSource!.Cast<object>());

        // Select and Radio show the options grid and the lookup section.
        Find<TextBox>(window, "FieldSearchBox")!.Text = string.Empty;
        main.Tables.SelectedField = customers.Fields.Single(f => f.Name == "Email");
        Find<ComboBox>(window, "FieldControlCombo")!.SelectedItem = EditControl.Select;
        Settle(window);
        ExpandAll(window);
        Assert.True(Find<DataGrid>(window, "FieldOptionsGrid")!.IsEffectivelyVisible);
        Assert.True(Find<ComboBox>(window, "LookupTableCombo")!.IsEffectivelyVisible);
        Assert.NotNull(Find<DataGrid>(window, "BadgeRulesGrid"));
    }

    [AvaloniaFact]
    public void PagesTabShowsThePageTypesEnablesSectionsAndDrawsAPreview()
    {
        var (main, _) = CustomizationTests.Project();
        var window = ShowWindow(main);
        SelectTab(window, "Pages");
        main.Tables.SelectedTable = main.Current.Tables[0];
        Settle(window);

        ExpandAll(window);
        var selector = Find<ListBox>(window, "PageTypeSelector")!;
        Assert.Equal(
            "List, View, Add, Copy, Edit, Delete, Search",
            string.Join(", ", selector.ItemsSource!.Cast<object>()));
        Assert.NotNull(Find<ComboBox>(window, "PageTablePicker"));
        Assert.NotNull(Find<Button>(window, "ResetPageDesignButton"));
        Assert.NotNull(Find<Button>(window, "CopyPageDesignButton"));
        Assert.NotNull(Find<DataGrid>(window, "ColumnsGrid"));
        Assert.NotNull(Find<DataGrid>(window, "RowStyleGrid"));
        Assert.NotNull(Find<TextBox>(window, "TableDescriptionBox"));

        // List page: list options on, form options and container off.
        Assert.True(Find<ComboBox>(window, "ListLayoutCombo")!.IsEffectivelyEnabled);
        Assert.False(Find<ComboBox>(window, "FormLayoutCombo")!.IsEffectivelyEnabled);
        Assert.False(Find<ComboBox>(window, "PageContainerCombo")!.IsEffectivelyEnabled);
        Assert.True(HasText(window, "Customers"));

        selector.SelectedItem = PageKind.Add;
        Settle(window);
        Assert.False(Find<ComboBox>(window, "ListLayoutCombo")!.IsEffectivelyEnabled);
        Assert.True(Find<ComboBox>(window, "FormLayoutCombo")!.IsEffectivelyEnabled);
        Assert.True(Find<ComboBox>(window, "PageContainerCombo")!.IsEffectivelyEnabled);
        Assert.True(HasText(window, "Add Customers"));

        Find<TextBox>(window, "PageTitleBox")!.Text = "New customer";
        Find<TextBox>(window, "SubmitTextBox")!.Text = "Create it";
        Settle(window);
        Assert.Equal("New customer", main.Current.Tables[0].Designs.Add.Title);
        Assert.True(HasText(window, "New customer"), "The preview did not follow the title.");
        Assert.True(HasText(window, "Create it"), "The preview did not follow the submit text.");

        selector.SelectedItem = PageKind.View;
        Settle(window);
        Assert.False(Find<ComboBox>(window, "FormLayoutCombo")!.IsEffectivelyEnabled);
        Assert.True(Find<ComboBox>(window, "PageContainerCombo")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void EditingOnTheNewTabsMarksTheProjectDirtyThroughTheWindow()
    {
        var (main, dialogs) = CustomizationTests.Project();
        using var folder = new TestSupport.TempFolder();
        dialogs.NextSaveProject = folder.File("p.xml");
        main.SaveAsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.False(main.IsDirty);

        var window = ShowWindow(main);
        SelectTab(window, "Project");
        ExpandAll(window);
        Find<TextBox>(window, "ProjectDescriptionBox")!.Text = "Edited";
        Settle(window);

        Assert.True(main.IsDirty);
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

    /// <summary>Content of a collapsed expander is not part of the visual tree, so tests open every section first.</summary>
    private static void ExpandAll(Window window)
    {
        foreach (var expander in window.GetVisualDescendants().OfType<Expander>().Where(e => e.IsEffectivelyVisible))
        {
            expander.IsExpanded = true;
        }

        Settle(window);
    }

    private static bool HasVisibleText(Window window, string text)
        => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);

    private static bool HasText(Window window, string text) => HasVisibleText(window, text);
}
