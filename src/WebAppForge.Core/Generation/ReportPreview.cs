using System.Data.Common;
using System.Globalization;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;

namespace WebAppForge.Core.Generation;

/// <summary>A few rows of a report, computed for the editor's preview: column headings, rows of text and bar sizes for charts.</summary>
public sealed record ReportPreviewResult(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, IReadOnlyList<double>? Bars, string Message)
{
    public static ReportPreviewResult Note(string message) => new([], [], null, message);
}

/// <summary>
/// Runs a report on the project's database the way the generated page groups it (the same group, date and lookup SQL),
/// and returns the first rows. The preview reads every row the table filter and the report filter allow: owner fields and user
/// levels are not applied, because the editor has no signed-in user.
/// </summary>
public static class ReportPreview
{
    public const int MaxRows = 12;

    private const int MaxColumns = 8;

    public static ReportPreviewResult Run(Project project, ReportInfo report, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(report);
        if (report.Kind == ReportKind.Dashboard)
        {
            return ReportPreviewResult.Note("A dashboard shows other reports. Preview them one at a time.");
        }

        var tables = project.Tables.Where(t => t.Generate).ToList();
        var problems = ReportValidator.Problems([report], tables).Where(p => !p.Contains("dashboard", StringComparison.Ordinal)).ToList();
        if (problems.Count > 0)
        {
            return ReportPreviewResult.Note(problems[0]);
        }

        var table = tables.First(t => string.Equals(t.Name, report.SourceTable.Trim(), StringComparison.OrdinalIgnoreCase));
        var previous = Templates.MdTables;
        var previousDb = Templates.Db;
        Templates.MdTables = tables;
        Templates.Db = SqlDialect.For(project.Database.Provider);
        try
        {
            using var connection = DatabaseConnections.Create(project.Database.Provider, connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            return report.Kind switch
            {
                ReportKind.Summary => Summary(command, report, table),
                ReportKind.Crosstab => Grid(command, report, table, pivot: true),
                ReportKind.Chart => Grid(command, report, table, pivot: report.ColumnField.Trim().Length > 0),
                _ => Calendar(command, report, table),
            };
        }
        catch (DbException ex)
        {
            return ReportPreviewResult.Note("The database refused the query: " + ex.Message);
        }
        finally
        {
            Templates.MdTables = previous;
            Templates.Db = previousDb;
        }
    }

    private static string Where(TableInfo table, ReportInfo report)
    {
        var clauses = new[] { table.Filter, report.Filter }.Where(f => f.Trim().Length > 0).Select(f => "(" + f + ")").ToList();
        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    private static string From(TableInfo table, ReportInfo report) => " FROM " + Templates.QuoteIdent(table.Name) + Where(table, report);

    private static SqlDialect Db => Templates.Db;

    /// <summary>
    /// A grouped query: the groups g0, g1, ... and the aggregates a0, a1, ... of the values v0, v1, ... read by a derived table, so
    /// that every dialect can group by a subquery (SQL Server cannot group by position or by a subquery).
    /// </summary>
    private static string GroupedSql(TableInfo table, ReportInfo report, List<string> groups, List<(AggregateFunction Function, FieldInfo? Field)> aggregates,
        string order, int limit)
    {
        var inner = groups.Select((g, i) => g + " AS g" + i).ToList();
        var outer = groups.Select((_, i) => "g" + i).ToList();
        for (var j = 0; j < aggregates.Count; j++)
        {
            var (function, field) = aggregates[j];
            if (field is null)
            {
                outer.Add(AggregateSql(function, (string?)null) + " AS a" + j);
                continue;
            }

            inner.Add(Templates.QuoteIdent(field.Name) + " AS v" + j);
            outer.Add(AggregateSql(function, "v" + j) + " AS a" + j);
        }

        if (inner.Count == 0)
        {
            inner.Add("1 AS one");
        }

        var groupBy = groups.Count == 0 ? string.Empty : " GROUP BY " + string.Join(", ", groups.Select((_, i) => "g" + i));
        var orderBy = order.Length == 0 ? string.Empty : " ORDER BY " + order;
        return "SELECT " + Db.Top(limit) + string.Join(", ", outer) + " FROM (SELECT " + string.Join(", ", inner) + From(table, report) + ") AS r"
            + groupBy + orderBy + Db.Limit(limit);
    }

    private static string AggregateSql(AggregateFunction function, FieldInfo? field) =>
        AggregateSql(function, field is null ? null : Templates.QuoteIdent(field.Name));

    private static string AggregateSql(AggregateFunction function, string? column)
    {
        var value = column ?? "*";
        return function switch
        {
            AggregateFunction.Sum => $"SUM({value})",
            AggregateFunction.Average => $"AVG({value})",
            AggregateFunction.Min => $"MIN({value})",
            AggregateFunction.Max => $"MAX({value})",
            _ => $"COUNT({value})",
        };
    }

    private static string Heading(AggregateFunction function, FieldInfo? field, string caption) =>
        caption.Length > 0 ? caption : field is null ? "Count" : $"{function} of {(field.Caption.Length > 0 ? field.Caption : field.Name)}";

    private static string Caption(FieldInfo field) => field.Caption.Length > 0 ? field.Caption : field.Name;

    private static string Text(object? value) => value switch
    {
        null or DBNull => "(empty)",
        bool flag => flag ? "Yes" : "No",
        double d => d.ToString("#,0.##", CultureInfo.InvariantCulture),
        decimal m => m.ToString("#,0.##", CultureInfo.InvariantCulture),
        long l => l.ToString("#,0", CultureInfo.InvariantCulture),
        int i => i.ToString("#,0", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static double Number(object? value) => value is null or DBNull ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static ReportPreviewResult Summary(DbCommand command, ReportInfo report, TableInfo table)
    {
        var groups = report.Groups.Select(g => (Field: Templates.ReportField(table, g.Field), g.Interval, g.Descending)).ToList();
        var aggregates = report.Aggregates.Count > 0
            ? report.Aggregates.Select(a => (Field: Templates.OptionalReportField(table, a.Field), a.Function, a.Caption)).ToList()
            : [((FieldInfo?)null, AggregateFunction.Count, string.Empty)];
        var groupSql = groups.Select(g => Templates.GroupSql(table, g.Field, g.Interval)).ToList();
        var order = string.Join(", ", groups.Select((g, i) => "g" + i + (g.Descending ? " DESC" : string.Empty)));
        command.CommandText = GroupedSql(table, report, groupSql, aggregates.Select(a => (a.Function, a.Field)).ToList(), order, MaxRows);
        var headers = groups.Select(g => Caption(g.Field)).Concat(aggregates.Select(a => Heading(a.Function, a.Field, a.Caption))).ToList();
        var rows = new List<string[]>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(Enumerable.Range(0, headers.Count).Select(i => Text(reader.GetValue(i))).ToArray());
        }

        return new ReportPreviewResult(headers, rows, null, rows.Count == 0 ? "No rows." : $"First {rows.Count} groups.");
    }

    private static ReportPreviewResult Grid(DbCommand command, ReportInfo report, TableInfo table, bool pivot)
    {
        var row = Templates.ReportField(table, report.RowField);
        var column = Templates.OptionalReportField(table, report.ColumnField);
        var value = Templates.OptionalReportField(table, report.ValueField);
        var rowSql = Templates.GroupSql(table, row, report.RowInterval);
        var heading = Heading(report.Function, value, string.Empty);
        if (!pivot || column is null)
        {
            var byValue = report.Kind == ReportKind.Chart && report.ChartOrder == ChartOrder.ValueDescending;
            var limit = report.Kind == ReportKind.Chart && report.TopCount > 0 ? Math.Min(report.TopCount, MaxRows) : MaxRows;
            command.CommandText = GroupedSql(table, report, [rowSql], [(report.Function, value)], byValue ? "a0 DESC" : "g0", limit);
            var rows = new List<string[]>();
            var numbers = new List<double>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add([Text(reader.GetValue(0)), Text(reader.GetValue(1))]);
                numbers.Add(Number(reader.GetValue(1)));
            }

            var max = numbers.Count == 0 ? 0 : numbers.Max(Math.Abs);
            var bars = report.Kind == ReportKind.Chart ? numbers.Select(n => max == 0 ? 0 : Math.Abs(n) / max).ToList() : null;
            return new ReportPreviewResult([Caption(row), heading], rows, bars, rows.Count == 0 ? "No rows." : $"First {rows.Count} {(report.Kind == ReportKind.Chart ? "categories" : "rows")}.");
        }

        var columnSql = Templates.GroupSql(table, column, report.ColumnInterval);
        command.CommandText = GroupedSql(table, report, [rowSql, columnSql], [(report.Function, value)], "g0, g1", 100000);
        var cells = new Dictionary<(string, string), string>();
        var rowKeys = new List<string>();
        var columnKeys = new SortedSet<string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var r = Text(reader.GetValue(0));
                var c = Text(reader.GetValue(1));
                if (!rowKeys.Contains(r))
                {
                    if (rowKeys.Count == MaxRows)
                    {
                        continue;
                    }

                    rowKeys.Add(r);
                }

                columnKeys.Add(c);
                cells[(r, c)] = Text(reader.GetValue(2));
            }
        }

        var shown = columnKeys.Take(MaxColumns).ToList();
        var result = rowKeys.Select(r => new[] { r }.Concat(shown.Select(c => cells.TryGetValue((r, c), out var v) ? v : string.Empty)).ToArray()).ToList();
        var note = rowKeys.Count == 0 ? "No rows." : $"{heading}, first {rowKeys.Count} rows" + (columnKeys.Count > shown.Count ? $" and {shown.Count} of {columnKeys.Count} columns." : ".");
        return new ReportPreviewResult(new[] { Caption(row) }.Concat(shown).ToList(), result, null, note);
    }

    private static ReportPreviewResult Calendar(DbCommand command, ReportInfo report, TableInfo table)
    {
        var date = Templates.ReportField(table, report.DateField);
        var title = Templates.CalendarTitle(report, table);
        var dateSql = Templates.DateTextSql(table, date);
        var where = Where(table, report);
        command.CommandText = $"SELECT {Db.Top(MaxRows)}{Db.Substring(dateSql, 1, 10)}, {(title is null ? "NULL" : Templates.QuoteIdent(title.Name))} FROM {Templates.QuoteIdent(table.Name)}"
            + (where.Length == 0 ? " WHERE " : where + " AND ") + $"{dateSql} IS NOT NULL ORDER BY {dateSql} DESC{Db.Limit(MaxRows)}";
        var rows = new List<string[]>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add([Text(reader.GetValue(0)), Text(reader.GetValue(1))]);
        }

        return new ReportPreviewResult([Caption(date), title is null ? "Entry" : Caption(title)], rows, null, rows.Count == 0 ? "No dated rows." : "The latest entries.");
    }
}
