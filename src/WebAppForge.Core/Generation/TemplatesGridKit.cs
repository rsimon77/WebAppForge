using System.Text;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    /// <summary>True when the list page of <paramref name="table"/> has the filter builder, the quick filter or the reset button.</summary>
    internal static bool UsesGridTools(TableInfo table)
    {
        var design = table.Designs.List;
        return design.FilterBuilder || design.QuickFilter || design.ResetButton;
    }

    /// <summary>
    /// The columns the filter builder and the quick filter can use: fields the list shows or that are searchable, except binary ones.
    /// The generated copy of the table no longer has secret fields.
    /// </summary>
    private static List<FieldInfo> GridFilterFields(TableInfo table)
        => table.Fields.Where(f => f.Type != FieldType.Binary && (f.ShowInList || f.Searchable)).ToList();

    /// <summary>GridKit.Text, GridKit.Number, GridKit.Date or GridKit.Boolean. A field shown as a lookup or option text is filtered as text.</summary>
    private static string GridKind(FieldInfo field)
    {
        if (ReportLookup(field) is not null || field.Ui.Options.Any(o => o.Text.Length > 0))
        {
            return "GridKit.Text";
        }

        if (ReportValidator.IsDateField(field))
        {
            return "GridKit.Date";
        }

        return field.Type switch
        {
            FieldType.Boolean => "GridKit.Boolean",
            FieldType.Integer or FieldType.Decimal => "GridKit.Number",
            _ => "GridKit.Text",
        };
    }

    /// <summary>SQL of a filter column: the day (yyyy-MM-dd) of a date, the display text of a lookup or option, else the column.</summary>
    private static string GridSql(TableInfo table, FieldInfo field)
        => GridKind(field) == "GridKit.Date" ? $"date({DateTextSql(table, field)})" : GroupSql(table, field, DateInterval.None);

    /// <summary>
    /// GridColumns (the filter columns) and GridSorts (the sortable columns, in the order of SortFields) of a page class whose list
    /// uses the grid tools. They are properties because the captions are translated per request on a localized site.
    /// </summary>
    private static void AppendGridStatics(StringBuilder sb, TableInfo table)
    {
        if (!UsesGridTools(table))
        {
            return;
        }

        var columns = GridFilterFields(table).Select(f => $"new({Lit(f.Name)}, {FieldText(f)}, {Lit(GridSql(table, f))}, {GridKind(f)})");
        sb.AppendLine($"    private static GridKit.Column[] GridColumns => [{string.Join(", ", columns)}];");
        var sorts = table.Fields.Where(f => f.ShowInList && f.Ui.Sortable && f.Type != FieldType.Binary).Select(f => $"({Lit(f.Name)}, {FieldText(f)})");
        sb.AppendLine($"    private static (string Field, string Caption)[] GridSorts => [{string.Join(", ", sorts)}];");
    }

    /// <summary>
    /// Web/GridKit.cs of the generated site: the filter builder, the quick filter, multi-column sorting and the reset button of the
    /// list pages. Written only for sites where a list uses them.
    /// </summary>
    public static string GridKitRuntime(string ns)
    {
        var texts = string.Join("\n", UiStrings.Grid.Select(s => $"        {Lit(s.Id)} => {Ui(s.Id)},"));
        var text = ("""
            using System.Globalization;
            using System.Text;
            @@USING@@
            namespace @@NS@@.Web;

            /// <summary>
            /// Filter builder, quick filter, multi-column sorting and reset of the generated list pages. No request text reaches the SQL:
            /// fields and operators are looked up in the page's whitelist, the SQL comes from that whitelist and every value is a parameter.
            /// </summary>
            public static class GridKit
            {
                public const int Text = 0;
                public const int Number = 1;
                public const int Date = 2;
                public const int Boolean = 3;

                /// <summary>Most conditions of the filter builder.</summary>
                public const int MaxConditions = 8;

                /// <summary>Sort levels of the filter builder.</summary>
                public const int SortLevels = 3;

                /// <summary>Most terms of the quick filter.</summary>
                public const int MaxTerms = 8;

                /// <summary>A column that can be filtered: its field name, caption, SQL expression and kind (Text, Number, Date or Boolean).</summary>
                public sealed record Column(string Field, string Caption, string Sql, int Kind);

                /// <summary>A condition of the filter builder. Valid is false when its value is not a number or date the column needs.</summary>
                public sealed record Condition(int Index, Column Column, string Operator, string Value, string Value2, bool Valid);

                /// <summary>A term of the quick filter: the text, the column it is limited to (or null for any column) and whether it excludes.</summary>
                public sealed record Term(Column? Column, string Text, bool Exclude);

                /// <summary>The operators, their caption and the column kinds they apply to, in the order of the operator list.</summary>
                private static readonly (string Code, string Caption, int[] Kinds)[] Operators =
                [
                    ("has", "gridContains", [Text]),
                    ("nhas", "gridNotContains", [Text]),
                    ("eq", "=", [Text, Number, Date]),
                    ("ne", "≠", [Text, Number, Date]),
                    ("sw", "gridStartsWith", [Text]),
                    ("ew", "gridEndsWith", [Text]),
                    ("gt", ">", [Number, Date]),
                    ("ge", "≥", [Number, Date]),
                    ("lt", "<", [Number, Date]),
                    ("le", "≤", [Number, Date]),
                    ("bt", "gridBetween", [Number, Date]),
                    ("yes", "gridIsYes", [Boolean]),
                    ("no", "gridIsNo", [Boolean]),
                    ("null", "gridIsEmpty", [Text, Number, Date, Boolean]),
                    ("nnull", "gridNotEmpty", [Text, Number, Date, Boolean]),
                ];

                /// <summary>A fixed text of the panel.</summary>
                public static string T(string id) => id switch
                {
            @@TEXTS@@
                    _ => id,
                };

                private static string OperatorCaption(string code)
                {
                    var caption = Array.Find(Operators, o => o.Code == code).Caption ?? code;
                    return caption.StartsWith("grid", StringComparison.Ordinal) ? T(caption) : caption;
                }

                private static bool NeedsValue(string code) => code is not ("yes" or "no" or "null" or "nnull");

                /// <summary>True for the query parameters of the filter builder: fm, ffN, foN, fvN, fxN, sN and dN.</summary>
                private static bool IsBuilderKey(string key)
                {
                    if (key == "fm")
                    {
                        return true;
                    }

                    var prefix = key.StartsWith("ff", StringComparison.Ordinal) || key.StartsWith("fo", StringComparison.Ordinal)
                        || key.StartsWith("fv", StringComparison.Ordinal) || key.StartsWith("fx", StringComparison.Ordinal) ? 2
                        : key.StartsWith('s') || key.StartsWith('d') ? 1 : 0;
                    return prefix > 0 && key.Length > prefix && key.AsSpan(prefix).IndexOfAnyExceptInRange('0', '9') < 0;
                }

                /// <summary>True for every query parameter that searches, filters or sorts the list.</summary>
                private static bool IsGridKey(string key)
                    => key is "q" or "qf" or "sort" or "dir" || key.StartsWith("f_", StringComparison.Ordinal) || IsBuilderKey(key);

                /// <summary>True when the list is searched, filtered or sorted by the request.</summary>
                public static bool Active(HttpRequest request)
                {
                    foreach (var pair in request.Query)
                    {
                        if (IsGridKey(pair.Key) && pair.Key != "dir" && pair.Value.Any(v => !string.IsNullOrWhiteSpace(v)))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                /// <summary>The conditions of the request that name a known column and an operator of its kind and have the values they need.</summary>
                public static List<Condition> Conditions(HttpRequest request, Column[] columns)
                {
                    var list = new List<Condition>();
                    for (var i = 0; i < MaxConditions; i++)
                    {
                        var field = request.Query["ff" + i].ToString();
                        var column = Array.Find(columns, c => string.Equals(c.Field, field, StringComparison.Ordinal));
                        var code = request.Query["fo" + i].ToString();
                        if (column is null || !Array.Exists(Operators, o => o.Code == code && o.Kinds.Contains(column.Kind)))
                        {
                            continue;
                        }

                        var value = request.Query["fv" + i].ToString().Trim();
                        var value2 = request.Query["fx" + i].ToString().Trim();
                        if (NeedsValue(code) && (value.Length == 0 || (code == "bt" && value2.Length == 0)))
                        {
                            continue;
                        }

                        var valid = !NeedsValue(code) || column.Kind == Text
                            || (Parse(column.Kind, value) is not null && (code != "bt" || Parse(column.Kind, value2) is not null));
                        list.Add(new Condition(i, column, code, value, value2, valid));
                    }

                    return list;
                }

                /// <summary>A number (as double) or a date (as yyyy-MM-dd text) of the value, or null when it is not one.</summary>
                private static object? Parse(int kind, string value)
                {
                    if (kind == Number)
                    {
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                            || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
                        {
                            return number;
                        }

                        return null;
                    }

                    if (kind == Date)
                    {
                        string[] formats = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];
                        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                        {
                            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        }

                        return null;
                    }

                    return value;
                }

                /// <summary>Escapes the LIKE wildcards of a value, so % and _ match themselves (the pattern uses ESCAPE '\').</summary>
                private static string Like(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

                private static string Add(List<KeyValuePair<string, object>> parameters, object value)
                {
                    var name = "@g" + parameters.Count;
                    parameters.Add(new KeyValuePair<string, object>(name, value));
                    return name;
                }

                private static string ConditionSql(Condition c, List<KeyValuePair<string, object>> parameters)
                {
                    var sql = c.Column.Sql;
                    var text = "COALESCE(CAST(" + sql + " AS TEXT), '')";
                    switch (c.Operator)
                    {
                        case "null":
                            return "(" + sql + " IS NULL OR CAST(" + sql + " AS TEXT) = '')";
                        case "nnull":
                            return "(" + sql + " IS NOT NULL AND CAST(" + sql + " AS TEXT) <> '')";
                        case "yes":
                            return sql + " = 1";
                        case "no":
                            return sql + " = 0";
                        case "has":
                            return text + " LIKE " + Add(parameters, "%" + Like(c.Value) + "%") + " ESCAPE '\\'";
                        case "nhas":
                            return text + " NOT LIKE " + Add(parameters, "%" + Like(c.Value) + "%") + " ESCAPE '\\'";
                        case "sw":
                            return text + " LIKE " + Add(parameters, Like(c.Value) + "%") + " ESCAPE '\\'";
                        case "ew":
                            return text + " LIKE " + Add(parameters, "%" + Like(c.Value)) + " ESCAPE '\\'";
                    }

                    if (c.Column.Kind == Text)
                    {
                        var name = Add(parameters, c.Value);
                        return c.Operator == "eq" ? text + " = " + name + " COLLATE NOCASE" : text + " <> " + name + " COLLATE NOCASE";
                    }

                    var value = Parse(c.Column.Kind, c.Value)!;
                    if (c.Operator == "bt")
                    {
                        var high = Parse(c.Column.Kind, c.Value2)!;
                        if (Comparer<object>.Default.Compare(value, high) > 0)
                        {
                            (value, high) = (high, value);
                        }

                        return sql + " BETWEEN " + Add(parameters, value) + " AND " + Add(parameters, high);
                    }

                    var parameter = Add(parameters, value);
                    return c.Operator switch
                    {
                        "eq" => sql + " = " + parameter,
                        "ne" => "(" + sql + " IS NULL OR " + sql + " <> " + parameter + ")",
                        "gt" => sql + " > " + parameter,
                        "ge" => sql + " >= " + parameter,
                        "lt" => sql + " < " + parameter,
                        _ => sql + " <= " + parameter,
                    };
                }

                /// <summary>
                /// The terms of a quick filter. Words are separated by spaces, "a phrase" is one term, column:value limits a term to a column
                /// (by field name or caption, ignoring case) and a leading - excludes the rows that match.
                /// </summary>
                public static List<Term> Terms(string text, Column[] columns)
                {
                    var terms = new List<Term>();
                    var i = 0;
                    while (i < text.Length && terms.Count < MaxTerms)
                    {
                        while (i < text.Length && char.IsWhiteSpace(text[i]))
                        {
                            i++;
                        }

                        if (i >= text.Length)
                        {
                            break;
                        }

                        var exclude = false;
                        if (text[i] == '-' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
                        {
                            exclude = true;
                            i++;
                        }

                        Column? column = null;
                        var j = i;
                        while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_'))
                        {
                            j++;
                        }

                        if (j > i && j + 1 < text.Length && text[j] == ':' && !char.IsWhiteSpace(text[j + 1]))
                        {
                            var name = text[i..j];
                            column = Array.Find(columns, c => string.Equals(c.Field, name, StringComparison.OrdinalIgnoreCase))
                                ?? Array.Find(columns, c => string.Equals(c.Caption, name, StringComparison.OrdinalIgnoreCase));
                            if (column is not null)
                            {
                                i = j + 1;
                            }
                        }

                        string word;
                        if (text[i] == '"')
                        {
                            var end = text.IndexOf('"', i + 1);
                            if (end < 0)
                            {
                                end = text.Length;
                            }

                            word = text[(i + 1)..end];
                            i = Math.Min(text.Length, end + 1);
                        }
                        else
                        {
                            j = i;
                            while (j < text.Length && !char.IsWhiteSpace(text[j]))
                            {
                                j++;
                            }

                            // An unknown column prefix stays part of the word, so "a:b" is searched as it is.
                            word = text[i..j];
                            i = j;
                        }

                        if (word.Trim().Length > 0)
                        {
                            terms.Add(new Term(column, word.Trim(), exclude));
                        }
                    }

                    return terms;
                }

                /// <summary>
                /// The WHERE part (without the keyword, empty when nothing applies) of the filter builder and the quick filter. Conditions
                /// are joined with AND, or with OR when fm=any; every quick filter term must match.
                /// </summary>
                public static string Where(HttpRequest request, Column[] columns, List<KeyValuePair<string, object>> parameters, bool builder, bool quick)
                {
                    var clauses = new List<string>();
                    if (builder)
                    {
                        var parts = Conditions(request, columns).Where(c => c.Valid).Select(c => ConditionSql(c, parameters)).ToList();
                        if (parts.Count > 0)
                        {
                            clauses.Add("(" + string.Join(request.Query["fm"].ToString() == "any" ? " OR " : " AND ", parts) + ")");
                        }
                    }

                    if (quick)
                    {
                        foreach (var term in Terms(request.Query["qf"].ToString(), columns))
                        {
                            Column[] targets = term.Column is null ? columns.Where(c => c.Kind != Boolean).ToArray() : [term.Column];
                            if (targets.Length == 0)
                            {
                                continue;
                            }

                            var name = Add(parameters, "%" + Like(term.Text) + "%");
                            var any = string.Join(" OR ", targets.Select(c => "COALESCE(CAST(" + c.Sql + " AS TEXT), '') LIKE " + name + " ESCAPE '\\'"));
                            clauses.Add(term.Exclude ? "NOT (" + any + ")" : "(" + any + ")");
                        }
                    }

                    return string.Join(" AND ", clauses);
                }

                /// <summary>The ORDER BY of the sort levels s0..s2 (with d0..d2 = desc), or null. Fields are matched against the page's whitelist.</summary>
                public static string? Order(HttpRequest request, string[] fields, string[] columns)
                {
                    var parts = new List<string>();
                    var used = new HashSet<int>();
                    for (var level = 0; level < SortLevels; level++)
                    {
                        var index = Array.IndexOf(fields, request.Query["s" + level].ToString());
                        if (index >= 0 && used.Add(index))
                        {
                            parts.Add(columns[index] + (request.Query["d" + level].ToString() == "desc" ? " DESC" : " ASC"));
                        }
                    }

                    return parts.Count == 0 ? null : " ORDER BY " + string.Join(", ", parts);
                }

                /// <summary>The route with the query of the request, except the parameters <paramref name="skip"/> picks and page.</summary>
                private static string Url(HttpRequest request, string route, Func<string, bool> skip)
                {
                    var query = new StringBuilder();
                    foreach (var pair in request.Query)
                    {
                        if (pair.Key == "page" || skip(pair.Key))
                        {
                            continue;
                        }

                        foreach (var value in pair.Value)
                        {
                            query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value ?? ""));
                        }
                    }

                    return route + query;
                }

                /// <summary>Hidden inputs that keep the query of the request, except the parameters <paramref name="skip"/> picks and page.</summary>
                private static void Hidden(StringBuilder html, HttpRequest request, Func<string, bool> skip)
                {
                    foreach (var pair in request.Query)
                    {
                        if (pair.Key == "page" || skip(pair.Key))
                        {
                            continue;
                        }

                        foreach (var value in pair.Value)
                        {
                            html.Append("<input type=\"hidden\" name=\"").Append(Html.Attr(pair.Key)).Append("\" value=\"").Append(Html.Attr(value ?? "")).Append("\">");
                        }
                    }
                }

                private static string Option(string value, string caption, bool selected, string attributes = "")
                    => "<option value=\"" + Html.Attr(value) + "\"" + attributes + (selected ? " selected" : "") + ">" + Html.Encode(caption) + "</option>";

                private static string KindsOf(int[] kinds) => string.Join(" ", kinds);

                /// <summary>
                /// The toolbar above a list: the quick filter box, the filter and sort panel (a dropdown that needs no script) and the reset
                /// button, followed by the active conditions and sort levels, each with a link that removes it.
                /// </summary>
                public static string Panel(HttpRequest request, string route, Column[] columns, (string Field, string Caption)[] sorts, bool builder, bool quick, bool reset)
                {
                    var html = new StringBuilder("<div class=\"gk mb-3\"><style>")
                        .Append(".gk-builder>summary{list-style:none}.gk-builder>summary::-webkit-details-marker{display:none}")
                        .Append(".gk-builder>form{position:absolute;z-index:1050;top:calc(100% + .25rem);left:0;width:min(44rem,92vw)}")
                        .Append(".gk-row,.gk-sort{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:.25rem;margin-bottom:.25rem}")
                        .Append("@media (max-width:576px){.gk-row,.gk-sort{grid-template-columns:repeat(2,minmax(0,1fr))}}")
                        .Append(".gk-chip{font-size:.8rem;font-weight:400;padding:.35em .75em}.gk-chip a{color:inherit;text-decoration:none;margin-left:.4rem}")
                        .Append("</style><div class=\"d-flex flex-wrap gap-2 align-items-center\">");
                    List<Condition> conditions = builder ? Conditions(request, columns) : [];
                    var levels = new List<(string Caption, bool Descending, int Level)>();
                    if (builder && request.Query["sort"].ToString().Length == 0)
                    {
                        var used = new HashSet<string>();
                        for (var level = 0; level < SortLevels; level++)
                        {
                            var field = request.Query["s" + level].ToString();
                            var sort = Array.Find(sorts, s => s.Field == field);
                            if (sort.Field is not null && used.Add(field))
                            {
                                levels.Add((sort.Caption, request.Query["d" + level].ToString() == "desc", level));
                            }
                        }
                    }

                    if (quick)
                    {
                        var value = request.Query["qf"].ToString();
                        html.Append("<form method=\"get\" action=\"").Append(Html.Attr(route)).Append("\" role=\"search\" class=\"gk-quick\">");
                        Hidden(html, request, k => k == "qf");
                        html.Append("<div class=\"input-group input-group-sm\"><input type=\"search\" name=\"qf\" class=\"form-control\" style=\"min-width:16rem\" placeholder=\"")
                            .Append(Html.Attr(T("gridQuick"))).Append("\" aria-label=\"").Append(Html.Attr(T("gridQuick"))).Append("\" title=\"").Append(Html.Attr(T("gridQuickHint")))
                            .Append("\" value=\"").Append(Html.Attr(value)).Append("\"><button type=\"submit\" class=\"btn btn-outline-secondary\">")
                            .Append(Html.Encode(T("gridApply"))).Append("</button></div></form>");
                    }

                    if (builder)
                    {
                        var count = conditions.Count + levels.Count;
                        html.Append("<details class=\"gk-builder position-relative\"").Append(conditions.Any(c => !c.Valid) ? " open" : "").Append('>')
                            .Append("<summary class=\"btn btn-sm btn-outline-secondary\">").Append(Html.Encode(T("gridFilter")))
                            .Append(count > 0 ? " <span class=\"badge text-bg-primary\">" + count + "</span>" : "").Append("</summary>");
                        html.Append("<form method=\"get\" action=\"").Append(Html.Attr(route)).Append("\" class=\"card card-body shadow p-3\">");
                        Hidden(html, request, k => IsBuilderKey(k) || k is "sort" or "dir");
                        var any = request.Query["fm"].ToString() == "any";
                        html.Append("<div class=\"d-flex align-items-center gap-2 mb-2\"><label class=\"small text-muted\" for=\"gk-fm\">").Append(Html.Encode(T("gridMatch")))
                            .Append("</label><select id=\"gk-fm\" name=\"fm\" class=\"form-select form-select-sm w-auto\">")
                            .Append(Option("all", T("gridMatchAll"), !any)).Append(Option("any", T("gridMatchAny"), any)).Append("</select></div>");

                        var rows = conditions.Select(c => (c.Column.Field, c.Operator, c.Value, c.Value2, c.Valid)).ToList();
                        if (rows.Count < MaxConditions)
                        {
                            rows.Add((string.Empty, "has", string.Empty, string.Empty, true));
                        }

                        for (var i = 0; i < rows.Count; i++)
                        {
                            var row = rows[i];
                            html.Append("<div class=\"gk-row\"><select name=\"ff").Append(i).Append("\" class=\"form-select form-select-sm gk-field\" aria-label=\"")
                                .Append(Html.Attr(T("gridColumn"))).Append("\">").Append(Option("", T("gridColumn"), row.Field.Length == 0, " data-kind=\"\""));
                            foreach (var column in columns)
                            {
                                html.Append(Option(column.Field, column.Caption, column.Field == row.Field, " data-kind=\"" + column.Kind + "\""));
                            }

                            html.Append("</select><select name=\"fo").Append(i).Append("\" class=\"form-select form-select-sm gk-op\" aria-label=\"").Append(Html.Attr(T("gridOperator"))).Append("\">");
                            foreach (var (code, _, kinds) in Operators)
                            {
                                html.Append(Option(code, OperatorCaption(code), code == row.Operator, " data-kinds=\"" + KindsOf(kinds) + "\""));
                            }

                            html.Append("</select><input name=\"fv").Append(i).Append("\" class=\"form-control form-control-sm gk-value").Append(row.Valid ? "" : " is-invalid")
                                .Append("\" aria-label=\"").Append(Html.Attr(T("gridValue"))).Append("\" placeholder=\"").Append(Html.Attr(T("gridValue"))).Append("\" value=\"").Append(Html.Attr(row.Value)).Append("\">")
                                .Append("<input name=\"fx").Append(i).Append("\" class=\"form-control form-control-sm gk-to").Append(row.Valid ? "" : " is-invalid")
                                .Append("\" aria-label=\"").Append(Html.Attr(T("gridAnd"))).Append("\" placeholder=\"").Append(Html.Attr(T("gridAnd"))).Append("\" value=\"").Append(Html.Attr(row.Value2)).Append("\"></div>");
                        }

                        if (sorts.Length > 0)
                        {
                            html.Append("<div class=\"small text-muted mt-2 mb-1\">").Append(Html.Encode(T("gridSortBy"))).Append("</div>");
                            for (var level = 0; level < SortLevels; level++)
                            {
                                var field = request.Query["sort"].ToString().Length > 0 ? "" : request.Query["s" + level].ToString();
                                var descending = request.Query["d" + level].ToString() == "desc";
                                html.Append("<div class=\"gk-sort\"><select name=\"s").Append(level).Append("\" class=\"form-select form-select-sm\" aria-label=\"")
                                    .Append(Html.Attr(level == 0 ? T("gridSortBy") : T("gridThenBy"))).Append("\">")
                                    .Append(Option("", level == 0 ? T("gridNoSort") : T("gridThenBy"), !Array.Exists(sorts, s => s.Field == field)));
                                foreach (var (name, caption) in sorts)
                                {
                                    html.Append(Option(name, caption, name == field));
                                }

                                html.Append("</select><select name=\"d").Append(level).Append("\" class=\"form-select form-select-sm\" aria-label=\"").Append(Html.Attr(T("gridDirection"))).Append("\">")
                                    .Append(Option("asc", T("gridAscending"), !descending)).Append(Option("desc", T("gridDescending"), descending)).Append("</select></div>");
                            }
                        }

                        html.Append("<div class=\"mt-2\"><button type=\"submit\" class=\"btn btn-sm btn-primary\">").Append(Html.Encode(T("gridApply"))).Append("</button></div></form></details>");
                    }

                    if (reset)
                    {
                        var pageSize = request.Query["pageSize"].ToString();
                        var href = route + (pageSize.Length > 0 ? "?pageSize=" + Uri.EscapeDataString(pageSize) : "");
                        html.Append(Active(request)
                            ? "<a class=\"btn btn-sm btn-outline-secondary gk-reset\" href=\"" + Html.Attr(href) + "\">" + Html.Encode(T("gridReset")) + "</a>"
                            : "<a class=\"btn btn-sm btn-outline-secondary gk-reset disabled\" aria-disabled=\"true\" tabindex=\"-1\">" + Html.Encode(T("gridReset")) + "</a>");
                    }

                    html.Append("</div>");
                    if (conditions.Count > 0 || levels.Count > 0)
                    {
                        var joiner = request.Query["fm"].ToString() == "any" ? T("gridOr") : T("gridAnd");
                        html.Append("<div class=\"d-flex flex-wrap gap-1 align-items-center mt-2\">");
                        for (var i = 0; i < conditions.Count; i++)
                        {
                            var c = conditions[i];
                            if (i > 0)
                            {
                                html.Append("<span class=\"text-muted small\">").Append(Html.Encode(joiner)).Append("</span>");
                            }

                            var index = c.Index.ToString(CultureInfo.InvariantCulture);
                            var remove = Url(request, route, k => k == "ff" + index || k == "fo" + index || k == "fv" + index || k == "fx" + index);
                            var value = !NeedsValue(c.Operator) ? "" : c.Operator == "bt" ? " " + c.Value + " – " + c.Value2 : " " + c.Value;
                            html.Append("<span class=\"gk-chip badge rounded-pill border text-body bg-body-tertiary").Append(c.Valid ? "" : " border-danger").Append("\">")
                                .Append(Html.Encode(c.Column.Caption + " " + OperatorCaption(c.Operator) + value))
                                .Append("<a href=\"").Append(Html.Attr(remove)).Append("\" aria-label=\"").Append(Html.Attr(T("gridRemove"))).Append("\" title=\"").Append(Html.Attr(T("gridRemove"))).Append("\">&times;</a></span>");
                        }

                        if (levels.Count > 0)
                        {
                            var caption = T("gridSortBy") + " " + string.Join(", ", levels.Select(l => l.Caption + (l.Descending ? " ↓" : " ↑")));
                            var remove = Url(request, route, k => k.Length > 1 && (k[0] == 's' || k[0] == 'd') && IsBuilderKey(k));
                            html.Append("<span class=\"gk-chip badge rounded-pill border text-body bg-body-tertiary\">").Append(Html.Encode(caption))
                                .Append("<a href=\"").Append(Html.Attr(remove)).Append("\" aria-label=\"").Append(Html.Attr(T("gridRemove"))).Append("\" title=\"").Append(Html.Attr(T("gridRemove"))).Append("\">&times;</a></span>");
                        }

                        html.Append("</div>");
                    }

                    if (builder)
                    {
                        // Shows only the operators of the chosen column, the second value only for "gridBetween" and date or number inputs where they fit.
                        html.Append("<script>document.querySelectorAll('.gk-row').forEach(function(r){var f=r.querySelector('.gk-field'),o=r.querySelector('.gk-op'),v=r.querySelector('.gk-value'),x=r.querySelector('.gk-to');")
                            .Append("function u(){var k=f.options[f.selectedIndex].getAttribute('data-kind');Array.prototype.forEach.call(o.options,function(p){p.hidden=k!==''&&p.getAttribute('data-kinds').split(' ').indexOf(k)<0;});")
                            .Append("if(o.options[o.selectedIndex].hidden){for(var i=0;i<o.options.length;i++){if(!o.options[i].hidden){o.selectedIndex=i;break;}}}")
                            .Append("var t=k==='2'?'date':k==='1'?'number':'text',n=['yes','no','null','nnull'].indexOf(o.value)<0;")
                            .Append("[v,x].forEach(function(e){var old=e.value;e.type=t;if(old&&!e.value){e.type='text';e.value=old;}if(t==='number'){e.step='any';}});")
                            .Append("v.hidden=!n;x.hidden=o.value!=='bt';}f.addEventListener('change',u);o.addEventListener('change',u);u();});</script>");
                    }

                    return html.Append("</div>").ToString();
                }
            }
            """).Replace("@@NS@@", ns);

        return text.Replace("@@USING@@\n", Localized ? $"using {ns}.Localization;\n" : string.Empty)
            .Replace("@@TEXTS@@", texts);
    }
}
