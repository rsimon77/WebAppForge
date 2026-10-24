using System.Text;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Master/detail in the generated site: detail grids on the view and edit page of a master, previews on its list page, presets on the
/// add and copy pages of a detail table, return URLs and the details endpoint. Everything here is reached only for tables that take part
/// in a <see cref="DetailLink"/>; other tables are generated exactly as before.
/// </summary>
internal static partial class Templates
{
    /// <summary>The generated copies of all generated tables, set by <see cref="ProjectGenerator"/> for one Generate call.</summary>
    [ThreadStatic]
    private static IReadOnlyList<TableInfo>? _mdTables;

    internal static IReadOnlyList<TableInfo> MdTables
    {
        get => _mdTables ?? [];
        set => _mdTables = value;
    }

    /// <summary>A detail link with its tables and key pairs resolved. <see cref="Index"/> is the position in TableInfo.Details.</summary>
    internal sealed record MdLink(TableInfo Master, int Index, DetailLink Link, TableInfo Detail, List<(FieldInfo Master, FieldInfo Detail)> Pairs);

    private static bool MdSame(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static TableInfo? MdTable(string name) => MdTables.FirstOrDefault(t => MdSame(t.Name, name));

    private static List<MdLink> MdLinks(TableInfo master)
    {
        var result = new List<MdLink>();
        for (var i = 0; i < master.Details.Count; i++)
        {
            var link = master.Details[i];
            var detail = MdTable(link.DetailTable);
            if (detail is null || link.Links.Count == 0)
            {
                continue;
            }

            var pairs = new List<(FieldInfo, FieldInfo)>();
            foreach (var pair in link.Links)
            {
                var m = master.Fields.FirstOrDefault(f => MdSame(f.Name, pair.MasterField));
                var d = detail.Fields.FirstOrDefault(f => MdSame(f.Name, pair.DetailField));
                if (m is null || d is null)
                {
                    pairs.Clear();
                    break;
                }

                pairs.Add((m, d));
            }

            if (pairs.Count > 0)
            {
                result.Add(new MdLink(master, i, link, detail, pairs));
            }
        }

        return result;
    }

    /// <summary>The links of every table that name <paramref name="detail"/> as their detail table.</summary>
    private static List<MdLink> MdMasters(TableInfo detail) =>
        MdTables.SelectMany(MdLinks).Where(l => MdSame(l.Detail.Name, detail.Name)).ToList();

    internal static bool UsesMasterDetail(IEnumerable<TableInfo> tables) => tables.Any(t => t.Details.Count > 0);

    private static bool ViewHasContext(TableInfo t) => IsOwned(t) || Designed(t) || MdViewLinks(t).Count > 0;

    private static bool IsDetailTable(TableInfo table) => MdMasters(table).Count > 0;

    /// <summary>Where a detail page goes after it is done or cancelled: the verified returnUrl, else <c>Route</c>.</summary>
    private static string BackTarget(TableInfo table) => IsDetailTable(table) ? "MasterDetailKit.Back(context.Request, Route)" : "Route";

    private static bool MdCanEdit(TableInfo t) =>
        t.Pages.Edit && t.PrimaryKeys.Count > 0 && (t.PrimaryKeys.Count > 1 || UpdateFields(t).Count > 0);

    private static string MdCaptionExpr(MdLink md) =>
        Has(md.Link.Caption) ? Lit(md.Link.Caption.Trim()) : TableText(md.Detail);

    private static List<MdLink> MdViewLinks(TableInfo t) => MdLinks(t).Where(l => l.Link.ShowOnView).ToList();

    private static List<MdLink> MdEditLinks(TableInfo t) => MdLinks(t).Where(l => l.Link.ShowOnEdit).ToList();

    private static bool MdHasGrids(TableInfo t) =>
        t.PrimaryKeys.Count > 0 && MdLinks(t).Any(l => l.Link.ShowOnView || l.Link.ShowOnEdit || l.Link.Preview != DetailPreview.None);

    private static bool MdNeedsSecurity(TableInfo t) =>
        (t.PrimaryKeys.Count > 0 && MdLinks(t).Any(l => l.Detail.ViewMinLevel > 0 || l.Detail.EditMinLevel > 0))
        || MdMasters(t).Any(l => l.Master.ViewMinLevel > 0);

    private static bool UsesSecurityNamespace(TableInfo t) => IsLevelGuarded(t) || MdNeedsSecurity(t) || ExchangeNeedsSecurity(t);

    /// <summary>The preview a link really gets: the expandable row needs table rows, so the cards layout uses an offcanvas.</summary>
    private static DetailPreview MdPreviewOf(TableInfo t, MdLink l) =>
        l.Link.Preview == DetailPreview.ExpandableRow && Designed(t) && t.Designs.List.ListLayout == ListLayout.Cards
            ? DetailPreview.Offcanvas
            : l.Link.Preview;

    private static List<MdLink> MdPreviewLinks(TableInfo t) =>
        t.PrimaryKeys.Count > 0 ? MdLinks(t).Where(l => l.Link.Preview != DetailPreview.None).ToList() : [];

    /// <summary>True when the list page of the table opens details in a modal or offcanvas, which needs the page runtime.</summary>
    internal static bool UsesPreviewContainers(TableInfo t) =>
        t.Pages.List && MdPreviewLinks(t).Any(l => MdPreviewOf(t, l) is DetailPreview.Modal or DetailPreview.Offcanvas);

    private static int MdHostIndex(TableInfo t, MdLink l)
    {
        var name = l.Link.FormPage.Trim();
        return name.Length == 0 ? -1 : t.FormPages.FindIndex(p => MdSame(p.Name, name));
    }

    /// <summary>Page index to the expressions of the grids hosted on that form page, and the grids that go below the form.</summary>
    private static (Dictionary<int, List<string>> Hosted, List<string> Below) MdPlacement(TableInfo t, IEnumerable<MdLink> links, bool paged, Func<MdLink, string> expr)
    {
        var hosted = new Dictionary<int, List<string>>();
        var below = new List<string>();
        foreach (var l in links)
        {
            var host = paged ? MdHostIndex(t, l) : -1;
            if (host < 0)
            {
                below.Add(expr(l));
                continue;
            }

            if (!hosted.TryGetValue(host, out var list))
            {
                hosted[host] = list = [];
            }

            list.Add(expr(l));
        }

        return (hosted, below);
    }

    private static List<(int Index, List<FieldInfo> Fields)> MdWithHosts(List<(int Index, List<FieldInfo> Fields)> byPage, IEnumerable<int> hosts)
    {
        var result = byPage.ToList();
        foreach (var h in hosts.Distinct())
        {
            if (!result.Any(r => r.Index == h))
            {
                result.Add((h, []));
            }
        }

        return result.OrderBy(r => r.Index).ToList();
    }

    // ---- extra RenderForm parameters ------------------------------------------------------------

    /// <summary>Optional trailing parameters of RenderForm: presets of a detail table and the grids of a master's edit page.</summary>
    private static string MdFormParams(TableInfo table) =>
        (IsDetailTable(table) ? ", IReadOnlyDictionary<string, string>? presets = null" : string.Empty)
        + (MdEditLinks(table).Count > 0 ? ", string[]? grids = null" : string.Empty);

    private static void AppendMdEditTail(StringBuilder sb, TableInfo table, bool paged)
    {
        var links = MdEditLinks(table);
        if (links.Count == 0)
        {
            return;
        }

        foreach (var l in links)
        {
            var hosted = paged && MdHostIndex(table, l) >= 0;
            sb.AppendLine(hosted
                ? $"        if (mp == \"None\") body.Append(grids?[{l.Index}] ?? string.Empty);"
                : $"        body.Append(grids?[{l.Index}] ?? string.Empty);");
        }
    }

    private static string MdGridExpr(MdLink l) => $"(grids?[{l.Index}] ?? string.Empty)";

    /// <summary>Wraps the input statements of a link field so that a preset shows it read-only with a hidden input.</summary>
    private static void AppendMdFixed(StringBuilder sb, TableInfo table, FieldInfo field, Action<StringBuilder> emit)
    {
        if (!IsDetailTable(table) || !MdMasters(table).Any(m => m.Pairs.Any(p => MdSame(p.Detail.Name, field.Name))))
        {
            emit(sb);
            return;
        }

        var inner = new StringBuilder();
        emit(inner);
        var n = table.Fields.IndexOf(field);
        sb.AppendLine($"        if (MasterDetailKit.Fixed(presets, {Lit(field.Name)}) is {{ }} fx{n})");
        sb.AppendLine("        {");
        sb.AppendLine($"            body.Append(MasterDetailKit.FixedField({FieldText(field)}, {Lit(field.Name)}, fx{n}));");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        foreach (var line in inner.ToString().Split('\n'))
        {
            if (line.TrimEnd('\r').Length > 0)
            {
                sb.AppendLine("    " + line.TrimEnd('\r'));
            }
        }

        sb.AppendLine("        }");
    }

    // ---- statics and the grid -------------------------------------------------------------------

    private static List<(FieldInfo Field, string Width)> MdColumns(MdLink md)
    {
        var list = new List<(FieldInfo, string)>();
        if (md.Link.Columns.Count > 0)
        {
            foreach (var name in md.Link.Columns)
            {
                var f = md.Detail.Fields.FirstOrDefault(x => MdSame(x.Name, name));
                if (f is not null && !list.Any(x => ReferenceEquals(x.Item1, f)))
                {
                    list.Add((f, string.Empty));
                }
            }
        }
        else
        {
            list.AddRange(ListColumnsOf(md.Detail));
        }

        return list.Where(c => c.Item1.Type != FieldType.Binary).ToList();
    }

    private static string MdFmt(int i, string prop) => "Dt" + i + "_" + Fmt(prop);

    private static string MdLookup(int i, string prop) => "Dt" + i + "_" + LookupName(prop);

    private static void AppendMdStatics(StringBuilder sb, string className, TableInfo table)
    {
        if (table.PrimaryKeys.Count == 0)
        {
            return;
        }

        foreach (var md in MdLinks(table))
        {
            sb.AppendLine($"    private const int MdPageSize{md.Index} = {Math.Clamp(md.Link.PageSize, 1, 200)};");
            if (md.Link.FilterAndSort)
            {
                // The quick filter searches the grid's columns; the sortable ones may be sorted by their header.
                var savedTable = _localizedTable;
                _localizedTable = md.Detail.Name;
                var filterable = MdColumns(md).Select(c => c.Field).Where(f => f.Type != FieldType.Binary).ToList();
                var columns = filterable.Select(f => $"new({Lit(f.Name)}, {FieldText(f)}, {Lit(GridSql(md.Detail, f))}, {GridKind(f)})");
                sb.AppendLine($"    private static GridKit.Column[] MdGridColumns{md.Index} => [{string.Join(", ", columns)}];");
                var sortable = filterable.Where(f => f.Ui.Sortable).ToList();
                sb.AppendLine($"    private static readonly string[] MdSortFields{md.Index} = {StringArray(sortable.Select(f => f.Name))};");
                sb.AppendLine($"    private static readonly string[] MdSortColumns{md.Index} = {StringArray(sortable.Select(f => QuoteIdent(f.Name)))};");
                _localizedTable = savedTable;
            }

            var dClass = Identifier.Pascal(md.Detail.Name);
            foreach (var (field, _) in MdColumns(md))
            {
                var prop = PropName(dClass, field);
                if (NeedsFmt(field))
                {
                    sb.AppendLine($"    private static readonly FieldView {MdFmt(md.Index, prop)} = {FmtInit(field)};");
                }

                if (LookupOf(field) is { } lookup)
                {
                    var order = lookup.OrderField.Length > 0 ? lookup.OrderField : lookup.DisplayField;
                    sb.AppendLine($"    private static readonly LookupSource {MdLookup(md.Index, prop)} = new({Lit(QuoteIdent(lookup.Table))}, {Lit(QuoteIdent(lookup.KeyField))}, "
                        + $"{Lit(QuoteIdent(lookup.DisplayField))}, {Lit(lookup.Filter)}, {Lit(QuoteIdent(order))});");
                }
            }
        }
    }

    private static string MdValueText(FieldInfo f, string expr)
    {
        var required = f.IsRequired;
        return f.Type switch
        {
            FieldType.Integer or FieldType.Decimal => required
                ? $"{expr}.ToString(CultureInfo.InvariantCulture)"
                : $"({expr}?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)",
            FieldType.Boolean => required ? $"({expr} ? \"1\" : \"0\")" : $"({expr} == true ? \"1\" : \"0\")",
            FieldType.Date or FieldType.DateTime => required
                ? $"{expr}.ToString(\"o\", CultureInfo.InvariantCulture)"
                : $"({expr}?.ToString(\"o\", CultureInfo.InvariantCulture) ?? string.Empty)",
            FieldType.Text => required ? expr : $"({expr} ?? string.Empty)",
            _ => "string.Empty",
        };
    }

    private static string MdKindOf(FieldInfo f) => f.Type switch
    {
        FieldType.Integer => "int",
        FieldType.Decimal => "dec",
        FieldType.Boolean => "bool",
        FieldType.Date or FieldType.DateTime => "date",
        _ => "text",
    };

    private static string MdRowKey(TableInfo detail, string row)
    {
        var dClass = Identifier.Pascal(detail.Name);
        return string.Join(" + \"/\" + ", detail.PrimaryKeys.Select(k => $"Uri.EscapeDataString({row}.{PropName(dClass, k)}.ToString())"));
    }

    private static void AppendMdGridMethods(StringBuilder sb, string className, TableInfo table)
    {
        if (table.PrimaryKeys.Count == 0)
        {
            return;
        }

        var links = MdLinks(table);
        foreach (var md in links)
        {
            AppendMdGrid(sb, className, md);
        }

        if (MdEditLinks(table).Count > 0)
        {
            sb.AppendLine($"    private static async Task<string[]> DetailGridsForEdit(HttpContext context, SqliteConnection connection, {className} item)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var grids = new string[{table.Details.Count}];");
            sb.AppendLine("        Array.Fill(grids, string.Empty);");
            foreach (var l in MdEditLinks(table))
            {
                sb.AppendLine($"        grids[{l.Index}] = await DetailGrid{l.Index}(context, connection, item, false{MdToolsArg(l, false)});");
            }

            sb.AppendLine("        return grids;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        foreach (var md in MdPreviewLinks(table))
        {
            var keyParams = KeyParams(table);
            sb.AppendLine($"    private static async Task<IResult> DetailsFragment{md.Index}({keyParams}, HttpContext context, string connectionString)");
            sb.AppendLine("    {");
            sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("        await connection.OpenAsync();");
            sb.AppendLine($"        var item = await Find(connection, {KeyArgs(table)}{OwnerArg(table)});");
            sb.AppendLine("        if (item is null)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        var grid = await DetailGrid{md.Index}(context, connection, item, true{MdToolsArg(md, false)});");
            sb.AppendLine("        return grid.Length == 0");
            sb.AppendLine($"            ? Results.Text({Ui("deniedView")}, \"text/plain; charset=utf-8\", statusCode: StatusCodes.Status403Forbidden)");
            sb.AppendLine("            : Results.Content(\"<div data-partial=\\\"1\\\">\" + grid + \"</div>\", \"text/html; charset=utf-8\");");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
    }

    /// <summary>The tools argument of a DetailGrid call: empty for a link without filter and sort, so its code stays as it was.</summary>
    private static string MdToolsArg(MdLink md, bool tools) => md.Link.FilterAndSort ? (tools ? ", true" : ", false") : string.Empty;

    private static void AppendMdGrid(StringBuilder sb, string className, MdLink md)
    {
        var i = md.Index;
        var d = md.Detail;
        var dClass = Identifier.Pascal(d.Name);
        var detailRoute = "/" + Identifier.Route(d.Name);
        var keys = d.PrimaryKeys;
        var owned = IsOwned(d);
        var columns = MdColumns(md);
        var canView = d.Pages.View && keys.Count > 0;
        var canEdit = md.Link.AllowEdit && MdCanEdit(d);
        var canDelete = md.Link.AllowDelete && d.Pages.Delete && keys.Count > 0;
        var canAdd = md.Link.AllowAdd && d.Pages.Add && keys.Count > 0;
        var changeGuard = d.EditMinLevel > 0;
        var showActions = canView || canEdit || canDelete;
        var masterClass = className;
        void L(string line) => sb.AppendLine(line);

        var savedTable = _localizedTable;
        _localizedTable = d.Name;

        var tools = md.Link.FilterAndSort;
        L($"    private static async Task<string> DetailGrid{i}(HttpContext context, SqliteConnection connection, {className} item, bool fragment{(tools ? ", bool tools" : string.Empty)})");
        L("    {");
        if (d.ViewMinLevel > 0)
        {
            L($"        if (!Permissions.Allowed(context.User, {d.ViewMinLevel}))");
            L("        {");
            L("            return string.Empty;");
            L("        }");
            L(string.Empty);
        }

        var cond = string.Join(" AND ", md.Pairs.Select((p, j) => QuoteIdent(p.Detail.Name) + " = @m" + j));
        if (d.Filter.Length > 0)
        {
            cond += " AND (" + d.Filter + ")";
        }

        if (owned)
        {
            cond += " AND " + OwnerCondition(d);
        }

        L("        var request = context.Request;");
        if (tools)
        {
            // The quick filter and the header sort only apply where the grid shows them: on the view page.
            L($"        var where = {Lit(" WHERE " + cond)};");
            L($"        var order = {Lit(OrderSql(d))};");
            L("        var gridParameters = new List<KeyValuePair<string, object>>();");
            L("        if (tools)");
            L("        {");
            L($"            var quick = GridKit.QuickWhere(request.Query[{Lit("dq" + i)}].ToString(), MdGridColumns{i}, gridParameters);");
            L("            if (quick.Length > 0)");
            L("            {");
            L("                where += \" AND \" + quick;");
            L("            }");
            L(string.Empty);
            var tieBreak = keys.Count == 0 ? string.Empty : ", " + string.Join(", ", keys.Select(k => QuoteIdent(k.Name) + " ASC"));
            L($"            if (GridKit.DetailOrder(request, {i}, MdSortFields{i}, MdSortColumns{i}) is {{ }} sorted)");
            L("            {");
            L($"                order = sorted + {Lit(tieBreak)};");
            L("            }");
            L("        }");
            L(string.Empty);
        }
        else
        {
            L($"        const string where = {Lit(" WHERE " + cond)};");
        }

        L($"        var requested = int.TryParse(request.Query[{Lit("dp" + i)}], out var parsed) ? parsed : 1;");
        L(tools ? "        var args = Db.Args(gridParameters);" : "        var args = new DynamicParameters();");
        for (var j = 0; j < md.Pairs.Count; j++)
        {
            L($"        args.Add(\"@m{j}\", {ParameterValue(md.Pairs[j].Master, "item", masterClass)});");
        }

        if (owned)
        {
            L("        args.Add(\"@owner\", context.User.Identity?.Name ?? string.Empty);");
        }

        L($"        var total = await connection.ExecuteScalarAsync<long>({Lit("SELECT COUNT(*) FROM " + QuoteIdent(d.Name))} + where, args);");
        L(string.Empty);
        L($"        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)MdPageSize{i}));");
        L("        var page = Math.Clamp(requested, 1, pageCount);");
        L($"        args.Add(\"@limit\", MdPageSize{i});");
        L($"        args.Add(\"@offset\", (page - 1) * MdPageSize{i});");
        L($"        var rows = (await connection.QueryAsync<{dClass}>({Lit("SELECT " + SelectColumns(d) + " FROM " + QuoteIdent(d.Name))} + where + {(tools ? "order" : Lit(OrderSql(d)))} + {Lit(Db.Page)}, args)).AsList();");
        L(string.Empty);
        foreach (var (field, _) in columns.Where(c => ShowsLookupText(c.Field)))
        {
            var prop = PropName(dClass, field);
            L($"        var lk_{prop} = await {MdLookup(i, prop)}.TextsAsync(connection, rows.Select(x => (object?)x.{prop}));");
        }

        L("        var back = fragment ? Route : MasterDetailKit.Here(request);");
        if (changeGuard)
        {
            L($"        var mayChange = Permissions.Allowed(context.User, {d.EditMinLevel});");
        }

        L("        var grid = new StringBuilder();");
        var gridId = tools ? " id=\"md-grid-" + i + "\"" : string.Empty;
        L($"        grid.Append({Lit("<div class=\"mt-4\"" + gridId + " data-md-grid=\"" + i + "\"><div class=\"d-flex align-items-center mb-2\"><h2 class=\"h5 mb-0 me-auto\">")}).Append(Html.Encode({MdCaptionExpr(md)})).Append(\"</h2>\");");
        if (tools)
        {
            L($"        if (tools) grid.Append(GridKit.DetailTools(request, {i}));");
        }

        if (canAdd)
        {
            var presetQuery = string.Join(" + \"&\" + ", md.Pairs.Select(p =>
                Lit(Uri.EscapeDataString("m_" + p.Detail.Name) + "=") + " + Uri.EscapeDataString(" + MdValueText(p.Master, "item." + PropName(masterClass, p.Master)) + ")"));
            var conds = new List<string>();
            if (changeGuard)
            {
                conds.Add("mayChange");
            }

            foreach (var (m, _) in md.Pairs.Where(p => !p.Master.IsRequired))
            {
                var expr = "item." + PropName(masterClass, m);
                conds.Add(m.Type == FieldType.Text ? $"!string.IsNullOrEmpty({expr})" : $"{expr} is not null");
            }

            var href = $"{Lit(detailRoute + "/add?")} + {presetQuery} + {Lit("&returnUrl=")} + Uri.EscapeDataString(back)";
            var link = $"grid.Append(Html.Link({href}, {Ui("add")}, \"btn btn-sm btn-primary\"));";
            L(conds.Count == 0 ? $"        {link}" : $"        if ({string.Join(" && ", conds)}) {link}");
        }

        L("        grid.Append(\"</div>\");");
        L("        grid.Append(\"<div class=\\\"table-responsive\\\"><table class=\\\"table table-sm table-striped align-middle\\\"><thead><tr>\");");
        foreach (var (field, width) in columns)
        {
            var header = $"grid.Append({Lit("<th" + CellAttributes(field, true, width) + ">")}).Append(Html.Encode({FieldText(field)})).Append(\"</th>\");";
            if (tools && field.Ui.Sortable && field.Type != FieldType.Binary)
            {
                L($"        if (tools) grid.Append(GridKit.DetailHeader(request, {i}, {Lit(field.Name)}, {FieldText(field)}, {Lit(CellAttributes(field, true, width))}));");
                L($"        else {header}");
            }
            else
            {
                L($"        {header}");
            }
        }

        if (showActions)
        {
            L("        grid.Append(\"<th></th>\");");
        }

        L("        grid.Append(\"</tr></thead><tbody>\");");
        L("        foreach (var row in rows)");
        L("        {");
        L("            grid.Append(\"<tr>\");");
        foreach (var (field, _) in columns)
        {
            var prop = PropName(dClass, field);
            string value;
            if (!NeedsFmt(field))
            {
                value = $"Html.Encode(row.{prop})";
            }
            else
            {
                var display = ShowsLookupText(field) ? $", LookupSource.Pick(lk_{prop}, row.{prop})" : string.Empty;
                value = $"{MdFmt(i, prop)}.Render(row.{prop}{display})";
            }

            L($"            grid.Append({Lit("<td" + CellAttributes(field, false) + ">")}).Append({value}).Append(\"</td>\");");
        }

        if (showActions)
        {
            L("            grid.Append(\"<td class=\\\"text-nowrap\\\">\");");
            var key = MdRowKey(d, "row");
            if (canView)
            {
                L($"            grid.Append(Html.Link({Lit(detailRoute + "/view/")} + {key}, {Ui("view")})).Append(' ');");
            }

            var change = changeGuard ? "if (mayChange) " : string.Empty;
            if (canEdit)
            {
                L($"            {change}grid.Append(Html.Link({Lit(detailRoute + "/edit/")} + {key} + {Lit("?returnUrl=")} + Uri.EscapeDataString(back), {Ui("edit")}, \"btn btn-sm btn-outline-secondary\")).Append(' ');");
            }

            if (canDelete)
            {
                L($"            {change}grid.Append(Html.Link({Lit(detailRoute + "/delete/")} + {key} + {Lit("?returnUrl=")} + Uri.EscapeDataString(back), {Ui("delete")}, \"btn btn-sm btn-outline-danger\"));");
            }

            L("            grid.Append(\"</td>\");");
        }

        L("            grid.Append(\"</tr>\");");
        L("        }");
        var width2 = columns.Count + (showActions ? 1 : 0);
        L($"        if (rows.Count == 0) grid.Append(\"<tr><td colspan=\\\"{Math.Max(1, width2)}\\\" class=\\\"text-muted\\\">{UiInText("detailNoRecords")}</td></tr>\");");
        L("        grid.Append(\"</tbody></table></div>\");");
        var fragmentBase = $"Route + \"/details/{i}/\" + " + string.Join(" + \"/\" + ", KeyPropExprs(masterClass, md.Master).Select(v => $"Uri.EscapeDataString({v}.ToString())"));
        L($"        grid.Append(MasterDetailKit.Pager(request, {Lit("dp" + i)}, page, pageCount, fragment ? {fragmentBase} : null));");
        L("        grid.Append(\"</div>\");");
        L("        return grid.ToString();");
        L("    }");
        L(string.Empty);
        _localizedTable = savedTable;
    }

    // ---- presets of a detail table --------------------------------------------------------------

    private static bool MdNeedsPresets(TableInfo table) =>
        IsDetailTable(table) && table.PrimaryKeys.Count > 0 && (table.Pages.Add || table.Pages.Copy);

    private static void AppendMdPresets(StringBuilder sb, string className, TableInfo table)
    {
        var masters = MdMasters(table);
        void L(string line) => sb.AppendLine(line);
        L("    private static async Task<IReadOnlyDictionary<string, string>?> ReadPresets(HttpContext context, SqliteConnection connection)");
        L("    {");
        L("        var query = context.Request.Query;");
        L("        var result = new Dictionary<string, string>(StringComparer.Ordinal);");
        for (var n = 0; n < masters.Count; n++)
        {
            var md = masters[n];
            var fields = md.Pairs.Select(p => Lit(p.Detail.Name));
            L($"        switch (MasterDetailKit.PresetState(query, new[] {{ {string.Join(", ", fields)} }}, out var pv{n}))");
            L("        {");
            L("            case 0:");
            L("                break;");
            L("            case 1:");
            if (md.Master.ViewMinLevel > 0)
            {
                L($"                if (!Permissions.Allowed(context.User, {md.Master.ViewMinLevel}))");
                L("                {");
                L("                    return null;");
                L("                }");
                L(string.Empty);
            }

            for (var j = 0; j < md.Pairs.Count; j++)
            {
                L($"                if (MasterDetailKit.Parse(pv{n}[{j}], {Lit(MdKindOf(md.Pairs[j].Master))}) is not {{ }} pp{n}_{j})");
                L("                {");
                L("                    return null;");
                L("                }");
                L(string.Empty);
            }

            var where = string.Join(" AND ", md.Pairs.Select((p, j) => QuoteIdent(p.Master.Name) + " = @p" + j));
            var masterOwned = IsOwned(md.Master);
            if (masterOwned)
            {
                where += " AND " + OwnerCondition(md.Master);
            }

            L($"                var check{n} = new DynamicParameters();");
            for (var j = 0; j < md.Pairs.Count; j++)
            {
                L($"                check{n}.Add(\"@p{j}\", pp{n}_{j});");
            }

            if (masterOwned)
            {
                L($"                check{n}.Add(\"@owner\", context.User.Identity?.Name ?? string.Empty);");
            }

            L($"                if (await connection.ExecuteScalarAsync<long>({Lit("SELECT COUNT(*) FROM " + QuoteIdent(md.Master.Name) + " WHERE " + where)}, check{n}) == 0)");
            L("                {");
            L("                    return null;");
            L("                }");
            L(string.Empty);
            for (var j = 0; j < md.Pairs.Count; j++)
            {
                L($"                result[{Lit(md.Pairs[j].Detail.Name)}] = pv{n}[{j}];");
            }

            L("                break;");
            L("            default:");
            L("                return null;");
            L("        }");
            L(string.Empty);
        }

        L("        return result;");
        L("    }");
        L(string.Empty);

        L($"    private static void ApplyPresets({className} item, IReadOnlyDictionary<string, string> presets)");
        L("    {");
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var k = 0;
        foreach (var md in masters)
        {
            foreach (var (_, d) in md.Pairs)
            {
                if (!done.Add(d.Name))
                {
                    continue;
                }

                var prop = PropName(className, d);
                var kind = MdKindOf(d);
                (string pattern, string assign) = d.Type switch
                {
                    FieldType.Integer => ("long", "v" + k),
                    FieldType.Decimal => ("decimal", "v" + k),
                    FieldType.Boolean => ("long", "v" + k + " != 0"),
                    FieldType.Date or FieldType.DateTime => ("DateTime", "v" + k),
                    FieldType.Text => ("string", "v" + k),
                    _ => (string.Empty, string.Empty),
                };
                if (pattern.Length > 0)
                {
                    L($"        if (presets.TryGetValue({Lit(d.Name)}, out var raw{k}) && MasterDetailKit.Parse(raw{k}, {Lit(kind)}) is {pattern} v{k})");
                    L("        {");
                    L($"            item.{prop} = {assign};");
                    L("        }");
                    L(string.Empty);
                }

                k++;
            }
        }

        L("    }");
        L(string.Empty);
    }
}
