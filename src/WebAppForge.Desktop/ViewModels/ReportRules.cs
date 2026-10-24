using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WebAppForge.Core.Model;
using FieldInfo = WebAppForge.Core.Model.FieldInfo;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>The report types as the type chooser shows them: a label with a one-line description.</summary>
public sealed record ReportTypeInfo(ReportType Type, string Description, string IconPath)
{
    public string Label => $"{Type} - {Description}";

    public static IReadOnlyList<ReportTypeInfo> All { get; } =
    [
        new(ReportType.Summary, "Records grouped by one or more fields, with totals and optional detail rows.", "M2,3 H14 M2,6.5 H10 M2,10 H14 M2,13.5 H10"),
        new(ReportType.Crosstab, "A cross table: one field down the rows, one across the columns, a total in each cell.", "M1.5,2.5 H14.5 V13.5 H1.5 Z M1.5,6 H14.5 M6,2.5 V13.5"),
        new(ReportType.Chart, "A chart (column, bar, line, pie and more) of totals by category.", "M2,14 V8 H5 V14 M6.5,14 V3 H9.5 V14 M11,14 V6 H14 V14"),
        new(ReportType.Calendar, "A calendar that shows the records of a table by their dates.", "M2,3.5 H14 V14 H2 Z M2,6.5 H14 M5,2 V5 M11,2 V5"),
        new(ReportType.Dashboard, "A page that lays out KPI cards and other reports side by side.", "M2,2 H7 V8 H2 Z M9,2 H14 V5 H9 Z M9,7 H14 V14 H9 Z M2,10 H7 V14 H2 Z"),
    ];

    public static ReportTypeInfo Of(ReportType type) => All.First(i => i.Type == type);

    public static ReportType? FromChoice(string? label)
        => All.FirstOrDefault(i => i.Label == label)?.Type;
}

/// <summary>Which fields of the source table a picker offers.</summary>
public enum FieldRole
{
    /// <summary>Any field that can group or categorize (every type except binary).</summary>
    Group,

    /// <summary>Integer and decimal fields (Sum, Average, Range).</summary>
    Numeric,

    /// <summary>Date and date-time fields.</summary>
    Date,

    /// <summary>Numeric, date and date-time fields (Min, Max).</summary>
    NumericOrDate,

    Boolean,
}

/// <summary>Which fields, intervals and functions make sense together. Plain functions, so they can be tested without a window.</summary>
public static class ReportFieldRules
{
    public const string NoneLabel = CustomizationChoices.NoneLabel;

    public const string AllRowsLabel = "(all rows)";

    public static bool Matches(FieldType type, FieldRole role) => role switch
    {
        FieldRole.Numeric => type is FieldType.Integer or FieldType.Decimal,
        FieldRole.Date => type is FieldType.Date or FieldType.DateTime,
        FieldRole.NumericOrDate => type is FieldType.Integer or FieldType.Decimal or FieldType.Date or FieldType.DateTime,
        FieldRole.Boolean => type == FieldType.Boolean,
        _ => type != FieldType.Binary,
    };

    public static IReadOnlyList<string> FieldsFor(TableInfo? table, FieldRole role)
        => table?.Fields.Where(f => Matches(f.Type, role)).Select(f => f.Name).ToList() ?? [];

    /// <summary>The fields an aggregate of <paramref name="function"/> can read. Count and CountDistinct accept any field.</summary>
    public static IReadOnlyList<string> AggregateFields(TableInfo? table, AggregateFunction function) => function switch
    {
        AggregateFunction.Sum or AggregateFunction.Average => FieldsFor(table, FieldRole.Numeric),
        AggregateFunction.Min or AggregateFunction.Max => FieldsFor(table, FieldRole.NumericOrDate),
        _ => FieldsFor(table, FieldRole.Group),
    };

    /// <summary>Only Count may leave the field empty (it then counts rows).</summary>
    public static bool AllowsEmptyField(AggregateFunction function) => function == AggregateFunction.Count;

