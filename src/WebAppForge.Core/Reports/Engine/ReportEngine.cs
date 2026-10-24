// This file is compiled into WebAppForge.Core and is also embedded as text in every generated site that has reports
// (Web/ReportEngine.cs), so it must not use anything from the project model. It builds the SQL of the reports, runs it and
// shapes the result. Table and column names reach it only through the Spec* descriptors, which the generator writes from the
// project; request values are always SQL parameters.
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace WebAppForge.Core.Reports;

public enum SpecKind
{
    Text,
    Number,
    Date,
    Boolean,
}

public enum SpecInterval
{
    None,
    Day,
    Week,
    Month,
    Quarter,
    Year,
    Range,
    FirstLetter,
}

public enum SpecFunction
{
    Count,
    CountDistinct,
    Sum,
    Average,
    Min,
    Max,
}

public enum CrosstabPart
{
    Cells,
    RowTotals,
    ColumnTotals,
    GrandTotal,
}

/// <summary>A column of the source table. <see cref="Column"/> is the quoted SQL name.</summary>
public sealed class SpecColumn
{
    public string Name { get; init; } = string.Empty;

    public string Column { get; init; } = string.Empty;

    public string Caption { get; init; } = string.Empty;

    public SpecKind Kind { get; init; }

    /// <summary>"", "number", "currency", "percent", "date", "datetime", "time" or "boolean".</summary>
    public string Format { get; init; } = string.Empty;

    public string FormatString { get; init; } = string.Empty;

    public string TrueText { get; init; } = string.Empty;

    public string FalseText { get; init; } = string.Empty;
}

public sealed class SpecGroup
{
    public SpecColumn Field { get; init; } = new();

    public SpecInterval Interval { get; init; }

    public double RangeSize { get; init; } = 10;

    public bool Descending { get; init; }

    /// <summary>Order by the first aggregate instead of the value.</summary>
    public bool ByAggregate { get; init; }

    /// <summary>Heading.</summary>
    public string Caption { get; init; } = string.Empty;
}

public sealed class SpecAggregate
{
    /// <summary>Null = count the rows.</summary>
    public SpecColumn? Field { get; init; }

    public SpecFunction Function { get; init; }

    public string Caption { get; init; } = string.Empty;

    public string FormatString { get; init; } = string.Empty;
}

public sealed class SpecFilter
{
    public SpecColumn Field { get; init; } = new();

    public string Caption { get; init; } = string.Empty;

    public bool Relative { get; init; } = true;
}

/// <summary>The table a report reads. <see cref="Table"/>, <see cref="OwnerColumn"/> and <see cref="Order"/> are quoted SQL.</summary>
public sealed class SpecSource
{
    public string Table { get; init; } = string.Empty;

    /// <summary>SQL conditions (without WHERE) that always apply: the table filter and the report filter.</summary>
    public IReadOnlyList<string> Conditions { get; init; } = [];

    /// <summary>Quoted owner column, empty when the table has no record-level security.</summary>
    public string OwnerColumn { get; init; } = string.Empty;

    /// <summary>ORDER BY terms (without ORDER BY) for detail rows.</summary>
    public string Order { get; init; } = string.Empty;

    public IReadOnlyList<SpecColumn> Columns { get; init; } = [];
}

public sealed class SpecSummary
{
    public SpecSource Source { get; init; } = new();

    public IReadOnlyList<SpecGroup> Groups { get; init; } = [];

    public IReadOnlyList<SpecAggregate> Aggregates { get; init; } = [];

    public IReadOnlyList<SpecColumn> DetailColumns { get; init; } = [];

    public bool Compact { get; init; }

    public bool ShowGroupTotals { get; init; } = true;

    public bool ShowGrandTotal { get; init; } = true;

    public int GroupsPerPage { get; init; } = 20;

    public int DetailRowsPerGroup { get; init; } = 20;

    public IReadOnlyList<SpecFilter> Filters { get; init; } = [];
}

public sealed class SpecCrosstab
{
    public SpecSource Source { get; init; } = new();

    public SpecGroup Rows { get; init; } = new();

    public SpecGroup Columns { get; init; } = new();

    public SpecAggregate Value { get; init; } = new();

    public bool ShowRowTotals { get; init; } = true;

    public bool ShowColumnTotals { get; init; } = true;

    public bool ShowGrandTotal { get; init; } = true;

    public int MaxColumns { get; init; } = 30;

    public IReadOnlyList<SpecFilter> Filters { get; init; } = [];
}

/// <summary>Filter input of one field. Text and numbers use <see cref="Value"/>; dates use <see cref="From"/>/<see cref="To"/> (yyyy-MM-dd) or <see cref="Relative"/>.</summary>
public sealed record SpecFilterValue(string? Value = null, string? From = null, string? To = null, string? Relative = null);

