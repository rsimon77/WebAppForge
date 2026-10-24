using System.Globalization;
using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// Project file form of the report contract:
/// &lt;reports&gt;&lt;report name caption type table filter description showInMenu menuGroup menuOrder icon minLevel drillDownReport&gt;
/// followed by the element of the report's own type (&lt;summary&gt;, &lt;crosstab&gt;, &lt;chart&gt;, &lt;calendar&gt; or &lt;dashboard&gt;).
/// Only values that differ from the default are written and a project without reports has no &lt;reports&gt; element, so older
/// project files load unchanged and a project without reports is written exactly as before.
/// </summary>
internal static class ReportsXml
{
    private static readonly ReportDef D = new();
    private static readonly ReportField FieldDefaults = new();
    private static readonly ReportAggregate AggregateDefaults = new();
    private static readonly ReportFilter FilterDefaults = new();
    private static readonly SummarySettings SummaryDefaults = new();
    private static readonly CrosstabSettings CrosstabDefaults = new();
    private static readonly ChartSettings ChartDefaults = new();
    private static readonly ChartSeries SeriesDefaults = new();
    private static readonly CalendarSettings CalendarDefaults = new();
    private static readonly DashboardItem ItemDefaults = new();
    private static readonly ValueRule RuleDefaults = new();

    // ---- write ----------------------------------------------------------------------------------

    public static void Append(XElement root, Project project)
    {
        if (project.Reports.Count == 0)
        {
            return;
        }

        var reports = new XElement("reports");
        foreach (var report in project.Reports)
        {
            var e = new XElement("report", new XAttribute("name", report.Name), new XAttribute("type", report.Type.ToString()));
            Str(e, "caption", report.Caption, D.Caption);
            Str(e, "table", report.Table, D.Table);
            Str(e, "filter", report.Filter, D.Filter);
            Str(e, "description", report.Description, D.Description);
            Bool(e, "showInMenu", report.ShowInMenu, D.ShowInMenu);
            Str(e, "menuGroup", report.MenuGroup, D.MenuGroup);
            Int(e, "menuOrder", report.MenuOrder, D.MenuOrder);
            Str(e, "icon", report.Icon, D.Icon);
            Int(e, "minLevel", report.MinLevel, D.MinLevel);
            Str(e, "drillDownReport", report.DrillDownReport, D.DrillDownReport);
            switch (report.Type)
            {
                case ReportType.Summary:
                    e.Add(WriteSummary(report.Summary));
                    break;
                case ReportType.Crosstab:
                    e.Add(WriteCrosstab(report.Crosstab));
                    break;
                case ReportType.Chart:
                    e.Add(WriteChart(report.Chart));
                    break;
                case ReportType.Calendar:
                    e.Add(WriteCalendar(report.Calendar));
                    break;
                case ReportType.Dashboard:
                    e.Add(WriteDashboard(report.Dashboard));
                    break;
            }

            reports.Add(e);
        }

        root.Add(reports);
    }

    private static XElement WriteSummary(SummarySettings s)
    {
        var d = SummaryDefaults;
        var e = new XElement("summary");
        Enum(e, "layout", s.Layout, d.Layout);
        Bool(e, "showGroupTotals", s.ShowGroupTotals, d.ShowGroupTotals);
        Bool(e, "showGrandTotal", s.ShowGrandTotal, d.ShowGrandTotal);
        Int(e, "groupsPerPage", s.GroupsPerPage, d.GroupsPerPage);
        Int(e, "detailRowsPerGroup", s.DetailRowsPerGroup, d.DetailRowsPerGroup);
        foreach (var group in s.Groups)
        {
            e.Add(WriteField("group", group));
        }

        foreach (var aggregate in s.Aggregates)
        {
            e.Add(WriteAggregate("aggregate", aggregate));
        }

        foreach (var field in s.DetailFields)
        {
            e.Add(new XElement("detail", new XAttribute("field", field)));
        }

        WriteFilters(e, s.Filters);
        return e;
    }

