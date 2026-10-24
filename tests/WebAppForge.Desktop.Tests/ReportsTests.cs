using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class ReportsTests
{
    private static RelationChoice<T> Choice<T>(System.Collections.Generic.IReadOnlyList<RelationChoice<T>> choices, T value) =>
        choices.Single(c => Equals(c.Value, value));

    [Fact]
    public void AddReportReadsTheSelectedTableAndEditsWriteThroughToTheProject()
    {
        var (main, _) = CustomizationTests.Project();
        main.Tables.SelectedTable = main.Current.Tables.Single(t => t.Name == "Orders");
        var reports = main.Reports;
        reports.Reload();

        reports.AddReportCommand.Execute(null);

        var report = main.Current.Reports.Single();
        Assert.Equal(("report1", "Report 1", "Orders", ReportKind.Summary), (report.Name, report.Caption, report.SourceTable, report.Kind));
        Assert.Same(report, reports.Report);
        Assert.Equal("/reports/report1", reports.Route);
        Assert.Equal(["Id", "CustomerId", "Total", "Status"], reports.FieldNames);
        Assert.Equal(ReportsViewModel.RowsLabel, reports.ValueFieldNames[0]);

        reports.Caption = "Sales";
        reports.Description = "By status";
        reports.Filter = "Total > 0";
        reports.MenuGroup = "Reports";
        reports.MinLevel = "3";
        reports.AddGroupCommand.Execute(null);
        reports.Groups[0].Field = "Status";
        reports.AddAggregateCommand.Execute(null);
        reports.Aggregates[0].Function = Choice(reports.FunctionChoices, AggregateFunction.Sum);
        reports.Aggregates[0].Field = "Total";
        reports.Aggregates[0].Caption = "Revenue";
        reports.ShowDetails = true;
        reports.AddDetailColumnCommand.Execute(null);
        reports.AddDetailColumnCommand.Execute(null);
        reports.AddDetailColumnCommand.Execute(null);
        reports.DetailColumns[1].Field = "Status";
        reports.DetailColumns[2].Field = "Total";

        Assert.Equal(("Sales", "By status", "Total > 0", "Reports", 3, true), (report.Caption, report.Description, report.Filter, report.MenuGroup, report.MinLevel, report.ShowDetails));
        Assert.Equal("Status", report.Groups.Single().Field);
        Assert.Equal(("Total", AggregateFunction.Sum, "Revenue"), (report.Aggregates[0].Field, report.Aggregates[0].Function, report.Aggregates[0].Caption));
        Assert.Equal(["Id", "Status", "Total"], report.DetailColumns);
        Assert.Equal("report1 (Summary of Orders)", reports.Reports.Single().Text);
        Assert.Equal("No problems found.", reports.ProblemSummary);

        reports.Aggregates[0].Field = ReportsViewModel.RowsLabel;
        Assert.Equal(string.Empty, report.Aggregates[0].Field);
        Assert.Contains("Report report1: choose the value field.", reports.Problems);
    }

    [Fact]
    public void TheKindChoosesWhichSettingsAreShown()
    {
        var (main, _) = CustomizationTests.Project();
        var reports = main.Reports;
        reports.AddReportCommand.Execute(null);
        Assert.True(reports.IsSummary);
        Assert.False(reports.UsesGrid);

        reports.Kind = Choice(reports.KindChoices, ReportKind.Chart);
        Assert.Equal((false, true, true, false, false, true), (reports.IsSummary, reports.IsChart, reports.UsesGrid, reports.IsCalendar, reports.IsDashboard, reports.HasSource));
        Assert.Equal(("Category field", "Series field"), (reports.RowFieldLabel, reports.ColumnFieldLabel));

        reports.Kind = Choice(reports.KindChoices, ReportKind.Crosstab);
        Assert.Equal(("Row field", "Column field"), (reports.RowFieldLabel, reports.ColumnFieldLabel));

        reports.Kind = Choice(reports.KindChoices, ReportKind.Dashboard);
        Assert.False(reports.HasSource);
        Assert.True(reports.IsDashboard);
        Assert.Equal(ReportKind.Dashboard, main.Current.Reports.Single().Kind);
    }

    [Fact]
    public void ChartAndCalendarSettingsWriteThroughAndProblemsFollowThem()
    {
        var (main, _) = CustomizationTests.Project();
        main.Current.Tables[0].Fields.Add(new FieldInfo { Name = "Joined", Type = FieldType.Date });
        main.Tables.SelectedTable = main.Current.Tables[0];
        var reports = main.Reports;
        reports.AddReportCommand.Execute(null);
        var report = main.Current.Reports.Single();

        reports.Kind = Choice(reports.KindChoices, ReportKind.Chart);
        Assert.Contains("Report report1: choose the category field.", reports.Problems);
        reports.RowField = "Joined";
        reports.RowInterval = Choice(reports.IntervalChoices, DateInterval.Month);
        reports.ColumnField = "Active";
        reports.ValueField = "Age";
        reports.Function = Choice(reports.FunctionChoices, AggregateFunction.Average);
        reports.ChartType = Choice(reports.ChartTypeChoices, ChartType.Pie);
        Assert.Contains("Report report1: a pie or doughnut chart has one series; clear the series field.", reports.Problems);
        reports.ColumnField = ReportsViewModel.NoneLabel;
        reports.ChartOrder = Choice(reports.ChartOrderChoices, ChartOrder.ValueDescending);
        reports.TopCount = "5";
        reports.MaxColumns = "0";
        Assert.Equal("The most columns must be a whole number from 1 to 500. The last valid value was kept.", reports.Message);
        Assert.Equal("0", reports.MaxColumns);
        reports.ShowDataTable = false;

        Assert.Equal(("Joined", DateInterval.Month, string.Empty, "Age", AggregateFunction.Average, ChartType.Pie, ChartOrder.ValueDescending, 5, 50, false),
            (report.RowField, report.RowInterval, report.ColumnField, report.ValueField, report.Function, report.ChartType, report.ChartOrder, report.TopCount, report.MaxColumns, report.ShowDataTable));
        Assert.Empty(reports.Problems);

        reports.Kind = Choice(reports.KindChoices, ReportKind.Calendar);
        reports.DateField = "Name";
        Assert.Contains("Report report1: the date field 'Name' is not a date.", reports.Problems);
        reports.DateField = "Joined";
        reports.TitleField = "Email";
        reports.LinkToView = false;
        reports.WeekStartsMonday = false;
        Assert.Equal(("Joined", string.Empty, "Email", false, false), (report.DateField, report.EndDateField, report.TitleField, report.LinkToView, report.WeekStartsMonday));
        Assert.Equal(ReportsViewModel.NoneLabel, reports.EndDateField);
        Assert.Empty(reports.Problems);
    }

    [Fact]
    public void FiltersColumnsAndDrillDownWriteThroughAndNumberDatesCountAsDates()
    {
        var (main, _) = CustomizationTests.Project();
        var customers = main.Current.Tables[0];
        customers.Fields.Add(new FieldInfo { Name = "Joined", Type = FieldType.Integer });
        main.Tables.SelectedTable = customers;
        var reports = main.Reports;
        reports.AddReportCommand.Execute(null);
        var report = main.Current.Reports.Single();
        Assert.Equal([ReportsViewModel.NoneLabel], reports.DateFieldNames);
        Assert.True(reports.HasDateRange && reports.HasDrillDown && reports.DrillDown);

        // The field designer marks the number column as Unix time; the report then offers it as a date.
        main.Tables.SelectedField = customers.Fields.Single(f => f.Name == "Joined");
        Assert.True(main.FieldDesigner.CanStoreDates);
        main.FieldDesigner.DateStorage = DateStorage.UnixSeconds;
        Assert.Equal(DateStorage.UnixSeconds, customers.Fields.Single(f => f.Name == "Joined").DateStorage);
        main.Tables.SelectedField = customers.Fields.Single(f => f.Name == "Name");
        Assert.False(main.FieldDesigner.CanStoreDates);
        main.FieldDesigner.DateStorage = DateStorage.JulianDay;
        Assert.Equal(DateStorage.Text, customers.Fields.Single(f => f.Name == "Name").DateStorage);

        reports.Reload();
        Assert.Equal([ReportsViewModel.NoneLabel, "Joined"], reports.DateFieldNames);
        reports.DateFilterField = "Joined";
        reports.AddGroupCommand.Execute(null);
        reports.Groups[0].Field = "Joined";
        reports.Groups[0].Interval = Choice(reports.IntervalChoices, DateInterval.Month);
        reports.AddFilterFieldCommand.Execute(null);
        reports.AddFilterFieldCommand.Execute(null);
        reports.FilterFields[1].Field = "Active";
        reports.AddDetailColumnCommand.Execute(null);
        reports.AddDetailColumnCommand.Execute(null);
        reports.SelectedDetailColumn = reports.DetailColumns[1];
        reports.MoveDetailColumnUpCommand.Execute(null);
        reports.DrillDown = false;

        Assert.Equal(("Joined", false), (report.DateFilterField, report.DrillDown));
        Assert.Equal(["Id", "Active"], report.FilterFields);
        Assert.Equal(["Name", "Id"], report.DetailColumns);
        Assert.Empty(reports.Problems);

        reports.SelectedFilterField = reports.FilterFields[0];
        reports.RemoveFilterFieldCommand.Execute(null);
        reports.FilterFields[0].Field = "Active";
        reports.AddFilterFieldCommand.Execute(null);
        reports.FilterFields[1].Field = "Active";
        Assert.Contains("Report report1: the filter field 'Active' is listed twice.", reports.Problems);

        reports.DateFilterField = ReportsViewModel.NoneLabel;
        Assert.Equal(string.Empty, report.DateFilterField);
        reports.Kind = Choice(reports.KindChoices, ReportKind.Calendar);
        Assert.False(reports.HasDateRange);
        Assert.False(reports.HasDrillDown);
    }

    [Fact]
    public void NamesMustBeUsableAndUniqueAndDashboardsFollowARename()
    {
        var (main, _) = CustomizationTests.Project();
        var reports = main.Reports;
        reports.AddReportCommand.Execute(null);
        reports.AddReportCommand.Execute(null);
        reports.Kind = Choice(reports.KindChoices, ReportKind.Dashboard);
        Assert.Equal(["report1"], reports.DashboardChoices);
        reports.AddItemCommand.Execute(null);
        reports.Items[0].WidthValue = 20;
        var board = main.Current.Reports[1];
        Assert.Equal(("report1", 12), (board.Items[0].Report, board.Items[0].Width));

        reports.SelectedRow = reports.Reports[0];
        reports.Name = "!!";
        Assert.Equal("A report name needs at least one letter or digit. The last valid name was kept.", reports.Message);
        Assert.Equal("report1", main.Current.Reports[0].Name);
        reports.Name = "Report2";
        Assert.StartsWith("Another report already uses the URL /reports/report2.", reports.Message);

        reports.Name = "sales";
        Assert.Equal(string.Empty, reports.Message);
        Assert.Equal("sales", main.Current.Reports[0].Name);
        Assert.Equal("sales", board.Items[0].Report);
        Assert.Equal("Project1 > Reports > sales".Replace("Project1", main.ProjectName), BreadcrumbOn(main, "Reports"));
    }

    [Fact]
    public void MoveAndRemoveKeepTheListAndTheProjectInStep()
    {
        var (main, _) = CustomizationTests.Project();
        var reports = main.Reports;
        reports.AddReportCommand.Execute(null);
        reports.AddReportCommand.Execute(null);
        reports.AddReportCommand.Execute(null);

        reports.MoveReportUpCommand.Execute(null);
        Assert.Equal(["report1", "report3", "report2"], main.Current.Reports.Select(r => r.Name));
        Assert.Equal(main.Current.Reports, reports.Reports.Select(r => r.Report));

        reports.RemoveReportCommand.Execute(null);
        Assert.Equal(["report1", "report2"], main.Current.Reports.Select(r => r.Name));
        Assert.Equal("report2", reports.Report?.Name);
        reports.AddReportCommand.Execute(null);
        Assert.Equal("report3", main.Current.Reports.Last().Name);
    }

    [Fact]
    public async Task ReportsMakeTheProjectDirtyAndSurviveSaveAndOpen()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Current.Tables.Add(new TableInfo { Name = "Orders" });
        main.Current.Tables[0].Fields.Add(new FieldInfo { Name = "Status", Type = FieldType.Text });
        await main.SaveProjectCommand.ExecuteAsync(null);
        Assert.False(main.IsDirty);

        main.Reports.AddReportCommand.Execute(null);
        main.Reports.Kind = Choice(main.Reports.KindChoices, ReportKind.Crosstab);
        main.Reports.RowField = "Status";
        Assert.True(main.IsDirty);
        await main.SaveProjectCommand.ExecuteAsync(null);

        var dialogs = new FakeDialogService { NextOpenProject = folder.File("project.xml") };
        var reopened = new MainViewModel(dialogs);
        await reopened.OpenProjectCommand.ExecuteAsync(null);
        var report = reopened.Current.Reports.Single();
        Assert.Equal((ReportKind.Crosstab, "Status", "Orders"), (report.Kind, report.RowField, report.SourceTable));
        Assert.Same(report, reopened.Reports.Report);
        Assert.Equal("Crosstab", reopened.Reports.Kind?.Label);
    }

    [NorthwindFact]
    public async Task NorthwindReportsMadeInTheTabGenerateASite()
    {
        using var folder = new TempFolder();
        var main = await Harness.NorthwindProjectAsync(folder);
        main.Tables.SelectedTable = main.Current.Tables.Single(t => t.Name == "Orders");
        var reports = main.Reports;

        reports.AddReportCommand.Execute(null);
        reports.Name = "sales";
        reports.AddGroupCommand.Execute(null);
        reports.Groups[0].Field = "ShipCountry";
        reports.AddAggregateCommand.Execute(null);
        reports.Aggregates[0].Function = Choice(reports.FunctionChoices, AggregateFunction.Sum);
        reports.Aggregates[0].Field = "Freight";

        reports.AddReportCommand.Execute(null);
        reports.Name = "by-month";
        reports.Kind = Choice(reports.KindChoices, ReportKind.Chart);
        reports.RowField = "OrderDate";
        reports.RowInterval = Choice(reports.IntervalChoices, DateInterval.Month);
        reports.ChartType = Choice(reports.ChartTypeChoices, ChartType.Line);

        Assert.Contains("OrderDate", reports.FieldNames);
        Assert.Empty(reports.Problems);
        await main.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.StartsWith("Generated ", main.Status);
        Assert.True(File.Exists(folder.File(Path.Combine("generated", "Reports", "SalesReport.cs"))));
        Assert.Contains("substr(\\\"Orders\\\".\\\"OrderDate\\\", 1, 7)", File.ReadAllText(folder.File(Path.Combine("generated", "Reports", "ByMonthReport.cs"))));
    }

    private static string BreadcrumbOn(MainViewModel main, string tab)
    {
        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf(tab);
        return main.Breadcrumb;
    }
}