/// <summary>"The group expression of <see cref="Group"/> equals <see cref="Key"/>": a clicked group, used by detail rows and drill-down.</summary>
public sealed record SpecConstraint(SpecGroup Group, string Key);

/// <summary>Everything a request adds to a report: the owner, the filter inputs and the clicked-value constraints.</summary>
public sealed class SpecRequest
{
    /// <summary>Value of the owner column. Required when the source has an owner column.</summary>
    public string? Owner { get; set; }

    /// <summary>Filter inputs by field name. Only the filters the report defines are used.</summary>
    public Dictionary<string, SpecFilterValue> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<SpecConstraint> Constraints { get; } = [];

    /// <summary>The day relative date presets are resolved against.</summary>
    public DateTime Today { get; set; } = DateTime.Today;
}

public sealed class ReportPlan
{
    public string Sql { get; init; } = string.Empty;

    public List<KeyValuePair<string, object?>> Parameters { get; init; } = [];
}

public sealed class ReportResult
{
    public string[] Columns { get; init; } = [];

    public List<object?[]> Rows { get; init; } = [];
}

/// <summary>One group of a summary: its key, its aggregates and either child groups or detail rows.</summary>
public sealed class SummaryNode
{
    /// <summary>1 for the outermost group.</summary>
    public int Level { get; init; }

    public object?[] Values { get; init; } = [];

    public string[] Keys { get; init; } = [];

    public object?[] Aggregates { get; set; } = [];

    public List<SummaryNode> Children { get; } = [];

    public ReportResult? Detail { get; set; }

    public long DetailTotal { get; set; }
}

public sealed class SummaryData
{
    public List<SummaryNode> Groups { get; } = [];

    public object?[]? GrandTotal { get; set; }

    /// <summary>Number of groups paged over: the outermost groups, or all groups of a compact summary.</summary>
    public long TotalGroups { get; set; }

    public int Page { get; set; } = 1;

    public int PageCount { get; set; } = 1;

    /// <summary>Detail rows of a summary without groups.</summary>
    public ReportResult? RootDetail { get; set; }

    public long RootDetailTotal { get; set; }
}

public sealed class CrosstabHead
{
    public string Key { get; init; } = string.Empty;

    public object? Value { get; init; }

    public bool IsOther { get; init; }

    public object? Total { get; set; }
}

public sealed class CrosstabData
{
    public List<CrosstabHead> Rows { get; } = [];

    public List<CrosstabHead> Columns { get; } = [];

    public Dictionary<(string Row, string Column), object?> Cells { get; } = [];

    public object? GrandTotal { get; set; }
}

public static class ReportEngine
{
    /// <summary>Key of the folded "Other" column of a crosstab.</summary>
    public const string OtherKey = "\u001Fother";

    public static readonly IReadOnlyList<(string Key, string Label)> RelativePresets =
    [
        ("today", "Today"),
        ("yesterday", "Yesterday"),
        ("last7", "Last 7 days"),
        ("last30", "Last 30 days"),
        ("thisWeek", "This week"),
        ("lastWeek", "Last week"),
        ("thisMonth", "This month"),
        ("lastMonth", "Last month"),
        ("thisQuarter", "This quarter"),
        ("thisYear", "This year"),
        ("lastYear", "Last year"),
    ];

    // ---- expressions ----------------------------------------------------------------------------

    /// <summary>The SQL expression a group is computed with (SQLite).</summary>
    public static string GroupExpression(SpecGroup group)
    {
        var x = group.Field.Column;
        switch (group.Interval)
        {
            case SpecInterval.Day:
                return $"date({x})";
            case SpecInterval.Week:
                return $"strftime('%Y-W%W', {x})";
            case SpecInterval.Month:
                return $"strftime('%Y-%m', {x})";
            case SpecInterval.Quarter:
                return $"strftime('%Y', {x}) || '-Q' || ((CAST(strftime('%m', {x}) AS INTEGER) + 2) / 3)";
            case SpecInterval.Year:
                return $"strftime('%Y', {x})";
            case SpecInterval.Range:
                var size = RangeLiteral(group.RangeSize);
                return $"CAST({x} / {size} AS INTEGER) * {size}";
            case SpecInterval.FirstLetter:
                return $"upper(substr({x}, 1, 1))";
            default:
                return x;
        }
    }

    private static string RangeLiteral(double size) =>
        size > 0 && !double.IsInfinity(size) ? size.ToString("R", CultureInfo.InvariantCulture) : "10";

    /// <summary>The SQL expression of an aggregate.</summary>
    public static string AggregateExpression(SpecAggregate aggregate)
    {
        var column = aggregate.Field?.Column;
        if (column is null || column.Length == 0)
        {
            return "COUNT(*)";
        }

        return aggregate.Function switch
        {
            SpecFunction.Count => $"COUNT({column})",
            SpecFunction.CountDistinct => $"COUNT(DISTINCT {column})",
            SpecFunction.Sum => $"SUM({column})",
            SpecFunction.Average => $"AVG({column})",
            SpecFunction.Min => $"MIN({column})",
            _ => $"MAX({column})",
        };
    }