    private static XElement WriteCrosstab(CrosstabSettings c)
    {
        var d = CrosstabDefaults;
        var e = new XElement("crosstab");
        Bool(e, "showRowTotals", c.ShowRowTotals, d.ShowRowTotals);
        Bool(e, "showColumnTotals", c.ShowColumnTotals, d.ShowColumnTotals);
        Bool(e, "showGrandTotal", c.ShowGrandTotal, d.ShowGrandTotal);
        Int(e, "maxColumns", c.MaxColumns, d.MaxColumns);
        if (!IsDefault(c.Rows))
        {
            e.Add(WriteField("rows", c.Rows));
        }

        if (!IsDefault(c.Columns))
        {
            e.Add(WriteField("columns", c.Columns));
        }

        if (!IsDefault(c.Value))
        {
            e.Add(WriteAggregate("value", c.Value));
        }

        WriteFilters(e, c.Filters);
        return e;
    }

    private static XElement WriteChart(ChartSettings c)
    {
        var d = ChartDefaults;
        var e = new XElement("chart");
        Enum(e, "kind", c.Kind, d.Kind);
        Str(e, "title", c.Title, d.Title);
        Enum(e, "legend", c.Legend, d.Legend);
        Bool(e, "showDataLabels", c.ShowDataLabels, d.ShowDataLabels);
        Int(e, "height", c.Height, d.Height);
        Int(e, "topN", c.TopN, d.TopN);
        Bool(e, "trendLine", c.TrendLine, d.TrendLine);
        Str(e, "xAxisTitle", c.XAxisTitle, d.XAxisTitle);
        Str(e, "yAxisTitle", c.YAxisTitle, d.YAxisTitle);
        if (!IsDefault(c.Category))
        {
            e.Add(WriteField("category", c.Category));
        }

        foreach (var series in c.Series)
        {
            var s = new XElement("series");
            Str(s, "color", series.Color, SeriesDefaults.Color);
            Enum(s, "axis", series.Axis, SeriesDefaults.Axis);
            if (series.Kind is { } kind)
            {
                s.Add(new XAttribute("kind", kind.ToString()));
            }

            Str(s, "caption", series.Caption, SeriesDefaults.Caption);
            if (!IsDefault(series.Value))
            {
                s.Add(WriteAggregate("value", series.Value));
            }

            e.Add(s);
        }

        if (c.SeriesField is { } seriesField)
        {
            e.Add(WriteField("seriesField", seriesField));
        }

        WriteFilters(e, c.Filters);
        return e;
    }

    private static XElement WriteCalendar(CalendarSettings c)
    {
        var d = CalendarDefaults;
        var e = new XElement("calendar");
        Str(e, "titleField", c.TitleField, d.TitleField);
        Str(e, "startField", c.StartField, d.StartField);
        Str(e, "endField", c.EndField, d.EndField);
        Str(e, "allDayField", c.AllDayField, d.AllDayField);
        Str(e, "colorField", c.ColorField, d.ColorField);
        Str(e, "defaultColor", c.DefaultColor, d.DefaultColor);
        Enum(e, "defaultView", c.DefaultView, d.DefaultView);
        Int(e, "firstDay", c.FirstDay, d.FirstDay);
        Bool(e, "showWeekends", c.ShowWeekends, d.ShowWeekends);
        Bool(e, "allowAdd", c.AllowAdd, d.AllowAdd);
        Bool(e, "allowOpen", c.AllowOpen, d.AllowOpen);
        foreach (var rule in c.ColorRules)
        {
            var r = new XElement("colorRule");
            Enum(r, "operator", rule.Operator, RuleDefaults.Operator);
            Str(r, "value", rule.Value, RuleDefaults.Value);
            Str(r, "style", rule.Style, RuleDefaults.Style);
            Str(r, "text", rule.Text, RuleDefaults.Text);
            e.Add(r);
        }

        foreach (var field in c.PopoverFields)
        {
            e.Add(new XElement("popover", new XAttribute("field", field)));
        }

        if (!c.Views.SequenceEqual(d.Views))
        {
            // The container tells an empty list from the default list.
            var views = new XElement("views");
            foreach (var view in c.Views)
            {
                views.Add(new XElement("view", new XAttribute("name", view.ToString())));
            }

            e.Add(views);
        }

        WriteFilters(e, c.Filters);
        return e;
    }

    private static XElement WriteDashboard(DashboardSettings d)
    {
        var e = new XElement("dashboard");
        Int(e, "refreshSeconds", d.RefreshSeconds, 0);
        foreach (var item in d.Items)
        {
            var i = ItemDefaults;
            var x = new XElement("item");
            Enum(x, "kind", item.Kind, i.Kind);
            Str(x, "report", item.Report, i.Report);
            Str(x, "caption", item.Caption, i.Caption);
            Str(x, "table", item.Table, i.Table);
            Str(x, "filter", item.Filter, i.Filter);
            Str(x, "style", item.Style, i.Style);
            Str(x, "icon", item.Icon, i.Icon);
            Str(x, "linkTo", item.LinkTo, i.LinkTo);
            Int(x, "span", item.Span, i.Span);
            Int(x, "height", item.Height, i.Height);
            if (!IsDefault(item.Value))
            {
                x.Add(WriteAggregate("value", item.Value));
            }

            e.Add(x);
        }

        return e;
    }

