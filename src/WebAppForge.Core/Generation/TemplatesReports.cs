using System.Text;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

// Report pages: one Reports/{Name}Report.cs per generated report, at /reports/{route}. Aggregation happens in SQL (COUNT, SUM,
// MIN and MAX per group) and the totals are combined by Web/ReportKit.cs, so averages of subtotals stay exact.
internal static partial class Templates
{
    /// <summary>The generated reports, set by <see cref="ProjectGenerator"/> for one Generate call.</summary>
    [ThreadStatic]
    private static IReadOnlyList<ReportInfo>? _reports;

    internal static IReadOnlyList<ReportInfo> Reports
    {
        get => _reports ?? [];
        set => _reports = value;
    }

    private const string ReportDeniedMessage = "You do not have permission to view this report.";

    internal static string ReportClass(ReportInfo report) => Identifier.Pascal(report.Name) + "Report";

    internal static string ReportRoute(ReportInfo report) => "/reports/" + Identifier.Route(report.Name);

    private static string ReportCaption(ReportInfo report) => string.IsNullOrEmpty(report.Caption) ? report.Name : report.Caption;

    private static string ReportText(ReportInfo report) => Localized
        ? $"Strings.T({Lit(LocalizationKeys.Report(report.Name))}, {Lit(ReportCaption(report))})"
        : Lit(ReportCaption(report));

    /// <summary>Reports listed on the home page and in the navigation menu.</summary>
    private static List<ReportInfo> MenuReports() => Reports.Where(r => r.ShowInMenu).ToList();