    // ---- plan building --------------------------------------------------------------------------

    private sealed class Ctx
    {
        public List<KeyValuePair<string, object?>> Parameters { get; } = [];

        public string Add(object? value)
        {
            var name = "@p" + Parameters.Count.ToString(CultureInfo.InvariantCulture);
            Parameters.Add(new KeyValuePair<string, object?>(name, value));
            return name;
        }

        public void Set(string name, object? value) => Parameters.Add(new KeyValuePair<string, object?>(name, value));
    }

    /// <summary>
    /// The WHERE clause: the source conditions, the owner, the filter inputs of the filters the report defines and the constraints
    /// of the request and of <paramref name="extra"/>. Empty when there is nothing to apply.
    /// </summary>
    private static string Where(SpecSource source, IReadOnlyList<SpecFilter> filters, SpecRequest request, IReadOnlyList<SpecConstraint>? extra, Ctx ctx)
    {
        var clauses = new List<string>();
        foreach (var condition in source.Conditions)
        {
            if (condition.Trim().Length > 0)
            {
                clauses.Add("(" + condition + ")");
            }
        }

        if (source.OwnerColumn.Length > 0)
        {
            clauses.Add(source.OwnerColumn + " = @owner");
            ctx.Set("@owner", request.Owner ?? string.Empty);
        }

        foreach (var filter in filters)
        {
            if (request.Filters.TryGetValue(filter.Field.Name, out var input))
            {
                AddFilter(clauses, filter, input, request.Today, ctx);
            }
        }

        foreach (var constraint in request.Constraints.Concat(extra ?? []))
        {
            clauses.Add(Condition(constraint.Group, constraint.Key, ctx));
        }

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    private static void AddFilter(List<string> clauses, SpecFilter filter, SpecFilterValue input, DateTime today, Ctx ctx)
    {
        var column = filter.Field.Column;
        switch (filter.Field.Kind)
        {
            case SpecKind.Date:
                string? from = null;
                string? to = null;
                if (filter.Relative && input.Relative is { Length: > 0 } preset && TryRelative(preset, today, out var f, out var t))
                {
                    from = f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    to = t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else
                {
                    from = IsoDate(input.From);
                    to = IsoDate(input.To);
                }

                if (from is not null)
                {
                    clauses.Add($"date({column}) >= {ctx.Add(from)}");
                }

                if (to is not null)
                {
                    clauses.Add($"date({column}) <= {ctx.Add(to)}");
                }

                break;
            case SpecKind.Number:
                if (input.Value is { Length: > 0 } number && TryNumber(number.Trim(), out var n))
                {
                    clauses.Add($"{column} = {ctx.Add(n)}");
                }

                break;
            case SpecKind.Boolean:
                if (input.Value is { Length: > 0 } flag && TryFlag(flag.Trim(), out var b))
                {
                    clauses.Add($"{column} = {ctx.Add(b)}");
                }

                break;
            default:
                if (!string.IsNullOrEmpty(input.Value))
                {
                    clauses.Add($"{column} = {ctx.Add(input.Value)}");
                }

                break;
        }
    }

    private static string? IsoDate(string? text) =>
        text is not null && DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static bool TryNumber(string text, out object? value)
    {
        value = null;
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
        {
            value = l;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
        {
            value = d;
            return true;
        }

        return false;
    }

    private static bool TryFlag(string text, out object? value)
    {
        value = null;
        if (text is "1" or "true" or "True" or "TRUE" or "yes" or "Yes")
        {
            value = 1L;
            return true;
        }

        if (text is "0" or "false" or "False" or "FALSE" or "no" or "No")
        {
            value = 0L;
            return true;
        }

        return false;
    }

    /// <summary>Resolves a relative date preset to an inclusive day range. Weeks start on Monday.</summary>
    public static bool TryRelative(string preset, DateTime today, out DateTime from, out DateTime to)
    {
        var day = today.Date;
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var quarterStart = new DateTime(day.Year, ((day.Month - 1) / 3) * 3 + 1, 1);
        switch (preset)
        {
            case "today":
                (from, to) = (day, day);
                return true;
            case "yesterday":
                (from, to) = (day.AddDays(-1), day.AddDays(-1));
                return true;
            case "last7":
                (from, to) = (day.AddDays(-6), day);
                return true;
            case "last30":
                (from, to) = (day.AddDays(-29), day);
                return true;
            case "thisWeek":
                (from, to) = (monday, monday.AddDays(6));
                return true;
            case "lastWeek":
                (from, to) = (monday.AddDays(-7), monday.AddDays(-1));
                return true;
            case "thisMonth":
                from = new DateTime(day.Year, day.Month, 1);
                to = from.AddMonths(1).AddDays(-1);
                return true;
            case "lastMonth":
                from = new DateTime(day.Year, day.Month, 1).AddMonths(-1);
                to = from.AddMonths(1).AddDays(-1);
                return true;
            case "thisQuarter":
                (from, to) = (quarterStart, quarterStart.AddMonths(3).AddDays(-1));
                return true;
            case "thisYear":
                (from, to) = (new DateTime(day.Year, 1, 1), new DateTime(day.Year, 12, 31));
                return true;
            case "lastYear":
                (from, to) = (new DateTime(day.Year - 1, 1, 1), new DateTime(day.Year - 1, 12, 31));
                return true;
            default:
                from = to = default;
                return false;
        }
    }

    /// <summary>Converts a group key to the value the group expression is compared with. False when the key cannot be a value of that group.</summary>
    private static bool TryKeyValue(SpecGroup group, string key, out object? value)
    {
        value = null;
        if (group.Interval == SpecInterval.Range || (group.Interval == SpecInterval.None && group.Field.Kind == SpecKind.Number))
        {
            return TryNumber(key.Trim(), out value);
        }

        if (group.Interval == SpecInterval.None && group.Field.Kind == SpecKind.Boolean)
        {
            return TryFlag(key.Trim(), out value);
        }

        value = key;
        return true;
    }

    /// <summary>SQL condition "the group expression equals the key". The empty key matches NULL and the empty text.</summary>
    private static string Condition(SpecGroup group, string key, Ctx ctx)
    {
        var expression = GroupExpression(group);
        if (key.Length == 0)
        {
            return $"({expression} IS NULL OR {expression} = '')";
        }

        return TryKeyValue(group, key, out var value) ? $"{expression} = {ctx.Add(value)}" : "1 = 0";
    }

    private static string AnyCondition(SpecGroup group, IReadOnlyList<string> keys, Ctx ctx)
    {
        if (keys.Count == 0)
        {
            return "1 = 0";
        }

        return "(" + string.Join(" OR ", keys.Select(k => Condition(group, k, ctx))) + ")";
    }

    private static string Direction(bool descending) => descending ? " DESC" : " ASC";

    private static string From(SpecSource source) => " FROM " + source.Table;

    /// <summary>
    /// Groups by the first <paramref name="level"/> groups (0 = one total row) and returns the group values (columns _g0...) followed by
    /// the aggregates (_a0...). Ordered by the group at <c>level - 1</c>: its value or the first aggregate. <paramref name="firstGroupKeys"/>
    /// limits the outermost group to those keys; <paramref name="limit"/> (0 = all) and <paramref name="offset"/> page the result.
    /// </summary>
    public static ReportPlan Groups(
        SpecSource source,
        IReadOnlyList<SpecFilter> filters,
        SpecRequest request,
        IReadOnlyList<SpecGroup> groups,
        int level,
        IReadOnlyList<SpecAggregate> aggregates,
        IReadOnlyList<string>? firstGroupKeys = null,
        int limit = 0,
        int offset = 0)
    {
        level = Math.Clamp(level, 0, groups.Count);
        var ctx = new Ctx();
        var select = new List<string>();
        var expressions = new List<string>();
        for (var i = 0; i < level; i++)
        {
            expressions.Add(GroupExpression(groups[i]));
            select.Add(expressions[i] + " AS _g" + i);
        }

        for (var i = 0; i < aggregates.Count; i++)
        {
            select.Add(AggregateExpression(aggregates[i]) + " AS _a" + i);
        }

        if (select.Count == 0)
        {
            select.Add("COUNT(*) AS _a0");
        }

        var sql = new StringBuilder("SELECT ").Append(string.Join(", ", select)).Append(From(source));
        var where = Where(source, filters, request, null, ctx);
        if (firstGroupKeys is not null && level > 0)
        {
            var any = AnyCondition(groups[0], firstGroupKeys, ctx);
            where = where.Length == 0 ? " WHERE " + any : where + " AND " + any;
        }

        sql.Append(where);
        if (level > 0)
        {
            sql.Append(" GROUP BY ").Append(string.Join(", ", expressions));
            var own = groups[level - 1];
            var order = new List<string>();
            if (own.ByAggregate && aggregates.Count > 0)
            {
                order.Add("_a0" + Direction(own.Descending));
            }
            else
            {
                order.Add("_g" + (level - 1) + Direction(own.Descending));
            }

            for (var i = 0; i < level; i++)
            {
                if (i != level - 1 || order[0].StartsWith("_a0", StringComparison.Ordinal))
                {
                    order.Add("_g" + i + " ASC");
                }
            }

            sql.Append(" ORDER BY ").Append(string.Join(", ", order));
            if (limit > 0)
            {
                sql.Append(" LIMIT ").Append(ctx.Add((long)limit)).Append(" OFFSET ").Append(ctx.Add((long)Math.Max(0, offset)));
            }
        }

        return new ReportPlan { Sql = sql.ToString(), Parameters = ctx.Parameters };
    }

    /// <summary>The number of groups at <paramref name="level"/>.</summary>
    public static ReportPlan CountGroups(SpecSource source, IReadOnlyList<SpecFilter> filters, SpecRequest request, IReadOnlyList<SpecGroup> groups, int level)
    {
        level = Math.Clamp(level, 1, Math.Max(1, groups.Count));
        var ctx = new Ctx();
        var where = Where(source, filters, request, null, ctx);
        var by = string.Join(", ", groups.Take(level).Select(GroupExpression));
        return new ReportPlan { Sql = "SELECT COUNT(*) FROM (SELECT 1" + From(source) + where + " GROUP BY " + by + ")", Parameters = ctx.Parameters };
    }

    /// <summary>The detail rows of a group (the constraints of its key), in the source order. <paramref name="limit"/> 0 = all rows.</summary>
    public static ReportPlan Detail(
        SpecSource source,
        IReadOnlyList<SpecFilter> filters,
        SpecRequest request,
        IReadOnlyList<SpecConstraint> group,
        IReadOnlyList<SpecColumn> columns,
        int limit)
    {
        var ctx = new Ctx();
        var where = Where(source, filters, request, group, ctx);
        var select = columns.Count == 0 ? "1" : string.Join(", ", columns.Select(c => c.Column));
        var sql = new StringBuilder("SELECT ").Append(select).Append(From(source)).Append(where);
        if (source.Order.Length > 0)
        {
            sql.Append(" ORDER BY ").Append(source.Order);
        }

        if (limit > 0)
        {
            sql.Append(" LIMIT ").Append(ctx.Add((long)limit));
        }

        return new ReportPlan { Sql = sql.ToString(), Parameters = ctx.Parameters };
    }

    /// <summary>The number of rows of a group.</summary>
    public static ReportPlan CountRows(SpecSource source, IReadOnlyList<SpecFilter> filters, SpecRequest request, IReadOnlyList<SpecConstraint> group)
    {
        var ctx = new Ctx();
        var where = Where(source, filters, request, group, ctx);
        return new ReportPlan { Sql = "SELECT COUNT(*)" + From(source) + where, Parameters = ctx.Parameters };
    }

    /// <summary>
    /// The rows of the source for one table without grouping: columns and rows in the source order (used by the calendar and previews).
    /// </summary>
    public static ReportPlan Rows(SpecSource source, IReadOnlyList<SpecFilter> filters, SpecRequest request, IReadOnlyList<SpecColumn> columns, int limit = 0)
        => Detail(source, filters, request, [], columns, limit);

    private static string ColumnExpression(SpecCrosstab x, IReadOnlyList<string>? topColumns, Ctx ctx)
    {
        var expression = GroupExpression(x.Columns);
        if (topColumns is null)
        {
            return expression;
        }

        var other = ctx.Add(OtherKey);
        return $"CASE WHEN {AnyCondition(x.Columns, topColumns, ctx)} THEN {expression} ELSE {other} END";
    }

    /// <summary>
    /// A crosstab query. Cells: _r, _c, _v. RowTotals: _r, _v ordered like the rows. ColumnTotals: _c, _v ordered like the columns.
    /// GrandTotal: _v. <paramref name="topColumns"/> (the keys kept) folds all other columns into <see cref="OtherKey"/>.
    /// </summary>
    public static ReportPlan Crosstab(SpecCrosstab x, SpecRequest request, CrosstabPart part, IReadOnlyList<string>? topColumns = null)
    {
        var ctx = new Ctx();
        var value = AggregateExpression(x.Value);
        var rows = GroupExpression(x.Rows);
        var columns = ColumnExpression(x, topColumns, ctx);
        var where = Where(x.Source, x.Filters, request, null, ctx);
        string sql;
        switch (part)
        {
            case CrosstabPart.Cells:
                sql = $"SELECT {rows} AS _r, {columns} AS _c, {value} AS _v{From(x.Source)}{where} GROUP BY {rows}, {columns}";
                break;
            case CrosstabPart.RowTotals:
                sql = $"SELECT {rows} AS _r, {value} AS _v{From(x.Source)}{where} GROUP BY {rows} ORDER BY {OrderBy(x.Rows, "_r")}";
                break;
            case CrosstabPart.ColumnTotals:
                sql = $"SELECT {columns} AS _c, {value} AS _v{From(x.Source)}{where} GROUP BY {columns} ORDER BY {OrderBy(x.Columns, "_c")}";
                break;
            default:
                sql = $"SELECT {value} AS _v{From(x.Source)}{where}";
                break;
        }

        return new ReportPlan { Sql = sql, Parameters = ctx.Parameters };
    }

    private static string OrderBy(SpecGroup group, string keyColumn) =>
        group.ByAggregate
            ? "_v" + Direction(group.Descending) + ", " + keyColumn + " ASC"
            : keyColumn + Direction(group.Descending);

    // ---- execution ------------------------------------------------------------------------------

    private static SqliteCommand Command(SqliteConnection connection, ReportPlan plan)
    {
        var command = connection.CreateCommand();
        command.CommandText = plan.Sql;
        foreach (var parameter in plan.Parameters)
        {
            command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }

        return command;
    }

    public static async Task<ReportResult> RunAsync(SqliteConnection connection, ReportPlan plan)
    {
        await using var command = Command(connection, plan);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new string[reader.FieldCount];
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i] = reader.GetName(i);
        }

        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[columns.Length];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return new ReportResult { Columns = columns, Rows = rows };
    }

    public static ReportResult Run(SqliteConnection connection, ReportPlan plan) => RunAsync(connection, plan).GetAwaiter().GetResult();

    private static async Task<long> CountAsync(SqliteConnection connection, ReportPlan plan)
    {
        await using var command = Command(connection, plan);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    // ---- summary --------------------------------------------------------------------------------

    private static List<SpecConstraint> Constraints(SpecSummary spec, string[] keys)
    {
        var list = new List<SpecConstraint>();
        for (var i = 0; i < keys.Length; i++)
        {
            list.Add(new SpecConstraint(spec.Groups[i], keys[i]));
        }

        return list;
    }

    /// <summary>
    /// Loads one page of a summary. <paramref name="showAll"/> is the key path of a group whose detail rows are all shown (null = none);
    /// it only takes effect when it matches a loaded group, so request data never reaches the SQL.
    /// </summary>
    public static async Task<SummaryData> LoadSummaryAsync(SqliteConnection connection, SpecSummary spec, SpecRequest request, int page, string[]? showAll)
    {
        var data = new SummaryData();
        var g = spec.Groups.Count;
        if (spec.Aggregates.Count > 0 && (spec.ShowGrandTotal || g == 0))
        {
            var total = await RunAsync(connection, Groups(spec.Source, spec.Filters, request, spec.Groups, 0, spec.Aggregates));
            data.GrandTotal = total.Rows.Count > 0 ? total.Rows[0] : null;
        }

        var detailLimit = spec.DetailRowsPerGroup;
        if (g == 0)
        {
            if (!spec.Compact && spec.DetailColumns.Count > 0)
            {
                var all = showAll is { Length: 0 };
                var (rows, count) = await LoadDetailAsync(connection, spec, request, [], all ? 0 : detailLimit);
                data.RootDetail = rows;
                data.RootDetailTotal = count;
            }

            return data;
        }

        var perPage = Math.Max(0, spec.GroupsPerPage);
        var topLevel = spec.Compact ? g : 1;
        data.TotalGroups = await CountAsync(connection, CountGroups(spec.Source, spec.Filters, request, spec.Groups, topLevel));
        data.PageCount = perPage == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(data.TotalGroups / (double)perPage));
        data.Page = Math.Clamp(page, 1, data.PageCount);
        var offset = perPage == 0 ? 0 : (data.Page - 1) * perPage;
        var top = await RunAsync(connection, Groups(spec.Source, spec.Filters, request, spec.Groups, topLevel, spec.Aggregates, null, perPage, offset));
        var nodes = top.Rows.Select(row => NewNode(row, topLevel, spec.Aggregates.Count)).ToList();
        data.Groups.AddRange(nodes);
        if (spec.Compact || nodes.Count == 0)
        {
            return data;
        }

        // Deeper levels, restricted to the outermost groups of this page; each level is attached to its parent by the key prefix.
        var firstKeys = nodes.Select(n => n.Keys[0]).ToList();
        var parents = nodes;
        for (var level = 2; level <= g; level++)
        {
            var result = await RunAsync(connection, Groups(spec.Source, spec.Filters, request, spec.Groups, level, spec.Aggregates, firstKeys));
            var byParent = parents.ToDictionary(p => PathOf(p.Keys), p => p);
            var next = new List<SummaryNode>();
            foreach (var row in result.Rows)
            {
                var node = NewNode(row, level, spec.Aggregates.Count);
                if (byParent.TryGetValue(PathOf(node.Keys.Take(level - 1).ToArray()), out var parent))
                {
                    parent.Children.Add(node);
                    next.Add(node);
                }
            }

            parents = next;
        }

        if (!spec.Compact && spec.DetailColumns.Count > 0)
        {
            foreach (var leaf in parents)
            {
                var all = showAll is not null && showAll.Length == g && PathOf(showAll) == PathOf(leaf.Keys);
                var (rows, count) = await LoadDetailAsync(connection, spec, request, leaf.Keys, all ? 0 : detailLimit);
                leaf.Detail = rows;
                leaf.DetailTotal = count;
            }
        }

        return data;
    }

    public static SummaryData LoadSummary(SqliteConnection connection, SpecSummary spec, SpecRequest request, int page = 1, string[]? showAll = null) =>
        LoadSummaryAsync(connection, spec, request, page, showAll).GetAwaiter().GetResult();

    private static async Task<(ReportResult Rows, long Total)> LoadDetailAsync(SqliteConnection connection, SpecSummary spec, SpecRequest request, string[] keys, int limit)
    {
        var constraints = Constraints(spec, keys);
        var rows = await RunAsync(connection, Detail(spec.Source, spec.Filters, request, constraints, spec.DetailColumns, limit > 0 ? limit + 1 : 0));
        if (limit > 0 && rows.Rows.Count > limit)
        {
            var total = await CountAsync(connection, CountRows(spec.Source, spec.Filters, request, constraints));
            rows.Rows.RemoveRange(limit, rows.Rows.Count - limit);
            return (rows, total);
        }

        return (rows, rows.Rows.Count);
    }

    private static SummaryNode NewNode(object?[] row, int level, int aggregates)
    {
        var values = row.Take(level).ToArray();
        var node = new SummaryNode { Level = level, Values = values, Keys = values.Select(KeyOf).ToArray() };
        node.Aggregates = row.Skip(level).Take(Math.Max(1, aggregates)).ToArray();
        if (aggregates == 0)
        {
            node.Aggregates = [];
        }

        return node;
    }

    /// <summary>The key path of a group as one comparable string.</summary>
    public static string PathOf(IEnumerable<string> keys) => string.Join("\u001E", keys);

    // ---- crosstab -------------------------------------------------------------------------------

    public static async Task<CrosstabData> LoadCrosstabAsync(SqliteConnection connection, SpecCrosstab spec, SpecRequest request)
    {
        var data = new CrosstabData();
        var allColumns = await RunAsync(connection, Crosstab(spec, request, CrosstabPart.ColumnTotals));
        List<string>? top = null;
        var columnRows = allColumns.Rows;
        if (spec.MaxColumns > 0 && columnRows.Count > spec.MaxColumns)
        {
            top = columnRows.Take(spec.MaxColumns).Select(r => KeyOf(r[0])).ToList();
        }

        if (top is null)
        {
            foreach (var row in columnRows)
            {
                data.Columns.Add(new CrosstabHead { Key = KeyOf(row[0]), Value = row[0], Total = row[1] });
            }
        }
        else
        {
            var folded = await RunAsync(connection, Crosstab(spec, request, CrosstabPart.ColumnTotals, top));
            foreach (var row in folded.Rows.Where(r => KeyOf(r[0]) != OtherKey))
            {
                data.Columns.Add(new CrosstabHead { Key = KeyOf(row[0]), Value = row[0], Total = row[1] });
            }

            if (folded.Rows.FirstOrDefault(r => KeyOf(r[0]) == OtherKey) is { } other)
            {
                data.Columns.Add(new CrosstabHead { Key = OtherKey, Value = null, IsOther = true, Total = other[1] });
            }
        }

        var rowTotals = await RunAsync(connection, Crosstab(spec, request, CrosstabPart.RowTotals));
        foreach (var row in rowTotals.Rows)
        {
            data.Rows.Add(new CrosstabHead { Key = KeyOf(row[0]), Value = row[0], Total = row[1] });
        }

        var cells = await RunAsync(connection, Crosstab(spec, request, CrosstabPart.Cells, top));
        foreach (var row in cells.Rows)
        {
            data.Cells[(KeyOf(row[0]), KeyOf(row[1]))] = row[2];
        }

        var grand = await RunAsync(connection, Crosstab(spec, request, CrosstabPart.GrandTotal, null));
        data.GrandTotal = grand.Rows.Count > 0 ? grand.Rows[0][0] : null;
        return data;
    }

    public static CrosstabData LoadCrosstab(SqliteConnection connection, SpecCrosstab spec, SpecRequest request) =>
        LoadCrosstabAsync(connection, spec, request).GetAwaiter().GetResult();

    // ---- keys, formatting, constraints in URLs --------------------------------------------------

    /// <summary>The text key of a value: empty for NULL, invariant for numbers.</summary>
    public static string KeyOf(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        byte[] => string.Empty,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>The heading text of a group value.</summary>
    public static string DisplayKey(SpecGroup group, object? value)
    {
        var key = KeyOf(value);
        if (key.Length == 0)
        {
            return "(empty)";
        }

        switch (group.Interval)
        {
            case SpecInterval.None:
                return FormatValue(value, group.Field);
            case SpecInterval.Range:
                if (TryNumber(key, out var start) && double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var from))
                {
                    var size = group.RangeSize > 0 ? group.RangeSize : 10;
                    var whole = from == Math.Floor(from) && size == Math.Floor(size);
                    return whole
                        ? (from.ToString("0", CultureInfo.InvariantCulture) + " - " + (from + size - 1).ToString("0", CultureInfo.InvariantCulture))
                        : (from.ToString("R", CultureInfo.InvariantCulture) + " to <" + (from + size).ToString("R", CultureInfo.InvariantCulture));
                }

                return key;
            default:
                return key;
        }
    }

    /// <summary>Text of a value of a column, with the column's display format. Never HTML-encoded.</summary>
    public static string FormatValue(object? value, SpecColumn column)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (column.Kind == SpecKind.Boolean || column.Format == "boolean")
        {
            var flag = value switch
            {
                bool b => b,
                string s => s is "1" or "true" or "True" or "yes" or "Yes",
                _ => KeyOf(value) is not ("0" or string { Length: 0 }),
            };
            var custom = flag ? column.TrueText : column.FalseText;
            return custom.Length > 0 ? custom : flag ? "Yes" : "No";
        }

        switch (column.Format)
        {
            case "number":
                return Numeric(value, column.FormatString.Length > 0 ? column.FormatString : value is long ? "N0" : "N2");
            case "currency":
                return Numeric(value, column.FormatString.Length > 0 ? column.FormatString : "C");
            case "percent":
                return Numeric(value, column.FormatString.Length > 0 ? column.FormatString : "P");
            case "date":
                return Dated(value, column.FormatString.Length > 0 ? column.FormatString : "yyyy-MM-dd");
            case "datetime":
                return Dated(value, column.FormatString.Length > 0 ? column.FormatString : "yyyy-MM-dd HH:mm");
            case "time":
                return Dated(value, column.FormatString.Length > 0 ? column.FormatString : "HH:mm");
            default:
                return value is byte[] ? "(binary)" : KeyOf(value);
        }
    }