    private static XElement WriteField(string name, ReportField f)
    {
        var d = FieldDefaults;
        var e = new XElement(name);
        Str(e, "field", f.Field, d.Field);
        Enum(e, "interval", f.Interval, d.Interval);
        if (f.RangeSize != d.RangeSize)
        {
            e.Add(new XAttribute("rangeSize", f.RangeSize.ToString("R", CultureInfo.InvariantCulture)));
        }

        Enum(e, "sort", f.Sort, d.Sort);
        Enum(e, "sortBy", f.SortBy, d.SortBy);
        Str(e, "caption", f.Caption, d.Caption);
        return e;
    }

    private static XElement WriteAggregate(string name, ReportAggregate a)
    {
        var d = AggregateDefaults;
        var e = new XElement(name);
        Str(e, "field", a.Field, d.Field);
        Enum(e, "function", a.Function, d.Function);
        Str(e, "caption", a.Caption, d.Caption);
        Str(e, "formatString", a.FormatString, d.FormatString);
        return e;
    }

    private static void WriteFilters(XElement parent, List<ReportFilter> filters)
    {
        foreach (var filter in filters)
        {
            var e = new XElement("filter", new XAttribute("field", filter.Field));
            Str(e, "caption", filter.Caption, FilterDefaults.Caption);
            Bool(e, "relativeDates", filter.RelativeDates, FilterDefaults.RelativeDates);
            parent.Add(e);
        }
    }

    private static bool IsDefault(ReportField f) =>
        f.Field == FieldDefaults.Field && f.Interval == FieldDefaults.Interval && f.RangeSize == FieldDefaults.RangeSize
        && f.Sort == FieldDefaults.Sort && f.SortBy == FieldDefaults.SortBy && f.Caption == FieldDefaults.Caption;

    private static bool IsDefault(ReportAggregate a) =>
        a.Field == AggregateDefaults.Field && a.Function == AggregateDefaults.Function
        && a.Caption == AggregateDefaults.Caption && a.FormatString == AggregateDefaults.FormatString;

    // ---- read -----------------------------------------------------------------------------------

    public static void Read(XElement root, Project project)
    {
        if (root.Element("reports") is not { } reports)
        {
            return;
        }

        foreach (var e in reports.Elements("report"))
        {
            var report = new ReportDef
            {
                Name = ReadStr(e, "name", string.Empty),
                Type = ReadEnum(e, "type", ReportType.Summary),
                Caption = ReadStr(e, "caption", D.Caption),
                Table = ReadStr(e, "table", D.Table),
                Filter = ReadStr(e, "filter", D.Filter),
                Description = ReadStr(e, "description", D.Description),
                ShowInMenu = ReadBool(e, "showInMenu", D.ShowInMenu),
                MenuGroup = ReadStr(e, "menuGroup", D.MenuGroup),
                MenuOrder = ReadInt(e, "menuOrder", D.MenuOrder),
                Icon = ReadStr(e, "icon", D.Icon),
                MinLevel = ReadInt(e, "minLevel", D.MinLevel),
                DrillDownReport = ReadStr(e, "drillDownReport", D.DrillDownReport),
            };
            if (e.Element("summary") is { } summary)
            {
                ReadSummary(summary, report.Summary);
            }

            if (e.Element("crosstab") is { } crosstab)
            {
                ReadCrosstab(crosstab, report.Crosstab);
            }

            if (e.Element("chart") is { } chart)
            {
                ReadChart(chart, report.Chart);
            }

            if (e.Element("calendar") is { } calendar)
            {
                ReadCalendar(calendar, report.Calendar);
            }

            if (e.Element("dashboard") is { } dashboard)
            {
                ReadDashboard(dashboard, report.Dashboard);
            }

            project.Reports.Add(report);
        }
    }