    private static FieldInfo ReportField(TableInfo table, string name) =>
        table.Fields.First(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static FieldInfo? OptionalReportField(TableInfo table, string name) =>
        name.Trim().Length == 0 ? null : ReportField(table, name);

    /// <summary>
    /// SQL expression of a group value of <paramref name="table"/>: the leading part of an ISO date text for a date interval, the display
    /// text of a lookup or of a static option when the field has one, else the column.
    /// </summary>
    private static string GroupSql(TableInfo table, FieldInfo field, DateInterval interval)
    {
        var column = QuoteIdent(table.Name) + "." + QuoteIdent(field.Name);
        if (ReportValidator.IsDate(field.Type) && interval != DateInterval.None)
        {
            return interval switch
            {
                DateInterval.Year => $"substr({column}, 1, 4)",
                DateInterval.Quarter => $"substr({column}, 1, 4) || '-Q' || ((CAST(substr({column}, 6, 2) AS INTEGER) + 2) / 3)",
                DateInterval.Month => $"substr({column}, 1, 7)",
                _ => $"substr({column}, 1, 10)",
            };
        }

        if (ReportLookup(field) is { } lookup)
        {
            return $"COALESCE((SELECT l.{QuoteIdent(lookup.Display.Name)} FROM {QuoteIdent(lookup.Table.Name)} AS l WHERE l.{QuoteIdent(lookup.Key.Name)} = {column} LIMIT 1), {column})";
        }

        var options = field.Ui.Options.Where(o => o.Text.Length > 0).ToList();
        if (options.Count > 0)
        {
            static string Text(string value) => "'" + value.Replace("'", "''") + "'";
            return $"CASE CAST({column} AS TEXT) " + string.Join(" ", options.Select(o => $"WHEN {Text(o.Value)} THEN {Text(o.Text)}")) + $" ELSE {column} END";
        }

        return column;
    }

    /// <summary>The lookup of a field when its table, key and display field are part of the generated site and the display is not secret.</summary>
    private static (TableInfo Table, FieldInfo Key, FieldInfo Display)? ReportLookup(FieldInfo field)
    {
        if (field.Ui.Lookup is not { ShowDisplayInView: true } lookup)
        {
            return null;
        }

        var table = MdTables.FirstOrDefault(t => string.Equals(t.Name, lookup.Table.Trim(), StringComparison.OrdinalIgnoreCase));
        var key = table?.Fields.FirstOrDefault(f => string.Equals(f.Name, lookup.KeyField.Trim(), StringComparison.OrdinalIgnoreCase));
        var display = table?.Fields.FirstOrDefault(f => string.Equals(f.Name, lookup.DisplayField.Trim(), StringComparison.OrdinalIgnoreCase));
        return table is null || key is null || display is null || display.IsSecret ? null : (table, key, display);
    }

    /// <summary>True when the group value is shown as Yes or No: a boolean column without a lookup or options.</summary>
    private static bool BooleanGroup(FieldInfo field, DateInterval interval) =>
        IsBooleanField(field) && interval == DateInterval.None && ReportLookup(field) is null && field.Ui.Options.Count == 0;

    /// <summary>The four columns ReportKit.Partial reads: COUNT, SUM, MIN and MAX. Counting rows uses the constant 1.</summary>
    private static string PartialSql(FieldInfo? field)
    {
        var value = field is null ? "1" : QuoteIdent(field.Name);
        return $"COUNT({value}), SUM({value}), MIN({value}), MAX({value})";
    }

    private static string FunctionConst(AggregateFunction function) => "ReportKit." + function switch
    {
        AggregateFunction.Sum => "Sum",
        AggregateFunction.Average => "Average",
        AggregateFunction.Min => "Min",
        AggregateFunction.Max => "Max",
        _ => "Count",
    };

    private static string FunctionId(AggregateFunction function) => "report" + function;

    /// <summary>C# expression of the heading of an aggregate: the custom caption, "Count", or e.g. "Sum of Freight".</summary>
    private static string MeasureCaption(ReportInfo report, int? index, string custom, AggregateFunction function, FieldInfo? field)
    {
        if (custom.Length > 0)
        {
            return Localized && index is { } i
                ? $"Strings.T({Lit(LocalizationKeys.ReportMeasure(report.Name, i))}, {Lit(custom)})"
                : Lit(custom);
        }

        if (field is null)
        {
            return Ui("reportCount");
        }

        return Localized
            ? $"Strings.F({Lit(UiStrings.KeyOf("reportOf"))}, {Lit(UiStrings.English("reportOf"))}, {Ui(FunctionId(function))}, {FieldText(field)})"
            : Lit(string.Format(UiStrings.English("reportOf"), UiStrings.English(FunctionId(function)), FieldCaption(field)));
    }

    private static string MeasureExpr(ReportInfo report, int? index, string custom, AggregateFunction function, FieldInfo? field) =>
        $"new ReportKit.Measure({MeasureCaption(report, index, custom, function, field)}, {FunctionConst(function)}, {(field?.Type == FieldType.Decimal ? "true" : "false")})";

    private static string Bool(bool value) => value ? "true" : "false";

    private static bool IsBooleanField(FieldInfo field) => field.Type == FieldType.Boolean;

    /// <summary>" WHERE ..." from the table filter, the report filter, the owner and <paramref name="extra"/>, or empty.</summary>
    private static string ReportWhere(TableInfo table, ReportInfo report, params string[] extra)
    {
        var clauses = new List<string>();
        if (table.Filter.Trim().Length > 0)
        {
            clauses.Add("(" + table.Filter + ")");
        }

        if (report.Filter.Trim().Length > 0)
        {
            clauses.Add("(" + report.Filter + ")");
        }

        if (IsOwned(table))
        {
            clauses.Add(OwnerCondition(table));
        }

        clauses.AddRange(extra);
        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    /// <summary>The level a user needs for the report: the stricter of its own and the source table's view level.</summary>
    private static int ReportLevel(ReportInfo report, TableInfo? table, bool levelSecured) =>
        levelSecured ? Math.Max(report.MinLevel, table?.ViewMinLevel ?? 0) : 0;

    /// <summary>Reports/{Name}Report.cs of one report. <paramref name="table"/> is the generated copy of its source table (null for a dashboard).</summary>
    public static string ReportPage(string ns, ReportInfo report, TableInfo? table, IReadOnlyList<(ReportInfo Report, TableInfo? Table)> all, bool levelSecured)
    {
        _localizedTable = table?.Name;
        var className = ReportClass(report);
        var level = ReportLevel(report, table, levelSecured);
        var dashboard = report.Kind == ReportKind.Dashboard;
        var sb = new StringBuilder();
        if (report.Kind == ReportKind.Calendar)
        {
            sb.AppendLine("using System.Globalization;");
        }

        if (dashboard)
        {
            sb.AppendLine("using System.Text;");
        }
        else
        {
            sb.AppendLine("using Microsoft.Data.Sqlite;");
        }

        if (level > 0 || (dashboard && levelSecured))
        {
            sb.AppendLine($"using {ns}.Security;");
        }

        if (Localized)
        {
            sb.AppendLine($"using {ns}.Localization;");
        }

        sb.AppendLine($"using {ns}.Web;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Reports;");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine($"    public const string Route = {Lit(ReportRoute(report))};");
        sb.AppendLine($"    public const int MinLevel = {level};");
        sb.AppendLine($"    private const string Description = {Lit(report.Description)};");
        sb.AppendLine();
        sb.AppendLine($"    public static string Caption => {ReportText(report)};");
        sb.AppendLine();
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{className}(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        var guard = level > 0 ? ".RequireLevel(MinLevel, " + Lit(ReportDeniedMessage) + ")" : string.Empty;
        // The handler takes HttpRequest: a lambda on HttpContext alone would bind to the RequestDelegate overload and drop the result.
        sb.AppendLine($"        app.MapGet(Route, async (HttpRequest request) => Html.Page(Caption, ReportKit.Page(Description, await Body(request.HttpContext, connectionString)))){guard};");
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine();

        switch (report.Kind)
        {
            case ReportKind.Summary:
                AppendSummary(sb, report, table!);
                break;
            case ReportKind.Crosstab:
                AppendCrosstab(sb, report, table!);
                break;
            case ReportKind.Chart:
                AppendChart(sb, report, table!);
                break;
            case ReportKind.Calendar:
                AppendCalendar(sb, report, table!);
                break;
            default:
                AppendDashboard(sb, report, all, levelSecured);
                break;
        }

        sb.AppendLine("}");
        _localizedTable = null;
        return sb.ToString();
    }

    /// <summary>The start of Body: open the connection, run Sql and bind the owner.</summary>
    private static void AppendQueryStart(StringBuilder sb, TableInfo table, params string[] parameters)
    {
        sb.AppendLine("    public static async Task<string> Body(HttpContext context, string connectionString)");
        sb.AppendLine("    {");
        foreach (var line in parameters)
        {
            sb.AppendLine("        " + line);
        }

        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine("        await using var command = connection.CreateCommand();");
        sb.AppendLine("        command.CommandText = Sql;");
        if (IsOwned(table))
        {
            sb.AppendLine("        command.Parameters.AddWithValue(\"@owner\", context.User.Identity?.Name ?? string.Empty);");
        }
    }

    private static void AppendSummary(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var groups = report.Groups.Select(g => (Field: ReportField(table, g.Field), g.Interval, g.Descending)).ToList();
        var aggregates = report.Aggregates.Count > 0
            ? report.Aggregates.Select(a => (Field: OptionalReportField(table, a.Field), a.Function, a.Caption)).ToList()
            : [(Field: (FieldInfo?)null, Function: AggregateFunction.Count, Caption: string.Empty)];
        var details = report.ShowDetails
            ? (report.DetailColumns.Count > 0
                ? report.DetailColumns.Select(c => ReportField(table, c)).ToList()
                : table.Fields.Where(f => f.ShowInList && !f.IsSecret && f.Type != FieldType.Binary).ToList())
            : null;

        var select = groups.Select(g => GroupSql(table, g.Field, g.Interval)).ToList();
        string sql;
        if (details is null)
        {
            select.AddRange(aggregates.Select(a => PartialSql(a.Field)));
            sql = "SELECT " + string.Join(", ", select) + " FROM " + QuoteIdent(table.Name) + ReportWhere(table, report);
            if (groups.Count > 0)
            {
                sql += " GROUP BY " + string.Join(", ", groups.Select((_, i) => (i + 1).ToString()))
                    + " ORDER BY " + string.Join(", ", groups.Select((g, i) => (i + 1) + (g.Descending ? " DESC" : string.Empty)));
            }
        }
        else
        {
            select.AddRange(aggregates.Select(a => a.Field is null ? "1" : QuoteIdent(a.Field.Name)));
            select.AddRange(details.Select(f => QuoteIdent(f.Name)));
            var order = groups.Select((g, i) => (i + 1) + (g.Descending ? " DESC" : string.Empty)).ToList();
            order.AddRange(table.Sort.Count > 0
                ? table.Sort.Select(s => QuoteIdent(s.Field) + (s.Descending ? " DESC" : string.Empty))
                : table.PrimaryKeys.Select(k => QuoteIdent(k.Name)));
            sql = "SELECT " + string.Join(", ", select) + " FROM " + QuoteIdent(table.Name) + ReportWhere(table, report)
                + (order.Count > 0 ? " ORDER BY " + string.Join(", ", order) : string.Empty);
        }

        sb.AppendLine($"    private const string Sql = {Lit(sql)};");
        sb.AppendLine($"    private static readonly bool[] BooleanGroups = {{ {string.Join(", ", groups.Select(g => Bool(BooleanGroup(g.Field, g.Interval))))} }};");
        sb.AppendLine();
        AppendQueryStart(sb, table);
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine($"        var groups = new string[] {{ {string.Join(", ", groups.Select(g => FieldText(g.Field)))} }};");
        sb.AppendLine("        var measures = new[]");
        sb.AppendLine("        {");
        for (var i = 0; i < aggregates.Count; i++)
        {
            var a = aggregates[i];
            sb.AppendLine($"            {MeasureExpr(report, i, a.Caption, a.Function, a.Field)},");
        }

        sb.AppendLine("        };");
        sb.AppendLine(details is null
            ? "        string[]? details = null;"
            : $"        string[]? details = {{ {string.Join(", ", details.Select(FieldText))} }};");
        sb.AppendLine($"        return ReportKit.Summary(groups, BooleanGroups, measures, details, {Bool(report.ShowGrandTotal)}, Rows(reader));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static IEnumerable<ReportKit.SummaryRow> Rows(SqliteDataReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        while (reader.Read())");
        sb.AppendLine("        {");
        var keys = string.Join(", ", groups.Select((_, i) => $"ReportKit.Read(reader, {i})"));
        var n = groups.Count;
        var values = details is null
            ? string.Join(", ", aggregates.Select((_, j) => $"ReportKit.Partial(reader, {n + j * 4})"))
            : string.Join(", ", aggregates.Select((_, j) => $"ReportKit.Single(ReportKit.Read(reader, {n + j}))"));
        var cells = details is null
            ? "null"
            : "new[] { " + string.Join(", ", details.Select((f, j) => $"ReportKit.CellText(ReportKit.Read(reader, {n + aggregates.Count + j}), {Bool(IsBooleanField(f))})")) + " }";
        sb.AppendLine($"            yield return new ReportKit.SummaryRow(new object?[] {{ {keys} }}, new[] {{ {values} }}, {cells});");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    /// <summary>SQL of a crosstab or chart: row (category) and optional column (series) values with the partial aggregate.</summary>
    private static string GridSql(ReportInfo report, TableInfo table, FieldInfo row, FieldInfo? column, FieldInfo? value)
    {
        var select = new List<string> { GroupSql(table, row, report.RowInterval) };
        select.Add(column is null ? "NULL" : GroupSql(table, column, report.ColumnInterval));
        select.Add(PartialSql(value));
        var by = column is null ? "1" : "1, 2";
        return "SELECT " + string.Join(", ", select) + " FROM " + QuoteIdent(table.Name) + ReportWhere(table, report) + " GROUP BY " + by + " ORDER BY " + by;
    }

    private static void AppendCells(StringBuilder sb)
    {
        sb.AppendLine("    private static IEnumerable<ReportKit.Cell> Cells(SqliteDataReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        while (reader.Read())");
        sb.AppendLine("        {");
        sb.AppendLine("            yield return new ReportKit.Cell(ReportKit.Read(reader, 0), ReportKit.Read(reader, 1), ReportKit.Partial(reader, 2));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void AppendCrosstab(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var row = ReportField(table, report.RowField);
        var column = ReportField(table, report.ColumnField);
        var value = OptionalReportField(table, report.ValueField);
        sb.AppendLine($"    private const string Sql = {Lit(GridSql(report, table, row, column, value))};");
        sb.AppendLine();
        AppendQueryStart(sb, table);
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine($"        return ReportKit.Crosstab({FieldText(row)}, {Bool(BooleanGroup(row, report.RowInterval))}, {Bool(BooleanGroup(column, report.ColumnInterval))}, {MeasureExpr(report, null, string.Empty, report.Function, value)}, Cells(reader), {Math.Max(1, report.MaxColumns)});");
        sb.AppendLine("    }");
        sb.AppendLine();
        AppendCells(sb);
    }

    private static string ChartKind(ChartType type) => type switch
    {
        ChartType.HorizontalBar => "hbar",
        ChartType.Line => "line",
        ChartType.Area => "area",
        ChartType.Pie => "pie",
        ChartType.Doughnut => "doughnut",
        _ => "bar",
    };

    private static void AppendChart(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var row = ReportField(table, report.RowField);
        var column = OptionalReportField(table, report.ColumnField);
        var value = OptionalReportField(table, report.ValueField);
        sb.AppendLine($"    private const string Sql = {Lit(GridSql(report, table, row, column, value))};");
        sb.AppendLine();
        AppendQueryStart(sb, table);
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        var columnBoolean = column is not null && BooleanGroup(column, report.ColumnInterval);
        sb.AppendLine($"        return ReportKit.Chart({Lit("chart-" + Identifier.Route(report.Name))}, {Lit(ChartKind(report.ChartType))}, Caption, {FieldText(row)}, "
            + $"{Bool(BooleanGroup(row, report.RowInterval))}, {Bool(columnBoolean)}, {Bool(column is not null)}, "
            + $"{MeasureExpr(report, null, string.Empty, report.Function, value)}, Cells(reader), {Math.Max(1, report.MaxColumns)}, "
            + $"{Bool(report.ChartOrder == ChartOrder.ValueDescending)}, {Math.Max(0, report.TopCount)}, {Bool(report.ShowDataTable)});");
        sb.AppendLine("    }");
        sb.AppendLine();
        AppendCells(sb);
    }

    /// <summary>The text of a calendar entry: the title field, else the first visible text field that is not a key, else the first key.</summary>
    private static FieldInfo? CalendarTitle(ReportInfo report, TableInfo table) =>
        OptionalReportField(table, report.TitleField)
        ?? table.Fields.FirstOrDefault(f => f.Type == FieldType.Text && !f.IsPrimaryKey && !f.IsSecret && f.ShowInList)
        ?? table.PrimaryKeys.FirstOrDefault();

    private static void AppendCalendar(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var date = ReportField(table, report.DateField);
        var end = OptionalReportField(table, report.EndDateField);
        var title = CalendarTitle(report, table);
        var link = report.LinkToView && table.Pages.View && table.PrimaryKeys.Count > 0;
        var keys = link ? table.PrimaryKeys.ToList() : [];
        var dateSql = QuoteIdent(date.Name);
        var endSql = end is null ? dateSql : $"COALESCE(NULLIF({QuoteIdent(end.Name)}, ''), {dateSql})";
        var select = keys.Select(k => QuoteIdent(k.Name)).ToList();
        select.Add(dateSql);
        select.Add(end is null ? "NULL" : QuoteIdent(end.Name));
        select.Add(title is null ? "NULL" : QuoteIdent(title.Name));
        var sql = "SELECT " + string.Join(", ", select) + " FROM " + QuoteIdent(table.Name)
            + ReportWhere(table, report, $"{dateSql} IS NOT NULL", $"substr({dateSql}, 1, 10) <= @last", $"substr({endSql}, 1, 10) >= @first")
            + $" ORDER BY {dateSql} LIMIT {ReportKitMaxCalendarEntries}";
        sb.AppendLine($"    private const string Sql = {Lit(sql)};");
        sb.AppendLine($"    private const bool WeekStartsMonday = {Bool(report.WeekStartsMonday)};");
        sb.AppendLine();
        AppendQueryStart(sb, table,
            "var month = ReportKit.MonthOf(context.Request);",
            "var (first, last) = ReportKit.CalendarRange(month, WeekStartsMonday);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@first\", first);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@last\", last);");
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine("        return ReportKit.Calendar(Route, month, WeekStartsMonday, Entries(reader));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static string? Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);");
        sb.AppendLine();
        sb.AppendLine("    private static IEnumerable<ReportKit.CalendarEntry> Entries(SqliteDataReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        while (reader.Read())");
        sb.AppendLine("        {");
        var k = keys.Count;
        var href = link
            ? Lit("/" + Identifier.Route(table.Name) + "/view/") + " + " + string.Join(" + \"/\" + ", keys.Select((_, i) => $"Uri.EscapeDataString(Text(ReportKit.Read(reader, {i})) ?? string.Empty)"))
            : "null";
        var titleExpr = title is not null && IsBooleanField(title)
            ? $"ReportKit.PlainLabel(ReportKit.Read(reader, {k + 2}), true, ReportKit.Text())"
            : $"Text(ReportKit.Read(reader, {k + 2})) ?? string.Empty";
        sb.AppendLine($"            yield return new ReportKit.CalendarEntry(Text(ReportKit.Read(reader, {k})), Text(ReportKit.Read(reader, {k + 1})), {titleExpr}, {href});");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    /// <summary>Same value as ReportKit.MaxCalendarEntries in the generated runtime.</summary>
    private const int ReportKitMaxCalendarEntries = 5000;

    private static void AppendDashboard(StringBuilder sb, ReportInfo report, IReadOnlyList<(ReportInfo Report, TableInfo? Table)> all, bool levelSecured)
    {
        sb.AppendLine("    public static async Task<string> Body(HttpContext context, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        var sb = new StringBuilder(\"<div class=\\\"row g-4 report-dashboard\\\">\");");
        foreach (var item in report.Items)
        {
            var target = all.First(r => string.Equals(r.Report.Name.Trim(), item.Report.Trim(), StringComparison.OrdinalIgnoreCase));
            var other = ReportClass(target.Report);
            var width = Math.Clamp(item.Width, 1, 12);
            var indent = "        ";
            if (levelSecured && ReportLevel(target.Report, target.Table, levelSecured) > 0)
            {
                // Items the user may not open are left out of the dashboard.
                sb.AppendLine($"        if (Permissions.Allowed(context.User, {other}.MinLevel))");
                sb.AppendLine("        {");
                indent = "            ";
            }

            sb.AppendLine($"{indent}sb.Append(\"<div class=\\\"col-12 col-lg-{width}\\\"><div class=\\\"card h-100\\\"><div class=\\\"card-header\\\"><a href=\\\"\").Append({other}.Route).Append(\"\\\">\")");
            sb.AppendLine($"{indent}    .Append(Html.Encode({other}.Caption)).Append(\"</a></div><div class=\\\"card-body\\\">\").Append(await {other}.Body(context, connectionString)).Append(\"</div></div></div>\");");
            if (indent.Length > 8)
            {
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return sb.Append(\"</div>\").ToString();");
        sb.AppendLine("    }");
    }
}