    public static FieldInfo? Find(TableInfo? table, string? name)
        => string.IsNullOrEmpty(name) ? null : table?.Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The intervals offered for a field of <paramref name="type"/>: date intervals for date fields, Range for numbers, first letter for text.</summary>
    public static IReadOnlyList<GroupInterval> IntervalsFor(FieldType? type) => type switch
    {
        FieldType.Date or FieldType.DateTime =>
            [GroupInterval.None, GroupInterval.Day, GroupInterval.Week, GroupInterval.Month, GroupInterval.Quarter, GroupInterval.Year],
        FieldType.Integer or FieldType.Decimal => [GroupInterval.None, GroupInterval.Range],
        FieldType.Text => [GroupInterval.None, GroupInterval.FirstLetter],
        _ => [GroupInterval.None],
    };

    public static bool IsDateInterval(GroupInterval interval)
        => interval is GroupInterval.Day or GroupInterval.Week or GroupInterval.Month or GroupInterval.Quarter or GroupInterval.Year;

    /// <summary>Chart kinds a single series of a combination chart may be drawn as.</summary>
    public static IReadOnlyList<ChartKind> CombinationKinds { get; } = [ChartKind.Column, ChartKind.Line, ChartKind.Area];

    public static bool IsSingleSeriesKind(ChartKind kind) => kind is ChartKind.Pie or ChartKind.Doughnut or ChartKind.PolarArea;

    public static string Display(string? field, string emptyLabel) => string.IsNullOrEmpty(field) ? emptyLabel : field;

    public static string Stored(string? display, string emptyLabel) => display is null || display == emptyLabel ? string.Empty : display;
}

/// <summary>Route-safe report names: suggestions from a caption and the uniqueness check.</summary>
public static partial class ReportNames
{
    public const int MaxLength = 60;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex RouteSafe();

    public static bool IsRouteSafe(string? name) => !string.IsNullOrEmpty(name) && name.Length <= MaxLength && RouteSafe().IsMatch(name);

    /// <summary>"Sales by month" becomes "SalesByMonth". Digits first get a "Report" prefix; nothing usable gives "Report".</summary>
    public static string FromCaption(string? caption)
    {
        var text = new StringBuilder();
        var upperNext = true;
        foreach (var ch in caption ?? string.Empty)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                text.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        if (text.Length == 0)
        {
            return "Report";
        }

        if (char.IsDigit(text[0]))
        {
            text.Insert(0, "Report");
        }

        return text.Length > MaxLength ? text.ToString(0, MaxLength) : text.ToString();
    }

    /// <summary>The name from <paramref name="caption"/>, numbered (Sales2, Sales3) until no name in <paramref name="taken"/> equals it (case-insensitive).</summary>
    public static string Suggest(string? caption, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var baseName = FromCaption(caption);
        var name = baseName;
        for (var n = 2; used.Contains(name); n++)
        {
            var suffix = n.ToString(CultureInfo.InvariantCulture);
            name = (baseName.Length + suffix.Length > MaxLength ? baseName[..(MaxLength - suffix.Length)] : baseName) + suffix;
        }

        return name;
    }

    /// <summary>Null when <paramref name="name"/> is usable; otherwise why not.</summary>
    public static string? Check(string? name, IEnumerable<string> others)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The report name cannot be empty.";
        }

        if (!IsRouteSafe(name))
        {
            return "The report name must start with a letter and use only letters, digits, '-' and '_' (up to 60 characters), so it can be part of a web address.";
        }

        return others.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? $"Another report is already called '{name}'. Report names must be unique."
            : null;
    }
}

/// <summary>Starting points for new reports and the clean-up after the source table changes.</summary>
public static class ReportDefaults
{
    /// <summary>The caption a new report of <paramref name="type"/> starts with.</summary>
    public static string CaptionFor(ReportType type) => type switch
    {
        ReportType.Crosstab => "Crosstab report",
        ReportType.Chart => "Chart report",
        ReportType.Calendar => "Calendar",
        ReportType.Dashboard => "Dashboard",
        _ => "Summary report",
    };

    public static ReportDef Create(ReportType type, string name, TableInfo? table)
    {
        var report = new ReportDef { Name = name, Caption = CaptionFor(type), Type = type, Table = type == ReportType.Dashboard ? string.Empty : table?.Name ?? string.Empty };
        FillFields(report, table);
        return report;
    }

