using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Checks of the report definitions. The generator refuses a project with any problem; the desktop editor lists the same
/// problems ("Report Sales: ...") while the reports are edited.
/// </summary>
public static class ReportValidator
{
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the field type supports Sum and Average.</summary>
    public static bool IsNumeric(FieldType type) => type is FieldType.Integer or FieldType.Decimal;

    /// <summary>True when the field type can be grouped by year, quarter, month or day.</summary>
    public static bool IsDate(FieldType type) => type is FieldType.Date or FieldType.DateTime;

    /// <summary>True when a report can treat the field as a date: a date column, or a number column that stores dates (<see cref="FieldInfo.DateStorage"/>).</summary>
    public static bool IsDateField(FieldInfo field) => IsDate(field.Type) || (IsNumeric(field.Type) && field.DateStorage != DateStorage.Text);

    /// <summary>Throws for the first problem of the generated reports of <paramref name="project"/>.</summary>
    public static void Validate(Project project, IReadOnlyList<TableInfo> tables)
    {
        var problems = Problems(project.Reports, tables);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(problems[0]);
        }
    }

    /// <summary>
    /// Every problem of the generated reports. <paramref name="tables"/> are the tables the site is generated from; fields that
    /// the site hides as secret (password hashes, user levels) cannot be used.
    /// </summary>
    public static List<string> Problems(IReadOnlyList<ReportInfo> reports, IReadOnlyList<TableInfo> tables)
    {
        var problems = new List<string>();
        var generated = reports.Where(r => r.Generate).ToList();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var report in generated)
        {
            var label = report.Name.Trim().Length == 0 ? "(no name)" : report.Name.Trim();
            void Add(string text) => problems.Add($"Report {label}: {text}");

            if (Identifier.Route(report.Name).Length == 0)
            {
                Add("give the report a name with at least one letter or digit.");
                continue;
            }

            if (!routes.Add(Identifier.Route(report.Name)) || !classes.Add(Identifier.Pascal(report.Name)))
            {
                Add("another report has the same name or URL.");
            }

            if (report.Kind == ReportKind.Dashboard)
            {
                CheckDashboard(report, generated, Add);
                continue;
            }

            if (string.IsNullOrWhiteSpace(report.SourceTable))
            {
                Add("choose the table it reads.");
                continue;
            }

            var table = tables.FirstOrDefault(t => Same(t.Name, report.SourceTable));
            if (table is null)
            {
                Add($"the table '{report.SourceTable}' is not one of the generated tables.");
                continue;
            }

            if (report.MaxColumns < 1)
            {
                Add("the most columns must be at least 1.");
            }

            if (report.TopCount < 0)
            {
                Add("the number of categories shown cannot be negative.");
            }

            if (report.DateFilterField.Trim().Length > 0)
            {
                if (report.Kind == ReportKind.Calendar)
                {
                    Add("a calendar moves by month; clear the date range field.");
                }
                else if (CheckField(table, report.DateFilterField, "date range field", Add) is { } range && !IsDateField(range))
                {
                    Add($"the date range field '{range.Name}' is not a date.");
                }
            }

            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var filter in report.FilterFields)
            {
                if (CheckField(table, filter, "filter field", Add) is { } field && !filters.Add(field.Name))
                {
                    Add($"the filter field '{field.Name}' is listed twice.");
                }
            }

            switch (report.Kind)
            {
                case ReportKind.Summary:
                    foreach (var group in report.Groups)
                    {
                        CheckGrouping(table, group.Field, group.Interval, "group field", Add);
                    }

                    foreach (var aggregate in report.Aggregates)
                    {
                        CheckAggregate(table, aggregate.Field, aggregate.Function, Add);
                    }

                    foreach (var column in report.DetailColumns)
                    {
                        CheckField(table, column, "detail column", Add);
                    }

                    break;

                case ReportKind.Crosstab:
                    CheckGrouping(table, report.RowField, report.RowInterval, "row field", Add);
                    CheckGrouping(table, report.ColumnField, report.ColumnInterval, "column field", Add);
                    CheckAggregate(table, report.ValueField, report.Function, Add);
                    break;

                case ReportKind.Chart:
                    CheckGrouping(table, report.RowField, report.RowInterval, "category field", Add);
                    if (report.ColumnField.Trim().Length > 0)
                    {
                        CheckGrouping(table, report.ColumnField, report.ColumnInterval, "series field", Add);
                        if (report.ChartType is ChartType.Pie or ChartType.Doughnut)
                        {
                            Add("a pie or doughnut chart has one series; clear the series field.");
                        }

                        if (report.ChartOrder == ChartOrder.ValueDescending)
                        {
                            Add("a chart with a series field is ordered by category; choose the category order.");
                        }
                    }

                    CheckAggregate(table, report.ValueField, report.Function, Add);
                    if (report.Function is AggregateFunction.Min or AggregateFunction.Max
                        && table.Fields.FirstOrDefault(f => Same(f.Name, report.ValueField)) is { } plotted && !IsNumeric(plotted.Type))
                    {
                        Add($"a chart plots numbers, but the {report.Function.ToString().ToLowerInvariant()} of '{plotted.Name}' is not a number.");
                    }

                    break;

                case ReportKind.Calendar:
                    if (CheckField(table, report.DateField, "date field", Add) is { } date && !IsDateField(date))
                    {
                        Add($"the date field '{date.Name}' is not a date.");
                    }

                    if (report.EndDateField.Trim().Length > 0
                        && CheckField(table, report.EndDateField, "end date field", Add) is { } end && !IsDateField(end))
                    {
                        Add($"the end date field '{end.Name}' is not a date.");
                    }

                    if (report.TitleField.Trim().Length > 0)
                    {
                        CheckField(table, report.TitleField, "title field", Add);
                    }

                    break;
            }
        }

        return problems;
    }

    private static void CheckDashboard(ReportInfo report, List<ReportInfo> generated, Action<string> add)
    {
        if (report.Items.Count == 0)
        {
            add("add at least one report to the dashboard.");
        }

        foreach (var item in report.Items)
        {
            var target = generated.FirstOrDefault(r => Same(r.Name, item.Report));
            if (target is null)
            {
                add($"the dashboard shows '{item.Report}', which is not a generated report.");
            }
            else if (target.Kind == ReportKind.Dashboard)
            {
                add($"a dashboard cannot show another dashboard ('{target.Name}').");
            }

            if (item.Width is < 1 or > 12)
            {
                add($"the width of '{item.Report}' must be between 1 and 12 columns.");
            }
        }
    }

    /// <summary>The field, or null after adding a problem when it is missing, secret or binary.</summary>
    private static FieldInfo? CheckField(TableInfo table, string name, string what, Action<string> add)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            add($"choose the {what}.");
            return null;
        }

        var field = table.Fields.FirstOrDefault(f => Same(f.Name, name));
        if (field is null || field.IsSecret)
        {
            add($"the {what} '{name}' is not a field of '{table.Name}'.");
            return null;
        }

        if (field.Type == FieldType.Binary)
        {
            add($"the {what} '{field.Name}' holds binary data, which a report cannot show.");
            return null;
        }

        return field;
    }

    private static void CheckGrouping(TableInfo table, string name, DateInterval interval, string what, Action<string> add)
    {
        if (CheckField(table, name, what, add) is { } field && interval != DateInterval.None && !IsDateField(field))
        {
            add($"the {what} '{field.Name}' is not a date, so it cannot be grouped by {interval.ToString().ToLowerInvariant()}.");
        }
    }

    private static void CheckAggregate(TableInfo table, string name, AggregateFunction function, Action<string> add)
    {
        if (function == AggregateFunction.Count && string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (CheckField(table, name, "value field", add) is { } field
            && function is AggregateFunction.Sum or AggregateFunction.Average && !IsNumeric(field.Type))
        {
            add($"{function.ToString().ToLowerInvariant()} needs a number field, but '{field.Name}' is {field.Type}.");
        }
    }
}
