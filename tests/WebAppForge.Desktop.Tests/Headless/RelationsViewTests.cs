using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Fonts.Inter;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>Opens the Relations tab and the multi-page section of the Pages tab on the real MainWindow and finds their named controls.</summary>
public class RelationsViewTests
{
    [AvaloniaFact]
    public void RelationsTabShowsItsControlsAndWritesEditsToTheModel()
    {
        var (main, _) = CustomizationTests.Project();
        var window = ShowWindow(main);
        SelectTab(window, "Relations");
        main.Tables.SelectedTable = main.Current.Tables[0];
        Settle(window);
        ExpandAll(window);

        foreach (var id in new[]
                 {
                     "RelationsTablePicker", "SuggestFromForeignKeysButton", "DetailLinkList", "AddLinkButton", "RemoveLinkButton",
                     "MoveLinkUpButton", "MoveLinkDownButton", "RelationsOverviewList", "RelationIssueSummaryText", "DetailTableCombo",
                     "KeyPairsGrid", "AddKeyPairButton", "RemoveKeyPairButton", "LinkCaptionBox", "ShowOnViewCheck", "ShowOnEditCheck",
                     "AfterAddGoToEditCheck", "PreviewModeCombo", "OnDeleteCombo", "OnDeleteExplanationText", "OnUpdateCombo",
                     "DetailColumnsGrid", "AddDetailColumnButton", "RemoveDetailColumnButton", "MoveDetailColumnUpButton",
                     "MoveDetailColumnDownButton", "DetailPageSizeBox", "AllowAddCheck", "AllowEditCheck", "AllowDeleteCheck", "LinkFormPageCombo",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.Equal("Project1 > Relations > Customers".Replace("Project1", main.ProjectName), main.Breadcrumb);
        Assert.False(Find<ComboBox>(window, "DetailTableCombo")!.IsEffectivelyEnabled);

        Find<Button>(window, "AddLinkButton")!.Command!.Execute(null);
        Settle(window);
        ExpandAll(window);

        var links = Find<ListBox>(window, "DetailLinkList")!;
        Assert.Single(links.ItemsSource!.Cast<object>());
        Assert.True(Find<ComboBox>(window, "DetailTableCombo")!.IsEffectivelyEnabled);
        Assert.Single(Find<DataGrid>(window, "KeyPairsGrid")!.ItemsSource!.Cast<object>());
        Assert.True(HasVisibleText(window, "Orders (Id = CustomerId)"));

        var link = main.Current.Tables[0].Details.Single();
        Find<TextBox>(window, "LinkCaptionBox")!.Text = "Customer orders";
        Find<CheckBox>(window, "AfterAddGoToEditCheck")!.IsChecked = true;
        Find<ComboBox>(window, "OnDeleteCombo")!.SelectedItem = main.Relations.DeleteRuleOptions.Single(o => o.Rule == DeleteRule.Cascade);
        Find<ComboBox>(window, "PreviewModeCombo")!.SelectedItem = main.Relations.PreviewChoices.Single(c => c.Value == DetailPreview.Modal);
        Find<TextBox>(window, "DetailPageSizeBox")!.Text = "50";
        Settle(window);

        Assert.Equal("Customer orders", link.Caption);
        Assert.True(link.ShowOnAdd);
        Assert.Equal(DeleteRule.Cascade, link.OnDelete);
        Assert.Equal(DetailPreview.Modal, link.Preview);
        Assert.Equal(50, link.PageSize);
        Assert.True(HasVisibleText(window, "Delete the detail records together with the master record, in one transaction."));

        Find<TextBox>(window, "DetailPageSizeBox")!.Text = "500";
        Settle(window);
        Assert.Equal(50, link.PageSize);
        Assert.True(HasVisibleText(window, main.Relations.Message));

        Find<ComboBox>(window, "OnUpdateCombo")!.SelectedItem = main.Relations.UpdateChoices.Single(c => c.Value == UpdateRule.Cascade);
        Settle(window);
        Assert.True(Find<TextBlock>(window, "UpdateNoteText")!.IsEffectivelyVisible);
        Assert.Equal(UpdateRule.Cascade, link.OnUpdate);
    }

    [AvaloniaFact]
    public void TheValidationListShowsErrorsAndDoubleClickingTheOverviewOpensALink()
    {
        var (main, _) = CustomizationTests.Project();
        var window = ShowWindow(main);
        SelectTab(window, "Relations");
        main.Tables.SelectedTable = main.Current.Tables[0];
        Settle(window);

        main.Relations.AddLinkCommand.Execute(null);
        main.Relations.SelectedKeyPair = main.Relations.KeyPairs[0];
        main.Relations.RemoveKeyPairCommand.Execute(null);
        Settle(window);
        ExpandAll(window);

        Assert.True(HasVisibleText(window, "Error: Master/detail Customers -> Orders: add at least one key link (master field = detail field)."));
        Assert.True(HasVisibleText(window, "1 error(s) (they disable Generate), 0 warning(s)."));
        Assert.True(Find<ItemsControl>(window, "LinkIssuesList")!.IsEffectivelyVisible);
        Assert.False(main.Security.CanGenerate);

        var overview = Find<ListBox>(window, "RelationsOverviewList")!;
        Assert.Single(overview.ItemsSource!.Cast<object>());
        main.Tables.SelectedTable = main.Current.Tables[1];
        Settle(window);
        Assert.Empty(main.Relations.Links);

        overview.SelectedItem = main.Relations.Overview[0];
        main.Relations.OpenOverviewItemCommand.Execute(main.Relations.Overview[0]);
        Settle(window);
        Assert.Same(main.Current.Tables[0], main.Tables.SelectedTable);
        Assert.Single(main.Relations.Links);
    }

    [AvaloniaFact]
    public void SuggestButtonRunsTheDialogAndAddsLinks()
    {
        var (main, dialogs) = CustomizationTests.Project();
        var key = new ForeignKeyInfo { ReferencedTable = "Customers", OnDelete = "CASCADE" };
        key.Columns.Add("CustomerId");
        key.ReferencedColumns.Add("Id");
        main.Current.Tables[1].ForeignKeys.Add(key);
        var window = ShowWindow(main);
        SelectTab(window, "Relations");
        main.Tables.SelectedTable = main.Current.Tables[0];
        Settle(window);

        dialogs.NextChoices = ["Customers -> Orders (Customers.Id = Orders.CustomerId), on delete Cascade"];
        var button = Find<Button>(window, "SuggestFromForeignKeysButton")!;
        button.Command!.Execute(null);
        Settle(window);

        var link = Assert.Single(main.Current.Tables[0].Details);
        Assert.Equal(DeleteRule.Cascade, link.OnDelete);
        Assert.True(HasVisibleText(window, main.Relations.SuggestMessage));
        Assert.Contains("Added 1 master/detail link", main.Relations.SuggestMessage);
    }

    [AvaloniaFact]
    public void PagesTabShowsTheMultiPageEditorAndDrawsThePagesInThePreview()
    {
        var (main, _) = CustomizationTests.Project();
        var window = ShowWindow(main);
        SelectTab(window, "Pages");
        main.Tables.SelectedTable = main.Current.Tables[0];
        Settle(window);
        ExpandAll(window);

        foreach (var id in new[]
                 {
                     "FormPagesGrid", "AddFormPageButton", "RemoveFormPageButton", "MoveFormPageUpButton", "MoveFormPageDownButton",
                     "ViewModeCombo", "AddModeCombo", "CopyModeCombo", "EditModeCombo", "FieldPagesGrid", "PickAllFieldsButton",
                     "ClearFieldPicksButton", "MoveFieldsUpButton", "MoveFieldsDownButton", "BulkPageCombo", "AssignFieldsButton",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.Equal(6, Find<DataGrid>(window, "FieldPagesGrid")!.ItemsSource!.Cast<object>().Count());

        Find<ComboBox>(window, "EditModeCombo")!.SelectedItem = MultiPageMode.Tabs;
        Settle(window);
        ExpandAll(window);
        Assert.True(Find<ItemsControl>(window, "FormPageWarnings")!.IsEffectivelyVisible);
        Assert.True(HasVisibleText(window, "Warning: Table 'Customers': the Edit page uses Tabs form pages but the table has no form pages."));

        Find<Button>(window, "AddFormPageButton")!.Command!.Execute(null);
        Find<Button>(window, "AddFormPageButton")!.Command!.Execute(null);
        main.PageDesigner.FormPages.Pages[0].Caption = "Contact data";
        main.PageDesigner.FormPages.FieldRows[2].PageChoice = "Page 2";
        main.PageDesigner.SelectedPage = PageKind.Edit;
        Settle(window);
        ExpandAll(window);

        Assert.False(Find<ItemsControl>(window, "FormPageWarnings")!.IsEffectivelyVisible);
        Assert.Equal(MultiPageMode.Tabs, main.Current.Tables[0].Designs.Edit.MultiPage);
        Assert.Equal("Page 2", main.Current.Tables[0].Fields[2].Ui.FormPage);
        Assert.True(HasVisibleText(window, "Contact data"), "The preview did not draw the first tab.");
        Assert.True(HasVisibleText(window, "Page 2"), "The preview did not draw the second page.");

        foreach (var mode in new[] { MultiPageMode.Pills, MultiPageMode.Accordion, MultiPageMode.Wizard })
        {
            Find<ComboBox>(window, "EditModeCombo")!.SelectedItem = mode;
            Settle(window);
        }

        Assert.True(HasVisibleText(window, "Previous"));
        Assert.True(HasVisibleText(window, "Next"));
        Assert.True(HasVisibleText(window, "Step 1 of 2: Contact data"));
    }

    [AvaloniaFact]
    public void FormPagesAndRelationsStaySeparateSectionsInTheSidebar()
    {
        var (main, _) = CustomizationTests.Project();
        // The app draws with Inter (WithInterFont in Program.cs); the headless platform would fall back to a system font whose width
        // differs per machine, so the window uses Inter here too.
        FontManager.Current.AddFontCollection(new InterFontCollection());
        var window = new MainWindow { DataContext = main, FontFamily = new FontFamily("fonts:Inter#Inter") };
        window.Show();
        Settle(window);
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();

        var headers = tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString() ?? string.Empty).ToList();
        Assert.Equal(MainViewModel.TabNames.ToArray(), headers.ToArray());

        // Form pages live on the Pages section and links on Relations: two sidebar entries, data before site.
        Assert.True(headers.IndexOf("Relations") < headers.IndexOf("Pages"));
        Assert.Single(headers, h => h == "Relations");
        Assert.Single(headers, h => h == "Pages");
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
}