    /// <summary>Gives the settings of the report's type a first usable shape for <paramref name="table"/> (a count, a first group or category).</summary>
    public static void FillFields(ReportDef report, TableInfo? table)
    {
        var groups = ReportFieldRules.FieldsFor(table, FieldRole.Group);
        var firstGroup = groups.FirstOrDefault(n => !ReportFieldRules.Find(table, n)!.IsPrimaryKey) ?? groups.FirstOrDefault() ?? string.Empty;
        var secondGroup = groups.FirstOrDefault(n => n != firstGroup && !ReportFieldRules.Find(table, n)!.IsPrimaryKey) ?? string.Empty;
        switch (report.Type)
        {
            case ReportType.Summary:
                if (report.Summary.Groups.Count == 0 && firstGroup.Length > 0)
                {
                    report.Summary.Groups.Add(new ReportField { Field = firstGroup });
                }

                if (report.Summary.Aggregates.Count == 0)
                {
                    report.Summary.Aggregates.Add(new ReportAggregate { Function = AggregateFunction.Count });
                }

                break;
            case ReportType.Crosstab:
                if (report.Crosstab.Rows.Field.Length == 0)
                {
                    report.Crosstab.Rows.Field = firstGroup;
                }

                if (report.Crosstab.Columns.Field.Length == 0)
                {
                    report.Crosstab.Columns.Field = secondGroup;
                }

                break;
            case ReportType.Chart:
                if (report.Chart.Category.Field.Length == 0)
                {
                    report.Chart.Category.Field = firstGroup;
                }

                if (report.Chart.Series.Count == 0)
                {
                    report.Chart.Series.Add(new ChartSeries());
                }

                break;
            case ReportType.Calendar:
                if (report.Calendar.StartField.Length == 0)
                {
                    report.Calendar.StartField = ReportFieldRules.FieldsFor(table, FieldRole.Date).FirstOrDefault() ?? string.Empty;
                }

                if (report.Calendar.TitleField.Length == 0)
                {
                    report.Calendar.TitleField = firstGroup;
                }

                break;
        }
    }

    /// <summary>
    /// Clears every field reference of the report that the new source table does not have (and removes list entries that
    /// cannot exist without one), so the pickers never show a field that is not offered.
    /// </summary>
    public static void PruneFields(ReportDef report, TableInfo? table)
    {
        bool Has(string name) => ReportFieldRules.Find(table, name) is not null;
        string Keep(string name) => name.Length == 0 || Has(name) ? name : string.Empty;

        void Group(ReportField f)
        {
            f.Field = Keep(f.Field);
            if (ReportFieldRules.IntervalsFor(ReportFieldRules.Find(table, f.Field)?.Type).Contains(f.Interval) is false)
            {
                f.Interval = GroupInterval.None;
            }
        }

        void Aggregate(ReportAggregate a)
        {
            a.Field = Keep(a.Field);
            if (a.Field.Length > 0 && !ReportFieldRules.AggregateFields(table, a.Function).Contains(a.Field))
            {
                a.Field = string.Empty;
            }
        }

        var summary = report.Summary;
        summary.Groups.ForEach(Group);
        summary.Aggregates.ForEach(Aggregate);
        summary.DetailFields.RemoveAll(n => !Has(n));
        summary.Filters.RemoveAll(f => !Has(f.Field));

        var crosstab = report.Crosstab;
        Group(crosstab.Rows);
        Group(crosstab.Columns);
        Aggregate(crosstab.Value);
        crosstab.Filters.RemoveAll(f => !Has(f.Field));

        var chart = report.Chart;
        Group(chart.Category);
        if (chart.SeriesField is { } seriesField)
        {
            Group(seriesField);
        }

        chart.Series.ForEach(s => Aggregate(s.Value));
        chart.Filters.RemoveAll(f => !Has(f.Field));

        var calendar = report.Calendar;
        calendar.TitleField = Keep(calendar.TitleField);
        calendar.StartField = Keep(calendar.StartField);
        calendar.EndField = Keep(calendar.EndField);
        calendar.AllDayField = Keep(calendar.AllDayField);
        calendar.ColorField = Keep(calendar.ColorField);
        calendar.PopoverFields.RemoveAll(n => !Has(n));
        calendar.Filters.RemoveAll(f => !Has(f.Field));
    }
}

