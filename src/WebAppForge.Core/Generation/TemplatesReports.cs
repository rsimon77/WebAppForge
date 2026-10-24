using System.Globalization;
using System.Reflection;
using System.Text;
using WebAppForge.Core.Model;
using WebAppForge.Core.Reports;

namespace WebAppForge.Core.Generation;

/// <summary>
/// The report files of a generated site: Web/ReportEngine.cs (the engine of <c>Reports\Engine\ReportEngine.cs</c>, embedded as text) and
/// Web/Reports.cs (the specs of the project's reports, the routes /reports and /reports/&lt;name&gt; and the HTML of summaries and crosstabs).
/// Both are written only when the project has reports.
/// </summary>
internal static class TemplatesReports
{
    /// <summary>The engine source with its namespace changed to the one of the generated site.</summary>
    public static string EngineFile(string ns)
    {
        using var stream = typeof(TemplatesReports).Assembly.GetManifestResourceStream("ReportEngine.cs")
            ?? throw new InvalidOperationException("The embedded report engine was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        const string anchor = "namespace WebAppForge.Core.Reports;";
        if (!text.Contains(anchor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The report engine namespace anchor was not found.");
        }

        return text.Replace(anchor, "namespace " + ns + ".Web;");
    }

    // ---- C# literals of the specs ---------------------------------------------------------------

    private static string L(string value) => Templates.Lit(value);

    private static string B(bool value) => value ? "true" : "false";

    private static string Col(SpecColumn c)
    {
        var sb = new StringBuilder("new SpecColumn { Name = ").Append(L(c.Name)).Append(", Column = ").Append(L(c.Column))
            .Append(", Caption = ").Append(L(c.Caption)).Append(", Kind = SpecKind.").Append(c.Kind);
        if (c.Format.Length > 0)
        {
            sb.Append(", Format = ").Append(L(c.Format));
        }

        if (c.FormatString.Length > 0)
        {
            sb.Append(", FormatString = ").Append(L(c.FormatString));
        }

        if (c.TrueText.Length > 0)
        {
            sb.Append(", TrueText = ").Append(L(c.TrueText));
        }

        if (c.FalseText.Length > 0)
        {
            sb.Append(", FalseText = ").Append(L(c.FalseText));
        }

        return sb.Append(" }").ToString();
    }

    private static string Group(SpecGroup g) =>
        "new SpecGroup { Field = " + Col(g.Field) + ", Interval = SpecInterval." + g.Interval
        + ", RangeSize = " + g.RangeSize.ToString("R", CultureInfo.InvariantCulture) + "d"
        + (g.Descending ? ", Descending = true" : string.Empty)
        + (g.ByAggregate ? ", ByAggregate = true" : string.Empty)
        + ", Caption = " + L(g.Caption) + " }";

    private static string Agg(SpecAggregate a) =>
        "new SpecAggregate { " + (a.Field is null ? string.Empty : "Field = " + Col(a.Field) + ", ")
        + "Function = SpecFunction." + a.Function + ", Caption = " + L(a.Caption)
        + (a.FormatString.Length > 0 ? ", FormatString = " + L(a.FormatString) : string.Empty) + " }";

    private static string Filter(SpecFilter f) =>
        "new SpecFilter { Field = " + Col(f.Field) + ", Caption = " + L(f.Caption) + ", Relative = " + B(f.Relative) + " }";

    private static string List<T>(string type, IEnumerable<T> items, Func<T, string> emit)
    {
        var list = items.Select(emit).ToList();
        return list.Count == 0 ? $"Array.Empty<{type}>()" : $"new {type}[] {{ " + string.Join(", ", list) + " }";
    }

    private static string Source(SpecSource s) =>
        "new SpecSource { Table = " + L(s.Table) + ", Conditions = " + List("string", s.Conditions, L)
        + ", OwnerColumn = " + L(s.OwnerColumn) + ", Order = " + L(s.Order)
        + ", Columns = " + List("SpecColumn", s.Columns, Col) + " }";

    // ---- the files ------------------------------------------------------------------------------

    public static string ReportsFile(Project project, string ns)
    {
        var builder = new ReportQueryBuilder(project);
        var levelSecured = project.Security.Enabled && !string.IsNullOrEmpty(project.Security.UserLevelField);
        var sb = new StringBuilder();
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        if (levelSecured)
        {
            sb.AppendLine($"using {ns}.Security;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Web;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The reports of the project: their specs, the index page at /reports and one route per report.</summary>");
        sb.AppendLine("public static class ReportPages");
        sb.AppendLine("{");

        var entries = new List<string>();
        var routes = new List<string>();
        var ordered = project.Reports
            .Select((report, index) => (report, index))
            .OrderBy(x => x.report.MenuOrder)
            .ThenBy(x => x.index)
            .ToList();
        for (var i = 0; i < project.Reports.Count; i++)
        {
            var report = project.Reports[i];
            var level = 0;
            var meta = "new ReportHtml.ReportMeta { Name = " + L(report.Name) + ", Caption = " + L(Templates.ReportCaption(report))
                + ", Description = " + L(report.Description);
            if (report.Type != ReportType.Dashboard)
            {
                var table = builder.Table(report.Table);
                if (levelSecured)
                {
                    level = Math.Max(report.MinLevel, table.ViewMinLevel);
                }

                var owner = project.Security.Enabled && table.OwnerField.Length > 0 ? table.OwnerField : string.Empty;
                var listFields = table.Fields
                    .Where(f => f.Searchable && f.Type != FieldType.Binary && !ReportQueryBuilder.IsHidden(project, table, f)
                        && !string.Equals(f.Name, owner, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Name);
                meta += ", ListRoute = " + L(table.Pages.List ? "/" + Identifier.Route(table.Name) : string.Empty)
                    + ", ListFields = " + List("string", listFields, L);
            }
            else if (levelSecured)
            {
                level = report.MinLevel;
            }

            if (report.DrillDownReport.Length > 0 && report.Type != ReportType.Dashboard)
            {
                var drill = project.Reports.First(r => ReportValidator.SameName(r.Name, report.DrillDownReport));
                meta += ", DrillRoute = " + L("/reports/" + drill.Name);
            }

            meta += " }";
            sb.AppendLine($"    private static readonly ReportHtml.ReportMeta Meta{i} = {meta};");
            string call;
            switch (report.Type)
            {
                case ReportType.Summary:
                    var summary = builder.Summary(report);
                    sb.AppendLine($"    private static readonly SpecSummary Spec{i} = new SpecSummary");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        Source = {Source(summary.Source)},");
                    sb.AppendLine($"        Groups = {List("SpecGroup", summary.Groups, Group)},");
                    sb.AppendLine($"        Aggregates = {List("SpecAggregate", summary.Aggregates, Agg)},");
                    sb.AppendLine($"        DetailColumns = {List("SpecColumn", summary.DetailColumns, Col)},");
                    sb.AppendLine($"        Compact = {B(summary.Compact)},");
                    sb.AppendLine($"        ShowGroupTotals = {B(summary.ShowGroupTotals)},");
                    sb.AppendLine($"        ShowGrandTotal = {B(summary.ShowGrandTotal)},");
                    sb.AppendLine($"        GroupsPerPage = {summary.GroupsPerPage.ToString(CultureInfo.InvariantCulture)},");
                    sb.AppendLine($"        DetailRowsPerGroup = {summary.DetailRowsPerGroup.ToString(CultureInfo.InvariantCulture)},");
                    sb.AppendLine($"        Filters = {List("SpecFilter", summary.Filters, Filter)},");
                    sb.AppendLine("    };");
                    call = $"ReportHtml.SummaryPage(request.HttpContext, connectionString, Meta{i}, Spec{i})";
                    break;
                case ReportType.Crosstab:
                    var crosstab = builder.Crosstab(report);
                    sb.AppendLine($"    private static readonly SpecCrosstab Spec{i} = new SpecCrosstab");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        Source = {Source(crosstab.Source)},");
                    sb.AppendLine($"        Rows = {Group(crosstab.Rows)},");
                    sb.AppendLine($"        Columns = {Group(crosstab.Columns)},");
                    sb.AppendLine($"        Value = {Agg(crosstab.Value)},");
                    sb.AppendLine($"        ShowRowTotals = {B(crosstab.ShowRowTotals)},");
                    sb.AppendLine($"        ShowColumnTotals = {B(crosstab.ShowColumnTotals)},");
                    sb.AppendLine($"        ShowGrandTotal = {B(crosstab.ShowGrandTotal)},");
                    sb.AppendLine($"        MaxColumns = {crosstab.MaxColumns.ToString(CultureInfo.InvariantCulture)},");
                    sb.AppendLine($"        Filters = {List("SpecFilter", crosstab.Filters, Filter)},");
                    sb.AppendLine("    };");
                    call = $"ReportHtml.CrosstabPage(request.HttpContext, connectionString, Meta{i}, Spec{i})";
                    break;
                default:
                    // Chart, calendar and dashboard pages are generated by a later step; until then the page says so.
                    call = $"Task.FromResult(ReportHtml.NotGenerated(request.HttpContext, Meta{i}, {L(report.Type.ToString().ToLowerInvariant())}))";
                    break;
            }

            sb.AppendLine();
            var guard = level > 0 ? $".RequireLevel({level.ToString(CultureInfo.InvariantCulture)}, \"You do not have permission to open this report.\")" : string.Empty;
            routes.Add($"        app.MapGet({L("/reports/" + report.Name)}, (HttpRequest request) => {call}){guard};");
            if (report.ShowInMenu)
            {
                entries.Add($"new ReportHtml.Entry({L(report.Name)}, {L(Templates.ReportCaption(report))}, {L(report.Description)}, {L(Templates.ReportMenuGroup(report))}, {L(Templates.Token(report.Icon))}, {level.ToString(CultureInfo.InvariantCulture)}, {ordered.FindIndex(x => ReferenceEquals(x.report, report)).ToString(CultureInfo.InvariantCulture)})");
            }
        }

        sb.AppendLine("    private static readonly ReportHtml.Entry[] Entries = " + List("ReportHtml.Entry", entries, e => e) + ";");
        sb.AppendLine();
        sb.AppendLine("    public static IEndpointRouteBuilder MapReportPages(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        app.MapGet(\"/reports\", (HttpRequest request) => ReportHtml.Index(request.HttpContext, Entries, CanOpen));");
        foreach (var route in routes)
        {
            sb.AppendLine(route);
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine(levelSecured
            ? "    private static bool CanOpen(HttpContext context, int level) => Permissions.Allowed(context.User, level);"
            : "    private static bool CanOpen(HttpContext context, int level) => true;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.Append(Runtime);
        return sb.ToString().Replace("@@NS@@", ns);
    }

    // ---- the runtime of the pages ---------------------------------------------------------------

    private const string Runtime = """
        /// <summary>HTML, CSV and request handling of the summary and crosstab reports. Everything from the request is encoded or a SQL parameter.</summary>
        public static class ReportHtml
        {
            public sealed class ReportMeta
            {
                public string Name { get; init; } = string.Empty;

                public string Caption { get; init; } = string.Empty;

                public string Description { get; init; } = string.Empty;

                /// <summary>Route of the drill-down report, or empty.</summary>
                public string DrillRoute { get; init; } = string.Empty;

                /// <summary>List page of the source table, or empty when it has none.</summary>
                public string ListRoute { get; init; } = string.Empty;

                /// <summary>Fields the list page can filter by (f_ parameters).</summary>
                public string[] ListFields { get; init; } = Array.Empty<string>();
            }

            public sealed record Entry(string Name, string Caption, string Description, string Group, string Icon, int Level, int Order);

            private const int MaxConstraints = 8;

            private static string E(object? value) => Html.Encode(value);

            private static string A(string value) => Html.Attr(value);

            private static string Route(ReportMeta meta) => "/reports/" + meta.Name;

            // ---- request ----------------------------------------------------------------------

            public static SpecRequest BuildRequest(HttpContext context, SpecSource source, IReadOnlyList<SpecFilter> filters)
            {
                var query = context.Request.Query;
                var request = new SpecRequest();
                if (source.OwnerColumn.Length > 0)
                {
                    request.Owner = context.User.Identity?.Name ?? string.Empty;
                }

                foreach (var filter in filters)
                {
                    var key = "f_" + filter.Field.Name;
                    var value = query[key].ToString().Trim();
                    var from = query[key + "_from"].ToString().Trim();
                    var to = query[key + "_to"].ToString().Trim();
                    var relative = query[key + "_rel"].ToString().Trim();
                    if (value.Length + from.Length + to.Length + relative.Length > 0)
                    {
                        request.Filters[filter.Field.Name] = new SpecFilterValue(value, from, to, relative);
                    }
                }

                foreach (var text in query["w"])
                {
                    if (request.Constraints.Count < MaxConstraints && ReportEngine.TryParseConstraint(source, text, out var constraint))
                    {
                        request.Constraints.Add(constraint);
                    }
                }

                return request;
            }

            private static string SelfUrl(HttpRequest request, string route, IReadOnlyList<KeyValuePair<string, string>> set, params string[] remove)
            {
                var parts = new List<string>();
                foreach (var pair in request.Query)
                {
                    if (Array.IndexOf(remove, pair.Key) >= 0 || set.Any(s => s.Key == pair.Key))
                    {
                        continue;
                    }

                    foreach (var value in pair.Value)
                    {
                        parts.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(value ?? string.Empty));
                    }
                }

                foreach (var pair in set)
                {
                    parts.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
                }

                return route + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
            }

            private static bool IsPartial(HttpRequest request) =>
                request.Query["partial"].ToString() == "1" || request.Headers["X-Requested-With"].ToString() == "fetch";

            private static bool IsCsv(HttpRequest request) => request.Query["format"].ToString() == "csv";

            // ---- links ------------------------------------------------------------------------

            /// <summary>The drill-down link of a clicked value: the drill-down report, else the filtered list page of the table. Empty when neither applies.</summary>
            private static string DrillUrl(HttpRequest request, ReportMeta meta, SpecRequest current, IEnumerable<SpecConstraint> clicked)
            {
                var all = current.Constraints.Concat(clicked).ToList();
                if (all.Any(c => c.Key == ReportEngine.OtherKey))
                {
                    return string.Empty;
                }

                var parts = new List<string>();
                if (meta.DrillRoute.Length > 0)
                {
                    foreach (var constraint in all)
                    {
                        parts.Add("w=" + Uri.EscapeDataString(ReportEngine.FormatConstraint(constraint)));
                    }

                    foreach (var pair in request.Query)
                    {
                        if (pair.Key.StartsWith("f_", StringComparison.Ordinal))
                        {
                            foreach (var value in pair.Value)
                            {
                                parts.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(value ?? string.Empty));
                            }
                        }
                    }

                    return meta.DrillRoute + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
                }

                if (meta.ListRoute.Length == 0)
                {
                    return string.Empty;
                }

                // Two levels on one field (year, then month) filter by the finer one, which comes last.
                foreach (var constraint in all.Where((c, i) => !all.Skip(i + 1).Any(o => o.Group.Field.Name == c.Group.Field.Name)))
                {
                    var interval = constraint.Group.Interval;
                    var listable = interval is SpecInterval.None or SpecInterval.Day or SpecInterval.Month or SpecInterval.Year;
                    if (!listable || constraint.Key.Length == 0 || Array.IndexOf(meta.ListFields, constraint.Group.Field.Name) < 0)
                    {
                        return string.Empty;
                    }

                    parts.Add(Uri.EscapeDataString("f_" + constraint.Group.Field.Name) + "=" + Uri.EscapeDataString(constraint.Key));
                }

                return meta.ListRoute + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts));
            }

            private static string Linked(string text, string url) =>
                url.Length == 0 ? E(text) : "<a href=\"" + A(url) + "\">" + E(text) + "</a>";

            // ---- shared page parts ------------------------------------------------------------

            private const string PrintCss =
                "<style>@media print{.report-filters,.report-actions,nav.navbar,.pagination{display:none!important}}</style>";

            private static IResult Render(HttpContext context, ReportMeta meta, string body)
            {
                if (IsPartial(context.Request))
                {
                    return Results.Content("<div data-partial=\"1\"><h2 class=\"h4 mb-3\">" + E(meta.Caption) + "</h2>" + body + "</div>", "text/html; charset=utf-8");
                }

                return Html.Page(meta.Caption, PrintCss + body);
            }

            private static IResult Csv(HttpContext context, ReportMeta meta, StringBuilder csv)
            {
                context.Response.Headers.ContentDisposition = "attachment; filename=\"" + meta.Name + ".csv\"";
                return Results.Text(csv.ToString(), "text/csv; charset=utf-8");
            }

            private static string FilterForm(HttpContext context, ReportMeta meta, SpecRequest current, IReadOnlyList<SpecFilter> filters)
            {
                var request = context.Request;
                var sb = new StringBuilder("<div class=\"report-body\" data-report=\"" + A(meta.Name) + "\">");
                if (meta.Description.Length > 0)
                {
                    sb.Append("<p class=\"text-muted\">").Append(E(meta.Description)).Append("</p>");
                }

                if (filters.Count > 0)
                {
                    sb.Append("<form method=\"get\" action=\"").Append(A(Route(meta))).Append("\" class=\"report-filters row g-2 align-items-end mb-3\">");
                    foreach (var constraint in current.Constraints)
                    {
                        sb.Append(Html.Hidden("w", ReportEngine.FormatConstraint(constraint)));
                    }

                    foreach (var filter in filters)
                    {
                        var key = "f_" + filter.Field.Name;
                        sb.Append("<div class=\"col-auto\"><label class=\"form-label small mb-0\">").Append(E(filter.Caption)).Append("</label>");
                        switch (filter.Field.Kind)
                        {
                            case SpecKind.Date:
                                sb.Append("<div class=\"input-group input-group-sm\">")
                                    .Append("<input class=\"form-control\" type=\"date\" name=\"").Append(A(key + "_from")).Append("\" value=\"").Append(A(request.Query[key + "_from"].ToString())).Append("\">")
                                    .Append("<input class=\"form-control\" type=\"date\" name=\"").Append(A(key + "_to")).Append("\" value=\"").Append(A(request.Query[key + "_to"].ToString())).Append("\">");
                                if (filter.Relative)
                                {
                                    sb.Append("<select class=\"form-select\" name=\"").Append(A(key + "_rel")).Append("\"><option value=\"\"></option>");
                                    foreach (var (presetKey, label) in ReportEngine.RelativePresets)
                                    {
                                        var chosen = request.Query[key + "_rel"].ToString() == presetKey ? " selected" : string.Empty;
                                        sb.Append("<option value=\"").Append(A(presetKey)).Append('"').Append(chosen).Append('>').Append(E(label)).Append("</option>");
                                    }

                                    sb.Append("</select>");
                                }

                                sb.Append("</div>");
                                break;
                            case SpecKind.Boolean:
                                var flag = request.Query[key].ToString();
                                sb.Append("<select class=\"form-select form-select-sm\" name=\"").Append(A(key)).Append("\"><option value=\"\"></option>")
                                    .Append("<option value=\"1\"").Append(flag == "1" ? " selected" : string.Empty).Append(">Yes</option>")
                                    .Append("<option value=\"0\"").Append(flag == "0" ? " selected" : string.Empty).Append(">No</option></select>");
                                break;
                            default:
                                sb.Append("<input class=\"form-control form-control-sm\" type=\"").Append(filter.Field.Kind == SpecKind.Number ? "number\" step=\"any" : "text")
                                    .Append("\" name=\"").Append(A(key)).Append("\" value=\"").Append(A(request.Query[key].ToString())).Append("\">");
                                break;
                        }

                        sb.Append("</div>");
                    }

                    sb.Append("<div class=\"col-auto\"><button type=\"submit\" class=\"btn btn-sm btn-primary\">Apply</button> ")
                        .Append("<a class=\"btn btn-sm btn-outline-secondary\" href=\"").Append(A(SelfUrl(request, Route(meta), [], request.Query.Keys.Where(k => k != "w").ToArray()))).Append("\">Reset</a></div></form>");
                }

                if (current.Constraints.Count > 0)
                {
                    sb.Append("<div class=\"alert alert-info py-1 report-actions\">");
                    foreach (var constraint in current.Constraints)
                    {
                        sb.Append(E(ReportEngine.DisplayKey(constraint.Group, null).Length > 0 ? constraint.Group.Field.Caption + ": " + (constraint.Key.Length == 0 ? "(empty)" : constraint.Key) : string.Empty)).Append("; ");
                    }

                    sb.Append("<a href=\"").Append(A(SelfUrl(request, Route(meta), [], "w", "page"))).Append("\">Clear</a></div>");
                }

                sb.Append("<p class=\"report-actions\"><a class=\"btn btn-sm btn-outline-secondary\" href=\"")
                    .Append(A(SelfUrl(request, Route(meta), [new("format", "csv")], "format", "partial", "page", "group", "allroot"))).Append("\">Export CSV</a></p>");
                return sb.ToString();
            }

            public static IResult NotGenerated(HttpContext context, ReportMeta meta, string type) =>
                Render(context, meta, "<p class=\"text-muted\">The " + E(type) + " report type is not generated yet.</p>");

            // ---- index ------------------------------------------------------------------------

            public static IResult Index(HttpContext context, Entry[] entries, Func<HttpContext, int, bool> canOpen)
            {
                var sb = new StringBuilder();
                var allowed = entries.Where(e => canOpen(context, e.Level)).OrderBy(e => e.Order).ToList();
                if (allowed.Count == 0)
                {
                    sb.Append("<p class=\"text-muted\">No data</p>");
                }

                foreach (var group in allowed.GroupBy(e => e.Group))
                {
                    sb.Append("<h2 class=\"h5 mt-4\">").Append(E(group.Key)).Append("</h2><ul class=\"list-group mb-3\">");
                    foreach (var entry in group)
                    {
                        var icon = entry.Icon.Length == 0 ? string.Empty : "<i class=\"bi bi-" + A(entry.Icon) + " me-2\"></i>";
                        sb.Append("<li class=\"list-group-item\">").Append(icon).Append("<a href=\"").Append(A("/reports/" + entry.Name)).Append("\">")
                            .Append(E(entry.Caption)).Append("</a>");
                        if (entry.Description.Length > 0)
                        {
                            sb.Append(" <small class=\"text-muted\">").Append(E(entry.Description)).Append("</small>");
                        }

                        sb.Append("</li>");
                    }

                    sb.Append("</ul>");
                }

                return Html.Page("Reports", sb.ToString());
            }

            // ---- summary ----------------------------------------------------------------------

            public static async Task<IResult> SummaryPage(HttpContext context, string connectionString, ReportMeta meta, SpecSummary spec)
            {
                var request = context.Request;
                var current = BuildRequest(context, spec.Source, spec.Filters);
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                var groups = spec.Groups.Count;
                if (IsCsv(request))
                {
                    var csv = new StringBuilder();
                    csv.Append(ReportEngine.CsvLine(spec.Groups.Select(g => (object?)g.Caption).Concat(spec.Aggregates.Select(a => (object?)a.Caption))));
                    if (groups > 0)
                    {
                        var leaves = await ReportEngine.RunAsync(connection, ReportEngine.Groups(spec.Source, spec.Filters, current, spec.Groups, groups, spec.Aggregates));
                        foreach (var row in leaves.Rows)
                        {
                            csv.Append(ReportEngine.CsvLine(row.Take(groups + spec.Aggregates.Count)));
                        }
                    }

                    if (spec.Aggregates.Count > 0 && (groups == 0 || spec.ShowGrandTotal))
                    {
                        var total = await ReportEngine.RunAsync(connection, ReportEngine.Groups(spec.Source, spec.Filters, current, spec.Groups, 0, spec.Aggregates));
                        var cells = new List<object?>();
                        for (var i = 0; i < groups; i++)
                        {
                            cells.Add(i == 0 ? "Total" : string.Empty);
                        }

                        cells.AddRange(total.Rows[0].Take(spec.Aggregates.Count));
                        csv.Append(ReportEngine.CsvLine(cells));
                    }

                    return Csv(context, meta, csv);
                }

                var page = int.TryParse(request.Query["page"], out var parsed) ? parsed : 1;
                string[]? showAll = request.Query["allroot"].ToString() == "1"
                    ? Array.Empty<string>()
                    : request.Query.ContainsKey("group") ? request.Query["group"].Select(g => g ?? string.Empty).ToArray() : null;
                var data = await ReportEngine.LoadSummaryAsync(connection, spec, current, page, showAll);
                var body = new StringBuilder(IsPartial(request) ? string.Empty : FilterForm(context, meta, current, spec.Filters));
                AppendSummary(body, request, meta, spec, current, data);
                if (!IsPartial(request))
                {
                    body.Append(Html.PageLinks(request, data.Page, data.PageCount, Route(meta))).Append("</div>");
                }

                return Render(context, meta, body.ToString());
            }

            private static string Cell(object? value, SpecAggregate aggregate) =>
                "<td class=\"text-end\">" + E(ReportEngine.FormatAggregate(value, aggregate)) + "</td>";

            private static void AppendSummary(StringBuilder sb, HttpRequest request, ReportMeta meta, SpecSummary spec, SpecRequest current, SummaryData data)
            {
                var groups = spec.Groups.Count;
                var aggregates = spec.Aggregates;
                if (data.Groups.Count == 0 && data.GrandTotal is null && data.RootDetail is null)
                {
                    sb.Append("<p class=\"text-muted\">No data</p>");
                    return;
                }

                sb.Append("<table class=\"table table-sm table-hover report-table\"><thead><tr>");
                if (spec.Compact)
                {
                    foreach (var group in spec.Groups)
                    {
                        sb.Append("<th>").Append(E(group.Caption)).Append("</th>");
                    }
                }
                else if (groups > 0)
                {
                    sb.Append("<th>").Append(E(string.Join(" / ", spec.Groups.Select(g => g.Caption)))).Append("</th>");
                }
                else
                {
                    sb.Append("<th></th>");
                }

                foreach (var aggregate in aggregates)
                {
                    sb.Append("<th class=\"text-end\">").Append(E(aggregate.Caption)).Append("</th>");
                }

                sb.Append("</tr></thead><tbody>");
                foreach (var node in data.Groups)
                {
                    if (spec.Compact)
                    {
                        sb.Append("<tr>");
                        for (var i = 0; i < groups; i++)
                        {
                            var url = DrillUrl(request, meta, current, ConstraintsOf(spec, node, i + 1));
                            sb.Append("<td>").Append(Linked(ReportEngine.DisplayKey(spec.Groups[i], node.Values[i]), url)).Append("</td>");
                        }

                        for (var i = 0; i < aggregates.Count; i++)
                        {
                            sb.Append(Cell(node.Aggregates[i], aggregates[i]));
                        }

                        sb.Append("</tr>");
                    }
                    else
                    {
                        AppendNode(sb, request, meta, spec, current, node);
                    }
                }

                if (data.RootDetail is { } rootDetail)
                {
                    sb.Append("<tr><td colspan=\"").Append(1 + aggregates.Count).Append("\">");
                    AppendDetail(sb, request, spec, rootDetail, data.RootDetailTotal, new[] { new KeyValuePair<string, string>("allroot", "1") }, "page");
                    sb.Append("</td></tr>");
                }

                if (data.GrandTotal is { } total && aggregates.Count > 0)
                {
                    sb.Append("<tr class=\"table-dark fw-bold\">");
                    if (spec.Compact && groups > 1)
                    {
                        sb.Append("<td colspan=\"").Append(groups).Append("\">Grand total</td>");
                    }
                    else
                    {
                        sb.Append("<td>Grand total</td>");
                    }

                    for (var i = 0; i < aggregates.Count; i++)
                    {
                        sb.Append(Cell(total[i], aggregates[i]));
                    }

                    sb.Append("</tr>");
                }

                sb.Append("</tbody></table>");
            }

            private static List<SpecConstraint> ConstraintsOf(SpecSummary spec, SummaryNode node, int levels)
            {
                var list = new List<SpecConstraint>();
                for (var i = 0; i < levels; i++)
                {
                    list.Add(new SpecConstraint(spec.Groups[i], node.Keys[i]));
                }

                return list;
            }

            private static void AppendNode(StringBuilder sb, HttpRequest request, ReportMeta meta, SpecSummary spec, SpecRequest current, SummaryNode node)
            {
                var group = spec.Groups[node.Level - 1];
                var url = DrillUrl(request, meta, current, ConstraintsOf(spec, node, node.Level));
                var indent = ((node.Level - 1) * 1.5).ToString("0.##", CultureInfo.InvariantCulture);
                sb.Append("<tr class=\"table-light fw-semibold\"><td style=\"padding-left:").Append(indent).Append("rem\">")
                    .Append(E(group.Caption)).Append(": ").Append(Linked(ReportEngine.DisplayKey(group, node.Values[node.Level - 1]), url)).Append("</td>");
                for (var i = 0; i < spec.Aggregates.Count; i++)
                {
                    sb.Append(spec.ShowGroupTotals ? Cell(node.Aggregates[i], spec.Aggregates[i]) : "<td></td>");
                }

                sb.Append("</tr>");
                foreach (var child in node.Children)
                {
                    AppendNode(sb, request, meta, spec, current, child);
                }

                if (node.Detail is { } detail)
                {
                    sb.Append("<tr><td colspan=\"").Append(1 + spec.Aggregates.Count).Append("\" style=\"padding-left:").Append(indent).Append("rem\">");
                    var set = node.Keys.Select(k => new KeyValuePair<string, string>("group", k)).ToList();
                    AppendDetail(sb, request, spec, detail, node.DetailTotal, set, "allroot");
                    sb.Append("</td></tr>");
                }
            }

            private static void AppendDetail(StringBuilder sb, HttpRequest request, SpecSummary spec, ReportResult detail, long total, IReadOnlyList<KeyValuePair<string, string>> showAll, string drop)
            {
                sb.Append("<table class=\"table table-sm table-bordered mb-1\"><thead><tr>");
                foreach (var column in spec.DetailColumns)
                {
                    sb.Append("<th>").Append(E(column.Caption)).Append("</th>");
                }

                sb.Append("</tr></thead><tbody>");
                foreach (var row in detail.Rows)
                {
                    sb.Append("<tr>");
                    for (var i = 0; i < spec.DetailColumns.Count; i++)
                    {
                        var column = spec.DetailColumns[i];
                        sb.Append(column.Kind == SpecKind.Number ? "<td class=\"text-end\">" : "<td>").Append(E(ReportEngine.FormatValue(row[i], column))).Append("</td>");
                    }

                    sb.Append("</tr>");
                }

                sb.Append("</tbody></table>");
                if (total > detail.Rows.Count)
                {
                    var route = request.Path.Value ?? string.Empty;
                    sb.Append("<small class=\"text-muted\">Showing ").Append(detail.Rows.Count).Append(" of ").Append(total).Append(" rows.</small> <a class=\"report-actions\" href=\"")
                        .Append(A(SelfUrl(request, route, showAll, "group", "allroot", drop == "allroot" ? "allroot" : "page"))).Append("\">Show all</a>");
                }
            }

            // ---- crosstab ---------------------------------------------------------------------

            public static async Task<IResult> CrosstabPage(HttpContext context, string connectionString, ReportMeta meta, SpecCrosstab spec)
            {
                var request = context.Request;
                var current = BuildRequest(context, spec.Source, spec.Filters);
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                var data = await ReportEngine.LoadCrosstabAsync(connection, spec, current);
                if (IsCsv(request))
                {
                    var csv = new StringBuilder();
                    var head = new List<object?> { spec.Rows.Caption + " / " + spec.Columns.Caption };
                    head.AddRange(data.Columns.Select(c => (object?)ColumnText(spec, c)));
                    if (spec.ShowRowTotals)
                    {
                        head.Add("Total");
                    }

                    csv.Append(ReportEngine.CsvLine(head));
                    foreach (var row in data.Rows)
                    {
                        var cells = new List<object?> { ReportEngine.DisplayKey(spec.Rows, row.Value) };
                        cells.AddRange(data.Columns.Select(c => data.Cells.TryGetValue((row.Key, c.Key), out var value) ? value : null));
                        if (spec.ShowRowTotals)
                        {
                            cells.Add(row.Total);
                        }

                        csv.Append(ReportEngine.CsvLine(cells));
                    }

                    if (spec.ShowColumnTotals)
                    {
                        var cells = new List<object?> { "Total" };
                        cells.AddRange(data.Columns.Select(c => c.Total));
                        if (spec.ShowRowTotals && spec.ShowGrandTotal)
                        {
                            cells.Add(data.GrandTotal);
                        }

                        csv.Append(ReportEngine.CsvLine(cells));
                    }

                    return Csv(context, meta, csv);
                }

                var body = new StringBuilder(IsPartial(request) ? string.Empty : FilterForm(context, meta, current, spec.Filters));
                if (data.Rows.Count == 0 || data.Columns.Count == 0)
                {
                    body.Append("<p class=\"text-muted\">No data</p>");
                }
                else
                {
                    AppendCrosstab(body, request, meta, spec, current, data);
                }

                if (!IsPartial(request))
                {
                    body.Append("</div>");
                }

                return Render(context, meta, body.ToString());
            }

            private static string ColumnText(SpecCrosstab spec, CrosstabHead column) =>
                column.IsOther ? "Other" : ReportEngine.DisplayKey(spec.Columns, column.Value);

            private static void AppendCrosstab(StringBuilder sb, HttpRequest request, ReportMeta meta, SpecCrosstab spec, SpecRequest current, CrosstabData data)
            {
                sb.Append("<table class=\"table table-sm table-bordered report-table\"><thead><tr><th>")
                    .Append(E(spec.Rows.Caption + " / " + spec.Columns.Caption)).Append("</th>");
                foreach (var column in data.Columns)
                {
                    sb.Append("<th class=\"text-end\">").Append(E(ColumnText(spec, column))).Append("</th>");
                }

                if (spec.ShowRowTotals)
                {
                    sb.Append("<th class=\"text-end\">Total</th>");
                }

                sb.Append("</tr></thead><tbody>");
                foreach (var row in data.Rows)
                {
                    sb.Append("<tr><th scope=\"row\">").Append(E(ReportEngine.DisplayKey(spec.Rows, row.Value))).Append("</th>");
                    foreach (var column in data.Columns)
                    {
                        if (!data.Cells.TryGetValue((row.Key, column.Key), out var value) || value is null)
                        {
                            sb.Append("<td></td>");
                            continue;
                        }

                        var url = column.IsOther
                            ? string.Empty
                            : DrillUrl(request, meta, current, [new SpecConstraint(spec.Rows, row.Key), new SpecConstraint(spec.Columns, column.Key)]);
                        sb.Append("<td class=\"text-end\">").Append(Linked(ReportEngine.FormatAggregate(value, spec.Value), url)).Append("</td>");
                    }

                    if (spec.ShowRowTotals)
                    {
                        sb.Append("<td class=\"text-end fw-semibold\">").Append(E(ReportEngine.FormatAggregate(row.Total, spec.Value))).Append("</td>");
                    }

                    sb.Append("</tr>");
                }

                sb.Append("</tbody>");
                if (spec.ShowColumnTotals)
                {
                    sb.Append("<tfoot><tr class=\"table-light fw-semibold\"><th scope=\"row\">Total</th>");
                    foreach (var column in data.Columns)
                    {
                        sb.Append("<td class=\"text-end\">").Append(E(ReportEngine.FormatAggregate(column.Total, spec.Value))).Append("</td>");
                    }

                    if (spec.ShowRowTotals)
                    {
                        sb.Append("<td class=\"text-end\">").Append(spec.ShowGrandTotal ? E(ReportEngine.FormatAggregate(data.GrandTotal, spec.Value)) : string.Empty).Append("</td>");
                    }

                    sb.Append("</tr></tfoot>");
                }

                sb.Append("</table>");
            }
        }
        """;
}
