using System.Xml.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class ReportsXmlTests
{
    private static ReportField F(string name) => new()
    {
        Field = name, Interval = GroupInterval.Range, RangeSize = 2.5, Sort = SortDirection.Descending, SortBy = GroupSortBy.Aggregate, Caption = "C" + name,
    };

    private static ReportAggregate A(string field) => new() { Field = field, Function = AggregateFunction.Average, Caption = "Avg", FormatString = "N3" };

    private static ReportFilter Fi(string f) => new() { Field = f, Caption = "Cap", RelativeDates = false };

    private static void Common(ReportDef r, ReportType type) 
    {
        r.Type = type; r.Caption = "Cap"; r.Table = "t"; r.Filter = "a = 1"; r.Description = "d"; r.ShowInMenu = false;
        r.MenuGroup = "g"; r.MenuOrder = 3; r.Icon = "bar"; r.MinLevel = 2; r.DrillDownReport = "other";
    }

    private static Project Full()
    {
        var p = new Project { Name = "P" };
        var s = new ReportDef { Name = "s" };
        Common(s, ReportType.Summary);
        s.Summary.Groups.Add(F("a")); s.Summary.Groups.Add(F("b"));
        s.Summary.Aggregates.Add(A("x"));
        s.Summary.Layout = SummaryLayout.Compact; s.Summary.DetailFields.AddRange(["d1", "d2"]);
        s.Summary.ShowGroupTotals = false; s.Summary.ShowGrandTotal = false; s.Summary.GroupsPerPage = 5; s.Summary.DetailRowsPerGroup = 7;
        s.Summary.Filters.Add(Fi("f"));
        var x = new ReportDef { Name = "x" };
        Common(x, ReportType.Crosstab);
        x.Crosstab.Rows = F("r"); x.Crosstab.Columns = F("c"); x.Crosstab.Value = A("v");
        x.Crosstab.ShowRowTotals = false; x.Crosstab.ShowColumnTotals = false; x.Crosstab.ShowGrandTotal = false; x.Crosstab.MaxColumns = 4;
        x.Crosstab.Filters.Add(Fi("f"));
        var c = new ReportDef { Name = "c" };
        Common(c, ReportType.Chart);
        c.Chart.Kind = ChartKind.PercentColumn; c.Chart.Category = F("cat"); c.Chart.SeriesField = F("ser");
        c.Chart.Series.Add(new ChartSeries { Value = A("v"), Color = "red", Axis = ChartAxis.Secondary, Kind = ChartKind.Line, Caption = "S" });
        c.Chart.Series.Add(new ChartSeries());
        c.Chart.Title = "T"; c.Chart.Legend = LegendPosition.Hidden; c.Chart.ShowDataLabels = true; c.Chart.Height = 200; c.Chart.TopN = 5;
        c.Chart.TrendLine = true; c.Chart.XAxisTitle = "X"; c.Chart.YAxisTitle = "Y"; c.Chart.Filters.Add(Fi("f"));
        var k = new ReportDef { Name = "k" };
        Common(k, ReportType.Calendar);
        var cal = k.Calendar;
        cal.TitleField = "t"; cal.StartField = "s"; cal.EndField = "e"; cal.AllDayField = "ad"; cal.ColorField = "cf"; cal.DefaultColor = "#fff";
        cal.ColorRules.Add(new ValueRule { Operator = ConditionOperator.Contains, Value = "v", Style = "danger", Text = "txt" });
        cal.PopoverFields.AddRange(["p1", "p2"]); cal.Views.Clear(); cal.Views.AddRange([CalendarView.Year, CalendarView.List]);
        cal.DefaultView = CalendarView.Year; cal.FirstDay = 0; cal.ShowWeekends = false; cal.AllowAdd = false; cal.AllowOpen = false;
        cal.Filters.Add(Fi("f"));
        var d = new ReportDef { Name = "d" };
        Common(d, ReportType.Dashboard);
        d.Dashboard.RefreshSeconds = 30;
        d.Dashboard.Items.Add(new DashboardItem { Kind = DashboardItemKind.Kpi, Caption = "K", Table = "t", Value = A("v"), Filter = "z", Style = "info", Icon = "i", LinkTo = "l", Span = 3, Height = 90 });
        d.Dashboard.Items.Add(new DashboardItem { Kind = DashboardItemKind.Report, Report = "s" });
        p.Reports.AddRange([s, x, c, k, d]);
        return p;
    }

    private static string Dump(Project p) => ProjectFile.Write(p).ToString();

    [Fact]
    public void EveryPropertyOfEveryTypeRoundTrips()
    {
        var p = Full();
        var again = ProjectFile.Read(ProjectFile.Write(p));
        Assert.Equal(5, again.Reports.Count);
        // The second write equals the first, and the model values match.
        Assert.Equal(Dump(p), Dump(again));
        var s = again.Reports[0].Summary;
        Assert.Equal(2.5, s.Groups[0].RangeSize);
        Assert.Equal(GroupSortBy.Aggregate, s.Groups[1].SortBy);
        Assert.Equal(["d1", "d2"], s.DetailFields);
        Assert.Equal(7, s.DetailRowsPerGroup);
        Assert.False(s.Filters[0].RelativeDates);
        Assert.Equal(4, again.Reports[1].Crosstab.MaxColumns);
        var c = again.Reports[2].Chart;
        Assert.Equal(ChartKind.Line, c.Series[0].Kind);
        Assert.Null(c.Series[1].Kind);
        Assert.NotNull(c.SeriesField);
        Assert.Equal([CalendarView.Year, CalendarView.List], again.Reports[3].Calendar.Views);
        Assert.Equal("txt", again.Reports[3].Calendar.ColorRules[0].Text);
        Assert.Equal(DashboardItemKind.Kpi, again.Reports[4].Dashboard.Items[0].Kind);
        Assert.Equal(3, again.Reports[4].Dashboard.Items[0].Span);
        Assert.Equal(2, again.Reports[0].MinLevel);
        Assert.Equal("other", again.Reports[0].DrillDownReport);
    }

    [Fact]
    public void OnlyTheElementOfTheOwnTypeIsWrittenAndEmptyViewsSurvive()
    {
        var p = Full();
        p.Reports[3].Calendar.Views.Clear();
        var xml = ProjectFile.Write(p).Root!.Element("reports")!;
        var summary = xml.Elements("report").First();
        Assert.NotNull(summary.Element("summary"));
        Assert.Null(summary.Element("crosstab"));
        Assert.Null(summary.Element("dashboard"));
        Assert.Empty(ProjectFile.Read(ProjectFile.Write(p)).Reports[3].Calendar.Views);
    }

    [Fact]
    public void DefaultsWriteNoSettingAttributesAndOldProjectsLoad()
    {
        var p = new Project { Name = "P" };
        p.Reports.Add(new ReportDef { Name = "a" });
        var doc = ProjectFile.Write(p);
        var report = doc.Root!.Element("reports")!.Element("report")!;
        Assert.Equal(["name", "type"], report.Attributes().Select(a => a.Name.LocalName).ToArray());
        Assert.Empty(report.Element("summary")!.Attributes());
        var again = ProjectFile.Read(doc);
        Assert.Equal(20, again.Reports[0].Summary.GroupsPerPage);
        Assert.True(again.Reports[0].ShowInMenu);

        var none = ProjectFile.Write(new Project { Name = "P" });
        Assert.Null(none.Root!.Element("reports"));
        Assert.Empty(ProjectFile.Read(none).Reports);
    }
}
