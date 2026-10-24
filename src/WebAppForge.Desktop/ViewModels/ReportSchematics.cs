using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WebAppForge.Core.Model;
using FieldInfo = WebAppForge.Core.Model.FieldInfo;

namespace WebAppForge.Desktop.ViewModels;

public enum SchematicRowKind
{
    /// <summary>A plain row (compact summary line, crosstab row).</summary>
    Row,

    /// <summary>The heading line of a group (Detail layout).</summary>
    Group,

    /// <summary>A detail record under a group.</summary>
    Detail,

    GroupTotal,

    GrandTotal,
}

public sealed record SchematicTableRow(SchematicRowKind Kind, IReadOnlyList<string> Cells);

/// <summary>What the schematic preview draws for a report. Plain data built from the model, so tests need no window.</summary>
public abstract record ReportSchematic(ReportType Type, string Title);

public sealed record SummarySchematic(
    string Title,
    SummaryLayout Layout,
    IReadOnlyList<string> Headers,
    IReadOnlyList<SchematicTableRow> Rows,
    bool ShowGroupTotals,
    bool ShowGrandTotal,
    IReadOnlyList<string> Filters,
    string Note) : ReportSchematic(ReportType.Summary, Title);

public sealed record CrosstabSchematic(
    string Title,
    string RowHeading,
    string ColumnHeading,
    IReadOnlyList<string> Headers,
    IReadOnlyList<SchematicTableRow> Rows,
    bool ShowRowTotals,
    bool ShowColumnTotals,
    bool ShowGrandTotal,
    IReadOnlyList<string> Filters,
    string Note) : ReportSchematic(ReportType.Crosstab, Title);

public sealed record SchematicSeries(string Caption, string Color, ChartAxis Axis, ChartKind Kind, IReadOnlyList<double> Values);

public sealed record ChartSchematic(
    string Title,
    ChartKind Kind,
    string XAxisTitle,
    string YAxisTitle,
    LegendPosition Legend,
    bool ShowDataLabels,
    bool TrendLine,
    int Height,
    IReadOnlyList<string> Categories,
    IReadOnlyList<SchematicSeries> Series,
    IReadOnlyList<string> Filters,
    string Note) : ReportSchematic(ReportType.Chart, Title);

public sealed record SchematicDay(int Number, bool InMonth);

/// <summary>An event bar of the sample month: grid row, first column, width in columns and its CSS colour (empty: the theme colour).</summary>
public sealed record SchematicEvent(string Title, int Row, int Column, int Span, string Color);

public sealed record CalendarSchematic(
    string Title,
    string MonthTitle,
    IReadOnlyList<string> DayHeaders,
    IReadOnlyList<IReadOnlyList<SchematicDay>> Weeks,
    IReadOnlyList<SchematicEvent> Events,
    IReadOnlyList<CalendarView> Views,
    CalendarView DefaultView,
    IReadOnlyList<string> PopoverFields,
    IReadOnlyList<string> Filters,
    bool AllowAdd,
    bool AllowOpen,
    string Note) : ReportSchematic(ReportType.Calendar, Title);

public sealed record DashboardSchematic(
    string Title,
    IReadOnlyList<DashboardBox> Boxes,
    int RowCount,
    int RefreshSeconds,
    string Note) : ReportSchematic(ReportType.Dashboard, Title);

