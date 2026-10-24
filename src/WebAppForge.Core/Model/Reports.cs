namespace WebAppForge.Core.Model;

// Report contract shared by the generator and the editor. A project without reports generates the same output as before
// reports existed.

public enum ReportKind
{
    /// <summary>Rows grouped by one or more fields, with subtotals per group and a grand total.</summary>
    Summary,

    /// <summary>One aggregate in a grid: the row field down the side, the column field across the top, totals on both.</summary>
    Crosstab,

    /// <summary>One aggregate per category (and optional series), drawn as a chart with its data table below.</summary>
    Chart,

    /// <summary>Rows placed on a month grid by a date field.</summary>
    Calendar,

    /// <summary>Several other reports on one page.</summary>
    Dashboard,
}

public enum AggregateFunction
{
    Count,
    Sum,
    Average,
    Min,
    Max,
}

/// <summary>How a date or date-time field is grouped. Other field types are always grouped by value.</summary>
public enum DateInterval
{
    None,
    Year,
    Quarter,
    Month,
    Day,
}

public enum ChartType
{
    Bar,
    HorizontalBar,
    Line,
    Area,
    Pie,
    Doughnut,
}

/// <summary>Order of the categories of a chart.</summary>
public enum ChartOrder
{
    /// <summary>By category value, ascending.</summary>
    Category,

    /// <summary>By the aggregate value, largest first. Only used for charts without a series field.</summary>
    ValueDescending,
}

/// <summary>One group level of a summary report.</summary>
public sealed class ReportGroup
{
    public string Field { get; set; } = string.Empty;

    public DateInterval Interval { get; set; } = DateInterval.None;

    public bool Descending { get; set; }
}

/// <summary>One aggregate column of a summary report.</summary>
public sealed class ReportAggregate
{
    /// <summary>Field to aggregate. Empty counts the rows (Count only).</summary>
    public string Field { get; set; } = string.Empty;

    public AggregateFunction Function { get; set; } = AggregateFunction.Count;

    /// <summary>Column heading. Empty = function and field caption, e.g. "Sum of Amount".</summary>
    public string Caption { get; set; } = string.Empty;
}

/// <summary>One report on a dashboard.</summary>
public sealed class DashboardItem
{
    /// <summary>Name of the report shown. Dashboards cannot contain dashboards.</summary>
    public string Report { get; set; } = string.Empty;

    /// <summary>Width in Bootstrap grid columns on large screens (1 to 12). Smaller screens stack the items.</summary>
    public int Width { get; set; } = 6;
}

/// <summary>
/// A report page of the generated site, at /reports/{route of Name}. Every report except a dashboard reads one table
/// (<see cref="SourceTable"/>). The table's filter, owner field and view level apply to the report as well.
/// </summary>
public sealed class ReportInfo
{
    /// <summary>Unique name; also the URL segment and the generated class name.</summary>
    public string Name { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public ReportKind Kind { get; set; } = ReportKind.Summary;

    /// <summary>Whether this report is generated at all.</summary>
    public bool Generate { get; set; } = true;

    public string SourceTable { get; set; } = string.Empty;

    /// <summary>Optional WHERE clause fragment (SQL, without the WHERE keyword), added to the source table's own filter.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Muted text under the page heading.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Lowest user level that may open this report. 0 means any signed-in user. The source table's view level applies too,
    /// so the stricter of the two wins. Only used when User Level security is on.
    /// </summary>
    public int MinLevel { get; set; }

    public bool ShowInMenu { get; set; } = true;

    /// <summary>Heading of the home page group the report is listed under. Empty = with the tables.</summary>
    public string MenuGroup { get; set; } = string.Empty;

    // ---- summary --------------------------------------------------------------------------------

    /// <summary>Group levels, outermost first (summary).</summary>
    public List<ReportGroup> Groups { get; } = [];

    /// <summary>Aggregate columns (summary). Empty = one row count.</summary>
    public List<ReportAggregate> Aggregates { get; } = [];

    /// <summary>List the rows of each innermost group (summary). Off shows the group totals only.</summary>
    public bool ShowDetails { get; set; }

    /// <summary>Fields listed for each row when <see cref="ShowDetails"/> is on. Empty = the fields shown in the table's list.</summary>
    public List<string> DetailColumns { get; } = [];

    public bool ShowGrandTotal { get; set; } = true;

    // ---- crosstab and chart ---------------------------------------------------------------------

    /// <summary>Row field of a crosstab; category field of a chart.</summary>
    public string RowField { get; set; } = string.Empty;

    public DateInterval RowInterval { get; set; } = DateInterval.None;

    /// <summary>Column field of a crosstab; optional series field of a chart (one series per value).</summary>
    public string ColumnField { get; set; } = string.Empty;

    public DateInterval ColumnInterval { get; set; } = DateInterval.None;

    /// <summary>Field aggregated in the cells of a crosstab or the points of a chart. Empty counts the rows (Count only).</summary>
    public string ValueField { get; set; } = string.Empty;

    public AggregateFunction Function { get; set; } = AggregateFunction.Count;

    /// <summary>Most columns of a crosstab or series of a chart; later values are left out and the page says so.</summary>
    public int MaxColumns { get; set; } = 50;

    // ---- chart ----------------------------------------------------------------------------------

    public ChartType ChartType { get; set; } = ChartType.Bar;

    public ChartOrder ChartOrder { get; set; } = ChartOrder.Category;

    /// <summary>Most categories shown. 0 = all.</summary>
    public int TopCount { get; set; }

    /// <summary>Show the numbers of the chart in a table below it.</summary>
    public bool ShowDataTable { get; set; } = true;

    // ---- calendar -------------------------------------------------------------------------------

    /// <summary>Date or date-time field that places a row on the calendar (its first day for multi-day rows).</summary>
    public string DateField { get; set; } = string.Empty;

    /// <summary>Optional last day of a multi-day row. Empty = one-day rows.</summary>
    public string EndDateField { get; set; } = string.Empty;

    /// <summary>Text shown for a row. Empty = the first text field of the table, or the key.</summary>
    public string TitleField { get; set; } = string.Empty;

    /// <summary>Link each entry to the table's view page (when the table has a key and a view page).</summary>
    public bool LinkToView { get; set; } = true;

    public bool WeekStartsMonday { get; set; } = true;

    // ---- dashboard ------------------------------------------------------------------------------

    public List<DashboardItem> Items { get; } = [];
}
