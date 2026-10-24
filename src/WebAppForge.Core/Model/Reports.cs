namespace WebAppForge.Core.Model;

// Report contract shared by the generator and the editor. A project that defines no reports produces exactly the
// output generated before reports existed.

public enum ReportType
{
    /// <summary>Records grouped by one or more fields with aggregates, optional detail rows and totals.</summary>
    Summary,

    /// <summary>A cross table: one field down the rows, one across the columns, an aggregate in the cells.</summary>
    Crosstab,

    /// <summary>A Chart.js chart.</summary>
    Chart,

    /// <summary>A FullCalendar calendar fed by date fields of a table.</summary>
    Calendar,

    /// <summary>A page that lays out KPI cards and other reports.</summary>
    Dashboard,
}

public enum AggregateFunction
{
    Count,
    CountDistinct,
    Sum,
    Average,
    Min,
    Max,
}

/// <summary>How a grouping field is bucketed. Date intervals apply to date and date-time fields only; Range to numbers.</summary>
public enum GroupInterval
{
    /// <summary>Group by the exact value.</summary>
    None,

    Day,

    Week,

    Month,

    Quarter,

    Year,

    /// <summary>Numeric ranges of <see cref="ReportField.RangeSize"/> (for example 0-9, 10-19).</summary>
    Range,

    /// <summary>First character of a text value.</summary>
    FirstLetter,
}

public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>What a summary or crosstab group is ordered by.</summary>
public enum GroupSortBy
{
    /// <summary>The grouping value itself.</summary>
    Value,

    /// <summary>The first aggregate of the report.</summary>
    Aggregate,
}

/// <summary>A field used to group or categorize (rows, columns, chart categories).</summary>
public sealed class ReportField
{
    public string Field { get; set; } = string.Empty;

    public GroupInterval Interval { get; set; } = GroupInterval.None;

    /// <summary>Bucket size for <see cref="GroupInterval.Range"/> (greater than 0).</summary>
    public double RangeSize { get; set; } = 10;

    public SortDirection Sort { get; set; } = SortDirection.Ascending;

    public GroupSortBy SortBy { get; set; } = GroupSortBy.Value;

    /// <summary>Heading. Empty = the field caption.</summary>
    public string Caption { get; set; } = string.Empty;
}

/// <summary>One aggregated value.</summary>
public sealed class ReportAggregate
{
    /// <summary>Field aggregated. Empty is allowed for Count (counts rows).</summary>
    public string Field { get; set; } = string.Empty;

    public AggregateFunction Function { get; set; } = AggregateFunction.Count;

    public string Caption { get; set; } = string.Empty;

    /// <summary>.NET format string such as "N2" or "C". Empty = automatic.</summary>
    public string FormatString { get; set; } = string.Empty;
}

/// <summary>A filter field offered above the report (equality or range, depending on the field type).</summary>
public sealed class ReportFilter
{
    public string Field { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    /// <summary>Date fields only: offer relative ranges (today, last 7 days, this month, last month, this year, ...).</summary>
    public bool RelativeDates { get; set; } = true;
}

public enum SummaryLayout
{
    /// <summary>Groups with their detail rows under a heading.</summary>
    Detail,

    /// <summary>One line per group: the group values and the aggregates.</summary>
    Compact,
}

public sealed class SummarySettings
{
    /// <summary>Grouping fields, outermost first.</summary>
    public List<ReportField> Groups { get; } = [];

    public List<ReportAggregate> Aggregates { get; } = [];

    public SummaryLayout Layout { get; set; } = SummaryLayout.Detail;

    /// <summary>Detail rows under each group (Detail layout). Empty = the table's list fields.</summary>
    public List<string> DetailFields { get; } = [];

    public bool ShowGroupTotals { get; set; } = true;

    public bool ShowGrandTotal { get; set; } = true;

    /// <summary>Groups per page (0 = all).</summary>
    public int GroupsPerPage { get; set; } = 20;

    /// <summary>Detail rows shown per group before "show all" (0 = all).</summary>
    public int DetailRowsPerGroup { get; set; } = 20;

    public List<ReportFilter> Filters { get; } = [];
}

public sealed class CrosstabSettings
{
    public ReportField Rows { get; set; } = new();

    public ReportField Columns { get; set; } = new();

    public ReportAggregate Value { get; set; } = new();

    public bool ShowRowTotals { get; set; } = true;

    public bool ShowColumnTotals { get; set; } = true;

    public bool ShowGrandTotal { get; set; } = true;

    /// <summary>Maximum number of columns (the rest are folded into "Other"). 0 = no limit.</summary>
    public int MaxColumns { get; set; } = 30;

    public List<ReportFilter> Filters { get; } = [];
}

public enum ChartKind
{
    Column,
    Bar,
    Line,
    Area,
    Pie,
    Doughnut,
    Radar,
    PolarArea,
    StackedColumn,
    StackedBar,
    StackedArea,
    /// <summary>Stacked columns normalised to 100 percent.</summary>
    PercentColumn,
}

public enum ChartAxis
{
    Primary,
    Secondary,
}

public enum LegendPosition
{
    Top,
    Bottom,
    Left,
    Right,
    Hidden,
}

public sealed class ChartSeries
{
    public ReportAggregate Value { get; set; } = new();

    /// <summary>CSS colour. Empty = from the palette.</summary>
    public string Color { get; set; } = string.Empty;

    public ChartAxis Axis { get; set; } = ChartAxis.Primary;

    /// <summary>Combination charts: draw this series as another kind (Column, Line or Area). Null = the chart kind.</summary>
    public ChartKind? Kind { get; set; }