/// <summary>One placed box of the dashboard strip: its row (0-based), first column (0-based) and width in columns.</summary>
public sealed record DashboardBox(int Index, string Caption, int Row, int Column, int Span, int Height);

/// <summary>The Bootstrap grid rule of the generated dashboard: boxes fill rows of 12 columns and wrap when the next one does not fit.</summary>
public static class DashboardLayout
{
    public const int Columns = 12;

    public static int ClampSpan(int span) => Math.Clamp(span, 1, Columns);

    /// <summary>Places boxes with the given spans (clamped to 1-12) left to right; a box that does not fit in the rest of the row starts the next row.</summary>
    public static IReadOnlyList<DashboardBox> Place(IReadOnlyList<(string Caption, int Span, int Height)> items)
    {
        var boxes = new List<DashboardBox>();
        int row = 0, column = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var span = ClampSpan(items[i].Span);
            if (column + span > Columns)
            {
                row++;
                column = 0;
            }

            boxes.Add(new DashboardBox(i, items[i].Caption, row, column, span, items[i].Height));
            column += span;
        }

        return boxes;
    }

    public static int RowCount(IReadOnlyList<DashboardBox> boxes) => boxes.Count == 0 ? 0 : boxes.Max(b => b.Row) + 1;
}

/// <summary>The colours of the series pickers and the schematic charts.</summary>
public static class ReportPalette
{
    /// <summary>A swatch of the colour picker: the CSS colour stored in the model and its brush.</summary>
    public sealed record Swatch(string Hex)
    {
        public IBrush Brush => new SolidColorBrush(ReportPalette.Parse(Hex));

        public override string ToString() => Hex;
    }

    public static IReadOnlyList<string> Colors { get; } =
    [
        "#0D6EFD", "#198754", "#DC3545", "#FFC107", "#0DCAF0", "#6F42C1", "#FD7E14", "#20C997", "#D63384", "#6C757D",
    ];

    public static IReadOnlyList<Swatch> Swatches { get; } = Colors.Select(c => new Swatch(c)).ToList();

    /// <summary>The palette colour of series <paramref name="index"/> (cycling).</summary>
    public static string ColorOf(int index) => Colors[index % Colors.Count];

    /// <summary>A CSS colour (#rrggbb, a colour name) or a Bootstrap context name; <paramref name="fallback"/> or grey when it is neither.</summary>
    public static Color Parse(string? text, string? fallback = null)
    {
        var value = text?.Trim() ?? string.Empty;
        if (ContextStyles.All.FirstOrDefault(s => string.Equals(s.Name, value, StringComparison.OrdinalIgnoreCase)) is { } style)
        {
            return Color.Parse(style.Hex);
        }

        if (value.Length > 0 && Color.TryParse(value, out var color))
        {
            return color;
        }

        return fallback is not null && fallback != text ? Parse(fallback) : Color.Parse("#6C757D");
    }

    public static bool IsColor(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && (ContextStyles.Names.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase) || Color.TryParse(text.Trim(), out _));
}

/// <summary>XAML: turns a CSS colour or Bootstrap context name into a swatch brush.</summary>
public sealed class ColorTextConverter : IValueConverter
{
    public static ColorTextConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text && ReportPalette.IsColor(text) ? new SolidColorBrush(ReportPalette.Parse(text)) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>A chart kind with a small icon (16 x 16 stroke path) for the kind picker.</summary>
public sealed record ChartKindChoice(ChartKind Kind, string Label, string IconPath)
{
    public Geometry Icon => Geometry.Parse(IconPath);

    public override string ToString() => Label;

