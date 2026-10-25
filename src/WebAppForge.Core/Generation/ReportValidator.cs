using WebAppForge.Core.Model;
using M = WebAppForge.Core.Localization.CoreMessages;

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

    /// <summary>The name of an aggregate function in the message language, lower case for the middle of a sentence.</summary>
    internal static string FunctionName(AggregateFunction function) => M.Text("Function_" + function);

    private static string LowerFunctionName(AggregateFunction function) => FunctionName(function).ToLower(M.Current);

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
            var label = report.Name.Trim().Length == 0 ? M.Text("Report_NoName") : report.Name.Trim();
            void Add(string text) => problems.Add(M.Format("Report_Problem", label, text));

            if (Identifier.Route(report.Name).Length == 0)
            {
                Add(M.Text("Report_NameNeeded"));
                continue;
            }

            if (!routes.Add(Identifier.Route(report.Name)) || !classes.Add(Identifier.Pascal(report.Name)))
            {
                Add(M.Text("Report_NameTaken"));
            }

            if (report.Kind == ReportKind.Dashboard)
            {
                CheckDashboard(report, generated, Add);
                continue;
            }

            if (string.IsNullOrWhiteSpace(report.SourceTable))
            {
                Add(M.Text("Report_ChooseTable"));
                continue;
            }

            var table = tables.FirstOrDefault(t => Same(t.Name, report.SourceTable));
            if (table is null)
            {
                Add(M.Format("Report_TableNotGenerated", report.SourceTable));
                continue;
            }

            if (report.MaxColumns < 1)
            {
                Add(M.Text("Report_MaxColumns"));
            }

            if (report.TopCount < 0)
            {
                Add(M.Text("Report_TopCountNegative"));
            }

            if (report.DateFilterField.Trim().Length > 0)
            {
                if (report.Kind == ReportKind.Calendar)
                {
                    Add(M.Text("Report_CalendarDateRange"));
                }
                else if (CheckField(table, report.DateFilterField, "DateRange", Add) is { } range && !IsDateField(range))
                {
                    Add(M.Format("Report_NotADate", What("DateRange"), range.Name));
                }
            }

            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var filter in report.FilterFields)
            {
                if (CheckField(table, filter, "Filter", Add) is { } field && !filters.Add(field.Name))
                {
                    Add(M.Format("Report_FilterTwice", field.Name));
                }
            }

            switch (report.Kind)
            {
                case ReportKind.Summary:
                    foreach (var group in report.Groups)
                    {
                        CheckGrouping(table, group.Field, group.Interval, "Group", Add);
                    }

                    foreach (var aggregate in report.Aggregates)
                    {
                        CheckAggregate(table, aggregate.Field, aggregate.Function, Add);
                    }

                    foreach (var column in report.DetailColumns)
                    {
                        CheckField(table, column, "DetailColumn", Add);
                    }

                    break;

                case ReportKind.Crosstab:
                    CheckGrouping(table, report.RowField, report.RowInterval, "Row", Add);
                    CheckGrouping(table, report.ColumnField, report.ColumnInterval, "Column", Add);
                    CheckAggregate(table, report.ValueField, report.Function, Add);
                    break;

                case ReportKind.Chart:
                    CheckGrouping(table, report.RowField, report.RowInterval, "Category", Add);
                    if (report.ColumnField.Trim().Length > 0)
                    {
                        CheckGrouping(table, report.ColumnField, report.ColumnInterval, "Series", Add);
                        if (report.ChartType is ChartType.Pie or ChartType.Doughnut)
                        {
                            Add(M.Text("Report_PieOneSeries"));
                        }

                        if (report.ChartOrder == ChartOrder.ValueDescending)
                        {
                            Add(M.Text("Report_SeriesOrder"));
                        }
                    }

                    CheckAggregate(table, report.ValueField, report.Function, Add);
                    if (report.Function is AggregateFunction.Min or AggregateFunction.Max
                        && table.Fields.FirstOrDefault(f => Same(f.Name, report.ValueField)) is { } plotted && !IsNumeric(plotted.Type))
                    {
                        Add(M.Format("Report_ChartNotNumber", LowerFunctionName(report.Function), plotted.Name));
                    }

                    break;

                case ReportKind.Calendar:
                    if (CheckField(table, report.DateField, "Date", Add) is { } date && !IsDateField(date))
                    {
                        Add(M.Format("Report_NotADate", What("Date"), date.Name));
                    }

                    if (report.EndDateField.Trim().Length > 0
                        && CheckField(table, report.EndDateField, "EndDate", Add) is { } end && !IsDateField(end))
                    {
                        Add(M.Format("Report_NotADate", What("EndDate"), end.Name));
                    }

                    if (report.TitleField.Trim().Length > 0)
                    {
                        CheckField(table, report.TitleField, "Title", Add);
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
            add(M.Text("Report_DashboardEmpty"));
        }

        foreach (var item in report.Items)
        {
            var target = generated.FirstOrDefault(r => Same(r.Name, item.Report));
            if (target is null)
            {
                add(M.Format("Report_DashboardMissing", item.Report));
            }
            else if (target.Kind == ReportKind.Dashboard)
            {
                add(M.Format("Report_DashboardNested", target.Name));
            }

            if (item.Width is < 1 or > 12)
            {
                add(M.Format("Report_DashboardWidth", item.Report));
            }
        }
    }

    /// <summary>The name of a report setting that holds a field ("date range field"), from its ReportField_ key.</summary>
    private static string What(string what) => M.Text("ReportField_" + what);

    /// <summary>The field, or null after adding a problem when it is missing, secret or binary.</summary>
    private static FieldInfo? CheckField(TableInfo table, string name, string what, Action<string> add)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            add(M.Format("Report_ChooseField", What(what)));
            return null;
        }

        var field = table.Fields.FirstOrDefault(f => Same(f.Name, name));
        if (field is null || field.IsSecret)
        {
            add(M.Format("Report_NotAField", What(what), name, table.Name));
            return null;
        }

        if (field.Type == FieldType.Binary)
        {
            add(M.Format("Report_Binary", What(what), field.Name));
            return null;
        }

        return field;
    }

    private static void CheckGrouping(TableInfo table, string name, DateInterval interval, string what, Action<string> add)
    {
        if (CheckField(table, name, what, add) is { } field && interval != DateInterval.None && !IsDateField(field))
        {
            add(M.Format("Report_GroupNotDate", What(what), field.Name, M.Text("Interval_" + interval)));
        }
    }

    private static void CheckAggregate(TableInfo table, string name, AggregateFunction function, Action<string> add)
    {
        if (function == AggregateFunction.Count && string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (CheckField(table, name, "Value", add) is { } field
            && function is AggregateFunction.Sum or AggregateFunction.Average && !IsNumeric(field.Type))
        {
            add(M.Format("Report_AggregateNeedsNumber", LowerFunctionName(function), field.Name, field.Type));
        }
    }
}
