using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>Opens the Reports tab and the designer of every report type on the real MainWindow and finds their named controls.</summary>
public class ReportsViewTests
{
    [AvaloniaFact]
    public void ReportsTabShowsTheListTheCommonSettingsAndEveryDesigner()
    {
        var (main, _) = ReportsTests.Rig();
        var window = ShowWindow(main);
        SelectTab(window, "Reports");

        foreach (var id in new[] { "ReportSearchBox", "AddReportButton", "DuplicateReportButton", "DeleteReportButton", "MoveReportUpButton", "MoveReportDownButton", "ReportList", "NoReportText" })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.True(Find<TextBlock>(window, "NoReportText")!.IsEffectivelyVisible);

        main.Reports.AddReport(ReportType.Summary);
        Settle(window);
        foreach (var id in new[]
                 {
                     "ReportNameBox", "ReportCaptionBox", "ReportTableCombo", "ReportFilterBox", "ReportDescriptionBox", "ReportShowInMenuCheck",
                     "ReportMenuGroupBox", "ReportMenuOrderBox", "ReportMinLevelBox", "ReportIconCombo", "ReportDrillDownCombo", "ReportDesignerHost",
                     "ReportSchematicPreview", "RunPreviewButton", "CancelPreviewButton", "PreviewGrid", "ReportIssueSummaryText",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.False(Find<TextBlock>(window, "NoReportText")!.IsEffectivelyVisible);

        // The common settings write through the window.
        Find<TextBox>(window, "ReportCaptionBox")!.Text = "Sales by customer";
        Find<TextBox>(window, "ReportFilterBox")!.Text = "Total > 5";
        Find<CheckBox>(window, "ReportShowInMenuCheck")!.IsChecked = false;
        Settle(window);
        var def = main.Current.Reports[0];
        Assert.Equal("Sales by customer", def.Caption);
        Assert.Equal("SalesByCustomer", Find<TextBox>(window, "ReportNameBox")!.Text);
        Assert.Equal("Total > 5", def.Filter);
        Assert.False(def.ShowInMenu);
        Assert.True(HasVisibleText(window, "Sales by customer"));

        // Summary designer.
        foreach (var id in new[]
                 {
                     "SummaryGroupsGrid", "AddSummaryGroupButton", "RemoveSummaryGroupButton", "MoveSummaryGroupUpButton", "SummaryAggregatesGrid",
                     "AddSummaryAggregateButton", "SummaryLayoutCombo", "SummaryDetailFieldsGrid", "SummaryGroupTotalsCheck", "SummaryGrandTotalCheck",
                     "SummaryGroupsPerPageBox", "SummaryDetailRowsBox", "SummaryFiltersGrid", "SummaryAddFilterButton",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Find<ComboBox>(window, "SummaryLayoutCombo")!.SelectedItem = SummaryLayout.Compact;
        Find<TextBox>(window, "SummaryGroupsPerPageBox")!.Text = "9";
        Settle(window);
        Assert.Equal(SummaryLayout.Compact, def.Summary.Layout);
        Assert.Equal(9, def.Summary.GroupsPerPage);
        Assert.False(Find<Control>(window, "SummaryDetailFieldsGrid")!.IsEffectivelyVisible);
        Find<Button>(window, "AddSummaryGroupButton")!.Command!.Execute(null);
        Settle(window);
        Assert.Equal(2, def.Summary.Groups.Count);
        Assert.Equal(2, Find<DataGrid>(window, "SummaryGroupsGrid")!.ItemsSource!.Cast<object>().Count());

        // Crosstab designer.
        main.Reports.AddReport(ReportType.Crosstab);
        Settle(window);
        foreach (var id in new[]
                 {
                     "CrosstabRowsFieldCombo", "CrosstabRowsIntervalCombo", "CrosstabColumnsFieldCombo", "CrosstabValueFunctionCombo", "CrosstabValueFieldCombo",
                     "CrosstabRowTotalsCheck", "CrosstabColumnTotalsCheck", "CrosstabGrandTotalCheck", "CrosstabMaxColumnsBox", "CrosstabFiltersGrid",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Find<CheckBox>(window, "CrosstabRowTotalsCheck")!.IsChecked = false;
        Find<TextBox>(window, "CrosstabMaxColumnsBox")!.Text = "8";
        Settle(window);
        var crosstab = main.Reports.SelectedReport!.Model;
        Assert.False(crosstab.Crosstab.ShowRowTotals);
        Assert.Equal(8, crosstab.Crosstab.MaxColumns);

        // Chart designer: the kind picker, series grid and the single-series warning.
        main.Reports.AddReport(ReportType.Chart);
        Settle(window);
        foreach (var id in new[]
                 {
                     "ChartKindCombo", "ChartCategoryFieldCombo", "ChartSeriesGrid", "AddChartSeriesButton", "ChartSeriesSwatches", "ChartUseSeriesFieldCheck",
                     "ChartTitleBox", "ChartLegendCombo", "ChartDataLabelsCheck", "ChartHeightBox", "ChartTopNBox", "ChartTrendLineCheck",
                     "ChartXAxisTitleBox", "ChartYAxisTitleBox", "ChartFiltersGrid", "ChartPieWarningText",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        var chart = main.Reports.SelectedReport!.Model;
        Assert.False(Find<TextBlock>(window, "ChartPieWarningText")!.IsEffectivelyVisible);
        Find<Button>(window, "AddChartSeriesButton")!.Command!.Execute(null);
        Find<ComboBox>(window, "ChartKindCombo")!.SelectedItem = ChartKindChoice.Of(ChartKind.Pie);
        Settle(window);
        Assert.Equal(ChartKind.Pie, chart.Chart.Kind);
        Assert.True(Find<TextBlock>(window, "ChartPieWarningText")!.IsEffectivelyVisible);
        Find<TextBox>(window, "ChartTitleBox")!.Text = "Revenue";
        Settle(window);
        Assert.Equal("Revenue", chart.Chart.Title);
        Assert.True(HasVisibleText(window, "Revenue"), "The schematic did not follow the title.");

        // Calendar designer.
        main.Reports.AddReport(ReportType.Calendar);
        Settle(window);
        foreach (var id in new[]
                 {
                     "CalendarTitleFieldCombo", "CalendarStartFieldCombo", "CalendarEndFieldCombo", "CalendarAllDayFieldCombo", "CalendarColorFieldCombo",
                     "CalendarColorRulesGrid", "AddCalendarRuleButton", "CalendarRuleSwatches", "CalendarDefaultColorBox", "CalendarDefaultSwatches",
                     "CalendarPopoverFieldsGrid", "CalendarViewList", "CalendarDefaultViewCombo", "CalendarFirstDayCombo", "CalendarWeekendsCheck",
                     "CalendarAllowAddCheck", "CalendarAllowOpenCheck", "CalendarFiltersGrid",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Find<CheckBox>(window, "CalendarWeekendsCheck")!.IsChecked = false;
        Find<TextBox>(window, "CalendarDefaultColorBox")!.Text = "#336699";
        Settle(window);
        var calendar = main.Reports.SelectedReport!.Model;
        Assert.False(calendar.Calendar.ShowWeekends);
        Assert.Equal("#336699", calendar.Calendar.DefaultColor);

        // Dashboard designer with the live layout strip.
        main.Reports.AddReport(ReportType.Dashboard);
        Settle(window);
        foreach (var id in new[]
                 {
                     "DashboardItemList", "AddDashboardKpiButton", "AddDashboardReportButton", "RemoveDashboardItemButton", "MoveDashboardItemUpButton",
                     "DashboardRefreshBox", "DashboardLayoutStrip",
                 })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.False(Find<Control>(window, "ReportTableCombo")!.IsEffectivelyVisible);
        Find<Button>(window, "AddDashboardKpiButton")!.Command!.Execute(null);
        Find<Button>(window, "AddDashboardKpiButton")!.Command!.Execute(null);
        Find<Button>(window, "AddDashboardKpiButton")!.Command!.Execute(null);
        Settle(window);
        foreach (var id in new[] { "DashboardSpanBox", "DashboardHeightBox", "DashboardKpiCaptionBox", "DashboardKpiTableCombo", "DashboardKpiFunctionCombo", "DashboardKpiStyleCombo", "DashboardKpiIconCombo", "DashboardKpiLinkCombo" })
        {
            Assert.True(Find<Control>(window, id) is not null, id);
        }

        Assert.NotNull(Find<Control>(window, "DashboardBox0"));
        Assert.NotNull(Find<Control>(window, "DashboardBox2"));
        Find<TextBox>(window, "DashboardSpanBox")!.Text = "8";
        Settle(window);
        var boxes = ((DashboardReportViewModel)main.Reports.Designer!).Boxes;
        Assert.Equal(8, boxes[2].Span);
        Assert.Equal(1, boxes[2].Row);
        Assert.Equal(0, boxes[2].Column);
    }

    [AvaloniaFact]
    public void EditingOnTheReportsTabMarksTheProjectDirtyThroughTheWindow()
    {
        var (main, dialogs) = ReportsTests.Rig();
        using var folder = new TestSupport.TempFolder();
        dialogs.NextSaveProject = folder.File("p.xml");
        main.SaveAsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.False(main.IsDirty);

        var window = ShowWindow(main);
        SelectTab(window, "Reports");
        main.Reports.AddReport(ReportType.Summary);
        main.SaveProjectCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Settle(window);
        Assert.False(main.IsDirty);

        Find<TextBox>(window, "ReportDescriptionBox")!.Text = "Edited";
        Settle(window);
        Assert.True(main.IsDirty);
    }

    [AvaloniaFact]
    public void ValidationIssuesAndThePreviewResultShowInTheWindow()
    {
        var (main, _) = ReportsTests.Rig();
        var validation = new ReportsTests.FakeValidation();
        main.ReportValidation = validation;
        var preview = new ReportsTests.FakePreview();
        main.ReportPreview = preview;
        var window = ShowWindow(main);
        SelectTab(window, "Reports");
        var def = main.Reports.AddReport(ReportType.Summary).Model;
        validation.Issues.Add(new ReportIssueView(ReportIssueSeverity.Error, def.Name, "Pick a group."));
        main.Reports.Description = "go";
        Settle(window);
        Assert.True(HasVisibleText(window, $"Error: {def.Name}: Pick a group."));
        Assert.True(Find<ItemsControl>(window, "SelectedReportIssueList")!.IsEffectivelyVisible);

        // Default message, then a result with its own columns.
        main.ReportPreview = new NoReportPreview();
        Find<Button>(window, "RunPreviewButton")!.Command!.Execute(null);
        Settle(window);
        Assert.True(HasVisibleText(window, "Preview is not available yet"));

        main.ReportPreview = preview;
        preview.Handler = (_, _) => Task.FromResult(new ReportPreviewResult(["City", "Total"], [["Oslo", 10], ["Rome", 20]], null));
        Find<Button>(window, "RunPreviewButton")!.Command!.Execute(null);
        Settle(window);
        var grid = Find<DataGrid>(window, "PreviewGrid")!;
        Assert.Equal(["City", "Total"], grid.Columns.Select(c => c.Header?.ToString()));
        Assert.All(grid.Columns, c => Assert.True(c.MinWidth >= 100));
        Assert.Equal(2, grid.ItemsSource!.Cast<object>().Count());
        Assert.False(Find<TextBlock>(window, "PreviewErrorText")!.IsEffectivelyVisible);
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
        for (var pass = 0; pass < 4; pass++)
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

    private static bool HasVisibleText(Window window, string text)
        => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);
}