    /// <summary>Text of an aggregate value: its format string, else thousands separators for whole numbers and two decimals.</summary>
    public static string FormatAggregate(object? value, SpecAggregate aggregate)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (aggregate.FormatString.Length > 0)
        {
            return Numeric(value, aggregate.FormatString);
        }

        // MIN and MAX of a date or text column are not numbers.
        if (value is string text)
        {
            return text;
        }

        return Numeric(value, value is long ? "N0" : "N2");
    }

    private static string Numeric(object value, string format)
    {
        if (value is string text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
        }

        if (value is IFormattable number and not DateTime)
        {
            try
            {
                return number.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
            }
        }

        return KeyOf(value);
    }

    private static string Dated(object value, string format)
    {
        if (value is string text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            try
            {
                return parsed.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
            }
        }

        return KeyOf(value);
    }

    /// <summary>A clicked value as one query-string value: "field:interval:rangeSize:key" (the field name escaped).</summary>
    public static string FormatConstraint(SpecConstraint constraint) =>
        Uri.EscapeDataString(constraint.Group.Field.Name) + ":" + constraint.Group.Interval + ":"
        + constraint.Group.RangeSize.ToString("R", CultureInfo.InvariantCulture) + ":" + constraint.Key;

    /// <summary>
    /// Reads a constraint from a query-string value. The field must be a column of the source and the interval a known one; anything else is
    /// refused, so only names from the project reach the SQL.
    /// </summary>
    public static bool TryParseConstraint(SpecSource source, string? text, out SpecConstraint constraint)
    {
        constraint = null!;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split(':', 4);
        if (parts.Length != 4)
        {
            return false;
        }

        var name = Uri.UnescapeDataString(parts[0]);
        var column = source.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (column is null
            || !Enum.TryParse<SpecInterval>(parts[1], out var interval) || !Enum.IsDefined(interval)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || !(size > 0) || double.IsInfinity(size))
        {
            return false;
        }

        constraint = new SpecConstraint(new SpecGroup { Field = column, Interval = interval, RangeSize = size }, parts[3]);
        return true;
    }

    // ---- CSV ------------------------------------------------------------------------------------

    /// <summary>
    /// One CSV field. Text is always quoted (quotes doubled) and, when it starts with = + - @ or a tab or carriage return, gets an
    /// apostrophe first so a spreadsheet does not run it as a formula. Numbers are written plain.
    /// </summary>
    public static string CsvField(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case long or int or double or decimal:
                return KeyOf(value);
            default:
                var text = KeyOf(value);
                if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                {
                    text = "'" + text;
                }

                return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }

    public static string CsvLine(IEnumerable<object?> values) => string.Join(",", values.Select(CsvField)) + "\r\n";
}
