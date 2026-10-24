using System.Text.RegularExpressions;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Reports;

public enum ReportIssueSeverity
{
    Error,
    Warning,
}

/// <summary>One problem found in a report. <see cref="Message"/> starts with "Report 'Name': ".</summary>
public sealed record ReportIssue(ReportIssueSeverity Severity, string ReportName, string Message);

/// <summary>Checks the reports of a project against its tables. Errors stop generation; warnings are only advice.</summary>
public static partial class ReportValidator
{
    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex RouteSafe();

    public static IReadOnlyList<ReportIssue> Validate(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<ReportIssue>();
        var reports = project.Reports;
        for (var i = 0; i < reports.Count; i++)
        {
            new Checker(project, reports[i], issues).Run(reports.Take(i).Any(r => SameName(r.Name, reports[i].Name)));
        }

        // Warnings that look at all reports.
        foreach (var report in reports)
        {
            if (report.Name.Length == 0 || report.ShowInMenu)
            {
                continue;
            }

            var used = reports.Any(r => !ReferenceEquals(r, report)
                && (SameName(r.DrillDownReport, report.Name)
                    || (r.Type == ReportType.Dashboard && r.Dashboard.Items.Any(x => x.Kind == DashboardItemKind.Report && SameName(x.Report, report.Name)))));
            if (!used)
            {
                issues.Add(new ReportIssue(ReportIssueSeverity.Warning, report.Name,
                    $"Report '{report.Name}': the report is not shown anywhere (it is not in the menu and no dashboard or drill-down uses it)."));
            }
        }

        return issues;
    }

    /// <summary>The errors of <see cref="Validate"/> only.</summary>
    public static IReadOnlyList<ReportIssue> Errors(Project project) =>
        Validate(project).Where(i => i.Severity == ReportIssueSeverity.Error).ToList();

