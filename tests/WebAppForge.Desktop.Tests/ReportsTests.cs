using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>Reports tab view models against in-memory projects: list commands, pickers, write-through, schematics, dirty tracking and the two service seams.</summary>
public class ReportsTests
{
    internal sealed class FakeValidation : IReportValidationService
    {
        public List<ReportIssueView> Issues { get; } = [];

        public int Calls { get; private set; }

        public IReadOnlyList<ReportIssueView> Validate(Project project)
        {
            Calls++;
            return Issues.ToList();
        }
    }

    internal sealed class FakePreview : IReportPreviewService
    {
        public List<(ReportDef Report, CancellationToken Token)> Calls { get; } = [];

        public Func<ReportDef, CancellationToken, Task<ReportPreviewResult>> Handler { get; set; } =
            (_, _) => Task.FromResult(new ReportPreviewResult(["A"], [[1]], null));

        public Task<ReportPreviewResult> RunAsync(Project project, ReportDef report, CancellationToken cancellationToken)
        {
            Calls.Add((report, cancellationToken));
            return Handler(report, cancellationToken);
        }
    }

    /// <summary>Customers and Orders (from the customization tests) plus Sales with one field of every type.</summary>
    internal static (MainViewModel Main, FakeDialogService Dialogs) Rig()
    {
        var (main, dialogs) = CustomizationTests.Project();
        var sales = new TableInfo { Name = "Sales", Caption = "Sales" };
        sales.Fields.Add(new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true });
        sales.Fields.Add(new FieldInfo { Name = "Customer", Type = FieldType.Text, Caption = "Customer name" });
        sales.Fields.Add(new FieldInfo { Name = "Total", Type = FieldType.Decimal });
        sales.Fields.Add(new FieldInfo { Name = "OrderDate", Type = FieldType.Date });
        sales.Fields.Add(new FieldInfo { Name = "ShipTime", Type = FieldType.DateTime });
        sales.Fields.Add(new FieldInfo { Name = "Shipped", Type = FieldType.Boolean });
        sales.Fields.Add(new FieldInfo { Name = "Photo", Type = FieldType.Binary });
        main.Current.Tables.Add(sales);
        main.Tables.Reload();
        main.Reports.Reload();
        main.Tables.SelectedTable = sales;
        return (main, dialogs);
    }

    private static ReportDef Add(MainViewModel main, ReportType type, string table = "Sales")
    {
        var row = main.Reports.AddReport(type);
        if (type != ReportType.Dashboard)
        {
            main.Reports.TableName = table;
        }

        return row.Model;
    }

    // ---- list ----

    [Fact]
    public async Task AddingAsksForTheTypeWithADescriptionAndAppendsTheReport()
    {
        var (main, dialogs) = Rig();
        dialogs.NextChoice = ReportTypeInfo.Of(ReportType.Chart).Label;
        await main.Reports.AddReportCommand.ExecuteAsync(null);

        Assert.Equal(5, dialogs.LastChoicesOffered.Count);
        Assert.All(dialogs.LastChoicesOffered, c => Assert.Contains(" - ", c));
        Assert.Contains(dialogs.LastChoicesOffered, c => c.StartsWith("Dashboard"));
        var report = Assert.Single(main.Current.Reports);
        Assert.Equal(ReportType.Chart, report.Type);
        Assert.Same(report, main.Reports.SelectedReport!.Model);
        Assert.IsType<ChartReportViewModel>(main.Reports.Designer);
        Assert.Equal("Sales", report.Table);

        dialogs.NextChoice = null;
        await main.Reports.AddReportCommand.ExecuteAsync(null);
        Assert.Single(main.Current.Reports);
    }

    [Theory]
    [InlineData(ReportType.Summary, typeof(SummaryReportViewModel))]
    [InlineData(ReportType.Crosstab, typeof(CrosstabReportViewModel))]
    [InlineData(ReportType.Chart, typeof(ChartReportViewModel))]
    [InlineData(ReportType.Calendar, typeof(CalendarReportViewModel))]
    [InlineData(ReportType.Dashboard, typeof(DashboardReportViewModel))]
    public void EveryTypeGetsItsOwnDesigner(ReportType type, Type designer)
    {
        var (main, _) = Rig();
        main.Reports.AddReport(type);
        Assert.IsType(designer, main.Reports.Designer);
        Assert.Equal(type != ReportType.Dashboard, main.Reports.ShowTable);
    }

    [Fact]
    public async Task DeleteAsksForConfirmationAndClearsDrillDownsToIt()
    {
        var (main, dialogs) = Rig();
        var first = Add(main, ReportType.Summary);
        var second = Add(main, ReportType.Chart);
        second.DrillDownReport = first.Name;
        main.Reports.SelectedReport = main.Reports.Reports[0];

        await main.Reports.DeleteReportCommand.ExecuteAsync(null);
        Assert.Equal(2, main.Current.Reports.Count);
        Assert.Equal("Delete report", dialogs.LastConfirmTitle);

        dialogs.NextConfirm = true;
        await main.Reports.DeleteReportCommand.ExecuteAsync(null);
        Assert.Single(main.Current.Reports);
        Assert.Same(second, main.Reports.SelectedReport!.Model);
        Assert.Equal(string.Empty, second.DrillDownReport);

        await main.Reports.DeleteReportCommand.ExecuteAsync(null);
        Assert.Empty(main.Current.Reports);
        Assert.Null(main.Reports.SelectedReport);
        Assert.Null(main.Reports.Designer);
    }

    [Fact]
    public void NamesAreSuggestedFromTheCaptionAndMustBeUniqueAndRouteSafe()
    {
        Assert.Equal("SalesByMonth", ReportNames.FromCaption("Sales by month!"));
        Assert.Equal("Report2026", ReportNames.FromCaption("2026"));
        Assert.Equal("Report", ReportNames.FromCaption("***"));
        Assert.Equal("Sales3", ReportNames.Suggest("sales", ["Sales", "sales2"]));
        Assert.True(ReportNames.IsRouteSafe("a-b_1"));
        Assert.False(ReportNames.IsRouteSafe("1a"));
        Assert.False(ReportNames.IsRouteSafe("a b"));

        var (main, _) = Rig();
        var one = Add(main, ReportType.Summary);
        var two = Add(main, ReportType.Summary);
        Assert.Equal("SummaryReport", one.Name);
        Assert.Equal("SummaryReport2", two.Name);

        // The name follows the caption while it is still the suggestion.
        main.Reports.Caption = "Totals per customer";
        Assert.Equal("TotalsPerCustomer", two.Name);
        Assert.Equal("TotalsPerCustomer", main.Reports.Name);

        // A name typed by hand stops following.
        main.Reports.Name = "custom";
        main.Reports.Caption = "Another caption";
        Assert.Equal("custom", two.Name);

        main.Reports.Name = "SUMMARYREPORT";
        Assert.Equal("custom", two.Name);
        Assert.True(main.Reports.HasMessage);
        Assert.Equal("SUMMARYREPORT", main.Reports.Name);

        main.Reports.Name = "bad name";
        Assert.Equal("custom", two.Name);
        Assert.True(main.Reports.HasMessage);

        main.Reports.Name = "good-name";
        Assert.Equal("good-name", two.Name);
        Assert.False(main.Reports.HasMessage);
    }

    [Fact]
    public void RenamingAReportFollowsDrillDownsAndDashboardItems()
    {
        var (main, _) = Rig();
        var summary = Add(main, ReportType.Summary);
        var chart = Add(main, ReportType.Chart);
        chart.DrillDownReport = summary.Name;
        var dashboard = Add(main, ReportType.Dashboard);
        dashboard.Dashboard.Items.Add(new DashboardItem { Kind = DashboardItemKind.Report, Report = summary.Name });

        main.Reports.SelectedReport = main.Reports.Reports.First(r => r.Model == summary);
        main.Reports.Name = "Overview";

        Assert.Equal("Overview", chart.DrillDownReport);
        Assert.Equal("Overview", dashboard.Dashboard.Items[0].Report);
    }

    [Fact]
    public void MoveAndDuplicateKeepTheModelListInStep()
    {
        var (main, _) = Rig();
        var a = Add(main, ReportType.Summary);
        var b = Add(main, ReportType.Crosstab);
        var c = Add(main, ReportType.Chart);

        main.Reports.SelectedReport = main.Reports.Reports[2];
        main.Reports.MoveReportUpCommand.Execute(null);
        Assert.Equal([a, c, b], main.Current.Reports);
        Assert.Equal([a, c, b], main.Reports.Reports.Select(r => r.Model));
        main.Reports.MoveReportUpCommand.Execute(null);
        main.Reports.MoveReportUpCommand.Execute(null);
        Assert.Equal([c, a, b], main.Current.Reports);
        main.Reports.MoveReportDownCommand.Execute(null);
        Assert.Equal([a, c, b], main.Current.Reports);

        main.Reports.SelectedReport = main.Reports.Reports.First(r => r.Model == a);
        ((SummaryReportViewModel)main.Reports.Designer!).Aggregates.Add();
        main.Reports.DuplicateReportCommand.Execute(null);
        Assert.Equal(4, main.Current.Reports.Count);
        var copy = main.Current.Reports[1];
        Assert.NotSame(a, copy);
        Assert.Equal("SummaryReport2", copy.Name);
        Assert.EndsWith("(copy)", copy.Caption);
        Assert.Equal(a.Summary.Aggregates.Count, copy.Summary.Aggregates.Count);
        Assert.NotSame(a.Summary.Aggregates[0], copy.Summary.Aggregates[0]);
        Assert.Same(copy, main.Reports.SelectedReport!.Model);
    }

    [Fact]
    public void SearchFiltersTheListAndMovesStayInTheFilter()
    {
        var (main, _) = Rig();
        var a = Add(main, ReportType.Summary);
        var b = Add(main, ReportType.Chart);
        Add(main, ReportType.Summary);

        main.Reports.SearchText = "chart";
        Assert.Equal([b], main.Reports.Reports.Select(r => r.Model));
        main.Reports.SearchText = "summary";
        Assert.Equal(2, main.Reports.Reports.Count);
        main.Reports.SearchText = string.Empty;
        Assert.Equal(3, main.Reports.Reports.Count);
        Assert.Equal("Summary", main.Reports.Reports[0].TypeText);
        Assert.Equal("Sales", main.Reports.Reports[0].TableText);
        Assert.NotNull(a);
    }

    [Fact]
    public void CommonSettingsWriteThroughToTheModel()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Summary);
        var vm = main.Reports;

        vm.Caption = "Cap";
        vm.TableName = "Orders";
        vm.Filter = "Total > 5";
        vm.Description = "Desc";
        vm.ShowInMenu = false;
        vm.MenuGroup = "Group";
        vm.MenuOrderText = "7";
        vm.Icon = "bar-chart";
        vm.MinLevelText = "3";

        Assert.Equal("Cap", def.Caption);
        Assert.Equal("Orders", def.Table);
        Assert.Equal("Total > 5", def.Filter);
        Assert.Equal("Desc", def.Description);
        Assert.False(def.ShowInMenu);
        Assert.Equal("Group", def.MenuGroup);
        Assert.Equal(7, def.MenuOrder);
        Assert.Equal("bar-chart", def.Icon);
        Assert.Equal(3, def.MinLevel);

        vm.MenuOrderText = "x";
        Assert.Equal(7, def.MenuOrder);
        Assert.Equal("x", vm.MenuOrderText);
        Assert.True(vm.HasMessage);
        vm.MinLevelText = "-1";
        Assert.Equal(3, def.MinLevel);
    }

    [Fact]
    public void ChangingTheTableClearsFieldsItDoesNotHaveAndDrillDownFollowsTheTable()
    {
        var (main, _) = Rig();
        var summary = Add(main, ReportType.Summary);
        var chart = Add(main, ReportType.Chart);
        chart.DrillDownReport = summary.Name;
        var designer = (ChartReportViewModel)main.Reports.Designer!;
        Assert.Equal("Customer", chart.Chart.Category.Field);
        Assert.Contains(summary.Name, main.Reports.DrillDownChoices);

        main.Reports.TableName = "Orders";
        Assert.Equal(string.Empty, chart.Chart.Category.Field);
        Assert.Equal(string.Empty, chart.DrillDownReport);
        Assert.DoesNotContain(summary.Name, main.Reports.DrillDownChoices);
        Assert.NotSame(designer, main.Reports.Designer);
        Assert.Equal("Orders", main.Reports.TableName);
    }

    [Fact]
    public void DrillDownOffersOnlySummaryCrosstabAndChartOfTheSameTable()
    {
        var (main, _) = Rig();
        var summary = Add(main, ReportType.Summary);
        var crosstab = Add(main, ReportType.Crosstab);
        var chart = Add(main, ReportType.Chart);
        var calendar = Add(main, ReportType.Calendar);
        var other = Add(main, ReportType.Summary, "Orders");
        Add(main, ReportType.Dashboard);

        main.Reports.SelectedReport = main.Reports.Reports.First(r => r.Model == calendar);
        Assert.Equal([ReportChoices.TableListLabel, summary.Name, crosstab.Name, chart.Name], main.Reports.DrillDownChoices);
        Assert.DoesNotContain(other.Name, main.Reports.DrillDownChoices);

        main.Reports.DrillDownChoice = chart.Name;
        Assert.Equal(chart.Name, calendar.DrillDownReport);
        main.Reports.DrillDownChoice = ReportChoices.TableListLabel;
        Assert.Equal(string.Empty, calendar.DrillDownReport);

        main.Reports.SelectedReport = main.Reports.Reports.First(r => r.Model == chart);
        Assert.DoesNotContain(chart.Name, main.Reports.DrillDownChoices);
    }

    // ---- pickers ----

    [Fact]
    public void PickersOfferOnlyFieldsThatMakeSense()
    {
        var (main, _) = Rig();
        var sales = main.Current.Tables.Single(t => t.Name == "Sales");
        Assert.Equal(["Id", "Total"], ReportFieldRules.FieldsFor(sales, FieldRole.Numeric));
        Assert.Equal(["OrderDate", "ShipTime"], ReportFieldRules.FieldsFor(sales, FieldRole.Date));
        Assert.Equal(["Shipped"], ReportFieldRules.FieldsFor(sales, FieldRole.Boolean));
        Assert.DoesNotContain("Photo", ReportFieldRules.FieldsFor(sales, FieldRole.Group));
        Assert.Equal(["Id", "Total", "OrderDate", "ShipTime"], ReportFieldRules.AggregateFields(sales, AggregateFunction.Max));
        Assert.Equal(["Id", "Total"], ReportFieldRules.AggregateFields(sales, AggregateFunction.Average));
        Assert.Equal(6, ReportFieldRules.AggregateFields(sales, AggregateFunction.CountDistinct).Count);

        Assert.Equal([GroupInterval.None, GroupInterval.Day, GroupInterval.Week, GroupInterval.Month, GroupInterval.Quarter, GroupInterval.Year], ReportFieldRules.IntervalsFor(FieldType.Date));
        Assert.Equal([GroupInterval.None, GroupInterval.Range], ReportFieldRules.IntervalsFor(FieldType.Decimal));
        Assert.Equal([GroupInterval.None, GroupInterval.FirstLetter], ReportFieldRules.IntervalsFor(FieldType.Text));
        Assert.Equal([GroupInterval.None], ReportFieldRules.IntervalsFor(FieldType.Boolean));
    }

    [Fact]
    public void AggregateAndGroupRowsOfferChoicesThatFollowTheirChoices()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Summary);
        var d = (SummaryReportViewModel)main.Reports.Designer!;

        var aggregate = d.Aggregates.Rows[0];
        Assert.Equal(ReportFieldRules.AllRowsLabel, aggregate.Field);
        Assert.Contains(ReportFieldRules.AllRowsLabel, aggregate.FieldChoices);
        aggregate.Function = AggregateFunction.Sum;
        Assert.Equal(["Id", "Total"], aggregate.FieldChoices);
        Assert.Equal("Id", aggregate.Field);
        aggregate.Function = AggregateFunction.Count;
        Assert.Equal(7, aggregate.FieldChoices.Count);

        d.Groups.Add();
        var group = d.Groups.Rows[1];
        group.Field = "OrderDate";
        Assert.Equal(6, group.IntervalChoices.Count);
        group.Interval = GroupInterval.Quarter;
        group.Field = "Total";
        Assert.Equal(GroupInterval.None, group.Interval);
        Assert.Equal([GroupInterval.None, GroupInterval.Range], group.IntervalChoices);
        Assert.True(!group.ShowRange);
        group.Interval = GroupInterval.Range;
        Assert.True(group.ShowRange);
    }

    [Fact]
    public void CalendarPickersFollowTheFieldTypes()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Calendar);
        var d = (CalendarReportViewModel)main.Reports.Designer!;
        Assert.Equal(["OrderDate", "ShipTime"], d.StartFieldChoices);
        Assert.Equal([ReportFieldRules.NoneLabel, "OrderDate", "ShipTime"], d.EndFieldChoices);
        Assert.Equal([ReportFieldRules.NoneLabel, "Shipped"], d.AllDayFieldChoices);
        Assert.DoesNotContain("Photo", d.TitleFieldChoices);
        Assert.Equal("OrderDate", d.StartField);
        Assert.Equal("Customer", d.TitleField);
    }

    [Fact]
    public void DashboardReportPickerExcludesDashboardsAndItself()
    {
        var (main, _) = Rig();
        var summary = Add(main, ReportType.Summary);
        var calendar = Add(main, ReportType.Calendar);
        var other = Add(main, ReportType.Dashboard);
        var dashboard = Add(main, ReportType.Dashboard);
        var d = (DashboardReportViewModel)main.Reports.Designer!;
        Assert.Equal([summary.Name, calendar.Name], d.EmbeddableReports);
        Assert.Contains(dashboard.Name, main.Reports.AllReportNames(other));
        Assert.DoesNotContain(dashboard.Name, d.LinkTargets);
        Assert.Contains("Sales", d.LinkTargets);
    }

    // ---- write-through per type ----

    [Fact]
    public void SummaryPropertiesWriteThroughToTheModel()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Summary);
        var d = (SummaryReportViewModel)main.Reports.Designer!;
        var model = def.Summary;

        Assert.Single(model.Groups);
        Assert.Equal("Customer", model.Groups[0].Field);
        d.Groups.Add();
        Assert.Equal(2, model.Groups.Count);
        var group = d.Groups.Rows[1];
        group.Field = "OrderDate";
        group.Interval = GroupInterval.Month;
        group.Sort = SortDirection.Descending;
        group.SortBy = GroupSortBy.Aggregate;
        group.Caption = "Month";
        Assert.Equal("OrderDate", model.Groups[1].Field);
        Assert.Equal(GroupInterval.Month, model.Groups[1].Interval);
        Assert.Equal(SortDirection.Descending, model.Groups[1].Sort);
        Assert.Equal(GroupSortBy.Aggregate, model.Groups[1].SortBy);
        Assert.Equal("Month", model.Groups[1].Caption);

        group.Field = "Total";
        group.Interval = GroupInterval.Range;
        group.RangeSizeText = "25";
        Assert.Equal(25, model.Groups[1].RangeSize);
        group.RangeSizeText = "0";
        Assert.Equal(25, model.Groups[1].RangeSize);
        Assert.Equal("0", group.RangeSizeText);
        Assert.True(d.HasMessage);
        group.RangeSizeText = "12.5";
        Assert.Equal(12.5, model.Groups[1].RangeSize);

        d.Groups.Move(-1);
        Assert.Equal("Total", model.Groups[0].Field);
        d.Groups.Remove();
        Assert.Single(model.Groups);

        d.Aggregates.Add();
        var aggregate = d.Aggregates.Rows[1];
        aggregate.Function = AggregateFunction.Sum;
        aggregate.Field = "Total";
        aggregate.Caption = "Revenue";
        aggregate.FormatString = "N2";
        Assert.Equal(AggregateFunction.Sum, model.Aggregates[1].Function);
        Assert.Equal("Total", model.Aggregates[1].Field);
        Assert.Equal("Revenue", model.Aggregates[1].Caption);
        Assert.Equal("N2", model.Aggregates[1].FormatString);
        d.Aggregates.Remove();
        Assert.Single(model.Aggregates);

        d.Layout = SummaryLayout.Compact;
        Assert.Equal(SummaryLayout.Compact, model.Layout);
        Assert.False(d.IsDetailLayout);
        d.Layout = SummaryLayout.Detail;
        d.DetailFields.Add();
        Assert.Single(model.DetailFields);
        d.DetailFields.Rows[0].Field = "Total";
        Assert.Equal(["Total"], model.DetailFields);
        d.DetailFields.Remove();
        Assert.Empty(model.DetailFields);

        d.ShowGroupTotals = false;
        d.ShowGrandTotal = false;
        d.GroupsPerPageText = "5";
        d.DetailRowsPerGroupText = "7";
        Assert.False(model.ShowGroupTotals);
        Assert.False(model.ShowGrandTotal);
        Assert.Equal(5, model.GroupsPerPage);
        Assert.Equal(7, model.DetailRowsPerGroup);
        d.GroupsPerPageText = "many";
        Assert.Equal(5, model.GroupsPerPage);
        Assert.Equal("many", d.GroupsPerPageText);

        d.Filters.Add();
        Assert.Single(model.Filters);
        d.Filters.Rows[0].Field = "OrderDate";
        d.Filters.Rows[0].Caption = "Ordered";
        d.Filters.Rows[0].RelativeDates = false;
        Assert.Equal("OrderDate", model.Filters[0].Field);
        Assert.Equal("Ordered", model.Filters[0].Caption);
        Assert.False(model.Filters[0].RelativeDates);
        Assert.True(d.Filters.Rows[0].IsDate);
        d.Filters.Remove();
        Assert.Empty(model.Filters);
    }

    [Fact]
    public void CrosstabPropertiesWriteThroughToTheModel()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Crosstab);
        var d = (CrosstabReportViewModel)main.Reports.Designer!;
        var model = def.Crosstab;

        d.Rows.Field = "OrderDate";
        d.Rows.Interval = GroupInterval.Year;
        d.Columns.Field = "Shipped";
        d.Columns.Sort = SortDirection.Descending;
        d.Value.Function = AggregateFunction.Average;
        d.Value.Field = "Total";
        d.Value.FormatString = "C";
        d.ShowRowTotals = false;
        d.ShowColumnTotals = false;
        d.ShowGrandTotal = false;
        d.MaxColumnsText = "12";
        d.Filters.Add();

        Assert.Equal("OrderDate", model.Rows.Field);
        Assert.Equal(GroupInterval.Year, model.Rows.Interval);
        Assert.Equal("Shipped", model.Columns.Field);
        Assert.Equal(SortDirection.Descending, model.Columns.Sort);
        Assert.Equal(AggregateFunction.Average, model.Value.Function);
        Assert.Equal("Total", model.Value.Field);
        Assert.Equal("C", model.Value.FormatString);
        Assert.False(model.ShowRowTotals);
        Assert.False(model.ShowColumnTotals);
        Assert.False(model.ShowGrandTotal);
        Assert.Equal(12, model.MaxColumns);
        Assert.Single(model.Filters);
        d.MaxColumnsText = "-3";
        Assert.Equal(12, model.MaxColumns);
    }

    [Fact]
    public void ChartPropertiesWriteThroughAndPieWithSeveralSeriesWarns()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Chart);
        var d = (ChartReportViewModel)main.Reports.Designer!;
        var model = def.Chart;

        Assert.Equal(12, d.KindChoices.Count);
        d.KindChoice = ChartKindChoice.Of(ChartKind.Pie);
        Assert.Equal(ChartKind.Pie, model.Kind);
        Assert.False(d.HasPieSeriesWarning);
        d.Series.Add();
        Assert.True(d.HasPieSeriesWarning);
        Assert.Equal(ChartReportViewModel.PieSeriesWarningText, d.PieSeriesWarning);
        d.Kind = ChartKind.Doughnut;
        Assert.True(d.HasPieSeriesWarning);
        d.Kind = ChartKind.PolarArea;
        Assert.True(d.HasPieSeriesWarning);
        d.Kind = ChartKind.StackedColumn;
        Assert.False(d.HasPieSeriesWarning);
        d.Kind = ChartKind.Pie;
        d.Series.Remove();
        Assert.False(d.HasPieSeriesWarning);
        d.Kind = ChartKind.Column;
        d.Series.Add();

        d.Category.Field = "OrderDate";
        d.Category.Interval = GroupInterval.Month;
        Assert.Equal("OrderDate", model.Category.Field);
        Assert.Equal(GroupInterval.Month, model.Category.Interval);

        var series = d.Series.Rows[1];
        series.Value.Function = AggregateFunction.Sum;
        series.Value.Field = "Total";
        series.Caption = "Revenue";
        series.Color = "#112233";
        series.Axis = ChartAxis.Secondary;
        series.KindOverride = "Line";
        Assert.Equal(AggregateFunction.Sum, model.Series[1].Value.Function);
        Assert.Equal("Total", model.Series[1].Value.Field);
        Assert.Equal("Revenue", model.Series[1].Caption);
        Assert.Equal("#112233", model.Series[1].Color);
        Assert.Equal(ChartAxis.Secondary, model.Series[1].Axis);
        Assert.Equal(ChartKind.Line, model.Series[1].Kind);
        series.KindOverride = "Pie";
        Assert.Equal(ChartKind.Line, model.Series[1].Kind);
        series.KindOverride = ReportChoices.ChartKindLabel;
        Assert.Null(model.Series[1].Kind);
        Assert.Equal(["(chart kind)", "Column", "Line", "Area"], d.SeriesKindChoices);

        d.Series.Selected = series;
        d.SetColorCommand.Execute("#0D6EFD");
        Assert.Equal("#0D6EFD", model.Series[1].Color);
        d.Series.MoveUpCommand.Execute(null);
        Assert.Equal("Revenue", model.Series[0].Caption);

        Assert.False(d.UseSeriesField);
        d.UseSeriesField = true;
        Assert.NotNull(model.SeriesField);
        Assert.NotNull(d.SeriesField);
        d.SeriesField!.Field = "Shipped";
        Assert.Equal("Shipped", model.SeriesField!.Field);
        d.UseSeriesField = false;
        Assert.Null(model.SeriesField);

        d.Title = "T";
        d.Legend = LegendPosition.Right;
        d.ShowDataLabels = true;
        d.HeightText = "420";
        d.TopNText = "5";
        d.TrendLine = true;
        d.XAxisTitle = "X";
        d.YAxisTitle = "Y";
        Assert.Equal("T", model.Title);
        Assert.Equal(LegendPosition.Right, model.Legend);
        Assert.True(model.ShowDataLabels);
        Assert.Equal(420, model.Height);
        Assert.Equal(5, model.TopN);
        Assert.True(model.TrendLine);
        Assert.Equal("X", model.XAxisTitle);
        Assert.Equal("Y", model.YAxisTitle);
        d.HeightText = "5";
        Assert.Equal(420, model.Height);

        d.Filters.Add();
        Assert.Single(model.Filters);
    }

    [Fact]
    public void CalendarPropertiesWriteThroughToTheModel()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Calendar);
        var d = (CalendarReportViewModel)main.Reports.Designer!;
        var model = def.Calendar;

        d.TitleField = "Total";
        d.StartField = "ShipTime";
        d.EndField = "OrderDate";
        d.AllDayField = "Shipped";
        d.ColorField = "Customer";
        d.DefaultColor = "#abcdef";
        Assert.Equal("Total", model.TitleField);
        Assert.Equal("ShipTime", model.StartField);
        Assert.Equal("OrderDate", model.EndField);
        Assert.Equal("Shipped", model.AllDayField);
        Assert.Equal("Customer", model.ColorField);
        Assert.Equal("#abcdef", model.DefaultColor);
        d.EndField = ReportFieldRules.NoneLabel;
        d.AllDayField = ReportFieldRules.NoneLabel;
        d.ColorField = ReportFieldRules.NoneLabel;
        Assert.Equal(string.Empty, model.EndField);
        Assert.Equal(string.Empty, model.AllDayField);
        Assert.Equal(string.Empty, model.ColorField);

        d.ColorRules.Add();
        var rule = d.ColorRules.Rows[0];
        rule.Operator = ConditionOperator.Contains;
        rule.Value = "VIP";
        rule.Style = "#ff8800";
        Assert.Equal(ConditionOperator.Contains, model.ColorRules[0].Operator);
        Assert.Equal("VIP", model.ColorRules[0].Value);
        Assert.Equal("#ff8800", model.ColorRules[0].Style);
        d.SetRuleColorCommand.Execute("#198754");
        Assert.Equal("#198754", model.ColorRules[0].Style);
        rule.Operator = ConditionOperator.IsEmpty;
        Assert.False(rule.NeedsValue);

        d.PopoverFields.Add();
        Assert.Single(model.PopoverFields);

        d.SetViewIncluded(CalendarView.Year, true);
        Assert.Contains(CalendarView.Year, model.Views);
        Assert.Equal(CalendarView.Year, model.Views[^1]);
        d.DefaultView = CalendarView.Week;
        Assert.Equal(CalendarView.Week, model.DefaultView);
        d.SetViewIncluded(CalendarView.Week, false);
        Assert.Equal(CalendarView.Month, model.DefaultView);
        Assert.DoesNotContain(CalendarView.Week, d.DefaultViewChoices);
        foreach (var view in model.Views.Skip(1).ToList())
        {
            d.SetViewIncluded(view, false);
        }

        d.SetViewIncluded(CalendarView.Month, false);
        Assert.Equal([CalendarView.Month], model.Views);
        Assert.True(d.HasMessage);

        d.FirstDayChoice = ReportChoices.Days[0];
        d.ShowWeekends = false;
        d.AllowAdd = false;
        d.AllowOpen = false;
        Assert.Equal(0, model.FirstDay);
        Assert.False(model.ShowWeekends);
        Assert.False(model.AllowAdd);
        Assert.False(model.AllowOpen);
        d.Filters.Add();
        Assert.Single(model.Filters);
    }

    [Fact]
    public void DashboardPropertiesWriteThroughAndTheLayoutWraps()
    {
        var (main, _) = Rig();
        var summary = Add(main, ReportType.Summary);
        var def = Add(main, ReportType.Dashboard);
        var d = (DashboardReportViewModel)main.Reports.Designer!;
        var model = def.Dashboard;

        d.AddKpi();
        d.AddReport();
        Assert.Equal(2, model.Items.Count);
        Assert.Equal(DashboardItemKind.Kpi, model.Items[0].Kind);
        Assert.Equal(summary.Name, model.Items[1].Report);

        var kpi = d.Items.Rows[0];
        kpi.Caption = "Revenue";
        kpi.Table = "Sales";
        kpi.Value.Function = AggregateFunction.Sum;
        kpi.Value.Field = "Total";
        kpi.Value.FormatString = "C";
        kpi.Filter = "Shipped = 1";
        kpi.Style = "success";
        kpi.Icon = "cash-coin";
        kpi.LinkTo = summary.Name;
        Assert.Equal("Revenue", model.Items[0].Caption);
        Assert.Equal("Sales", model.Items[0].Table);
        Assert.Equal(AggregateFunction.Sum, model.Items[0].Value.Function);
        Assert.Equal("Total", model.Items[0].Value.Field);
        Assert.Equal("C", model.Items[0].Value.FormatString);
        Assert.Equal("Shipped = 1", model.Items[0].Filter);
        Assert.Equal("success", model.Items[0].Style);
        Assert.Equal("cash-coin", model.Items[0].Icon);
        Assert.Equal(summary.Name, model.Items[0].LinkTo);
        Assert.Equal(["Id", "Total"], kpi.Value.FieldChoices);

        // A new table clears an aggregate field the table does not have.
        kpi.Table = "Customers";
        Assert.Equal(string.Empty, model.Items[0].Value.Field);
        Assert.Equal(AggregateFunction.Count, model.Items[0].Value.Function);

        var report = d.Items.Rows[1];
        report.Report = summary.Name;
        report.SpanText = "8";
        report.HeightText = "300";
        Assert.Equal(8, model.Items[1].Span);
        Assert.Equal(300, model.Items[1].Height);
        report.SpanText = "13";
        Assert.Equal(8, model.Items[1].Span);
        Assert.True(d.HasMessage);
        report.SpanText = "0";
        Assert.Equal(8, model.Items[1].Span);

        kpi.SpanText = "6";
        Assert.Equal([0, 1], d.Boxes.Select(b => b.Row));
        report.SpanText = "6";
        Assert.Equal([0, 0], d.Boxes.Select(b => b.Row));
        Assert.Equal([0, 6], d.Boxes.Select(b => b.Column));
        report.SpanText = "7";
        Assert.Equal(2, d.RowCount);

        d.Items.Selected = report;
        d.Items.MoveUpCommand.Execute(null);
        Assert.Same(model.Items[0], report.Model);
        d.Items.RemoveCommand.Execute(null);
        Assert.Single(model.Items);

        d.RefreshSecondsText = "30";
        Assert.Equal(30, model.RefreshSeconds);
        d.RefreshSecondsText = "-1";
        Assert.Equal(30, model.RefreshSeconds);
    }

    [Fact]
    public void DashboardLayoutPlacesBoxesLikeBootstrapRows()
    {
        var boxes = DashboardLayout.Place([("a", 8, 0), ("b", 5, 0), ("c", 4, 0), ("d", 3, 0), ("e", 20, 0), ("f", 0, 0)]);
        Assert.Equal([(0, 0, 8), (1, 0, 5), (1, 5, 4), (1, 9, 3), (2, 0, 12), (3, 0, 1)], boxes.Select(b => (b.Row, b.Column, b.Span)));
        Assert.Equal(4, DashboardLayout.RowCount(boxes));
        Assert.Equal(0, DashboardLayout.RowCount(DashboardLayout.Place([])));
        var exact = DashboardLayout.Place([("a", 6, 0), ("b", 6, 0), ("c", 6, 0)]);
        Assert.Equal([0, 0, 1], exact.Select(b => b.Row));
    }

    // ---- schematic models ----

    [Fact]
    public void SummarySchematicShowsGroupsTotalsAndLayout()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Summary);
        var d = (SummaryReportViewModel)main.Reports.Designer!;
        d.Aggregates.Rows[0].Caption = "Orders";
        var detail = (SummarySchematic)main.Reports.Schematic!;
        Assert.Equal(SummaryLayout.Detail, detail.Layout);
        Assert.Contains(detail.Rows, r => r.Kind == SchematicRowKind.Group && r.Cells[0].StartsWith("Customer name:"));
        Assert.Contains(detail.Rows, r => r.Kind == SchematicRowKind.GroupTotal);
        Assert.Equal(SchematicRowKind.GrandTotal, detail.Rows[^1].Kind);

        d.ShowGroupTotals = false;
        d.ShowGrandTotal = false;
        detail = (SummarySchematic)main.Reports.Schematic!;
        Assert.DoesNotContain(detail.Rows, r => r.Kind is SchematicRowKind.GroupTotal or SchematicRowKind.GrandTotal);

        d.Layout = SummaryLayout.Compact;
        d.ShowGrandTotal = true;
        var compact = (SummarySchematic)main.Reports.Schematic!;
        Assert.Equal(["Customer name", "Orders"], compact.Headers);
        Assert.Equal(4, compact.Rows.Count);
        Assert.Equal(SchematicRowKind.GrandTotal, compact.Rows[^1].Kind);

        d.Groups.Rows[0].Field = "OrderDate";
        d.Groups.Rows[0].Interval = GroupInterval.Month;
        compact = (SummarySchematic)main.Reports.Schematic!;
        Assert.Equal("2026-01", compact.Rows[0].Cells[0]);
        Assert.Equal("2026-02", compact.Rows[1].Cells[0]);
        Assert.NotNull(def);
    }

    [Fact]
    public void CrosstabSchematicUsesTheChosenHeadingsAndTotals()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Crosstab);
        var d = (CrosstabReportViewModel)main.Reports.Designer!;
        d.Rows.Field = "Customer";
        d.Columns.Field = "OrderDate";
        d.Columns.Interval = GroupInterval.Year;
        var matrix = (CrosstabSchematic)main.Reports.Schematic!;
        Assert.Equal("Customer name \\ OrderDate", matrix.Headers[0]);
        Assert.Equal("2026", matrix.Headers[1]);
        Assert.Equal("Total", matrix.Headers[^1]);
        Assert.Equal("Total", matrix.Rows[^1].Cells[0]);

        d.ShowRowTotals = false;
        d.ShowColumnTotals = false;
        d.MaxColumnsText = "2";
        matrix = (CrosstabSchematic)main.Reports.Schematic!;
        Assert.Equal(3, matrix.Headers.Count);
        Assert.Equal(3, matrix.Rows.Count);
        Assert.DoesNotContain("Total", matrix.Headers);
    }

    [Fact]
    public void ChartSchematicFollowsKindSeriesTopNAndSingleSeriesKinds()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Chart);
        var d = (ChartReportViewModel)main.Reports.Designer!;
        d.Series.Add();
        d.Series.Rows[1].Color = "#112233";
        d.Series.Rows[1].Axis = ChartAxis.Secondary;
        d.Series.Rows[1].KindOverride = "Line";
        var chart = (ChartSchematic)main.Reports.Schematic!;
        Assert.Equal(ChartKind.Column, chart.Kind);
        Assert.Equal(2, chart.Series.Count);
        Assert.Equal(5, chart.Categories.Count);
        Assert.Equal("#112233", chart.Series[1].Color);
        Assert.Equal(ChartAxis.Secondary, chart.Series[1].Axis);
        Assert.Equal(ChartKind.Line, chart.Series[1].Kind);
        Assert.Equal(ChartKind.Column, chart.Series[0].Kind);

        d.Kind = ChartKind.Pie;
        chart = (ChartSchematic)main.Reports.Schematic!;
        Assert.Single(chart.Series);

        d.Kind = ChartKind.Column;
        d.TopNText = "3";
        chart = (ChartSchematic)main.Reports.Schematic!;
        Assert.Equal(3, chart.Categories.Count);
        Assert.Equal(3, chart.Series[0].Values.Count);
        Assert.True(chart.Series[0].Values[0] >= chart.Series[0].Values[1]);

        d.UseSeriesField = true;
        d.TopNText = "0";
        chart = (ChartSchematic)main.Reports.Schematic!;
        Assert.Equal(3, chart.Series.Count);
        d.Title = "Revenue";
        Assert.Equal("Revenue", ((ChartSchematic)main.Reports.Schematic!).Title);
    }

    [Fact]
    public void CalendarSchematicHasSampleEventsInTheRuleColours()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Calendar);
        var d = (CalendarReportViewModel)main.Reports.Designer!;
        d.ColorRules.Add();
        d.ColorRules.Rows[0].Style = "#ff0000";
        d.ColorRules.Add();
        d.ColorRules.Rows[1].Style = "success";
        var calendar = (CalendarSchematic)main.Reports.Schematic!;
        Assert.Equal(7, calendar.DayHeaders.Count);
        Assert.Equal("Mon", calendar.DayHeaders[0]);
        Assert.True(calendar.Events.Count >= 4);
        Assert.Equal("#ff0000", calendar.Events[0].Color);
        Assert.Equal("success", calendar.Events[1].Color);
        Assert.All(calendar.Weeks, w => Assert.Equal(7, w.Count));

        d.ShowWeekends = false;
        d.FirstDayChoice = ReportChoices.Days[0];
        calendar = (CalendarSchematic)main.Reports.Schematic!;
        Assert.Equal(5, calendar.DayHeaders.Count);
        Assert.Equal("Mon", calendar.DayHeaders[0]);
        Assert.All(calendar.Weeks, w => Assert.Equal(5, w.Count));
        Assert.All(calendar.Events, e => Assert.True(e.Column < 5));

        d.EndField = "ShipTime";
        d.DefaultColor = "#123456";
        d.ColorRules.Selected = d.ColorRules.Rows[0];
        d.ColorRules.Remove();
        d.ColorRules.Remove();
        calendar = (CalendarSchematic)main.Reports.Schematic!;
        Assert.All(calendar.Events, e => Assert.Equal("#123456", e.Color));
        Assert.Contains(calendar.Events, e => e.Span > 1);
    }

    [Fact]
    public void DashboardSchematicHasOneBoxPerItemInItsSpan()
    {
        var (main, _) = Rig();
        var summary = Add(main, ReportType.Summary);
        Add(main, ReportType.Dashboard);
        var d = (DashboardReportViewModel)main.Reports.Designer!;
        d.AddKpi();
        d.AddReport();
        d.Items.Rows[0].Caption = "Revenue";
        d.Items.Rows[0].SpanText = "5";
        d.Items.Rows[1].SpanText = "9";
        var dashboard = (DashboardSchematic)main.Reports.Schematic!;
        Assert.Equal(["Revenue", summary.Caption], dashboard.Boxes.Select(b => b.Caption));
        Assert.Equal([5, 9], dashboard.Boxes.Select(b => b.Span));
        Assert.Equal(2, dashboard.RowCount);
        d.RefreshSecondsText = "60";
        Assert.Equal(60, ((DashboardSchematic)main.Reports.Schematic!).RefreshSeconds);
    }

    // ---- dirty tracking ----

    [Fact]
    public async Task EditingAnythingOnTheReportsTabMarksTheProjectDirty()
    {
        var (main, dialogs) = Rig();
        using var folder = new TempFolder();
        dialogs.NextSaveProject = folder.File("reports.xml");
        await main.SaveAsCommand.ExecuteAsync(null);
        Assert.False(main.IsDirty);

        var steps = new List<(string Name, Action Edit)>
        {
            ("add", () => main.Reports.AddReport(ReportType.Summary)),
            ("caption", () => main.Reports.Caption = "Edited"),
            ("filter", () => main.Reports.Filter = "Total > 1"),
            ("summary aggregate", () => ((SummaryReportViewModel)main.Reports.Designer!).Aggregates.Add()),
            ("summary layout", () => ((SummaryReportViewModel)main.Reports.Designer!).Layout = SummaryLayout.Compact),
            ("group interval", () => ((SummaryReportViewModel)main.Reports.Designer!).Groups.Rows[0].Caption = "G"),
            ("chart", () =>
            {
                main.Reports.AddReport(ReportType.Chart);
                ((ChartReportViewModel)main.Reports.Designer!).Title = "Chart";
            }),
            ("chart series colour", () => ((ChartReportViewModel)main.Reports.Designer!).Series.Rows[0].Color = "#123456"),
            ("calendar", () =>
            {
                main.Reports.AddReport(ReportType.Calendar);
                ((CalendarReportViewModel)main.Reports.Designer!).ShowWeekends = false;
            }),
            ("dashboard item", () =>
            {
                main.Reports.AddReport(ReportType.Dashboard);
                ((DashboardReportViewModel)main.Reports.Designer!).AddKpi();
            }),
            ("dashboard span", () => ((DashboardReportViewModel)main.Reports.Designer!).Items.Rows[0].SpanText = "9"),
            ("move", () => main.Reports.MoveReportUpCommand.Execute(null)),
            ("duplicate", () => main.Reports.DuplicateReportCommand.Execute(null)),
        };

        foreach (var (name, edit) in steps)
        {
            edit();
            Assert.True(main.IsDirty, $"{name} did not mark the project dirty.");
            await main.SaveProjectCommand.ExecuteAsync(null);
            Assert.False(main.IsDirty, $"saving after '{name}' left the project dirty.");
        }

        dialogs.NextConfirm = true;
        await main.Reports.DeleteReportCommand.ExecuteAsync(null);
        Assert.True(main.IsDirty);
    }

    [Fact]
    public void TheFingerprintCoversReportSettings()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Chart);
        var before = CustomizationFingerprint.Compute(main.Current);
        ((ChartReportViewModel)main.Reports.Designer!).TrendLine = true;
        Assert.NotEqual(before, CustomizationFingerprint.Compute(main.Current));
    }

    // ---- validation service ----

    [Fact]
    public void ValidationErrorsBlockGenerateAndWarningsDoNot()
    {
        var (main, _) = Rig();
        var validation = new FakeValidation();
        main.ReportValidation = validation;
        var def = Add(main, ReportType.Summary);
        Assert.True(main.Security.CanGenerate);
        Assert.Empty(main.Reports.Issues);

        validation.Issues.Add(new ReportIssueView(ReportIssueSeverity.Warning, def.Name, "Looks odd."));
        main.Reports.Description = "x";
        Assert.True(main.Security.CanGenerate);
        Assert.Single(main.Reports.Issues);
        Assert.Single(main.Reports.SelectedIssues);
        Assert.True(main.Reports.HasSelectedIssues);
        Assert.Contains("0 error(s)", main.Reports.IssueSummary);

        validation.Issues.Add(new ReportIssueView(ReportIssueSeverity.Error, def.Name, "Pick a group."));
        main.Reports.Description = "y";
        Assert.False(main.Security.CanGenerate);
        Assert.Contains("Pick a group.", main.Security.ValidationMessage);
        Assert.Contains(def.Name, main.Security.ValidationMessage);
        Assert.Equal(2, main.Reports.Issues.Count);
        Assert.Contains("1 error(s)", main.Reports.IssueSummary);
        Assert.StartsWith("Error: ", main.Reports.Issues[1].Text);

        validation.Issues.RemoveAt(1);
        main.Reports.Description = "z";
        Assert.True(main.Security.CanGenerate);

        // Setting a new service validates at once.
        var another = new FakeValidation();
        another.Issues.Add(new ReportIssueView(ReportIssueSeverity.Error, string.Empty, "Broken."));
        main.ReportValidation = another;
        Assert.False(main.Security.CanGenerate);
        Assert.True(another.Calls > 0);
    }

    [Fact]
    public void AReportErrorShowsAsTheGenerateGateMessage()
    {
        var (main, _) = Rig();
        var validation = new FakeValidation();
        validation.Issues.Add(new ReportIssueView(ReportIssueSeverity.Error, "R", "Bad."));
        main.ReportValidation = validation;
        Assert.False(main.Security.CanGenerate);
        Assert.Equal("Report 'R': Bad.", main.Security.ValidationMessage);
    }

    [Fact]
    public void ADefaultValidationFindsNothing()
    {
        var (main, _) = Rig();
        Assert.IsType<NoReportValidation>(main.ReportValidation);
        Add(main, ReportType.Summary);
        Assert.Empty(main.Reports.Issues);
        Assert.True(main.Security.CanGenerate);
    }

    // ---- preview service ----

    [Fact]
    public async Task ThePreviewIsNotAvailableByDefault()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Summary);
        Assert.IsType<NoReportPreview>(main.ReportPreview);
        await main.Reports.RunPreviewAsync();
        Assert.Equal("Preview is not available yet", main.Reports.PreviewError);
        Assert.True(main.Reports.HasPreviewError);
        Assert.Empty(main.Reports.PreviewRows);
        Assert.False(main.Reports.IsPreviewRunning);
    }

    [Fact]
    public async Task RunningThePreviewCallsTheServiceAndCapsTheRows()
    {
        var (main, _) = Rig();
        var def = Add(main, ReportType.Summary);
        var preview = new FakePreview
        {
            Handler = (_, _) => Task.FromResult(new ReportPreviewResult(
                ["Name", "When", "Amount"],
                Enumerable.Range(0, 250).Select(i => new object?[] { "n" + i, new DateTime(2026, 3, 1), i * 1.5m }).ToArray(),
                null)),
        };
        main.ReportPreview = preview;
        await main.Reports.RunPreviewAsync();

        var call = Assert.Single(preview.Calls);
        Assert.Same(def, call.Report);
        Assert.Equal(["Name", "When", "Amount"], main.Reports.PreviewColumns);
        Assert.Equal(ReportsViewModel.MaxPreviewRows, main.Reports.PreviewRows.Count);
        Assert.Equal(["n0", "2026-03-01", "0.0"], main.Reports.PreviewRows[0].Cells);
        Assert.Equal("First 200 of 250 row(s).", main.Reports.PreviewSummary);
        Assert.False(main.Reports.HasPreviewError);

        // A failed run replaces the grid with the error text.
        preview.Handler = (_, _) => Task.FromResult(ReportPreviewResult.Failed("no such column Foo"));
        await main.Reports.RunPreviewAsync();
        Assert.Equal("no such column Foo", main.Reports.PreviewError);
        Assert.Empty(main.Reports.PreviewRows);

        preview.Handler = (_, _) => throw new InvalidOperationException("boom");
        await main.Reports.RunPreviewAsync();
        Assert.Contains("boom", main.Reports.PreviewError);
    }

    [Fact]
    public async Task CancellingThePreviewCancelsTheToken()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Summary);
        var started = new TaskCompletionSource();
        var preview = new FakePreview
        {
            Handler = async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return ReportPreviewResult.Failed("unreachable");
            },
        };
        main.ReportPreview = preview;

        var run = main.Reports.RunPreviewAsync();
        await started.Task;
        Assert.True(main.Reports.IsPreviewRunning);
        main.Reports.CancelPreviewCommand.Execute(null);
        await run;

        Assert.True(preview.Calls[0].Token.IsCancellationRequested);
        Assert.False(main.Reports.IsPreviewRunning);
        Assert.Equal("The preview was cancelled.", main.Reports.PreviewError);
    }

    [Fact]
    public async Task ANewRunCancelsTheOneBeforeAndChangingTheSelectionDropsTheResult()
    {
        var (main, _) = Rig();
        Add(main, ReportType.Summary);
        Add(main, ReportType.Chart);
        var gate = new TaskCompletionSource<ReportPreviewResult>();
        var calls = 0;
        var preview = new FakePreview
        {
            Handler = (_, token) => ++calls == 1 ? gate.Task : Task.FromResult(new ReportPreviewResult(["X"], [["second"]], null)),
        };
        main.ReportPreview = preview;

        var first = main.Reports.RunPreviewAsync();
        await main.Reports.RunPreviewAsync();
        Assert.True(preview.Calls[0].Token.IsCancellationRequested);
        Assert.Equal("second", main.Reports.PreviewRows[0].Cells[0]);

        // The late answer of the first run must not replace the second.
        gate.SetResult(new ReportPreviewResult(["X"], [["first"]], null));
        await first;
        Assert.Equal("second", main.Reports.PreviewRows[0].Cells[0]);

        main.Reports.SelectedReport = main.Reports.Reports[0];
        Assert.Empty(main.Reports.PreviewRows);
        Assert.Equal(string.Empty, main.Reports.PreviewError);
    }

    // ---- shell ----

    [Fact]
    public void TheReportsTabHasABreadcrumbAndTheProjectTabASummary()
    {
        var (main, _) = Rig();
        Assert.Contains("Reports", MainViewModel.TabNames);
        Assert.Equal("Reports", MainViewModel.TabNames[^1]);
        Assert.Equal("No reports. The generated site has only the table pages.", ProjectSettingsViewModel.ReportsSummaryText([]));

        var def = Add(main, ReportType.Summary);
        Add(main, ReportType.Chart);
        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Reports");
        main.Reports.SelectedReport = main.Reports.Reports[0];
        Assert.EndsWith($" > Reports > {def.Caption}", main.Breadcrumb);

        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Project");
        Assert.Equal("2 reports: 1 Summary, 1 Chart.", main.ProjectSettings.ReportsSummary);
        main.ProjectSettings.ShowReportsTabCommand.Execute(null);
        Assert.Equal("Reports", MainViewModel.TabNames[main.ActiveTabIndex]);
    }

    [Fact]
    public void ReloadingTheProjectBuildsTheListFromTheModel()
    {
        var (main, _) = Rig();
        main.Current.Reports.Add(ReportDefaults.Create(ReportType.Summary, "Loaded", main.Current.Tables[0]));
        main.Current.Reports.Add(ReportDefaults.Create(ReportType.Dashboard, "Home", null));
        main.Reports.Reload();
        Assert.Equal(2, main.Reports.Reports.Count);
        Assert.Equal("Loaded", main.Reports.Reports[0].Model.Name);
        Assert.Same(main.Current.Reports[0], main.Reports.SelectedReport!.Model);
        Assert.Equal("(dashboard)", main.Reports.Reports[1].TableText);
    }
}