/// <summary>Builds the schematic of a report from the model. Sample data is fixed, so the same report always draws the same picture.</summary>
public static class ReportSchematicBuilder
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public const int SampleGroups = 3;

    public const int SampleCategories = 5;

    public static ReportSchematic Build(Project project, ReportDef report)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(report);
        var table = project.Tables.FirstOrDefault(t => string.Equals(t.Name, report.Table, StringComparison.OrdinalIgnoreCase));
        var title = string.IsNullOrWhiteSpace(report.Caption) ? report.Name : report.Caption;
        return report.Type switch
        {
            ReportType.Crosstab => Crosstab(report, table, title),
            ReportType.Chart => Chart(report, table, title),
            ReportType.Calendar => Calendar(report, table, title),
            ReportType.Dashboard => Dashboard(project, report, title),
            _ => Summary(report, table, title),
        };
    }

    // ---- summary ----

    private static SummarySchematic Summary(ReportDef report, TableInfo? table, string title)
    {
        var settings = report.Summary;
        var aggregates = settings.Aggregates.Count > 0 ? settings.Aggregates : [new ReportAggregate()];
        var groupNames = settings.Groups.Select(g => FieldHeading(table, g)).ToList();
        var rows = new List<SchematicTableRow>();
        var headers = new List<string>();

        if (settings.Layout == SummaryLayout.Compact)
        {
            headers.AddRange(groupNames.Count > 0 ? groupNames : ["(no group)"]);
            headers.AddRange(aggregates.Select(AggregateRow.AggregateText));
            for (var i = 0; i < SampleGroups; i++)
            {
                var cells = new List<string>();
                if (settings.Groups.Count == 0)
                {
                    cells.Add("-");
                }

                cells.AddRange(settings.Groups.Select(g => SampleLabel(table, g, i)));
                cells.AddRange(aggregates.Select(a => SampleValue(a, i)));
                rows.Add(new SchematicTableRow(SchematicRowKind.Row, cells));
            }
        }
        else
        {
            var detail = DetailFieldNames(table, settings).Take(4).ToList();
            headers.AddRange(detail.Count > 0 ? detail : ["(detail fields)"]);
            for (var i = 0; i < SampleGroups - 1; i++)
            {
                var label = settings.Groups.Count == 0
                    ? "(no group)"
                    : string.Join(" / ", settings.Groups.Select((g, level) => $"{FieldHeading(table, g)}: {SampleLabel(table, g, i)}"));
                rows.Add(new SchematicTableRow(SchematicRowKind.Group, [label]));
                for (var r = 0; r < 2; r++)
                {
                    rows.Add(new SchematicTableRow(SchematicRowKind.Detail, (detail.Count > 0 ? detail : ["(detail fields)"]).Select(n => DetailSample(table, n, i * 2 + r)).ToList()));
                }

                if (settings.ShowGroupTotals)
                {
                    rows.Add(new SchematicTableRow(SchematicRowKind.GroupTotal, ["Total", .. aggregates.Select(a => $"{AggregateRow.AggregateText(a)}: {SampleValue(a, i)}")]));
                }
            }
        }

        if (settings.ShowGrandTotal)
        {
            var cells = new List<string> { "Grand total" };
            if (settings.Layout == SummaryLayout.Compact)
            {
                cells.AddRange(Enumerable.Repeat(string.Empty, Math.Max(settings.Groups.Count, 1) - 1));
                cells.AddRange(aggregates.Select(a => SampleValue(a, SampleGroups)));
            }
            else
            {
                cells.AddRange(aggregates.Select(a => $"{AggregateRow.AggregateText(a)}: {SampleValue(a, SampleGroups)}"));
            }

            rows.Add(new SchematicTableRow(SchematicRowKind.GrandTotal, cells));
        }

        var note = settings.Groups.Count == 0 ? "Add a group to see how the report is split." : string.Empty;
        return new SummarySchematic(title, settings.Layout, headers, rows, settings.ShowGroupTotals, settings.ShowGrandTotal, FilterCaptions(settings.Filters, table), note);
    }

    private static IEnumerable<string> DetailFieldNames(TableInfo? table, SummarySettings settings)
        => settings.DetailFields.Count > 0
            ? settings.DetailFields.Select(n => FieldCaption(table, n))
            : table?.Fields.Where(f => f.ShowInList).Select(f => string.IsNullOrWhiteSpace(f.Caption) ? f.Name : f.Caption) ?? [];

    // ---- crosstab ----

    private static CrosstabSchematic Crosstab(ReportDef report, TableInfo? table, string title)
    {
        var settings = report.Crosstab;
        var rowHeading = FieldHeading(table, settings.Rows);
        var columnHeading = FieldHeading(table, settings.Columns);
        var columnCount = settings.MaxColumns > 0 ? Math.Min(4, settings.MaxColumns) : 4;
        var headers = new List<string> { $"{(rowHeading.Length == 0 ? "(rows)" : rowHeading)} \\ {(columnHeading.Length == 0 ? "(columns)" : columnHeading)}" };
        headers.AddRange(Enumerable.Range(0, columnCount).Select(c => SampleLabel(table, settings.Columns, c)));
        if (settings.ShowRowTotals)
        {
            headers.Add("Total");
        }

        var value = settings.Value;
        var rows = new List<SchematicTableRow>();
        for (var r = 0; r < SampleGroups; r++)
        {
            var cells = new List<string> { SampleLabel(table, settings.Rows, r) };
            cells.AddRange(Enumerable.Range(0, columnCount).Select(c => SampleValue(value, r + c)));
            if (settings.ShowRowTotals)
            {
                cells.Add(SampleValue(value, r + columnCount));
            }

            rows.Add(new SchematicTableRow(SchematicRowKind.Row, cells));
        }

        if (settings.ShowColumnTotals)
        {
            var cells = new List<string> { "Total" };
            cells.AddRange(Enumerable.Range(0, columnCount).Select(c => SampleValue(value, c + SampleGroups)));
            if (settings.ShowRowTotals)
            {
                cells.Add(settings.ShowGrandTotal ? SampleValue(value, SampleGroups + columnCount) : string.Empty);
            }

            rows.Add(new SchematicTableRow(SchematicRowKind.GrandTotal, cells));
        }

        var note = settings.Rows.Field.Length == 0 || settings.Columns.Field.Length == 0 ? "Choose a rows field and a columns field." : string.Empty;
        return new CrosstabSchematic(title, rowHeading, columnHeading, headers, rows, settings.ShowRowTotals, settings.ShowColumnTotals, settings.ShowGrandTotal, FilterCaptions(settings.Filters, table), note);
    }

    // ---- chart ----

    private static readonly double[] CategoryBase = [40, 65, 30, 80, 55];

    private static ChartSchematic Chart(ReportDef report, TableInfo? table, string title)
    {
        var settings = report.Chart;
        var categories = Enumerable.Range(0, SampleCategories).Select(i => SampleLabel(table, settings.Category, i)).ToList();

        var series = new List<SchematicSeries>();
        var defs = settings.Series.Count > 0 ? settings.Series : [new ChartSeries()];
        if (settings.SeriesField is { } by)
        {
            for (var s = 0; s < 3; s++)
            {
                var first = defs[0];
                series.Add(MakeSeries(settings, first, s, SampleLabel(table, by, s), 1));
            }
        }
        else
        {
            for (var s = 0; s < defs.Count; s++)
            {
                series.Add(MakeSeries(settings, defs[s], s, string.IsNullOrWhiteSpace(defs[s].Caption) ? AggregateRow.AggregateText(defs[s].Value) : defs[s].Caption, s));
            }
        }

        if (ReportFieldRules.IsSingleSeriesKind(settings.Kind) && series.Count > 1)
        {
            series = series.Take(1).ToList();
        }

        if (settings.TopN > 0 && settings.TopN < categories.Count)
        {
            var order = Enumerable.Range(0, categories.Count).OrderByDescending(i => series[0].Values[i]).Take(settings.TopN).ToList();
            categories = order.Select(i => categories[i]).ToList();
            series = series.Select(s => s with { Values = order.Select(i => s.Values[i]).ToList() }).ToList();
        }

        var note = settings.Category.Field.Length == 0 ? "Choose a category field." : string.Empty;
        return new ChartSchematic(
            string.IsNullOrWhiteSpace(settings.Title) ? title : settings.Title, settings.Kind, settings.XAxisTitle, settings.YAxisTitle, settings.Legend, settings.ShowDataLabels, settings.TrendLine,
            settings.Height, categories, series, FilterCaptions(settings.Filters, table), note);
    }

    private static SchematicSeries MakeSeries(ChartSettings settings, ChartSeries def, int index, string caption, int shift)
    {
        var values = CategoryBase.Select((v, i) => Math.Round(v * (1 + 0.35 * shift) + (i * index * 3 % 17), 1)).ToList();
        var color = string.IsNullOrWhiteSpace(def.Color) || settings.SeriesField is not null ? ReportPalette.ColorOf(index) : def.Color;
        return new SchematicSeries(caption, color, def.Axis, def.Kind ?? settings.Kind, values);
    }

    // ---- calendar ----

    private static CalendarSchematic Calendar(ReportDef report, TableInfo? table, string title)
    {
        var settings = report.Calendar;
        var month = new DateTime(2026, 3, 1);
        var first = (int)month.DayOfWeek;
        var offset = (first - settings.FirstDay + 7) % 7;
        var start = month.AddDays(-offset);

        var order = Enumerable.Range(0, 7).Select(i => (settings.FirstDay + i) % 7).Where(d => settings.ShowWeekends || (d != 0 && d != 6)).ToList();
        var headers = order.Select(d => ((DayOfWeek)d).ToString()[..3]).ToList();
        var weeks = new List<IReadOnlyList<SchematicDay>>();
        var cursor = start;
        while (cursor < month.AddMonths(1) || weeks.Count < 5)
        {
            var days = new List<SchematicDay>();
            for (var i = 0; i < 7; i++)
            {
                var date = cursor.AddDays(i);
                if (order.Contains((int)date.DayOfWeek))
                {
                    days.Add(new SchematicDay(date.Day, date.Month == month.Month));
                }
            }

            weeks.Add(days);
            cursor = cursor.AddDays(7);
            if (weeks.Count >= 6)
            {
                break;
            }
        }

        // Sample events on fixed days; each takes the colour of a colour rule, else the default colour.
        var colors = settings.ColorRules.Select(r => r.Style).Where(c => c.Length > 0).ToList();
        string ColorFor(int i) => colors.Count > 0 ? colors[i % colors.Count] : settings.DefaultColor;
        var hasEnd = settings.EndField.Length > 0;
        var plan = new (int Day, int Length)[] { (5, 1), (10, hasEnd ? 3 : 1), (10, 1), (18, 1), (24, hasEnd ? 2 : 1) };
        var titleName = settings.TitleField.Length == 0 ? "Event" : FieldCaption(table, settings.TitleField);
        var events = new List<SchematicEvent>();
        for (var i = 0; i < plan.Length; i++)
        {
            var date = month.AddDays(plan[i].Day - 1);
            var row = weeks.Select((w, index) => (w, index)).FirstOrDefault(x => x.w.Any(d => d.InMonth && d.Number == date.Day)).index;
            var column = weeks[row].Select((d, index) => (d, index)).FirstOrDefault(x => x.d.InMonth && x.d.Number == date.Day);
            if (column.d is null)
            {
                continue;
            }

            var span = Math.Min(plan[i].Length, weeks[row].Count - column.index);
            events.Add(new SchematicEvent($"{titleName} {i + 1}", row, column.index, span, ColorFor(i)));
        }

        var popover = settings.PopoverFields.Select(n => FieldCaption(table, n)).ToList();
        var note = settings.StartField.Length == 0 ? "Choose the start date field." : string.Empty;
        return new CalendarSchematic(
            title, month.ToString("MMMM yyyy", Culture), headers, weeks, events, settings.Views.ToList(), settings.DefaultView, popover,
            FilterCaptions(settings.Filters, table), settings.AllowAdd, settings.AllowOpen, note);
    }

    // ---- dashboard ----

    private static DashboardSchematic Dashboard(Project project, ReportDef report, string title)
    {
        var settings = report.Dashboard;
        var boxes = DashboardLayout.Place(settings.Items.Select(i => (ItemLabel(project.Reports, i), i.Span, i.Height)).ToList());
        var note = settings.Items.Count == 0 ? "Add a KPI card or a report to lay out the dashboard." : string.Empty;
        return new DashboardSchematic(title, boxes, DashboardLayout.RowCount(boxes), settings.RefreshSeconds, note);
    }

    /// <summary>The box label of a dashboard item: the card caption (or its aggregate), or the caption of the embedded report.</summary>
    public static string ItemLabel(IEnumerable<ReportDef> reports, DashboardItem item)
    {
        if (item.Kind == DashboardItemKind.Kpi)
        {
            return string.IsNullOrWhiteSpace(item.Caption) ? AggregateRow.AggregateText(item.Value) : item.Caption;
        }

        if (item.Report.Length == 0)
        {
            return "(choose a report)";
        }

        var report = reports.FirstOrDefault(r => string.Equals(r.Name, item.Report, StringComparison.OrdinalIgnoreCase));
        return report is null || string.IsNullOrWhiteSpace(report.Caption) ? item.Report : report.Caption;
    }

    // ---- sample data ----

    private static string FieldCaption(TableInfo? table, string name)
    {
        var field = ReportFieldRules.Find(table, name);
        return field is null || string.IsNullOrWhiteSpace(field.Caption) ? name : field.Caption;
    }

    private static string FieldHeading(TableInfo? table, ReportField field)
        => !string.IsNullOrWhiteSpace(field.Caption) ? field.Caption : FieldCaption(table, field.Field);

    private static IReadOnlyList<string> FilterCaptions(IEnumerable<ReportFilter> filters, TableInfo? table)
        => filters.Select(f => string.IsNullOrWhiteSpace(f.Caption) ? FieldCaption(table, f.Field) : f.Caption).ToList();

    /// <summary>A sample group label for a field: "2026-03" for a month interval, "10-19" for a range, "A" for the first letter.</summary>
    public static string SampleLabel(TableInfo? table, ReportField field, int index)
    {
        if (field.Field.Length == 0)
        {
            return $"Sample {index + 1}";
        }

        var type = ReportFieldRules.Find(table, field.Field)?.Type ?? FieldType.Text;
        var baseDate = new DateTime(2026, 1, 5);
        switch (field.Interval)
        {
            case GroupInterval.Day:
                return baseDate.AddDays(index).ToString("yyyy-MM-dd", Culture);
            case GroupInterval.Week:
                return $"{baseDate.Year}-W{ISOWeek.GetWeekOfYear(baseDate.AddDays(7 * index)):00}";
            case GroupInterval.Month:
                return baseDate.AddMonths(index).ToString("yyyy-MM", Culture);
            case GroupInterval.Quarter:
                return $"{baseDate.Year + index / 4} Q{index % 4 + 1}";
            case GroupInterval.Year:
                return (baseDate.Year + index).ToString(Culture);
            case GroupInterval.Range:
                var size = field.RangeSize > 0 ? field.RangeSize : 10;
                var low = size * index;
                return $"{low.ToString("0.##", Culture)}-{(low + size - (size % 1 == 0 ? 1 : 0.01)).ToString("0.##", Culture)}";
            case GroupInterval.FirstLetter:
                return ((char)('A' + index % 26)).ToString();
        }

        return type switch
        {
            FieldType.Date => baseDate.AddDays(index * 7).ToString("yyyy-MM-dd", Culture),
            FieldType.DateTime => baseDate.AddDays(index * 7).AddHours(9).ToString("yyyy-MM-dd HH:mm", Culture),
            FieldType.Integer or FieldType.Decimal => ((index + 1) * 10).ToString(Culture),
            FieldType.Boolean => index % 2 == 0 ? "Yes" : "No",
            _ => $"{field.Field} {index + 1}",
        };
    }

    private static readonly double[] SampleNumbers = [1250.5, 830, 410.25, 975, 620, 1480, 305];

    /// <summary>A sample aggregate value, formatted with the aggregate's format string (an invalid format shows the raw number).</summary>
    public static string SampleValue(ReportAggregate aggregate, int index)
    {
        var value = SampleNumbers[index % SampleNumbers.Length];
        var counts = aggregate.Function is AggregateFunction.Count or AggregateFunction.CountDistinct;
        if (counts)
        {
            value = Math.Round(value / 40);
        }

        if (!string.IsNullOrWhiteSpace(aggregate.FormatString))
        {
            try
            {
                return value.ToString(aggregate.FormatString, Culture);
            }
            catch (FormatException)
            {
            }
        }

        return counts ? value.ToString("0", Culture) : value.ToString("N2", Culture);
    }

    private static string DetailSample(TableInfo? table, string caption, int index)
    {
        var field = table?.Fields.FirstOrDefault(f => string.Equals(f.Name, caption, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Caption, caption, StringComparison.OrdinalIgnoreCase));
        return field is null ? $"value {index + 1}" : SampleLabel(table, new ReportField { Field = field.Name }, index);
    }
}