    internal static bool SameName(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static bool IsNumeric(FieldType type) => type is FieldType.Integer or FieldType.Decimal;

    internal static bool IsDate(FieldType type) => type is FieldType.Date or FieldType.DateTime;

    private sealed class Checker(Project project, ReportDef report, List<ReportIssue> issues)
    {
        private TableInfo? _table;

        private void Error(string message) =>
            issues.Add(new ReportIssue(ReportIssueSeverity.Error, report.Name, $"Report '{report.Name}': {message}"));

        private void Warning(string message) =>
            issues.Add(new ReportIssue(ReportIssueSeverity.Warning, report.Name, $"Report '{report.Name}': {message}"));

        public void Run(bool duplicate)
        {
            if (report.Name.Trim().Length == 0)
            {
                Error("the name is empty.");
            }
            else if (!RouteSafe().IsMatch(report.Name))
            {
                Error("the name may contain only letters, digits, dash and underscore.");
            }

            if (duplicate)
            {
                Error("the name is used by more than one report.");
            }

            if (report.MinLevel < 0)
            {
                Error("the minimum level must not be negative.");
            }

            if (report.Type != ReportType.Dashboard)
            {
                _table = FindTable(report.Table, null);
            }

            if (report.DrillDownReport.Length > 0 && report.Type != ReportType.Dashboard)
            {
                CheckDrillDown();
            }

            switch (report.Type)
            {
                case ReportType.Summary:
                    CheckSummary();
                    break;
                case ReportType.Crosstab:
                    CheckCrosstab();
                    break;
                case ReportType.Chart:
                    CheckChart();
                    break;
                case ReportType.Calendar:
                    CheckCalendar();
                    break;
                case ReportType.Dashboard:
                    CheckDashboard();
                    break;
            }
        }

        private TableInfo? FindTable(string name, string? where)
        {
            var prefix = where is null ? string.Empty : where + " ";
            if (name.Trim().Length == 0)
            {
                Error($"{prefix}no table is set.");
                return null;
            }

            var table = project.Tables.FirstOrDefault(t => string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (table is null)
            {
                Error($"{prefix}the table '{name}' does not exist.");
                return null;
            }

            if (!table.Generate)
            {
                Error($"{prefix}the table '{name}' is not generated.");
                return null;
            }

            return table;
        }

        private FieldInfo? FindField(TableInfo? table, string name)
        {
            if (table is null)
            {
                return null;
            }

            var field = table.Fields.FirstOrDefault(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                Error($"the field '{name}' does not exist in table '{table.Name}'.");
            }
            else if (ReportQueryBuilder.IsHidden(project, table, field))
            {
                Error($"the field '{field.Name}' of table '{table.Name}' is secret and cannot be used in a report.");
                return null;
            }

            return field;
        }

        private void CheckGroupField(TableInfo? table, ReportField f, string role)
        {
            if (f.Field.Trim().Length == 0)
            {
                Error($"the {role} field is not set.");
                return;
            }

            var field = FindField(table, f.Field);
            if (field is null)
            {
                return;
            }

            switch (f.Interval)
            {
                case GroupInterval.Day or GroupInterval.Week or GroupInterval.Month or GroupInterval.Quarter or GroupInterval.Year:
                    if (!IsDate(field.Type))
                    {
                        Error($"the field '{field.Name}' is not a date field, so the interval {f.Interval} cannot be used.");
                    }

                    break;
                case GroupInterval.Range:
                    if (!IsNumeric(field.Type))
                    {
                        Error($"the field '{field.Name}' is not a numeric field, so the interval Range cannot be used.");
                    }
                    else if (!(f.RangeSize > 0) || double.IsInfinity(f.RangeSize))
                    {
                        Error($"the range size of the field '{field.Name}' must be greater than 0.");
                    }

                    break;
                case GroupInterval.FirstLetter:
                    if (field.Type != FieldType.Text)
                    {
                        Error($"the field '{field.Name}' is not a text field, so the interval FirstLetter cannot be used.");
                    }

                    break;
            }

            if (f.Interval != GroupInterval.Range && !(f.RangeSize > 0))
            {
                // A stored size that cannot be used is reported even when the interval ignores it, because it would break when Range is chosen.
                Error($"the range size of the field '{field.Name}' must be greater than 0.");
            }
        }

        private void CheckAggregate(TableInfo? table, ReportAggregate a, string role)
        {
            var name = a.Field.Trim();
            if (name.Length == 0)
            {
                if (a.Function != AggregateFunction.Count)
                {
                    Error($"{a.Function} needs a field ({role}).");
                }

                return;
            }

            var field = FindField(table, name);
            if (field is null)
            {
                return;
            }

            if (a.Function is AggregateFunction.Sum or AggregateFunction.Average && !IsNumeric(field.Type))
            {
                Error($"{a.Function} of the field '{field.Name}' needs a numeric field ({role}).");
            }
        }

        private void CheckFilters(List<ReportFilter> filters)
        {
            foreach (var filter in filters)
            {
                if (filter.Field.Trim().Length == 0)
                {
                    Error("a filter has no field.");
                    continue;
                }

                FindField(_table, filter.Field);
            }
        }

        private void CheckDuplicateCaptions(IEnumerable<ReportAggregate> aggregates)
        {
            var list = aggregates.ToList();
            foreach (var group in list.GroupBy(a => ReportCaptions.Aggregate(a, _table), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                Warning($"more than one aggregate has the caption '{group.Key}'.");
            }
        }

        private void CheckSummary()
        {
            var s = report.Summary;
            if (s.Groups.Count == 0 && s.Aggregates.Count == 0)
            {
                Error("a summary needs at least one group or aggregate.");
            }
            else if (s.Layout == SummaryLayout.Compact && s.Groups.Count == 0)
            {
                Error("the compact layout needs at least one group.");
            }

            foreach (var group in s.Groups)
            {
                CheckGroupField(_table, group, "group");
            }

            foreach (var aggregate in s.Aggregates)
            {
                CheckAggregate(_table, aggregate, "aggregate");
            }

            foreach (var detail in s.DetailFields)
            {
                FindField(_table, detail);
            }

            CheckFilters(s.Filters);
            CheckDuplicateCaptions(s.Aggregates);
        }

        private void CheckCrosstab()
        {
            var c = report.Crosstab;
            if (c.Rows.Field.Trim().Length == 0)
            {
                Error("a crosstab needs a row field.");
            }
            else
            {
                CheckGroupField(_table, c.Rows, "row");
            }

            if (c.Columns.Field.Trim().Length == 0)
            {
                Error("a crosstab needs a column field.");
            }
            else
            {
                CheckGroupField(_table, c.Columns, "column");
            }

            CheckAggregate(_table, c.Value, "value");
            CheckFilters(c.Filters);
        }

        private void CheckChart()
        {
            var c = report.Chart;
            if (c.Category.Field.Trim().Length == 0)
            {
                Error("a chart needs a category field.");
            }
            else
            {
                CheckGroupField(_table, c.Category, "category");
            }

            if (c.Series.Count == 0 && c.SeriesField is null)
            {
                Error("a chart needs at least one series or a series field.");
            }

            if (c.SeriesField is { } seriesField)
            {
                if (seriesField.Field.Trim().Length == 0)
                {
                    Error("the series field is not set.");
                }
                else
                {
                    CheckGroupField(_table, seriesField, "series");
                }
            }

            foreach (var series in c.Series)
            {
                CheckAggregate(_table, series.Value, "series");
            }

            CheckFilters(c.Filters);
            CheckDuplicateCaptions(c.Series.Select(s => s.Value));
        }

        private void CheckCalendar()
        {
            var c = report.Calendar;
            if (c.TitleField.Trim().Length == 0)
            {
                Error("a calendar needs a title field.");
            }
            else
            {
                FindField(_table, c.TitleField);
            }

            if (c.StartField.Trim().Length == 0)
            {
                Error("a calendar needs a start date field.");
            }
            else if (FindField(_table, c.StartField) is { } start && !IsDate(start.Type))
            {
                Error($"the start field '{start.Name}' must be a date or date-time field.");
            }

            if (c.EndField.Trim().Length > 0 && FindField(_table, c.EndField) is { } end && !IsDate(end.Type))
            {
                Error($"the end field '{end.Name}' must be a date or date-time field.");
            }

            foreach (var name in new[] { c.AllDayField, c.ColorField }.Concat(c.PopoverFields))
            {
                if (name.Trim().Length > 0)
                {
                    FindField(_table, name);
                }
            }

            CheckFilters(c.Filters);
        }

        private void CheckDashboard()
        {
            var items = report.Dashboard.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var label = $"item {i + 1}";
                if (item.Kind == DashboardItemKind.Report)
                {
                    var target = project.Reports.FirstOrDefault(r => SameName(r.Name, item.Report));
                    if (item.Report.Trim().Length == 0)
                    {
                        Error($"{label} names no report.");
                    }
                    else if (target is null)
                    {
                        Error($"{label} names the report '{item.Report}', which does not exist.");
                    }
                    else if (ReferenceEquals(target, report))
                    {
                        Error($"{label} includes the dashboard itself.");
                    }
                    else if (target.Type == ReportType.Dashboard)
                    {
                        Error($"{label} includes the dashboard '{target.Name}'; a dashboard cannot include another dashboard.");
                    }
                }
                else
                {
                    var table = FindTable(item.Table, label);
                    CheckAggregate(table, item.Value, label);
                }

                if (item.Span is < 1 or > 12)
                {
                    Error($"{label} has the span {item.Span}; it must be from 1 to 12.");
                }
            }
        }

        private void CheckDrillDown()
        {
            var target = project.Reports.FirstOrDefault(r => SameName(r.Name, report.DrillDownReport));
            if (target is null)
            {
                Error($"the drill-down report '{report.DrillDownReport}' does not exist.");
                return;
            }

            if (target.Type is not (ReportType.Summary or ReportType.Chart or ReportType.Crosstab))
            {
                Error($"the drill-down report '{target.Name}' must be a summary, chart or crosstab report.");
            }
            else if (!string.Equals(target.Table.Trim(), report.Table.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Error($"the drill-down report '{target.Name}' reads the table '{target.Table}', not '{report.Table}'.");
            }
        }
    }
}

/// <summary>Default captions of report fields and aggregates, shared by the validator, the generated pages and the editor.</summary>
public static class ReportCaptions
{
    /// <summary>The caption of a field of a table: its caption, else its name.</summary>
    public static string FieldCaption(TableInfo? table, string field)
    {
        var info = table?.Fields.FirstOrDefault(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));
        return info is null ? field : info.Caption.Length > 0 ? info.Caption : info.Name;
    }

    /// <summary>The heading of a grouping field: its caption, else the caption of the table field.</summary>
    public static string Field(ReportField field, TableInfo? table) =>
        field.Caption.Length > 0 ? field.Caption : FieldCaption(table, field.Field);

    /// <summary>The heading of an aggregate: its caption, else for example "Count", "Sum of Freight".</summary>
    public static string Aggregate(ReportAggregate aggregate, TableInfo? table)
    {
        if (aggregate.Caption.Length > 0)
        {
            return aggregate.Caption;
        }

        var name = aggregate.Field.Length == 0 ? string.Empty : FieldCaption(table, aggregate.Field);
        var function = aggregate.Function switch
        {
            AggregateFunction.Count => "Count",
            AggregateFunction.CountDistinct => "Distinct count",
            AggregateFunction.Sum => "Sum",
            AggregateFunction.Average => "Average",
            AggregateFunction.Min => "Min",
            _ => "Max",
        };
        return name.Length == 0 ? function : function + " of " + name;
    }
}
