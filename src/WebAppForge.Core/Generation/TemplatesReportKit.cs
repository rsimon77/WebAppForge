namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    /// <summary>
    /// Web/ReportKit.cs of the generated site: aggregation, the summary and crosstab tables, SVG charts and the month calendar.
    /// Written only for sites with reports. Charts are drawn on the server as SVG, so reports need no script.
    /// </summary>
    public static string ReportKitRuntime(string ns)
    {
        var text = ("""
            using System.Globalization;
            using System.Net;
            using System.Text;
            using Microsoft.Data.Sqlite;
            @@USING@@
            namespace @@NS@@.Web;

            /// <summary>Runtime of the generated report pages. Every value written to HTML is encoded.</summary>
            public static class ReportKit
            {
                public const int Count = 0;
                public const int Sum = 1;
                public const int Average = 2;
                public const int Min = 3;
                public const int Max = 4;

                /// <summary>Most rows listed by a summary report with details. The totals still include every row.</summary>
                public const int MaxDetailRows = 5000;

                /// <summary>Most entries read for one calendar month.</summary>
                public const int MaxCalendarEntries = 5000;

                private static readonly string[] Palette = ["#0d6efd", "#fd7e14", "#198754", "#d63384", "#6f42c1", "#20c997", "#ffc107", "#dc3545", "#0dcaf0", "#6c757d"];

                public sealed record Texts(string Total, string GrandTotal, string Empty, string NoData, string MoreColumns, string MoreRows, string Previous, string Next, string ThisMonth, string Value,
                    string From, string To, string Apply, string Clear, string All, string RowCount, string Back);

                /// <summary>A pick-list filter: its query parameter, caption, the SQL compared with the chosen value and the query of its choices.</summary>
                public sealed record FilterField(string Name, string Caption, string Sql, string OptionsSql, bool Boolean);

                /// <summary>A row of the drill-down page: encoded cells and the view page link (or null).</summary>
                public sealed record ListedRow(string[] Cells, string? Href);

                /// <summary>One aggregate column: its heading, the function and whether its numbers have decimals.</summary>
                public sealed record Measure(string Caption, int Function, bool Decimals);

                public sealed record SummaryRow(object?[] Keys, Acc[] Values, string[]? Cells);

                public sealed record Cell(object? Row, object? Column, Acc Value);

                public sealed record CalendarEntry(string? Start, string? End, string Title, string? Href);

                public static Texts Text() => new(@@TOTAL@@, @@GRAND@@, @@EMPTY@@, @@NODATA@@, @@MORECOLUMNS@@, @@MOREROWS@@, @@PREVIOUS@@, @@NEXT@@, @@THISMONTH@@, @@VALUE@@,
                    @@FROM@@, @@TO@@, @@APPLY@@, @@CLEAR@@, @@ALL@@, @@ROWCOUNT@@, @@BACK@@);

                /// <summary>Most choices of a pick-list filter.</summary>
                public const int MaxFilterOptions = 500;

                /// <summary>Culture of month and day names.</summary>
                public static CultureInfo Culture() => @@CULTURE@@;

                /// <summary>Count, sum, minimum and maximum of the non-null values of one aggregate.</summary>
                public sealed class Acc
                {
                    public long Count { get; private set; }

                    public decimal Sum { get; private set; }

                    public object? Min { get; private set; }

                    public object? Max { get; private set; }

                    public void Add(object? value)
                    {
                        if (value is null or DBNull)
                        {
                            return;
                        }

                        var v = Normalize(value);
                        Count++;
                        if (v is decimal number)
                        {
                            Sum += number;
                        }

                        Extremes(v, v);
                    }

                    /// <summary>Adds the COUNT, SUM, MIN and MAX that the database computed for a group.</summary>
                    public void AddPartial(object? count, object? sum, object? min, object? max)
                    {
                        var n = count is null or DBNull ? 0L : Convert.ToInt64(count, CultureInfo.InvariantCulture);
                        if (n == 0)
                        {
                            return;
                        }

                        Count += n;
                        if (sum is not null and not DBNull && Normalize(sum) is decimal total)
                        {
                            Sum += total;
                        }

                        Extremes(min is null or DBNull ? null : Normalize(min), max is null or DBNull ? null : Normalize(max));
                    }

                    public void Merge(Acc other)
                    {
                        if (other.Count == 0)
                        {
                            return;
                        }

                        Count += other.Count;
                        Sum += other.Sum;
                        Extremes(other.Min, other.Max);
                    }

                    public object? Value(int function) => function switch
                    {
                        ReportKit.Count => Count,
                        ReportKit.Sum => Count == 0 ? null : Sum,
                        ReportKit.Average => Count == 0 ? null : Sum / Count,
                        ReportKit.Min => Min,
                        _ => Max,
                    };

                    private void Extremes(object? min, object? max)
                    {
                        if (min is not null && (Min is null || Compare(min, Min) < 0))
                        {
                            Min = min;
                        }

                        if (max is not null && (Max is null || Compare(max, Max) > 0))
                        {
                            Max = max;
                        }
                    }
                }

                public static object? Read(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);

                /// <summary>The aggregate of a group from four columns: COUNT, SUM, MIN and MAX of the value.</summary>
                public static Acc Partial(SqliteDataReader reader, int ordinal)
                {
                    var acc = new Acc();
                    acc.AddPartial(Read(reader, ordinal), Read(reader, ordinal + 1), Read(reader, ordinal + 2), Read(reader, ordinal + 3));
                    return acc;
                }

                /// <summary>The aggregate of one row.</summary>
                public static Acc Single(object? value)
                {
                    var acc = new Acc();
                    acc.Add(value);
                    return acc;
                }

                /// <summary>Numbers become decimals, so values of INTEGER and REAL columns compare and add up alike.</summary>
                public static object Normalize(object value) => value switch
                {
                    decimal d => d,
                    long or int or short or byte => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                    double d when !double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 7.9e28 => (decimal)d,
                    float f when !float.IsNaN(f) && !float.IsInfinity(f) => (decimal)f,
                    DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    string s => s,
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                };

                public static int Compare(object? a, object? b)
                {
                    if (a is null || b is null)
                    {
                        return a is null ? (b is null ? 0 : -1) : 1;
                    }

                    var x = Normalize(a);
                    var y = Normalize(b);
                    if (x is decimal dx && y is decimal dy)
                    {
                        return dx.CompareTo(dy);
                    }

                    if (x is decimal)
                    {
                        return -1;
                    }

                    return y is decimal ? 1 : string.CompareOrdinal((string)x, (string)y);
                }

                private static string KeyOf(object? value) => value switch
                {
                    null => "\u0000",
                    _ => Normalize(value) is decimal d ? "n:" + d.ToString(CultureInfo.InvariantCulture) : "s:" + Normalize(value),
                };

                /// <summary>HTML for a group, row or column value. Booleans are stored as 0 and 1.</summary>
                public static string Label(object? value, bool boolean, Texts texts)
                {
                    if (value is null)
                    {
                        return "<span class=\"text-muted\">" + Html.Encode(texts.Empty) + "</span>";
                    }

                    if (boolean && Normalize(value) is decimal flag)
                    {
                        return Html.Encode(flag != 0);
                    }

                    return Html.Encode(Normalize(value) is decimal number ? number.ToString(CultureInfo.InvariantCulture) : value);
                }

                /// <summary>Plain text of a value, used in chart labels (encoded by the chart).</summary>
                public static string PlainLabel(object? value, bool boolean, Texts texts)
                {
                    if (value is null)
                    {
                        return texts.Empty;
                    }

                    if (boolean && Normalize(value) is decimal flag)
                    {
                        return WebUtility.HtmlDecode(Html.Encode(flag != 0));
                    }

                    var v = Normalize(value);
                    return v is decimal number ? number.ToString(CultureInfo.InvariantCulture) : (string)v;
                }

                /// <summary>HTML for a detail cell.</summary>
                public static string CellText(object? value, bool boolean) =>
                    value is null ? string.Empty : boolean && Normalize(value) is decimal flag ? Html.Encode(flag != 0) : Html.Encode(value);

                /// <summary>HTML for an aggregate value: counts and integer sums without decimals, averages and decimal fields with two.</summary>
                public static string Format(object? value, Measure measure)
                {
                    if (value is null)
                    {
                        return string.Empty;
                    }

                    if (value is long count)
                    {
                        return count.ToString("#,0", CultureInfo.InvariantCulture);
                    }

                    if (Normalize(value) is decimal number)
                    {
                        var decimals = measure.Decimals || measure.Function == Average;
                        return number.ToString(decimals ? "#,0.00" : "#,0.##", CultureInfo.InvariantCulture);
                    }

                    return Html.Encode(value);
                }

                private static double? Number(object? value) =>
                    value is not null && Normalize(value) is decimal number ? (double)number : null;

                private static Acc[] NewAccs(int count)
                {
                    var accs = new Acc[count];
                    for (var i = 0; i < count; i++)
                    {
                        accs[i] = new Acc();
                    }

                    return accs;
                }

                private static void MergeInto(Acc[] target, Acc[] values)
                {
                    for (var i = 0; i < target.Length; i++)
                    {
                        target[i].Merge(values[i]);
                    }
                }

                /// <summary>The page body of a report: the description, then the report.</summary>
                public static string Page(string description, string body) =>
                    (description.Length == 0 ? string.Empty : "<p class=\"text-muted\">" + Html.Encode(description) + "</p>") + body;

                // ---- filters, drill-down and the rows page ------------------------------------------------

                private static bool IsFilterParameter(string key) => key is "from" or "to" || key.StartsWith("f_", StringComparison.Ordinal);

                private static DateOnly? DateParameter(HttpRequest request, string name) =>
                    DateOnly.TryParseExact(request.Query[name].ToString().Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

                /// <summary>The filter parameters of the request (from, to and f_*) as a query string ending in '&amp;', or empty.</summary>
                public static string FilterQuery(HttpRequest request)
                {
                    var sb = new StringBuilder();
                    foreach (var pair in request.Query.Where(p => IsFilterParameter(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        var value = pair.Value.ToString();
                        if (value.Length > 0)
                        {
                            sb.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value)).Append('&');
                        }
                    }

                    return sb.ToString();
                }

                /// <summary>
                /// The WHERE clause of a request: the static part, the From/To range on <paramref name="dateSql"/>, the chosen values of the
                /// pick lists, the keys k0, k1, ... of a drill-down link (n0=1 stands for an empty group) and <paramref name="extra"/>.
                /// Every value is a parameter; a key that cannot be read matches no row.
                /// </summary>
                public static (string Where, List<KeyValuePair<string, object>> Arguments) Where(HttpRequest request, string staticWhere, string dateSql,
                    FilterField[] filters, string[]? drillKeys, params string[] extra)
                {
                    var clauses = new List<string>();
                    var arguments = new List<KeyValuePair<string, object>>();
                    if (staticWhere.Length > 0)
                    {
                        clauses.Add(staticWhere);
                    }

                    if (dateSql.Length > 0)
                    {
                        if (DateParameter(request, "from") is { } from)
                        {
                            clauses.Add(dateSql + " >= @from");
                            arguments.Add(new("@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                        }

                        if (DateParameter(request, "to") is { } to)
                        {
                            clauses.Add(dateSql + " <= @to");
                            arguments.Add(new("@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                        }
                    }

                    for (var i = 0; i < filters.Length; i++)
                    {
                        var value = request.Query[filters[i].Name].ToString();
                        if (value.Length > 0)
                        {
                            clauses.Add(filters[i].Sql + " = @f" + i);
                            arguments.Add(new("@f" + i, value));
                        }
                    }

                    for (var i = 0; drillKeys is not null && i < drillKeys.Length; i++)
                    {
                        if (request.Query["n" + i].ToString() == "1")
                        {
                            clauses.Add("(" + drillKeys[i] + ") IS NULL");
                        }
                        else if (request.Query.ContainsKey("k" + i))
                        {
                            if (KeyValue(request.Query["k" + i].ToString()) is { } key)
                            {
                                clauses.Add("(" + drillKeys[i] + ") = @k" + i);
                                arguments.Add(new("@k" + i, key));
                            }
                            else
                            {
                                clauses.Add("0");
                            }
                        }
                    }

                    clauses.AddRange(extra);
                    return (clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses), arguments);
                }

                public static void Bind(SqliteCommand command, List<KeyValuePair<string, object>> arguments)
                {
                    foreach (var (name, value) in arguments)
                    {
                        command.Parameters.AddWithValue(name, value);
                    }
                }

                /// <summary>A group value in a link, typed so that it compares with the database value as read: i:42, r:4.5 or s:text.</summary>
                private static string? KeyText(object value) => value switch
                {
                    long or int or short or byte => "i:" + Convert.ToString(value, CultureInfo.InvariantCulture),
                    double d => "r:" + d.ToString("R", CultureInfo.InvariantCulture),
                    string text => "s:" + text,
                    _ => null,
                };

                private static object? KeyValue(string text)
                {
                    if (text.StartsWith("i:", StringComparison.Ordinal) && long.TryParse(text[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                    {
                        return whole;
                    }

                    if (text.StartsWith("r:", StringComparison.Ordinal) && double.TryParse(text[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
                    {
                        return real;
                    }

                    return text.StartsWith("s:", StringComparison.Ordinal) ? text[2..] : null;
                }

                /// <summary>The start of the drill-down links of a report: its rows page with the filters of this request.</summary>
                public static string DrillBase(HttpRequest request, string route) => route + "/rows?" + FilterQuery(request);

                /// <summary>A drill-down link naming the given group values, or null when one cannot be put in a link.</summary>
                public static string? Link(string drill, IEnumerable<(int Index, object? Value)> keys)
                {
                    var sb = new StringBuilder(drill);
                    foreach (var (index, value) in keys)
                    {
                        if (value is null)
                        {
                            sb.Append('n').Append(index).Append("=1&");
                            continue;
                        }

                        if (KeyText(value) is not { } text)
                        {
                            return null;
                        }

                        sb.Append('k').Append(index).Append('=').Append(Uri.EscapeDataString(text)).Append('&');
                    }

                    return sb.ToString().TrimEnd('&', '?');
                }

                /// <summary>The encoded <paramref name="html"/>, inside a link when there is one.</summary>
                public static string Linked(string html, string? href) =>
                    href is null || html.Length == 0 ? html : "<a class=\"link-underline-opacity-0\" href=\"" + Html.Attr(href) + "\">" + html + "</a>";

                /// <summary>The view page of a row: <paramref name="prefix"/>/key/key..., or null when a key is missing.</summary>
                public static string? ViewLink(string prefix, params object?[] keys) =>
                    keys.Any(k => k is null) ? null
                        : prefix + "/" + string.Join("/", keys.Select(k => Uri.EscapeDataString(Convert.ToString(k, CultureInfo.InvariantCulture) ?? string.Empty)));

                /// <summary>The choices of a pick-list filter: value and label, read from its options query.</summary>
                public static async Task<List<(string Value, string Label)>> ChoicesAsync(SqliteCommand command, FilterField filter)
                {
                    var texts = Text();
                    var choices = new List<(string, string)>();
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var value = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        choices.Add((value, PlainLabel(Read(reader, 1), filter.Boolean, texts)));
                    }

                    return choices;
                }

                /// <summary>The filter form above a report: the From/To range when <paramref name="dateCaption"/> is set, then one pick list per filter.</summary>
                public static string FilterForm(HttpRequest request, string route, string? dateCaption, FilterField[] filters, List<List<(string Value, string Label)>> choices)
                {
                    var texts = Text();
                    var sb = new StringBuilder("<form method=\"get\" action=\"").Append(Html.Attr(route)).Append("\" class=\"row g-2 align-items-end mb-3 report-filters\">");
                    if (dateCaption is not null)
                    {
                        foreach (var (name, label) in new[] { ("from", texts.From), ("to", texts.To) })
                        {
                            var value = DateParameter(request, name)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
                            sb.Append("<div class=\"col-auto\"><label class=\"form-label small mb-0\" for=\"rf-").Append(name).Append("\">")
                                .Append(Html.Encode(label + " (" + dateCaption + ")")).Append("</label><input class=\"form-control form-control-sm\" type=\"date\" id=\"rf-")
                                .Append(name).Append("\" name=\"").Append(name).Append("\" value=\"").Append(value).Append("\"></div>");
                        }
                    }

                    for (var i = 0; i < filters.Length; i++)
                    {
                        var chosen = request.Query[filters[i].Name].ToString();
                        sb.Append("<div class=\"col-auto\"><label class=\"form-label small mb-0\" for=\"rf-").Append(i).Append("\">").Append(Html.Encode(filters[i].Caption))
                            .Append("</label><select class=\"form-select form-select-sm\" id=\"rf-").Append(i).Append("\" name=\"").Append(Html.Attr(filters[i].Name)).Append("\">")
                            .Append("<option value=\"\">").Append(Html.Encode(texts.All)).Append("</option>");
                        foreach (var (value, label) in choices[i])
                        {
                            sb.Append("<option value=\"").Append(Html.Attr(value)).Append('"').Append(value == chosen ? " selected" : string.Empty).Append('>')
                                .Append(Html.Encode(label)).Append("</option>");
                        }

                        sb.Append("</select></div>");
                    }

                    // A calendar keeps the month it shows when the filters change.
                    if (request.Query["month"].ToString() is { Length: > 0 } month)
                    {
                        sb.Append(Html.Hidden("month", month));
                    }

                    sb.Append("<div class=\"col-auto\"><button type=\"submit\" class=\"btn btn-sm btn-primary\">").Append(Html.Encode(texts.Apply)).Append("</button> ")
                        .Append("<a class=\"btn btn-sm btn-outline-secondary\" href=\"").Append(Html.Attr(route)).Append("\">").Append(Html.Encode(texts.Clear)).Append("</a></div>");
                    return sb.Append("</form>").ToString();
                }

                /// <summary>The page number asked for with ?page=, 1 when missing or invalid.</summary>
                public static int PageOf(HttpRequest request) =>
                    int.TryParse(request.Query["page"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page > 0 ? page : 1;

                /// <summary>
                /// The drill-down page: a link back to the report with its filters, the groups the rows belong to, the number of rows, a page of
                /// rows linked to their view page, and the page links.
                /// </summary>
                public static string RowsPage(HttpRequest request, string route, string[] keyCaptions, string[] captions, List<ListedRow> rows, long total, int page, int pageSize)
                {
                    var texts = Text();
                    var sb = new StringBuilder("<p><a class=\"btn btn-sm btn-outline-secondary\" href=\"")
                        .Append(Html.Attr((route + "?" + FilterQuery(request)).TrimEnd('&', '?'))).Append("\">&lsaquo; ").Append(Html.Encode(texts.Back)).Append("</a></p>");
                    var groups = new List<string>();
                    for (var i = 0; i < keyCaptions.Length; i++)
                    {
                        if (request.Query["n" + i].ToString() == "1")
                        {
                            groups.Add("<strong>" + Html.Encode(keyCaptions[i]) + ":</strong> <span class=\"text-muted\">" + Html.Encode(texts.Empty) + "</span>");
                        }
                        else if (request.Query.ContainsKey("k" + i) && KeyValue(request.Query["k" + i].ToString()) is { } key)
                        {
                            groups.Add("<strong>" + Html.Encode(keyCaptions[i]) + ":</strong> " + Html.Encode(Normalize(key) is decimal number ? number.ToString(CultureInfo.InvariantCulture) : key));
                        }
                    }

                    if (groups.Count > 0)
                    {
                        sb.Append("<p class=\"report-keys\">").Append(string.Join(" &middot; ", groups)).Append("</p>");
                    }

                    sb.Append("<p class=\"text-muted report-count\">").Append(Html.Encode(string.Format(CultureInfo.InvariantCulture, texts.RowCount, total))).Append("</p>")
                        .Append("<div class=\"table-responsive\"><table class=\"table table-sm table-striped report-rows\"><thead><tr>");
                    foreach (var caption in captions)
                    {
                        sb.Append("<th>").Append(Html.Encode(caption)).Append("</th>");
                    }

                    sb.Append("</tr></thead><tbody>");
                    foreach (var row in rows)
                    {
                        sb.Append("<tr>");
                        for (var i = 0; i < row.Cells.Length; i++)
                        {
                            sb.Append("<td>").Append(i == 0 ? Linked(row.Cells[i], row.Href) : row.Cells[i]).Append("</td>");
                        }

                        sb.Append("</tr>");
                    }

                    if (rows.Count == 0)
                    {
                        sb.Append("<tr><td colspan=\"").Append(Math.Max(1, captions.Length)).Append("\" class=\"text-muted\">").Append(Html.Encode(texts.NoData)).Append("</td></tr>");
                    }

                    sb.Append("</tbody></table></div>");
                    var pages = (int)Math.Min(int.MaxValue, (total + pageSize - 1) / pageSize);
                    return sb.Append(Html.PageLinks(request, page, pages, route + "/rows")).ToString();
                }

                // ---- summary ----------------------------------------------------------------------------

                /// <summary>
                /// Grouped rows with a subtotal per group and a grand total. Without <paramref name="detailCaptions"/> each row is one
                /// innermost group; with them each row is a record listed under its group headings. Rows must be ordered by their keys.
                /// </summary>
                public static string Summary(string[] groupCaptions, bool[] booleanGroups, Measure[] measures, string[]? detailCaptions, bool grandTotal, IEnumerable<SummaryRow> rows, string? drill = null)
                {
                    var texts = Text();
                    var n = groupCaptions.Length;
                    var details = detailCaptions is not null;
                    var width = details ? detailCaptions!.Length : n + measures.Length;
                    var sb = new StringBuilder("<div class=\"table-responsive\"><table class=\"table table-sm report-summary\"><thead><tr>");
                    if (details)
                    {
                        foreach (var caption in detailCaptions!)
                        {
                            sb.Append("<th>").Append(Html.Encode(caption)).Append("</th>");
                        }
                    }
                    else
                    {
                        foreach (var caption in groupCaptions)
                        {
                            sb.Append("<th>").Append(Html.Encode(caption)).Append("</th>");
                        }

                        foreach (var measure in measures)
                        {
                            sb.Append("<th class=\"text-end\">").Append(Html.Encode(measure.Caption)).Append("</th>");
                        }
                    }

                    sb.Append("</tr></thead><tbody>");
                    var keys = new object?[n];
                    var levels = new Acc[n][];
                    var grand = NewAccs(measures.Length);
                    var any = false;
                    var listed = 0;
                    var truncated = false;

                    // The cells of the totals, each linked to the rows behind it when the report drills down. The keys name the groups.
                    string Totals(Acc[] accs, IEnumerable<object?> groupKeys)
                    {
                        var href = drill is null ? null : Link(drill, groupKeys.Select((value, index) => (index, value)));
                        var cells = new StringBuilder();
                        for (var i = 0; i < measures.Length; i++)
                        {
                            cells.Append("<td class=\"text-end\">").Append(Linked(Format(accs[i].Value(measures[i].Function), measures[i]), href)).Append("</td>");
                        }

                        return cells.ToString();
                    }

                    string InlineTotals(Acc[] accs)
                    {
                        var parts = new List<string>();
                        for (var i = 0; i < measures.Length; i++)
                        {
                            parts.Add(Html.Encode(measures[i].Caption) + ": " + Format(accs[i].Value(measures[i].Function), measures[i]));
                        }

                        return string.Join(" &middot; ", parts);
                    }

                    void Close(int level)
                    {
                        if (!details && level == n - 1)
                        {
                            return;
                        }

                        var label = Html.Encode(texts.Total) + " " + Label(keys[level], booleanGroups[level], texts);
                        sb.Append(details
                            ? "<tr class=\"table-light fw-semibold report-subtotal\"><td colspan=\"" + width + "\">" + label + " &middot; " + InlineTotals(levels[level]) + "</td></tr>"
                            : "<tr class=\"table-light fw-semibold report-subtotal\"><td colspan=\"" + n + "\">" + label + "</td>" + Totals(levels[level], keys.Take(level + 1)) + "</tr>");
                    }

                    foreach (var row in rows)
                    {
                        var changed = 0;
                        if (any)
                        {
                            changed = n;
                            for (var k = 0; k < n; k++)
                            {
                                if (Compare(keys[k], row.Keys[k]) != 0)
                                {
                                    changed = k;
                                    break;
                                }
                            }

                            for (var k = n - 1; k >= changed; k--)
                            {
                                Close(k);
                            }
                        }

                        for (var k = changed; k < n; k++)
                        {
                            keys[k] = row.Keys[k];
                            levels[k] = NewAccs(measures.Length);
                            if (details)
                            {
                                sb.Append("<tr class=\"table-secondary report-group\"><td colspan=\"").Append(width).Append("\" style=\"padding-left:")
                                    .Append((0.5 + k * 1.25).ToString(CultureInfo.InvariantCulture)).Append("rem\"><strong>")
                                    .Append(Html.Encode(groupCaptions[k])).Append(":</strong> ").Append(Label(row.Keys[k], booleanGroups[k], texts)).Append("</td></tr>");
                            }
                        }

                        any = true;
                        for (var k = 0; k < n; k++)
                        {
                            MergeInto(levels[k], row.Values);
                        }

                        MergeInto(grand, row.Values);
                        if (details)
                        {
                            if (listed < MaxDetailRows)
                            {
                                listed++;
                                sb.Append("<tr>");
                                foreach (var cell in row.Cells ?? Array.Empty<string>())
                                {
                                    sb.Append("<td>").Append(cell).Append("</td>");
                                }

                                sb.Append("</tr>");
                            }
                            else
                            {
                                truncated = true;
                            }
                        }
                        else if (n > 0)
                        {
                            sb.Append("<tr>");
                            for (var k = 0; k < n; k++)
                            {
                                sb.Append("<td>").Append(k >= changed ? Label(row.Keys[k], booleanGroups[k], texts) : string.Empty).Append("</td>");
                            }

                            sb.Append(Totals(row.Values, row.Keys)).Append("</tr>");
                        }
                    }

                    if (any)
                    {
                        for (var k = n - 1; k >= 0; k--)
                        {
                            Close(k);
                        }
                    }

                    if (!any)
                    {
                        sb.Append("<tr><td colspan=\"").Append(width).Append("\" class=\"text-muted\">").Append(Html.Encode(texts.NoData)).Append("</td></tr>");
                    }

                    sb.Append("</tbody>");
                    if (any && (grandTotal || (n == 0 && !details)))
                    {
                        var label = Html.Encode(texts.GrandTotal);
                        sb.Append(details
                            ? "<tfoot><tr class=\"fw-bold report-total\"><td colspan=\"" + width + "\">" + label + " &middot; " + InlineTotals(grand) + "</td></tr></tfoot>"
                            : "<tfoot><tr class=\"fw-bold report-total\">" + (n > 0 ? "<td colspan=\"" + n + "\">" + label + "</td>" : string.Empty) + Totals(grand, []) + "</tr></tfoot>");
                    }

                    sb.Append("</table></div>");
                    if (truncated)
                    {
                        sb.Append("<p class=\"text-muted small\">").Append(Html.Encode(string.Format(CultureInfo.InvariantCulture, texts.MoreRows, MaxDetailRows))).Append("</p>");
                    }

                    return sb.ToString();
                }

                // ---- crosstab and chart data ------------------------------------------------------------

                /// <summary>Row keys in the order read, the column keys sorted and cut to <paramref name="maxColumns"/>, and the cells.</summary>
                private sealed class Grid
                {
                    public List<object?> Rows { get; } = [];

                    public List<object?> Columns { get; } = [];

                    public bool Truncated { get; set; }

                    public Dictionary<(string Row, string Column), Acc> Cells { get; } = [];

                    public Dictionary<string, Acc> RowTotals { get; } = [];

                    public Dictionary<string, Acc> ColumnTotals { get; } = [];

                    public Acc Total { get; } = new();

                    public Acc At(object? row, object? column) => Cells.TryGetValue((KeyOf(row), KeyOf(column)), out var acc) ? acc : new Acc();
                }

                private static Grid Collect(IEnumerable<Cell> cells, int maxColumns)
                {
                    var grid = new Grid();
                    var rowKeys = new HashSet<string>();
                    var columns = new Dictionary<string, object?>();
                    foreach (var cell in cells)
                    {
                        var row = KeyOf(cell.Row);
                        var column = KeyOf(cell.Column);
                        if (rowKeys.Add(row))
                        {
                            grid.Rows.Add(cell.Row);
                        }

                        columns.TryAdd(column, cell.Column);
                        if (!grid.Cells.TryGetValue((row, column), out var acc))
                        {
                            grid.Cells[(row, column)] = acc = new Acc();
                        }

                        acc.Merge(cell.Value);
                        if (!grid.RowTotals.TryGetValue(row, out var rowTotal))
                        {
                            grid.RowTotals[row] = rowTotal = new Acc();
                        }

                        rowTotal.Merge(cell.Value);
                        if (!grid.ColumnTotals.TryGetValue(column, out var columnTotal))
                        {
                            grid.ColumnTotals[column] = columnTotal = new Acc();
                        }

                        columnTotal.Merge(cell.Value);
                        grid.Total.Merge(cell.Value);
                    }

                    var sorted = columns.Values.ToList();
                    sorted.Sort(Compare);
                    grid.Truncated = sorted.Count > maxColumns;
                    grid.Columns.AddRange(sorted.Take(Math.Max(1, maxColumns)));
                    return grid;
                }

                /// <summary>
                /// A grid of one aggregate: the row values down the side, the column values across the top, a total per row, per column and
                /// overall. The totals include the columns left out when there are more than <paramref name="maxColumns"/>.
                /// </summary>
                public static string Crosstab(string rowCaption, bool rowBoolean, bool columnBoolean, Measure measure, IEnumerable<Cell> cells, int maxColumns, string? drill = null)
                {
                    var texts = Text();
                    var grid = Collect(cells, maxColumns);
                    var sb = new StringBuilder("<div class=\"table-responsive\"><table class=\"table table-sm table-bordered report-crosstab\"><thead><tr><th>")
                        .Append(Html.Encode(rowCaption)).Append("</th>");
                    foreach (var column in grid.Columns)
                    {
                        sb.Append("<th class=\"text-end\">").Append(Label(column, columnBoolean, texts)).Append("</th>");
                    }

                    sb.Append("<th class=\"text-end\">").Append(Html.Encode(texts.Total)).Append("</th></tr></thead><tbody>");
                    if (grid.Rows.Count == 0)
                    {
                        sb.Append("<tr><td colspan=\"").Append(grid.Columns.Count + 2).Append("\" class=\"text-muted\">").Append(Html.Encode(texts.NoData)).Append("</td></tr>");
                    }

                    foreach (var row in grid.Rows)
                    {
                        sb.Append("<tr><th scope=\"row\">").Append(Label(row, rowBoolean, texts)).Append("</th>");
                        foreach (var column in grid.Columns)
                        {
                            var acc = grid.At(row, column);
                            sb.Append("<td class=\"text-end\">").Append(acc.Count == 0 ? (measure.Function == Count ? "0" : string.Empty)
                                : Linked(Format(acc.Value(measure.Function), measure), drill is null ? null : Link(drill, [(0, row), (1, column)]))).Append("</td>");
                        }

                        sb.Append("<td class=\"text-end fw-semibold\">").Append(Linked(Format(grid.RowTotals[KeyOf(row)].Value(measure.Function), measure), drill is null ? null : Link(drill, [(0, row)]))).Append("</td></tr>");
                    }

                    sb.Append("</tbody>");
                    if (grid.Rows.Count > 0)
                    {
                        sb.Append("<tfoot><tr class=\"fw-bold report-total\"><th scope=\"row\">").Append(Html.Encode(texts.Total)).Append("</th>");
                        foreach (var column in grid.Columns)
                        {
                            sb.Append("<td class=\"text-end\">").Append(Linked(Format(grid.ColumnTotals[KeyOf(column)].Value(measure.Function), measure), drill is null ? null : Link(drill, [(1, column)]))).Append("</td>");
                        }

                        sb.Append("<td class=\"text-end\">").Append(Linked(Format(grid.Total.Value(measure.Function), measure), drill is null ? null : Link(drill, []))).Append("</td></tr></tfoot>");
                    }

                    sb.Append("</table></div>");
                    if (grid.Truncated)
                    {
                        sb.Append("<p class=\"text-muted small\">").Append(Html.Encode(string.Format(CultureInfo.InvariantCulture, texts.MoreColumns, maxColumns))).Append("</p>");
                    }

                    return sb.ToString();
                }

                // ---- chart ------------------------------------------------------------------------------

                /// <summary>
                /// An SVG chart of one aggregate per category, one series per value of the series field (or one series without it), and
                /// optionally its data table. <paramref name="kind"/> is bar, hbar, line, area, pie or doughnut.
                /// </summary>
                public static string Chart(string id, string kind, string caption, string categoryCaption, bool categoryBoolean, bool seriesBoolean, bool series,
                    Measure measure, IEnumerable<Cell> cells, int maxSeries, bool byValue, int top, bool dataTable, string? drill = null)
                {
                    var texts = Text();
                    var grid = Collect(cells, series ? maxSeries : 1);
                    var categories = grid.Rows.ToList();
                    if (byValue && !series)
                    {
                        categories = categories.OrderByDescending(c => Number(grid.RowTotals[KeyOf(c)].Value(measure.Function)) ?? double.MinValue).ToList();
                    }

                    if (top > 0)
                    {
                        categories = categories.Take(top).ToList();
                    }

                    var labels = categories.Select(c => PlainLabel(c, categoryBoolean, texts)).ToArray();
                    var names = series ? grid.Columns.Select(c => PlainLabel(c, seriesBoolean, texts)).ToArray() : [measure.Caption];
                    var values = new double?[names.Length][];
                    for (var s = 0; s < names.Length; s++)
                    {
                        values[s] = categories.Select(c => series
                            ? (grid.At(c, grid.Columns[s]).Count == 0 && measure.Function != Count ? null : Number(grid.At(c, grid.Columns[s]).Value(measure.Function)))
                            : Number(grid.RowTotals[KeyOf(c)].Value(measure.Function))).ToArray();
                    }

                    string? Href(int c, int s) => drill is null ? null
                        : series ? Link(drill, [(0, categories[c]), (1, grid.Columns[s])]) : Link(drill, [(0, categories[c])]);
                    var sb = new StringBuilder("<figure class=\"report-chart mb-4\" id=\"").Append(Html.Attr(id)).Append("\">");
                    if (categories.Count == 0)
                    {
                        sb.Append("<p class=\"text-muted\">").Append(Html.Encode(texts.NoData)).Append("</p></figure>");
                        return sb.ToString();
                    }

                    sb.Append(kind is "pie" or "doughnut"
                        ? PieSvg(caption, labels, values[0], kind == "doughnut", measure, c => Href(c, 0))
                        : AxisSvg(caption, kind, labels, names, values, measure, Href));
                    if (names.Length > 1)
                    {
                        sb.Append(Legend(names));
                    }

                    sb.Append("</figure>");
                    if (dataTable)
                    {
                        sb.Append("<div class=\"table-responsive\"><table class=\"table table-sm report-data\"><thead><tr><th>").Append(Html.Encode(categoryCaption)).Append("</th>");
                        foreach (var name in names)
                        {
                            sb.Append("<th class=\"text-end\">").Append(Html.Encode(name)).Append("</th>");
                        }

                        sb.Append("</tr></thead><tbody>");
                        for (var c = 0; c < categories.Count; c++)
                        {
                            sb.Append("<tr><th scope=\"row\">").Append(Html.Encode(labels[c])).Append("</th>");
                            for (var s = 0; s < names.Length; s++)
                            {
                                var acc = series ? grid.At(categories[c], grid.Columns[s]) : grid.RowTotals[KeyOf(categories[c])];
                                sb.Append("<td class=\"text-end\">").Append(acc.Count == 0 ? (measure.Function == Count ? "0" : string.Empty)
                                    : Linked(Format(acc.Value(measure.Function), measure), Href(c, s))).Append("</td>");
                            }

                            sb.Append("</tr>");
                        }

                        sb.Append("</tbody></table></div>");
                    }

                    if (grid.Truncated)
                    {
                        sb.Append("<p class=\"text-muted small\">").Append(Html.Encode(string.Format(CultureInfo.InvariantCulture, texts.MoreColumns, maxSeries))).Append("</p>");
                    }

                    return sb.ToString();
                }

                private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

                private static string OpenLink(string? href) => href is null ? string.Empty : "<a href=\"" + Html.Attr(href) + "\">";

                private static string CloseLink(string? href) => href is null ? string.Empty : "</a>";

                private static string Short(string text) => text.Length > 18 ? text[..17] + "…" : text;

                private static string Tip(string label, double? value, Measure measure) =>
                    "<title>" + Html.Encode(label) + ": " + (value is null ? string.Empty : Format((decimal)value.Value, measure)) + "</title>";

                /// <summary>A round step for about five grid lines between <paramref name="min"/> and <paramref name="max"/>.</summary>
                private static (double Min, double Max, double Step) Scale(double min, double max)
                {
                    if (max <= min)
                    {
                        max = min + 1;
                    }

                    var raw = (max - min) / 5;
                    var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
                    var step = new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * magnitude).First(s => s >= raw);
                    return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
                }

                private static string AxisSvg(string caption, string kind, string[] labels, string[] names, double?[][] values, Measure measure, Func<int, int, string?> href)
                {
                    const double W = 800, H = 400;
                    var horizontal = kind == "hbar";
                    var all = values.SelectMany(v => v).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                    var (min, max, step) = Scale(Math.Min(0, all.DefaultIfEmpty(0).Min()), Math.Max(0, all.DefaultIfEmpty(0).Max()));
                    var rotate = !horizontal && labels.Length > 8;
                    var left = horizontal ? 150.0 : 70.0;
                    var bottom = horizontal ? 30.0 : rotate ? 110.0 : 40.0;
                    var top = 15.0;
                    var right = 20.0;
                    var plotW = W - left - right;
                    var plotH = H - top - bottom;
                    var count = labels.Length;
                    double Value(double v) => horizontal ? left + (v - min) / (max - min) * plotW : top + plotH - (v - min) / (max - min) * plotH;
                    var band = (horizontal ? plotH : plotW) / count;

                    var sb = new StringBuilder("<svg class=\"w-100 h-auto\" viewBox=\"0 0 800 400\" role=\"img\" aria-label=\"").Append(Html.Attr(caption))
                        .Append("\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"system-ui, sans-serif\" font-size=\"12\">");
                    for (var g = min; g <= max + step / 2; g += step)
                    {
                        var p = Value(g);
                        sb.Append(horizontal
                            ? $"<line x1=\"{Num(p)}\" y1=\"{Num(top)}\" x2=\"{Num(p)}\" y2=\"{Num(top + plotH)}\" stroke=\"#dee2e6\"/><text x=\"{Num(p)}\" y=\"{Num(H - 10)}\" text-anchor=\"middle\" fill=\"#6c757d\">{Html.Encode(Num(g))}</text>"
                            : $"<line x1=\"{Num(left)}\" y1=\"{Num(p)}\" x2=\"{Num(left + plotW)}\" y2=\"{Num(p)}\" stroke=\"#dee2e6\"/><text x=\"{Num(left - 6)}\" y=\"{Num(p + 4)}\" text-anchor=\"end\" fill=\"#6c757d\">{Html.Encode(Num(g))}</text>");
                    }

                    for (var c = 0; c < count; c++)
                    {
                        var center = (horizontal ? top : left) + band * (c + 0.5);
                        var label = Html.Encode(Short(labels[c]));
                        sb.Append(horizontal
                            ? $"<text x=\"{Num(left - 6)}\" y=\"{Num(center + 4)}\" text-anchor=\"end\">{label}</text>"
                            : rotate
                                ? $"<text transform=\"translate({Num(center)},{Num(top + plotH + 12)}) rotate(-45)\" text-anchor=\"end\">{label}</text>"
                                : $"<text x=\"{Num(center)}\" y=\"{Num(top + plotH + 18)}\" text-anchor=\"middle\">{label}</text>");
                    }

                    var zero = Value(0);
                    if (kind is "bar" or "hbar")
                    {
                        var inner = band * 0.8 / names.Length;
                        for (var s = 0; s < names.Length; s++)
                        {
                            var color = Palette[s % Palette.Length];
                            for (var c = 0; c < count; c++)
                            {
                                if (values[s][c] is not { } v)
                                {
                                    continue;
                                }

                                var start = (horizontal ? top : left) + band * c + band * 0.1 + inner * s;
                                var p = Value(v);
                                var low = Math.Min(p, zero);
                                var size = Math.Abs(p - zero);
                                var link = href(c, s);
                                sb.Append(OpenLink(link)).Append(horizontal
                                    ? $"<rect x=\"{Num(low)}\" y=\"{Num(start)}\" width=\"{Num(size)}\" height=\"{Num(inner)}\" fill=\"{color}\">"
                                    : $"<rect x=\"{Num(start)}\" y=\"{Num(low)}\" width=\"{Num(inner)}\" height=\"{Num(size)}\" fill=\"{color}\">");
                                sb.Append(Tip(names.Length > 1 ? labels[c] + " / " + names[s] : labels[c], v, measure)).Append("</rect>").Append(CloseLink(link));
                            }
                        }
                    }
                    else
                    {
                        for (var s = 0; s < names.Length; s++)
                        {
                            var color = Palette[s % Palette.Length];
                            var points = new List<(double X, double Y, int C)>();
                            for (var c = 0; c < count; c++)
                            {
                                if (values[s][c] is { } v)
                                {
                                    points.Add((left + band * (c + 0.5), Value(v), c));
                                }
                            }

                            if (points.Count == 0)
                            {
                                continue;
                            }

                            var line = string.Join(" ", points.Select(p => Num(p.X) + "," + Num(p.Y)));
                            if (kind == "area")
                            {
                                sb.Append($"<polygon points=\"{Num(points[0].X)},{Num(zero)} {line} {Num(points[^1].X)},{Num(zero)}\" fill=\"{color}\" fill-opacity=\"0.2\"/>");
                            }

                            sb.Append($"<polyline points=\"{line}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
                            foreach (var p in points)
                            {
                                var link = href(p.C, s);
                                sb.Append(OpenLink(link)).Append($"<circle cx=\"{Num(p.X)}\" cy=\"{Num(p.Y)}\" r=\"3.5\" fill=\"{color}\">")
                                    .Append(Tip(names.Length > 1 ? labels[p.C] + " / " + names[s] : labels[p.C], values[s][p.C], measure)).Append("</circle>").Append(CloseLink(link));
                            }
                        }
                    }

                    sb.Append(horizontal
                        ? $"<line x1=\"{Num(zero)}\" y1=\"{Num(top)}\" x2=\"{Num(zero)}\" y2=\"{Num(top + plotH)}\" stroke=\"#495057\"/>"
                        : $"<line x1=\"{Num(left)}\" y1=\"{Num(zero)}\" x2=\"{Num(left + plotW)}\" y2=\"{Num(zero)}\" stroke=\"#495057\"/>");
                    return sb.Append("</svg>").ToString();
                }

                private static string PieSvg(string caption, string[] labels, double?[] values, bool doughnut, Measure measure, Func<int, string?> href)
                {
                    var slices = labels.Select((label, i) => (Label: label, Value: values[i] ?? 0, Index: i)).Where(s => s.Value > 0).ToList();
                    var total = slices.Sum(s => s.Value);
                    var sb = new StringBuilder("<svg class=\"w-100 h-auto\" viewBox=\"0 0 800 400\" role=\"img\" aria-label=\"").Append(Html.Attr(caption))
                        .Append("\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"system-ui, sans-serif\" font-size=\"12\">");
                    const double cx = 200, cy = 200, r = 180;
                    var inner = doughnut ? r * 0.55 : 0;
                    var angle = -Math.PI / 2;
                    for (var i = 0; i < slices.Count; i++)
                    {
                        var slice = slices[i];
                        var color = Palette[i % Palette.Length];
                        var sweep = slice.Value / total * 2 * Math.PI;
                        var link = href(slice.Index);
                        sb.Append(OpenLink(link));
                        if (slices.Count == 1)
                        {
                            sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{Num(r)}\" fill=\"{color}\">").Append(Tip(slice.Label, slice.Value, measure)).Append("</circle>");
                        }
                        else
                        {
                            var end = angle + sweep;
                            var large = sweep > Math.PI ? 1 : 0;
                            (double X, double Y) P(double a, double radius) => (cx + radius * Math.Cos(a), cy + radius * Math.Sin(a));
                            var (x1, y1) = P(angle, r);
                            var (x2, y2) = P(end, r);
                            var path = $"M{Num(cx)},{Num(cy)} L{Num(x1)},{Num(y1)} A{Num(r)},{Num(r)} 0 {large} 1 {Num(x2)},{Num(y2)} Z";
                            sb.Append($"<path d=\"{path}\" fill=\"{color}\" stroke=\"#fff\" stroke-width=\"1\">").Append(Tip(slice.Label, slice.Value, measure)).Append("</path>");
                            angle = end;
                        }

                        sb.Append(CloseLink(link));
                    }

                    if (doughnut)
                    {
                        sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{Num(inner)}\" fill=\"#fff\"/>");
                    }

                    for (var i = 0; i < slices.Count && i < 16; i++)
                    {
                        var y = 30 + i * 22;
                        var share = slices[i].Value / total * 100;
                        sb.Append($"<rect x=\"420\" y=\"{y - 11}\" width=\"14\" height=\"14\" fill=\"{Palette[i % Palette.Length]}\"/>")
                            .Append($"<text x=\"442\" y=\"{y}\">{Html.Encode(Short(slices[i].Label))} ({share.ToString("0.#", CultureInfo.InvariantCulture)}%)</text>");
                    }

                    return sb.Append("</svg>").ToString();
                }

                private static string Legend(string[] names)
                {
                    var sb = new StringBuilder("<figcaption class=\"d-flex flex-wrap gap-3 small mt-2\">");
                    for (var s = 0; s < names.Length; s++)
                    {
                        sb.Append("<span><span class=\"d-inline-block me-1\" style=\"width:12px;height:12px;background:").Append(Palette[s % Palette.Length])
                            .Append("\"></span>").Append(Html.Encode(names[s])).Append("</span>");
                    }

                    return sb.Append("</figcaption>").ToString();
                }

                // ---- calendar ---------------------------------------------------------------------------

                /// <summary>The month asked for with ?month=yyyy-MM, or the current month.</summary>
                public static DateOnly MonthOf(HttpRequest request)
                {
                    var value = request.Query["month"].ToString();
                    if (DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                    {
                        return month;
                    }

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    return new DateOnly(today.Year, today.Month, 1);
                }

                private static DateOnly GridStart(DateOnly month, bool mondayFirst)
                {
                    var offset = ((int)month.DayOfWeek - (mondayFirst ? 1 : 0) + 7) % 7;
                    return month.AddDays(-offset);
                }

                private static DateOnly GridEnd(DateOnly month, bool mondayFirst)
                {
                    var last = month.AddMonths(1).AddDays(-1);
                    var weekEnd = mondayFirst ? DayOfWeek.Sunday : DayOfWeek.Saturday;
                    return last.AddDays(((int)weekEnd - (int)last.DayOfWeek + 7) % 7);
                }

                /// <summary>First and last day of the calendar grid as yyyy-MM-dd, for the query.</summary>
                public static (string First, string Last) CalendarRange(DateOnly month, bool mondayFirst) =>
                    (GridStart(month, mondayFirst).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), GridEnd(month, mondayFirst).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                private static DateOnly? DayOf(string? value) =>
                    value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

                /// <summary>A month grid with the entries on the days they cover, and links to the previous and next month.</summary>
                public static string Calendar(string route, string filterQuery, DateOnly month, bool mondayFirst, IEnumerable<CalendarEntry> entries)
                {
                    var texts = Text();
                    var culture = Culture();
                    var start = GridStart(month, mondayFirst);
                    var end = GridEnd(month, mondayFirst);
                    var byDay = new Dictionary<DateOnly, List<CalendarEntry>>();
                    foreach (var entry in entries)
                    {
                        if (DayOf(entry.Start) is not { } first)
                        {
                            continue;
                        }

                        var last = DayOf(entry.End) is { } e && e >= first ? e : first;
                        for (var day = first < start ? start : first; day <= last && day <= end; day = day.AddDays(1))
                        {
                            if (!byDay.TryGetValue(day, out var list))
                            {
                                byDay[day] = list = [];
                            }

                            list.Add(entry);
                        }
                    }

                    string MonthLink(DateOnly m) => Html.Attr(route + "?" + filterQuery + "month=" + m.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var sb = new StringBuilder("<div class=\"report-calendar\"><div class=\"d-flex align-items-center gap-2 mb-3\">")
                        .Append("<a class=\"btn btn-sm btn-outline-secondary\" rel=\"prev\" href=\"").Append(MonthLink(month.AddMonths(-1))).Append("\">&lsaquo; ").Append(Html.Encode(texts.Previous)).Append("</a>")
                        .Append("<a class=\"btn btn-sm btn-outline-secondary\" href=\"").Append(MonthLink(new DateOnly(today.Year, today.Month, 1))).Append("\">").Append(Html.Encode(texts.ThisMonth)).Append("</a>")
                        .Append("<a class=\"btn btn-sm btn-outline-secondary\" rel=\"next\" href=\"").Append(MonthLink(month.AddMonths(1))).Append("\">").Append(Html.Encode(texts.Next)).Append(" &rsaquo;</a>")
                        .Append("<h2 class=\"h5 mb-0 ms-2\">").Append(Html.Encode(culture.TextInfo.ToTitleCase(month.ToString("MMMM yyyy", culture)))).Append("</h2></div>")
                        .Append("<div class=\"table-responsive\"><table class=\"table table-bordered table-sm\" style=\"table-layout:fixed\"><thead><tr>");
                    for (var i = 0; i < 7; i++)
                    {
                        var dayOfWeek = (DayOfWeek)((i + (mondayFirst ? 1 : 0)) % 7);
                        sb.Append("<th class=\"text-center\">").Append(Html.Encode(culture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek))).Append("</th>");
                    }

                    sb.Append("</tr></thead><tbody>");
                    for (var day = start; day <= end; day = day.AddDays(1))
                    {
                        if (day == start || (day.DayOfWeek == (mondayFirst ? DayOfWeek.Monday : DayOfWeek.Sunday)))
                        {
                            if (day != start)
                            {
                                sb.Append("</tr>");
                            }

                            sb.Append("<tr>");
                        }

                        var outside = day.Month != month.Month;
                        sb.Append("<td style=\"height:6rem;vertical-align:top\"").Append(outside ? " class=\"bg-body-tertiary text-muted\"" : string.Empty)
                            .Append(" data-day=\"").Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\"><div class=\"small fw-semibold\">")
                            .Append(day.Day.ToString(CultureInfo.InvariantCulture)).Append("</div>");
                        if (byDay.TryGetValue(day, out var list))
                        {
                            foreach (var entry in list)
                            {
                                var title = Html.Encode(entry.Title.Length == 0 ? texts.Empty : entry.Title);
                                sb.Append(entry.Href is null
                                    ? "<div class=\"small text-truncate report-entry\" title=\"" + title + "\">" + title + "</div>"
                                    : "<a class=\"d-block small text-truncate report-entry\" title=\"" + title + "\" href=\"" + Html.Attr(entry.Href) + "\">" + title + "</a>");
                            }
                        }

                        sb.Append("</td>");
                    }

                    return sb.Append("</tr></tbody></table></div></div>").ToString();
                }
            }
            """).Replace("@@NS@@", ns);

        text = text.Replace("@@USING@@\n", Localized ? $"using {ns}.Localization;\n" : string.Empty)
            .Replace("@@TOTAL@@", Ui("reportTotal"))
            .Replace("@@GRAND@@", Ui("reportGrandTotal"))
            .Replace("@@EMPTY@@", Ui("reportEmpty"))
            .Replace("@@NODATA@@", Ui("reportNoData"))
            .Replace("@@MORECOLUMNS@@", Ui("reportMoreColumns"))
            .Replace("@@MOREROWS@@", Ui("reportMoreRows"))
            .Replace("@@PREVIOUS@@", Ui("reportPrevious"))
            .Replace("@@NEXT@@", Ui("reportNext"))
            .Replace("@@THISMONTH@@", Ui("reportThisMonth"))
            .Replace("@@VALUE@@", Ui("reportValue"))
            .Replace("@@FROM@@", Ui("reportFrom"))
            .Replace("@@TO@@", Ui("reportTo"))
            .Replace("@@APPLY@@", Ui("reportApply"))
            .Replace("@@CLEAR@@", Ui("reportClear"))
            .Replace("@@ALL@@", Ui("reportAll"))
            .Replace("@@ROWCOUNT@@", Ui("reportRowCount"))
            .Replace("@@BACK@@", Ui("reportBack"))
            .Replace("@@CULTURE@@", Localized ? "CultureOf(Strings.CurrentLanguage)" : "CultureInfo.InvariantCulture");
        if (Localized)
        {
            text = ReplaceOnce(text, "    public static object? Read(", """
                    private static CultureInfo CultureOf(string language)
                    {
                        try
                        {
                            return CultureInfo.GetCultureInfo(language);
                        }
                        catch (CultureNotFoundException)
                        {
                            return CultureInfo.InvariantCulture;
                        }
                    }

                    public static object? Read(
                """);
        }

        return text;
    }
}
