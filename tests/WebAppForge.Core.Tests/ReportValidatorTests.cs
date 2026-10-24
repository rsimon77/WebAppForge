using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using WebAppForge.Core.Reports;

namespace WebAppForge.Core.Tests;

public class ReportValidatorTests
{
    private static Project P(params ReportDef[] reports)
    {
        var p = new Project { Name = "P" };
        var t = new TableInfo { Name = "t" };
        t.Fields.Add(new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true });
        t.Fields.Add(new FieldInfo { Name = "name", Type = FieldType.Text });
        t.Fields.Add(new FieldInfo { Name = "odate", Type = FieldType.Date });
        t.Fields.Add(new FieldInfo { Name = "amount", Type = FieldType.Decimal });
        t.Fields.Add(new FieldInfo { Name = "flag", Type = FieldType.Boolean });
        p.Tables.Add(t);
        p.Tables.Add(new TableInfo { Name = "off", Generate = false });
        p.Reports.AddRange(reports);
        return p;
    }

    private static ReportDef S(string name = "r", Action<ReportDef>? edit = null)
    {
        var r = new ReportDef { Name = name, Table = "t" };
        r.Summary.Groups.Add(new ReportField { Field = "name" });
        edit?.Invoke(r);
        return r;
    }

    private static IReadOnlyList<ReportIssue> Run(params ReportDef[] reports) => ReportValidator.Validate(P(reports));

    private static void Err(string message, params ReportDef[] reports)
    {
        var issues = Run(reports);
        Assert.Contains(issues, i => i.Severity == ReportIssueSeverity.Error && i.Message == message);
    }

    private static ReportDef Dash(string name, params DashboardItem[] items)
    {
        var r = new ReportDef { Name = name, Type = ReportType.Dashboard };
        r.Dashboard.Items.AddRange(items);
        return r;
    }

    [Fact]
    public void ValidReportHasNoIssues() => Assert.Empty(Run(S()));

    [Fact]
    public void NameErrors()
    {
        Err("Report '': the name is empty.", S(""));
        Err("Report 'a b': the name may contain only letters, digits, dash and underscore.", S("a b"));
        Err("Report 'a/b': the name may contain only letters, digits, dash and underscore.", S("a/b"));
        Err("Report 'dup': the name is used by more than one report.", S("dup"), S("dup"));
        Assert.Empty(Run(S("a-b_9")));
    }

    [Fact]
    public void TableErrors()
    {
        Err("Report 'r': the table 'nope' does not exist.", S("r", r => r.Table = "nope"));
        Err("Report 'r': the table 'off' is not generated.", S("r", r => r.Table = "off"));
        Err("Report 'r': no table is set.", S("r", r => r.Table = ""));
    }

    [Fact]
    public void FieldAndIntervalErrors()
    {
        Err("Report 'r': the field 'zz' does not exist in table 't'.", S("r", r => r.Summary.Groups[0].Field = "zz"));
        Err("Report 'r': the field 'name' is not a date field, so the interval Month cannot be used.", S("r", r => r.Summary.Groups[0].Interval = GroupInterval.Month));
        Err("Report 'r': the field 'name' is not a numeric field, so the interval Range cannot be used.", S("r", r => r.Summary.Groups[0].Interval = GroupInterval.Range));
        Err("Report 'r': the field 'amount' is not a text field, so the interval FirstLetter cannot be used.", S("r", r => { r.Summary.Groups[0].Field = "amount"; r.Summary.Groups[0].Interval = GroupInterval.FirstLetter; }));
        Err("Report 'r': the range size of the field 'amount' must be greater than 0.", S("r", r => { r.Summary.Groups[0].Field = "amount"; r.Summary.Groups[0].Interval = GroupInterval.Range; r.Summary.Groups[0].RangeSize = 0; }));
    }

    [Fact]
    public void AggregateErrors()
    {
        Err("Report 'r': Sum of the field 'name' needs a numeric field (aggregate).", S("r", r => r.Summary.Aggregates.Add(new ReportAggregate { Field = "name", Function = AggregateFunction.Sum })));
        Err("Report 'r': Average of the field 'name' needs a numeric field (aggregate).", S("r", r => r.Summary.Aggregates.Add(new ReportAggregate { Field = "name", Function = AggregateFunction.Average })));
        Err("Report 'r': Sum needs a field (aggregate).", S("r", r => r.Summary.Aggregates.Add(new ReportAggregate { Function = AggregateFunction.Sum })));
        Assert.Empty(Run(S("r", r => r.Summary.Aggregates.Add(new ReportAggregate()))));
        Assert.Empty(Run(S("r", r => r.Summary.Aggregates.Add(new ReportAggregate { Field = "name", Function = AggregateFunction.Max }))));
    }

    [Fact]
    public void SummaryErrors()
    {
        Err("Report 'r': a summary needs at least one group or aggregate.", S("r", r => r.Summary.Groups.Clear()));
        Err("Report 'r': the compact layout needs at least one group.", S("r", r =>
        {
            r.Summary.Groups.Clear();
            r.Summary.Aggregates.Add(new ReportAggregate());
            r.Summary.Layout = SummaryLayout.Compact;
        }));
        Err("Report 'r': the field 'zz' does not exist in table 't'.", S("r", r => r.Summary.DetailFields.Add("zz")));
    }

    private static ReportDef X(Action<ReportDef> edit)
    {
        var r = new ReportDef { Name = "r", Table = "t", Type = ReportType.Crosstab };
        r.Crosstab.Rows.Field = "name";
        r.Crosstab.Columns.Field = "flag";
        edit(r);
        return r;
    }

    [Fact]
    public void CrosstabErrors()
    {
        Assert.Empty(Run(X(_ => { })));
        Err("Report 'r': a crosstab needs a row field.", X(r => r.Crosstab.Rows.Field = ""));
        Err("Report 'r': a crosstab needs a column field.", X(r => r.Crosstab.Columns.Field = ""));
        Err("Report 'r': Sum needs a field (value).", X(r => r.Crosstab.Value.Function = AggregateFunction.Sum));
    }

    private static ReportDef C(Action<ReportDef> edit)
    {
        var r = new ReportDef { Name = "r", Table = "t", Type = ReportType.Chart };
        r.Chart.Category.Field = "name";
        r.Chart.Series.Add(new ChartSeries());
        edit(r);
        return r;
    }

    [Fact]
    public void ChartErrors()
    {
        Assert.Empty(Run(C(_ => { })));
        Err("Report 'r': a chart needs a category field.", C(r => r.Chart.Category.Field = ""));
        Err("Report 'r': a chart needs at least one series or a series field.", C(r => r.Chart.Series.Clear()));
        Assert.DoesNotContain(Run(C(r => { r.Chart.Series.Clear(); r.Chart.SeriesField = new ReportField { Field = "flag" }; })), i => i.Severity == ReportIssueSeverity.Error);
    }

    private static ReportDef K(Action<ReportDef> edit)
    {
        var r = new ReportDef { Name = "r", Table = "t", Type = ReportType.Calendar };
        r.Calendar.TitleField = "name";
        r.Calendar.StartField = "odate";
        edit(r);
        return r;
    }

    [Fact]
    public void CalendarErrors()
    {
        Assert.Empty(Run(K(_ => { })));
        Err("Report 'r': a calendar needs a title field.", K(r => r.Calendar.TitleField = ""));
        Err("Report 'r': a calendar needs a start date field.", K(r => r.Calendar.StartField = ""));
        Err("Report 'r': the start field 'name' must be a date or date-time field.", K(r => r.Calendar.StartField = "name"));
        Err("Report 'r': the end field 'amount' must be a date or date-time field.", K(r => r.Calendar.EndField = "amount"));
    }

    [Fact]
    public void DashboardErrors()
    {
        Err("Report 'd': item 1 names the report 'zz', which does not exist.", Dash("d", new DashboardItem { Report = "zz" }));
        Err("Report 'd': item 1 includes the dashboard itself.", Dash("d", new DashboardItem { Report = "d" }));
        Err("Report 'd': item 1 includes the dashboard 'e'; a dashboard cannot include another dashboard.", Dash("d", new DashboardItem { Report = "e" }), Dash("e"));
        Err("Report 'd': item 1 has the span 13; it must be from 1 to 12.", Dash("d", new DashboardItem { Kind = DashboardItemKind.Kpi, Table = "t", Span = 13 }));
        Err("Report 'd': item 1 no table is set.", Dash("d", new DashboardItem { Kind = DashboardItemKind.Kpi }));
        Err("Report 'd': item 1 the table 'nope' does not exist.", Dash("d", new DashboardItem { Kind = DashboardItemKind.Kpi, Table = "nope" }));
        Err("Report 'd': Sum of the field 'name' needs a numeric field (item 1).", Dash("d", new DashboardItem { Kind = DashboardItemKind.Kpi, Table = "t", Value = new ReportAggregate { Field = "name", Function = AggregateFunction.Sum } }));
        Assert.DoesNotContain(Run(S("s"), Dash("d", new DashboardItem { Report = "s" })), i => i.Severity == ReportIssueSeverity.Error);
    }

    [Fact]
    public void DrillDownAndFilterAndLevelErrors()
    {
        Err("Report 'r': the drill-down report 'zz' does not exist.", S("r", r => r.DrillDownReport = "zz"));
        var cal = new ReportDef { Name = "cal", Table = "t", Type = ReportType.Calendar };
        Err("Report 'r': the drill-down report 'cal' must be a summary, chart or crosstab report.", S("r", r => r.DrillDownReport = "cal"), cal);
        var other = S("o");
        other.Table = "off";
        Err("Report 'r': the drill-down report 'o' reads the table 'off', not 't'.", S("r", r => r.DrillDownReport = "o"), other);
        Err("Report 'r': the field 'zz' does not exist in table 't'.", S("r", r => r.Summary.Filters.Add(new ReportFilter { Field = "zz" })));
        Err("Report 'r': the minimum level must not be negative.", S("r", r => r.MinLevel = -1));
    }

    [Fact]
    public void Warnings()
    {
        var hidden = Run(S("r", r => r.ShowInMenu = false));
        Assert.Contains(hidden, i => i.Severity == ReportIssueSeverity.Warning && i.Message.StartsWith("Report 'r': the report is not shown anywhere"));
        var used = S("u", r => r.ShowInMenu = false);
        var d = Dash("d", new DashboardItem { Report = "u" });
        Assert.DoesNotContain(Run(used, d), i => i.ReportName == "u");
        var dup = Run(S("r", r => { r.Summary.Aggregates.Add(new ReportAggregate()); r.Summary.Aggregates.Add(new ReportAggregate()); }));
        Assert.Contains(dup, i => i.Severity == ReportIssueSeverity.Warning && i.Message == "Report 'r': more than one aggregate has the caption 'Count'.");
    }

    [Fact]
    public void GeneratorThrowsTheFirstError()
    {
        var root = ReportSupport.TempFolder();
        try
        {
            var p = ReportSupport.CreateProject(root, seed: false);
            var r = ReportSupport.Summary();
            r.Summary.Groups[0].Field = "zz";
            p.Reports.Add(r);
            var ex = Assert.Throws<InvalidOperationException>(() => new ProjectGenerator().Generate(p, Path.Combine(root, "site"), root));
            Assert.Equal("Report 'sum': the field 'zz' does not exist in table 'orders'.", ex.Message);
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}