    public string Caption { get; set; } = string.Empty;
}

public sealed class ChartSettings
{
    public ChartKind Kind { get; set; } = ChartKind.Column;

    /// <summary>The field on the category axis (or the slices of a pie).</summary>
    public ReportField Category { get; set; } = new();

    /// <summary>Series. At least one unless <see cref="SeriesField"/> is set.</summary>
    public List<ChartSeries> Series { get; } = [];

    /// <summary>Optional: one series per distinct value of this field (uses <see cref="Series"/>[0] as the value).</summary>
    public ReportField? SeriesField { get; set; }

    public string Title { get; set; } = string.Empty;

    public LegendPosition Legend { get; set; } = LegendPosition.Bottom;

    public bool ShowDataLabels { get; set; }

    /// <summary>Pixels.</summary>
    public int Height { get; set; } = 360;

    /// <summary>Only the top N categories by the first series (0 = all); the rest are folded into "Other" for pies.</summary>
    public int TopN { get; set; }

    public bool TrendLine { get; set; }

    public string XAxisTitle { get; set; } = string.Empty;

    public string YAxisTitle { get; set; } = string.Empty;

    public List<ReportFilter> Filters { get; } = [];
}

public enum CalendarView
{
    Month,
    Week,
    Day,
    List,
    Year,
}

public sealed class CalendarSettings
{
    public string TitleField { get; set; } = string.Empty;

    public string StartField { get; set; } = string.Empty;

    /// <summary>Optional end date/time. Empty = single point events.</summary>
    public string EndField { get; set; } = string.Empty;

    /// <summary>Optional boolean field that marks all-day events.</summary>
    public string AllDayField { get; set; } = string.Empty;

    /// <summary>Optional field whose value picks the colour through <see cref="ColorRules"/>.</summary>
    public string ColorField { get; set; } = string.Empty;

    public List<ValueRule> ColorRules { get; } = [];

    /// <summary>Default event colour (CSS). Empty = the theme colour.</summary>
    public string DefaultColor { get; set; } = string.Empty;

    /// <summary>Fields shown in the event popover, in order. Empty = every field with ShowInView.</summary>
    public List<string> PopoverFields { get; } = [];

    public List<CalendarView> Views { get; } = [CalendarView.Month, CalendarView.Week, CalendarView.Day, CalendarView.List];

    public CalendarView DefaultView { get; set; } = CalendarView.Month;

    /// <summary>0 = Sunday ... 6 = Saturday.</summary>
    public int FirstDay { get; set; } = 1;

    public bool ShowWeekends { get; set; } = true;

    /// <summary>Click an empty day to open the table's add page, and click an event to open its view page.</summary>
    public bool AllowAdd { get; set; } = true;

    public bool AllowOpen { get; set; } = true;

    public List<ReportFilter> Filters { get; } = [];
}

public enum DashboardItemKind
{
    /// <summary>A card with one aggregate value.</summary>
    Kpi,

    /// <summary>Another report of the project (summary, crosstab, chart or calendar) embedded in the dashboard.</summary>
    Report,
}

public sealed class DashboardItem
{
    public DashboardItemKind Kind { get; set; } = DashboardItemKind.Report;

    /// <summary>For <see cref="DashboardItemKind.Report"/>: the name of a report of this project (not a dashboard).</summary>
    public string Report { get; set; } = string.Empty;

    // ---- KPI card ----
    public string Caption { get; set; } = string.Empty;

    public string Table { get; set; } = string.Empty;

    public ReportAggregate Value { get; set; } = new();

    public string Filter { get; set; } = string.Empty;

    /// <summary>Bootstrap contextual name for the card.</summary>
    public string Style { get; set; } = "primary";

    /// <summary>Bootstrap Icons name (without the "bi-" prefix).</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Optional link target: the name of a report or table route opened when the card is clicked.</summary>
    public string LinkTo { get; set; } = string.Empty;

    // ---- layout ----
    /// <summary>Bootstrap grid width 1-12.</summary>
    public int Span { get; set; } = 6;

    /// <summary>Pixels; 0 = automatic.</summary>
    public int Height { get; set; }
}

public sealed class DashboardSettings
{
    /// <summary>Items in display order; they wrap onto new rows according to their spans.</summary>
    public List<DashboardItem> Items { get; } = [];

    /// <summary>Seconds between automatic refreshes of the data. 0 = none.</summary>
    public int RefreshSeconds { get; set; }
}

/// <summary>A report of the project.</summary>
public sealed class ReportDef
{
    /// <summary>Route-safe identifier, unique among the reports of the project.</summary>
    public string Name { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public ReportType Type { get; set; } = ReportType.Summary;

    /// <summary>The table the report reads (not used by dashboards, which read through their items).</summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>Optional SQL condition (without WHERE) always applied, like a table's filter.</summary>
    public string Filter { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool ShowInMenu { get; set; } = true;

    public string MenuGroup { get; set; } = string.Empty;

    public int MenuOrder { get; set; }

    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Minimum user level to open the report when user-level security is on (0 = any signed-in user).
    /// Record-level ownership of the source table always applies to the data.
    /// </summary>
    public int MinLevel { get; set; }

    /// <summary>
    /// Drill-down: clicking a group, a crosstab cell, a chart point or a calendar event opens this report (a Summary
    /// or Chart of the same table, filtered to the clicked value). Empty = the source table's list page with a filter.
    /// </summary>
    public string DrillDownReport { get; set; } = string.Empty;

    public SummarySettings Summary { get; set; } = new();

    public CrosstabSettings Crosstab { get; set; } = new();

    public ChartSettings Chart { get; set; } = new();

    public CalendarSettings Calendar { get; set; } = new();

    public DashboardSettings Dashboard { get; set; } = new();
}
