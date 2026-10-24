using System.Text;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

// Report pages: one Reports/{Name}Report.cs per generated report, at /reports/{route}. Aggregation happens in SQL (COUNT, SUM,
// MIN and MAX per group) and the totals are combined by Web/ReportKit.cs, so averages of subtotals stay exact.
// The WHERE clause is built per request from the static part (table filter, report filter, owner) and the filters the visitor
// chose (date range, pick lists, and the keys of a drill-down), all bound as parameters.
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

    /// <summary>Rows per page of the drill-down page.</summary>
    private const int DrillPageSize = 50;

    internal static string ReportClass(ReportInfo report) => Identifier.Pascal(report.Name) + "Report";

    internal static string ReportRoute(ReportInfo report) => "/reports/" + Identifier.Route(report.Name);

    private static string ReportCaption(ReportInfo report) => string.IsNullOrEmpty(report.Caption) ? report.Name : report.Caption;

    private static string ReportText(ReportInfo report) => Localized
        ? $"Strings.T({Lit(LocalizationKeys.Report(report.Name))}, {Lit(ReportCaption(report))})"
        : Lit(ReportCaption(report));

    /// <summary>Reports listed on the home page and in the navigation menu.</summary>
    private static List<ReportInfo> MenuReports() => Reports.Where(r => r.ShowInMenu).ToList();

    internal static FieldInfo ReportField(TableInfo table, string name) =>
        table.Fields.First(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    internal static FieldInfo? OptionalReportField(TableInfo table, string name) =>
        name.Trim().Length == 0 ? null : ReportField(table, name);

    private static string ColumnSql(TableInfo table, FieldInfo field) => QuoteIdent(table.Name) + "." + QuoteIdent(field.Name);

    /// <summary>
    /// SQL expression of a date field as ISO text (yyyy-MM-dd HH:mm:ss or yyyy-MM-dd). Number columns that hold Unix time or Julian day
    /// numbers are converted with the dialect's date functions (SQLite's datetime()).
    /// </summary>
    internal static string DateTextSql(TableInfo table, FieldInfo field) => Db.DateText(ColumnSql(table, field), field.Type, field.DateStorage);

    /// <summary>
    /// SQL expression of a group value of <paramref name="table"/>: the leading part of the ISO date text for a date interval, the display
    /// text of a lookup or of a static option when the field has one, else the column.
    /// </summary>
    internal static string GroupSql(TableInfo table, FieldInfo field, DateInterval interval)
    {
        var column = ColumnSql(table, field);
        if (ReportValidator.IsDateField(field) && interval != DateInterval.None)
        {
            var date = DateTextSql(table, field);
            return interval switch
            {
                DateInterval.Year => Db.Substring(date, 1, 4),
                DateInterval.Quarter => Db.Concat(Db.Substring(date, 1, 4), Db.Text("-Q"), $"({Db.IntegerDivide($"({Db.IntegerCast(Db.Substring(date, 6, 2))} + 2)", "3")})"),
                DateInterval.Month => Db.Substring(date, 1, 7),
                _ => Db.Substring(date, 1, 10),
            };
        }

        if (ReportLookup(field) is { } lookup)
        {
            return $"COALESCE((SELECT {Db.Top(1)}l.{QuoteIdent(lookup.Display.Name)} FROM {QuoteIdent(lookup.Table.Name)} AS l WHERE l.{QuoteIdent(lookup.Key.Name)} = {column}{Db.Limit(1)}), {Db.TextFallback(column)})";
        }

        var options = field.Ui.Options.Where(o => o.Text.Length > 0).ToList();
        if (options.Count > 0)
        {
            return $"CASE {Db.TextCast(column)} " + string.Join(" ", options.Select(o => $"WHEN {Db.Text(o.Value)} THEN {Db.Text(o.Text)}")) + $" ELSE {Db.TextFallback(column)} END";
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
    /// <summary>
    /// The outer and inner select lists of a grouped query for a dialect that cannot group by position or by a subquery (SQL Server).
    /// The inner query names the group expressions g0, g1, ... and the aggregated columns v0, v1, ...; the outer one groups by the names
    /// and returns the same columns as the SQLite query: the groups, then COUNT, SUM, MIN and MAX of each value.
    /// </summary>
    private static (List<string> Outer, List<string> Inner) DerivedGroups(List<string> groups, List<FieldInfo?> values)
    {
        // A constant NULL group (a chart without series) stays a constant, so it needs no GROUP BY entry.
        var outer = groups.Select((g, i) => g == "NULL" ? "NULL" : "g" + i).ToList();
        var inner = groups.Select((g, i) => (g, i)).Where(x => x.g != "NULL").Select(x => x.g + " AS g" + x.i).ToList();
        for (var j = 0; j < values.Count; j++)
        {
            inner.Add((values[j] is { } field ? QuoteIdent(field.Name) : "1") + " AS v" + j);
            outer.Add($"COUNT(v{j}), SUM(v{j}), MIN(v{j}), MAX(v{j})");
        }

        return (outer, inner);
    }

    /// <summary>The select list joined with commas, except before a "FROM (SELECT ..." part, which follows with a space.</summary>
    private static string JoinSelect(List<string> select) =>
        select.Count > 0 && select[^1].StartsWith("FROM (", StringComparison.Ordinal)
            ? string.Join(", ", select[..^1]) + " " + select[^1]
            : string.Join(", ", select);

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

    /// <summary>The static part of the WHERE clause (without the keyword): the table filter, the report filter and the owner.</summary>
    private static string StaticWhere(TableInfo table, ReportInfo report)
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

        return string.Join(" AND ", clauses);
    }

    /// <summary>The level a user needs for the report: the stricter of its own and the source table's view level.</summary>
    private static int ReportLevel(ReportInfo report, TableInfo? table, bool levelSecured) =>
        levelSecured ? Math.Max(report.MinLevel, table?.ViewMinLevel ?? 0) : 0;

    /// <summary>True when the report kind offers the From/To date range (summary, crosstab and chart).</summary>
    private static bool UsesDateRange(ReportInfo report) =>
        report.Kind is ReportKind.Summary or ReportKind.Crosstab or ReportKind.Chart && report.DateFilterField.Trim().Length > 0;

    /// <summary>True when the report links its numbers to a page of the rows behind them. A summary that lists its rows needs no link.</summary>
    private static bool UsesDrillDown(ReportInfo report) =>
        report.DrillDown && (report.Kind is ReportKind.Crosstab or ReportKind.Chart || (report.Kind == ReportKind.Summary && !report.ShowDetails));

    /// <summary>The group expressions a drill-down link can name (k0, k1, ...) with their captions.</summary>
    private static List<(string Sql, FieldInfo Field)> DrillKeys(ReportInfo report, TableInfo table)
    {
        var keys = new List<(string, FieldInfo)>();
        if (report.Kind == ReportKind.Summary)
        {
            foreach (var group in report.Groups)
            {
                var field = ReportField(table, group.Field);
                keys.Add((GroupSql(table, field, group.Interval), field));
            }
        }
        else
        {
            var row = ReportField(table, report.RowField);
            keys.Add((GroupSql(table, row, report.RowInterval), row));
            if (OptionalReportField(table, report.ColumnField) is { } column)
            {
                keys.Add((GroupSql(table, column, report.ColumnInterval), column));
            }
        }

        return keys;
    }

    /// <summary>Reports/{Name}Report.cs of one report. <paramref name="table"/> is the generated copy of its source table (null for a dashboard).</summary>
    public static string ReportPage(string ns, ReportInfo report, TableInfo? table, IReadOnlyList<(ReportInfo Report, TableInfo? Table)> all, bool levelSecured)
    {
        _localizedTable = table?.Name;
        var className = ReportClass(report);
        var level = ReportLevel(report, table, levelSecured);
        var dashboard = report.Kind == ReportKind.Dashboard;
        var drill = !dashboard && UsesDrillDown(report);
        var sb = new StringBuilder();
        if (report.Kind == ReportKind.Calendar || drill)
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
        sb.AppendLine(Localized && report.Description.Trim().Length > 0
            ? $"    private static string Description => {ProjectText(LocalizationKeys.ReportDescription(report.Name), report.Description.Trim())};"
            : $"    private const string Description = {Lit(report.Description)};");
        if (table is not null)
        {
            sb.AppendLine($"    private const string From = {Lit(" FROM " + QuoteIdent(table.Name))};");
            sb.AppendLine($"    private const string StaticWhere = {Lit(StaticWhere(table, report))};");
            var dateFilter = UsesDateRange(report) ? Db.Substring(DateTextSql(table, ReportField(table, report.DateFilterField)), 1, 10) : string.Empty;
            sb.AppendLine($"    private const string DateFilter = {Lit(dateFilter)};");
            if (drill)
            {
                sb.AppendLine($"    private static readonly string[] DrillKeys = {StringArray(DrillKeys(report, table).Select(k => k.Sql))};");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"    public static string Caption => {ReportText(report)};");
        sb.AppendLine();
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{className}(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        var guard = level > 0 ? ".RequireLevel(MinLevel, " + Lit(ReportDeniedMessage) + ")" : string.Empty;
        // The handlers take HttpRequest: a lambda on HttpContext alone would bind to the RequestDelegate overload and drop the result.
        var form = table is null ? string.Empty : "await Form(request.HttpContext, connectionString) + ";
        sb.AppendLine($"        app.MapGet(Route, async (HttpRequest request) => Html.Page(Caption, ReportKit.Page(Description, {form}await Body(request.HttpContext, connectionString)))){guard};");
        if (drill)
        {
            sb.AppendLine($"        app.MapGet(Route + \"/rows\", async (HttpRequest request) => Html.Page(Caption, await Rows(request.HttpContext, connectionString))){guard};");
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (table is not null)
        {
            AppendFilters(sb, report, table);
        }

        switch (report.Kind)
        {
            case ReportKind.Summary:
                AppendSummary(sb, report, table!, drill);
                break;
            case ReportKind.Crosstab:
                AppendCrosstab(sb, report, table!, drill);
                break;
            case ReportKind.Chart:
                AppendChart(sb, report, table!, drill);
                break;
            case ReportKind.Calendar:
                AppendCalendar(sb, report, table!);
                break;
            default:
                AppendDashboard(sb, report, all, levelSecured);
                break;
        }

        if (drill)
        {
            AppendRows(sb, report, table!);
        }

        sb.AppendLine("}");
        _localizedTable = null;
        return sb.ToString();
    }

    /// <summary>
    /// Filters() lists the pick-list filters with the query that loads their choices; Form() draws the filter form above the report.
    /// The choices honor the static WHERE clause, so a user only sees values of rows they may read.
    /// </summary>
    private static void AppendFilters(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var staticWhere = StaticWhere(table, report);
        sb.AppendLine("    private static ReportKit.FilterField[] Filters() => new ReportKit.FilterField[]");
        sb.AppendLine("    {");
        foreach (var name in report.FilterFields)
        {
            var field = ReportField(table, name);
            var column = ColumnSql(table, field);
            var label = GroupSql(table, field, DateInterval.None);
            var where = (staticWhere.Length > 0 ? staticWhere + " AND " : string.Empty) + column + " IS NOT NULL";
            var options = $"SELECT DISTINCT {Db.Top(ReportKitMaxFilterOptions)}{Db.TextCast(column)}, {label} FROM {QuoteIdent(table.Name)} WHERE {where} ORDER BY 2{Db.Limit(ReportKitMaxFilterOptions)}";
            sb.AppendLine($"        new({Lit("f_" + field.Name)}, {FieldText(field)}, {Lit(Db.TextCast(column))}, {Lit(options)}, {Bool(BooleanGroup(field, DateInterval.None))}),");
        }

        sb.AppendLine("    };");
        sb.AppendLine();
        var dateCaption = UsesDateRange(report) ? FieldText(ReportField(table, report.DateFilterField)) : "null";
        sb.AppendLine("    private static async Task<string> Form(HttpContext context, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        var filters = Filters();");
        sb.AppendLine($"        if (filters.Length == 0 && DateFilter.Length == 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            return string.Empty;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine("        var choices = new List<List<(string Value, string Label)>>();");
        sb.AppendLine("        foreach (var filter in filters)");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var command = connection.CreateCommand();");
        sb.AppendLine("            command.CommandText = filter.OptionsSql;");
        AppendOwnerBinding(sb, table, "            ");
        sb.AppendLine("            choices.Add(await ReportKit.ChoicesAsync(command, filter));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        return ReportKit.FilterForm(context.Request, Route, {dateCaption}, filters, choices);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>Same value as ReportKit.MaxFilterOptions in the generated runtime.</summary>
    private const int ReportKitMaxFilterOptions = 500;

    private static void AppendOwnerBinding(StringBuilder sb, TableInfo table, string indent)
    {
        if (IsOwned(table))
        {
            sb.AppendLine($"{indent}command.Parameters.AddWithValue(\"@owner\", context.User.Identity?.Name ?? string.Empty);");
        }
    }

    /// <summary>The start of Body: open the connection and run Select with the WHERE clause of this request and <paramref name="tail"/>.</summary>
    private static void AppendQueryStart(StringBuilder sb, TableInfo table, string tail, string extra = "", params string[] parameters)
    {
        sb.AppendLine("    public static async Task<string> Body(HttpContext context, string connectionString)");
        sb.AppendLine("    {");
        foreach (var line in parameters)
        {
            sb.AppendLine("        " + line);
        }

        sb.AppendLine($"        var (where, arguments) = ReportKit.Where(context.Request, StaticWhere, DateFilter, Filters(), null{extra});");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine("        await using var command = connection.CreateCommand();");
        sb.AppendLine($"        command.CommandText = Select + From + where + {tail};");
        sb.AppendLine("        ReportKit.Bind(command, arguments);");
        AppendOwnerBinding(sb, table, "        ");
    }

    private static string DrillArg(bool drill) => drill ? "ReportKit.DrillBase(context.Request, Route)" : "null";

    private static void AppendSummary(StringBuilder sb, ReportInfo report, TableInfo table, bool drill)
    {
        var groups = report.Groups.Select(g => (Field: ReportField(table, g.Field), g.Interval, g.Descending)).ToList();
        var aggregates = report.Aggregates.Count > 0
            ? report.Aggregates.Select(a => (Field: OptionalReportField(table, a.Field), a.Function, a.Caption)).ToList()
            : [(Field: (FieldInfo?)null, Function: AggregateFunction.Count, Caption: string.Empty)];
        var details = report.ShowDetails ? DetailFields(report, table) : null;

        var select = groups.Select(g => GroupSql(table, g.Field, g.Interval)).ToList();
        string tail;
        if (details is null && groups.Count > 0 && !Db.GroupsByPosition)
        {
            var (outer, inner) = DerivedGroups(select, aggregates.Select(a => a.Field).ToList());
            select = [.. outer, "FROM (SELECT " + string.Join(", ", inner)];
            tail = ") AS r GROUP BY " + string.Join(", ", groups.Select((_, i) => "g" + i))
                + " ORDER BY " + string.Join(", ", groups.Select((g, i) => "g" + i + (g.Descending ? " DESC" : string.Empty)));
        }
        else if (details is null)
        {
            select.AddRange(aggregates.Select(a => PartialSql(a.Field)));
            tail = groups.Count == 0
                ? string.Empty
                : " GROUP BY " + string.Join(", ", groups.Select((_, i) => (i + 1).ToString()))
                    + " ORDER BY " + string.Join(", ", groups.Select((g, i) => (i + 1) + (g.Descending ? " DESC" : string.Empty)));
        }
        else
        {
            select.AddRange(aggregates.Select(a => a.Field is null ? "1" : QuoteIdent(a.Field.Name)));
            select.AddRange(details.Select(f => QuoteIdent(f.Name)));
            var order = groups.Select((g, i) => (i + 1) + (g.Descending ? " DESC" : string.Empty)).ToList();
            order.AddRange(TableOrder(table));
            tail = order.Count > 0 ? " ORDER BY " + string.Join(", ", order) : string.Empty;
        }

        sb.AppendLine($"    private const string Select = {Lit("SELECT " + JoinSelect(select))};");
        sb.AppendLine($"    private const string Tail = {Lit(tail)};");
        sb.AppendLine($"    private static readonly bool[] BooleanGroups = {{ {string.Join(", ", groups.Select(g => Bool(BooleanGroup(g.Field, g.Interval))))} }};");
        sb.AppendLine();
        AppendQueryStart(sb, table, "Tail");
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
        sb.AppendLine($"        return ReportKit.Summary(groups, BooleanGroups, measures, details, {Bool(report.ShowGrandTotal)}, Rows(reader), {DrillArg(drill)});");
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

    /// <summary>The fields listed for each row: the report's detail columns, else the fields shown in the table's list.</summary>
    private static List<FieldInfo> DetailFields(ReportInfo report, TableInfo table) =>
        report.DetailColumns.Count > 0
            ? report.DetailColumns.Select(c => ReportField(table, c)).ToList()
            : table.Fields.Where(f => f.ShowInList && !f.IsSecret && f.Type != FieldType.Binary).ToList();

    /// <summary>ORDER BY terms of the table: its sort fields, else its key.</summary>
    /// <remarks>SQL Server gets the columns qualified with the table, because the select list may name a key column twice.</remarks>
    private static IEnumerable<string> TableOrder(TableInfo table)
    {
        var prefix = Db.QualifiesOrder ? QuoteIdent(table.Name) + "." : string.Empty;
        return table.Sort.Count > 0
            ? table.Sort.Select(s => prefix + QuoteIdent(s.Field) + (s.Descending ? " DESC" : string.Empty))
            : table.PrimaryKeys.Select(k => prefix + QuoteIdent(k.Name));
    }

    /// <summary>Select and Tail of a crosstab or chart: row (category) and optional column (series) values with the partial aggregate.</summary>
    private static void AppendGridSql(StringBuilder sb, ReportInfo report, TableInfo table, FieldInfo row, FieldInfo? column, FieldInfo? value)
    {
        var select = new List<string> { GroupSql(table, row, report.RowInterval) };
        select.Add(column is null ? "NULL" : GroupSql(table, column, report.ColumnInterval));
        if (!Db.GroupsByPosition)
        {
            var (outer, inner) = DerivedGroups(select, [value]);
            var groupBy = column is null ? "g0" : "g0, g1";
            sb.AppendLine($"    private const string Select = {Lit("SELECT " + string.Join(", ", outer) + " FROM (SELECT " + string.Join(", ", inner))};");
            sb.AppendLine($"    private const string Tail = {Lit(") AS r GROUP BY " + groupBy + " ORDER BY " + groupBy)};");
            sb.AppendLine();
            return;
        }

        select.Add(PartialSql(value));
        var by = column is null ? "1" : "1, 2";
        sb.AppendLine($"    private const string Select = {Lit("SELECT " + string.Join(", ", select))};");
        sb.AppendLine($"    private const string Tail = {Lit(" GROUP BY " + by + " ORDER BY " + by)};");
        sb.AppendLine();
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

    private static void AppendCrosstab(StringBuilder sb, ReportInfo report, TableInfo table, bool drill)
    {
        var row = ReportField(table, report.RowField);
        var column = ReportField(table, report.ColumnField);
        var value = OptionalReportField(table, report.ValueField);
        AppendGridSql(sb, report, table, row, column, value);
        AppendQueryStart(sb, table, "Tail");
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine($"        return ReportKit.Crosstab({FieldText(row)}, {Bool(BooleanGroup(row, report.RowInterval))}, {Bool(BooleanGroup(column, report.ColumnInterval))}, {MeasureExpr(report, null, string.Empty, report.Function, value)}, Cells(reader), {Math.Max(1, report.MaxColumns)}, {DrillArg(drill)});");
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

    private static void AppendChart(StringBuilder sb, ReportInfo report, TableInfo table, bool drill)
    {
        var row = ReportField(table, report.RowField);
        var column = OptionalReportField(table, report.ColumnField);
        var value = OptionalReportField(table, report.ValueField);
        AppendGridSql(sb, report, table, row, column, value);
        AppendQueryStart(sb, table, "Tail");
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        var columnBoolean = column is not null && BooleanGroup(column, report.ColumnInterval);
        sb.AppendLine($"        return ReportKit.Chart({Lit("chart-" + Identifier.Route(report.Name))}, {Lit(ChartKind(report.ChartType))}, Caption, {FieldText(row)}, "
            + $"{Bool(BooleanGroup(row, report.RowInterval))}, {Bool(columnBoolean)}, {Bool(column is not null)}, "
            + $"{MeasureExpr(report, null, string.Empty, report.Function, value)}, Cells(reader), {Math.Max(1, report.MaxColumns)}, "
            + $"{Bool(report.ChartOrder == ChartOrder.ValueDescending)}, {Math.Max(0, report.TopCount)}, {Bool(report.ShowDataTable)}, {DrillArg(drill)});");
        sb.AppendLine("    }");
        sb.AppendLine();
        AppendCells(sb);
    }

    /// <summary>The text of a calendar entry: the title field, else the first visible text field that is not a key, else the first key.</summary>
    internal static FieldInfo? CalendarTitle(ReportInfo report, TableInfo table) =>
        OptionalReportField(table, report.TitleField)
        ?? table.Fields.FirstOrDefault(f => f.Type == FieldType.Text && !f.IsPrimaryKey && !f.IsSecret && f.ShowInList)
        ?? table.PrimaryKeys.FirstOrDefault();

    /// <summary>C# expression of the view page URL of the row read at <paramref name="first"/> (its key columns in order), or "null".</summary>
    private static string ViewHref(TableInfo table, int first) =>
        "ReportKit.ViewLink(" + Lit("/" + Identifier.Route(table.Name) + "/view") + ", "
        + string.Join(", ", table.PrimaryKeys.Select((_, i) => $"ReportKit.Read(reader, {first + i})")) + ")";

    private static void AppendCalendar(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var date = ReportField(table, report.DateField);
        var end = OptionalReportField(table, report.EndDateField);
        var title = CalendarTitle(report, table);
        var link = report.LinkToView && table.Pages.View && table.PrimaryKeys.Count > 0;
        var keys = link ? table.PrimaryKeys.ToList() : [];
        var dateSql = DateTextSql(table, date);
        var endSql = end is null ? dateSql : $"COALESCE(NULLIF({DateTextSql(table, end)}, ''), {dateSql})";
        var select = keys.Select(k => QuoteIdent(k.Name)).ToList();
        select.Add(dateSql);
        select.Add(end is null ? "NULL" : DateTextSql(table, end));
        select.Add(title is null ? "NULL" : QuoteIdent(title.Name));
        var range = $"{dateSql} IS NOT NULL AND {Db.Substring(dateSql, 1, 10)} <= @last AND {Db.Substring(endSql, 1, 10)} >= @first";
        sb.AppendLine($"    private const string Select = {Lit("SELECT " + Db.Top(ReportKitMaxCalendarEntries) + string.Join(", ", select))};");
        sb.AppendLine($"    private const string Range = {Lit(range)};");
        sb.AppendLine($"    private const string Tail = {Lit($" ORDER BY {dateSql}{Db.Limit(ReportKitMaxCalendarEntries)}")};");
        sb.AppendLine($"    private const bool WeekStartsMonday = {Bool(report.WeekStartsMonday)};");
        sb.AppendLine();
        AppendQueryStart(sb, table, "Tail", ", Range",
            "var month = ReportKit.MonthOf(context.Request);",
            "var (first, last) = ReportKit.CalendarRange(month, WeekStartsMonday);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@first\", first);");
        sb.AppendLine("        command.Parameters.AddWithValue(\"@last\", last);");
        sb.AppendLine("        await using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine("        return ReportKit.Calendar(Route, ReportKit.FilterQuery(context.Request), month, WeekStartsMonday, Entries(reader));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static string? Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);");
        sb.AppendLine();
        sb.AppendLine("    private static IEnumerable<ReportKit.CalendarEntry> Entries(SqliteDataReader reader)");
        sb.AppendLine("    {");
        sb.AppendLine("        while (reader.Read())");
        sb.AppendLine("        {");
        var k = keys.Count;
        var href = link ? ViewHref(table, 0) : "null";
        var titleExpr = title is not null && IsBooleanField(title)
            ? $"ReportKit.PlainLabel(ReportKit.Read(reader, {k + 2}), true, ReportKit.Text())"
            : $"Text(ReportKit.Read(reader, {k + 2})) ?? string.Empty";
        sb.AppendLine($"            yield return new ReportKit.CalendarEntry(Text(ReportKit.Read(reader, {k})), Text(ReportKit.Read(reader, {k + 1})), {titleExpr}, {href});");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    /// <summary>Same value as ReportKit.MaxCalendarEntries in the generated runtime.</summary>
    private const int ReportKitMaxCalendarEntries = 5000;

    /// <summary>
    /// The drill-down page at Route/rows: the rows behind a number of the report, chosen by the keys k0, k1, ... of the link and the
    /// filters of the report page, a page of <see cref="DrillPageSize"/> rows at a time, linked to the view page.
    /// </summary>
    private static void AppendRows(StringBuilder sb, ReportInfo report, TableInfo table)
    {
        var fields = DetailFields(report, table);
        var link = table.Pages.View && table.PrimaryKeys.Count > 0;
        var keys = link ? table.PrimaryKeys.ToList() : [];
        var select = keys.Select(k => QuoteIdent(k.Name)).Concat(fields.Select(f => QuoteIdent(f.Name)));
        var order = TableOrder(table).ToList();
        sb.AppendLine();
        sb.AppendLine($"    private const string RowsSelect = {Lit("SELECT " + string.Join(", ", select))};");
        sb.AppendLine($"    private const string RowsOrder = {Lit(order.Count > 0 ? " ORDER BY " + string.Join(", ", order) : Db.FallbackOrder)};");
        sb.AppendLine($"    private const int RowsPageSize = {DrillPageSize};");
        sb.AppendLine();
        sb.AppendLine("    public static async Task<string> Rows(HttpContext context, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        var (where, arguments) = ReportKit.Where(context.Request, StaticWhere, DateFilter, Filters(), DrillKeys);");
        sb.AppendLine("        var page = ReportKit.PageOf(context.Request);");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine("        long total;");
        sb.AppendLine("        await using (var command = connection.CreateCommand())");
        sb.AppendLine("        {");
        sb.AppendLine("            command.CommandText = \"SELECT COUNT(*)\" + From + where;");
        sb.AppendLine("            ReportKit.Bind(command, arguments);");
        AppendOwnerBinding(sb, table, "            ");
        sb.AppendLine("            total = Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var rows = new List<ReportKit.ListedRow>();");
        sb.AppendLine("        await using (var command = connection.CreateCommand())");
        sb.AppendLine("        {");
        sb.AppendLine($"            command.CommandText = RowsSelect + From + where + RowsOrder + {Lit(Db.Page)};");
        sb.AppendLine("            ReportKit.Bind(command, arguments);");
        AppendOwnerBinding(sb, table, "            ");
        sb.AppendLine("            command.Parameters.AddWithValue(\"@limit\", RowsPageSize);");
        sb.AppendLine("            command.Parameters.AddWithValue(\"@offset\", (page - 1L) * RowsPageSize);");
        sb.AppendLine("            await using var reader = await command.ExecuteReaderAsync();");
        sb.AppendLine("            while (await reader.ReadAsync())");
        sb.AppendLine("            {");
        var cells = string.Join(", ", fields.Select((f, j) => $"ReportKit.CellText(ReportKit.Read(reader, {keys.Count + j}), {Bool(IsBooleanField(f))})"));
        sb.AppendLine($"                rows.Add(new ReportKit.ListedRow(new[] {{ {cells} }}, {(link ? ViewHref(table, 0) : "null")}));");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        var keyCaptions = DrillKeys(report, table).Select(k => FieldText(k.Field));
        sb.AppendLine($"        var keyCaptions = new string[] {{ {string.Join(", ", keyCaptions)} }};");
        sb.AppendLine($"        var captions = new string[] {{ {string.Join(", ", fields.Select(FieldText))} }};");
        sb.AppendLine("        return ReportKit.RowsPage(context.Request, Route, keyCaptions, captions, rows, total, page, RowsPageSize);");
        sb.AppendLine("    }");
    }

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