    private static void ReadSummary(XElement e, SummarySettings s)
    {
        var d = SummaryDefaults;
        s.Layout = ReadEnum(e, "layout", d.Layout);
        s.ShowGroupTotals = ReadBool(e, "showGroupTotals", d.ShowGroupTotals);
        s.ShowGrandTotal = ReadBool(e, "showGrandTotal", d.ShowGrandTotal);
        s.GroupsPerPage = ReadInt(e, "groupsPerPage", d.GroupsPerPage);
        s.DetailRowsPerGroup = ReadInt(e, "detailRowsPerGroup", d.DetailRowsPerGroup);
        s.Groups.AddRange(e.Elements("group").Select(ReadField));
        s.Aggregates.AddRange(e.Elements("aggregate").Select(ReadAggregate));
        s.DetailFields.AddRange(e.Elements("detail").Select(x => ReadStr(x, "field", string.Empty)));
        ReadFilters(e, s.Filters);
    }

    private static void ReadCrosstab(XElement e, CrosstabSettings c)
    {
        var d = CrosstabDefaults;
        c.ShowRowTotals = ReadBool(e, "showRowTotals", d.ShowRowTotals);
        c.ShowColumnTotals = ReadBool(e, "showColumnTotals", d.ShowColumnTotals);
        c.ShowGrandTotal = ReadBool(e, "showGrandTotal", d.ShowGrandTotal);
        c.MaxColumns = ReadInt(e, "maxColumns", d.MaxColumns);
        c.Rows = e.Element("rows") is { } rows ? ReadField(rows) : new ReportField();
        c.Columns = e.Element("columns") is { } columns ? ReadField(columns) : new ReportField();
        c.Value = e.Element("value") is { } value ? ReadAggregate(value) : new ReportAggregate();
        ReadFilters(e, c.Filters);
    }

    private static void ReadChart(XElement e, ChartSettings c)
    {
        var d = ChartDefaults;
        c.Kind = ReadEnum(e, "kind", d.Kind);
        c.Title = ReadStr(e, "title", d.Title);
        c.Legend = ReadEnum(e, "legend", d.Legend);
        c.ShowDataLabels = ReadBool(e, "showDataLabels", d.ShowDataLabels);
        c.Height = ReadInt(e, "height", d.Height);
        c.TopN = ReadInt(e, "topN", d.TopN);
        c.TrendLine = ReadBool(e, "trendLine", d.TrendLine);
        c.XAxisTitle = ReadStr(e, "xAxisTitle", d.XAxisTitle);
        c.YAxisTitle = ReadStr(e, "yAxisTitle", d.YAxisTitle);
        c.Category = e.Element("category") is { } category ? ReadField(category) : new ReportField();
        foreach (var x in e.Elements("series"))
        {
            var series = new ChartSeries
            {
                Color = ReadStr(x, "color", SeriesDefaults.Color),
                Axis = ReadEnum(x, "axis", SeriesDefaults.Axis),
                Caption = ReadStr(x, "caption", SeriesDefaults.Caption),
                Value = x.Element("value") is { } value ? ReadAggregate(value) : new ReportAggregate(),
            };
            if (x.Attribute("kind") is { } kind && System.Enum.TryParse<ChartKind>(kind.Value, out var parsed) && System.Enum.IsDefined(parsed))
            {
                series.Kind = parsed;
            }

            c.Series.Add(series);
        }

        c.SeriesField = e.Element("seriesField") is { } seriesField ? ReadField(seriesField) : null;
        ReadFilters(e, c.Filters);
    }

    private static void ReadCalendar(XElement e, CalendarSettings c)
    {
        var d = CalendarDefaults;
        c.TitleField = ReadStr(e, "titleField", d.TitleField);
        c.StartField = ReadStr(e, "startField", d.StartField);
        c.EndField = ReadStr(e, "endField", d.EndField);
        c.AllDayField = ReadStr(e, "allDayField", d.AllDayField);
        c.ColorField = ReadStr(e, "colorField", d.ColorField);
        c.DefaultColor = ReadStr(e, "defaultColor", d.DefaultColor);
        c.DefaultView = ReadEnum(e, "defaultView", d.DefaultView);
        c.FirstDay = ReadInt(e, "firstDay", d.FirstDay);
        c.ShowWeekends = ReadBool(e, "showWeekends", d.ShowWeekends);
        c.AllowAdd = ReadBool(e, "allowAdd", d.AllowAdd);
        c.AllowOpen = ReadBool(e, "allowOpen", d.AllowOpen);
        foreach (var x in e.Elements("colorRule"))
        {
            c.ColorRules.Add(new ValueRule
            {
                Operator = ReadEnum(x, "operator", RuleDefaults.Operator),
                Value = ReadStr(x, "value", RuleDefaults.Value),
                Style = ReadStr(x, "style", RuleDefaults.Style),
                Text = ReadStr(x, "text", RuleDefaults.Text),
            });
        }

        c.PopoverFields.AddRange(e.Elements("popover").Select(x => ReadStr(x, "field", string.Empty)));
        if (e.Element("views") is { } views)
        {
            c.Views.Clear();
            foreach (var x in views.Elements("view"))
            {
                if (System.Enum.TryParse<CalendarView>(x.Attribute("name")?.Value, out var view) && System.Enum.IsDefined(view))
                {
                    c.Views.Add(view);
                }
            }
        }

        ReadFilters(e, c.Filters);
    }

