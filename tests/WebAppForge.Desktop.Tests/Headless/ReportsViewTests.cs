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

/// <summary>Opens the Reports tab on the real MainWindow, finds its named controls and edits a report through them.</summary>
public class ReportsViewTests
{
    [AvaloniaFact]
    public void ReportsTabShowsTheSettingsOfTheKindAndWritesEditsToTheModel()
    {
        var (main, _) = CustomizationTests.Project();
        main.Tables.SelectedTable = main.Current.Tables.Single(t => t.Name == "Orders");
        var window = ShowWindow(main);
        SelectTab(window, "Reports");

        Assert.False(Find<TextBox>(window, "ReportNameBox")!.IsEffectivelyEnabled);
        Find<Button>(window, "AddReportButton")!.Command!.Execute(null);
        Settle(window);

        foreach (var id in new[]
                 {
                     "ReportList", "AddReportButton", "RemoveReportButton", "MoveReportUpButton", "MoveReportDownButton", "ReportProblemSummaryText",
                     "ReportNameBox", "ReportKindCombo", "ReportRouteText", "ReportCaptionBox", "ReportDescriptionBox", "ReportTableCombo", "ReportFilterBox",
                     "ReportGenerateCheck", "ReportShowInMenuCheck", "ReportMenuGroupBox", "ReportMinLevelBox", "ReportGroupsGrid", "AddReportGroupButton",
                     "ReportAggregatesGrid", "AddReportAggregateButton", "ReportShowDetailsCheck", "ReportGrandTotalCheck",
                     "ReportDetailColumnsGrid", "AddReportColumnButton", "MoveReportColumnUpButton", "ReportDrillDownCheck", "ReportDateFilterCombo",
                     "ReportFilterFieldsGrid", "AddReportFilterButton", "RemoveReportFilterButton",
                 })
        {
            Assert.True(Find<Control>(window, id) is { IsEffectivelyVisible: true }, id);
        }

        Assert.True(Find<TextBox>(window, "ReportNameBox")!.IsEffectivelyEnabled);
        Assert.False(Visible(window, "ReportRowFieldCombo"));
        Assert.False(Visible(window, "ReportDateFieldCombo"));
        Assert.Equal("orders", (string?)Find<ComboBox>(window, "ReportTableCombo")!.SelectedItem, StringComparer.OrdinalIgnoreCase);

        Find<TextBox>(window, "ReportNameBox")!.Text = "Monthly sales";
        Find<TextBox>(window, "ReportCaptionBox")!.Text = "Monthly sales";
        Find<ComboBox>(window, "ReportKindCombo")!.SelectedItem = main.Reports.KindChoices.Single(c => c.Value == ReportKind.Chart);
        Settle(window);

        Assert.True(Visible(window, "ReportRowFieldCombo"));
        Assert.True(Visible(window, "ReportChartTypeCombo"));
        Assert.False(Visible(window, "ReportGroupsGrid"));
        Find<ComboBox>(window, "ReportRowFieldCombo")!.SelectedItem = "Status";
        Find<ComboBox>(window, "ReportValueFieldCombo")!.SelectedItem = "Total";
        Find<ComboBox>(window, "ReportFunctionCombo")!.SelectedItem = main.Reports.FunctionChoices.Single(c => c.Value == AggregateFunction.Sum);
        Find<ComboBox>(window, "ReportChartTypeCombo")!.SelectedItem = main.Reports.ChartTypeChoices.Single(c => c.Value == ChartType.Doughnut);
        Find<TextBox>(window, "ReportTopCountBox")!.Text = "3";
        Settle(window);

        var report = main.Current.Reports.Single();
        Assert.Equal(("Monthly sales", ReportKind.Chart, "Status", "Total", AggregateFunction.Sum, ChartType.Doughnut, 3),
            (report.Name, report.Kind, report.RowField, report.ValueField, report.Function, report.ChartType, report.TopCount));
        Assert.Equal("/reports/monthly-sales", Find<TextBlock>(window, "ReportRouteText")!.Text);
        Assert.Equal("No problems found.", Find<TextBlock>(window, "ReportProblemSummaryText")!.Text);
        Assert.EndsWith("> Reports > Monthly sales", main.Breadcrumb);

        Find<Button>(window, "AddReportFilterButton")!.Command!.Execute(null);
        Settle(window);
        Assert.Single(Find<DataGrid>(window, "ReportFilterFieldsGrid")!.ItemsSource!.Cast<object>());
        Assert.Equal(["Id"], report.FilterFields);
        Find<CheckBox>(window, "ReportDrillDownCheck")!.IsChecked = false;
        Settle(window);
        Assert.False(report.DrillDown);

        Find<ComboBox>(window, "ReportKindCombo")!.SelectedItem = main.Reports.KindChoices.Single(c => c.Value == ReportKind.Calendar);
        Settle(window);
        Assert.False(Visible(window, "ReportDateFilterCombo"));
        Assert.True(Visible(window, "ReportFilterFieldsGrid"));
        Assert.False(Visible(window, "ReportDrillDownCheck"));

        Find<ComboBox>(window, "ReportKindCombo")!.SelectedItem = main.Reports.KindChoices.Single(c => c.Value == ReportKind.Dashboard);
        Settle(window);
        Assert.True(Visible(window, "DashboardItemsGrid"));
        Assert.False(Visible(window, "ReportTableCombo"));
        Assert.Contains("Report Monthly sales: add at least one report to the dashboard.", main.Reports.Problems);
        Find<Button>(window, "AddDashboardItemButton")!.Command!.Execute(null);
        Settle(window);
        Assert.Single(Find<DataGrid>(window, "DashboardItemsGrid")!.ItemsSource!.Cast<object>());
        Assert.DoesNotContain("Report Monthly sales: add at least one report to the dashboard.", main.Reports.Problems);
    }

    private static bool Visible(Window window, string id) => Find<Control>(window, id) is { IsEffectivelyVisible: true };

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