    public static IReadOnlyList<ChartKindChoice> All { get; } =
    [
        new(ChartKind.Column, "Column", "M2,14 V8 H5 V14 M6.5,14 V3 H9.5 V14 M11,14 V6 H14 V14"),
        new(ChartKind.Bar, "Bar", "M2,2 H8 V5 H2 Z M2,6.5 H13 V9.5 H2 Z M2,11 H10 V14 H2 Z"),
        new(ChartKind.Line, "Line", "M1.5,12 L5,7 L8.5,10 L14.5,3"),
        new(ChartKind.Area, "Area", "M1.5,13 V9 L5,6 L8.5,9 L14.5,3 V13 Z"),
        new(ChartKind.Pie, "Pie", "M8,2 A6,6 0 1 0 14,8 L8,8 Z M9,1.5 A6,6 0 0 1 14.5,7 L9,7 Z"),
        new(ChartKind.Doughnut, "Doughnut", "M8,2 A6,6 0 1 0 8.01,2 Z M8,5.5 A2.5,2.5 0 1 0 8.01,5.5 Z"),
        new(ChartKind.Radar, "Radar", "M8,2 L13.5,6 L11.5,13 L4.5,13 L2.5,6 Z M8,5 L10.5,7 L9.5,10 L6.5,10 L5.5,7 Z"),
        new(ChartKind.PolarArea, "Polar area", "M8,8 L8,2 A6,6 0 0 1 13,5 Z M8,8 L13.5,9 A6,6 0 0 1 9,14 Z M8,8 L5,13 A6,6 0 0 1 2,8 Z"),
        new(ChartKind.StackedColumn, "Stacked column", "M2,14 V9 H6 V14 M2,9 V6 H6 V9 M9,14 V7 H13 V14 M9,7 V3 H13 V7"),
        new(ChartKind.StackedBar, "Stacked bar", "M2,2 H9 V6 H2 Z M9,2 H13 V6 H9 M2,9 H6 V13 H2 Z M6,9 H13 V13 H6"),
        new(ChartKind.StackedArea, "Stacked area", "M1.5,14 V10 L5,8 L8.5,10 L14.5,6 V14 Z M1.5,10 L5,5 L8.5,6 L14.5,2"),
        new(ChartKind.PercentColumn, "100% column", "M2,2 H6 V14 H2 Z M2,8 H6 M9,2 H13 V14 H9 Z M9,5 H13"),
    ];

    public static ChartKindChoice Of(ChartKind kind) => All.First(c => c.Kind == kind);
}

/// <summary>A label for an enum value, for pickers whose enum names read badly.</summary>
public sealed record ReportChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public static class ReportChoices
{
    public static IReadOnlyList<ReportType> Types { get; } = Enum.GetValues<ReportType>();

    public static IReadOnlyList<AggregateFunction> Functions { get; } = Enum.GetValues<AggregateFunction>();

    public static IReadOnlyList<SortDirection> Directions { get; } = Enum.GetValues<SortDirection>();

    public static IReadOnlyList<GroupSortBy> SortBys { get; } = Enum.GetValues<GroupSortBy>();

    public static IReadOnlyList<SummaryLayout> Layouts { get; } = Enum.GetValues<SummaryLayout>();

    public static IReadOnlyList<ChartAxis> Axes { get; } = Enum.GetValues<ChartAxis>();

    public static IReadOnlyList<LegendPosition> Legends { get; } = Enum.GetValues<LegendPosition>();

    public static IReadOnlyList<CalendarView> Views { get; } = Enum.GetValues<CalendarView>();

    public static IReadOnlyList<DashboardItemKind> ItemKinds { get; } = Enum.GetValues<DashboardItemKind>();

    public static IReadOnlyList<ConditionOperator> Operators { get; } = Enum.GetValues<ConditionOperator>();

    public const string ChartKindLabel = "(chart kind)";

    /// <summary>"(chart kind)" followed by the kinds a series may override the chart kind with.</summary>
    public static IReadOnlyList<string> SeriesKindChoices { get; } = [ChartKindLabel, .. ReportFieldRules.CombinationKinds.Select(k => k.ToString())];

    public static IReadOnlyList<ReportChoice<int>> Days { get; } =
        new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }.Select((d, i) => new ReportChoice<int>(i, d)).ToList();

    public const string TableListLabel = "(the table's list page)";
}