    private static void ReadDashboard(XElement e, DashboardSettings dashboard)
    {
        dashboard.RefreshSeconds = ReadInt(e, "refreshSeconds", 0);
        foreach (var x in e.Elements("item"))
        {
            var i = ItemDefaults;
            dashboard.Items.Add(new DashboardItem
            {
                Kind = ReadEnum(x, "kind", i.Kind),
                Report = ReadStr(x, "report", i.Report),
                Caption = ReadStr(x, "caption", i.Caption),
                Table = ReadStr(x, "table", i.Table),
                Filter = ReadStr(x, "filter", i.Filter),
                Style = ReadStr(x, "style", i.Style),
                Icon = ReadStr(x, "icon", i.Icon),
                LinkTo = ReadStr(x, "linkTo", i.LinkTo),
                Span = ReadInt(x, "span", i.Span),
                Height = ReadInt(x, "height", i.Height),
                Value = x.Element("value") is { } value ? ReadAggregate(value) : new ReportAggregate(),
            });
        }
    }

    private static ReportField ReadField(XElement e)
    {
        var d = FieldDefaults;
        return new ReportField
        {
            Field = ReadStr(e, "field", d.Field),
            Interval = ReadEnum(e, "interval", d.Interval),
            RangeSize = double.TryParse(e.Attribute("rangeSize")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : d.RangeSize,
            Sort = ReadEnum(e, "sort", d.Sort),
            SortBy = ReadEnum(e, "sortBy", d.SortBy),
            Caption = ReadStr(e, "caption", d.Caption),
        };
    }

    private static ReportAggregate ReadAggregate(XElement e)
    {
        var d = AggregateDefaults;
        return new ReportAggregate
        {
            Field = ReadStr(e, "field", d.Field),
            Function = ReadEnum(e, "function", d.Function),
            Caption = ReadStr(e, "caption", d.Caption),
            FormatString = ReadStr(e, "formatString", d.FormatString),
        };
    }

    private static void ReadFilters(XElement e, List<ReportFilter> filters)
    {
        foreach (var x in e.Elements("filter"))
        {
            filters.Add(new ReportFilter
            {
                Field = ReadStr(x, "field", string.Empty),
                Caption = ReadStr(x, "caption", FilterDefaults.Caption),
                RelativeDates = ReadBool(x, "relativeDates", FilterDefaults.RelativeDates),
            });
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static void Str(XElement e, string name, string value, string fallback)
    {
        if (!string.Equals(value, fallback, StringComparison.Ordinal))
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void Bool(XElement e, string name, bool value, bool fallback)
    {
        if (value != fallback)
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void Int(XElement e, string name, int value, int fallback)
    {
        if (value != fallback)
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void Enum<T>(XElement e, string name, T value, T fallback)
        where T : struct, System.Enum
    {
        if (!EqualityComparer<T>.Default.Equals(value, fallback))
        {
            e.Add(new XAttribute(name, value.ToString()));
        }
    }

    private static string ReadStr(XElement e, string name, string fallback) => e.Attribute(name)?.Value ?? fallback;

    private static bool ReadBool(XElement e, string name, bool fallback) =>
        bool.TryParse(e.Attribute(name)?.Value, out var value) ? value : fallback;

    private static int ReadInt(XElement e, string name, int fallback) =>
        int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static T ReadEnum<T>(XElement e, string name, T fallback)
        where T : struct, System.Enum =>
        System.Enum.TryParse<T>(e.Attribute(name)?.Value, out var value) && System.Enum.IsDefined(value) ? value : fallback;
}
