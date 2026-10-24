namespace WebAppForge.Core.Generation;

/// <summary>
/// Source of the generated Web/Ui.cs: the runtime half of field customization (edit controls, display formats, badges,
/// sortable headers and lookups). It is written to a generated site only when a table customizes a field or the table itself,
/// so projects that customize nothing get exactly the files they got before.
/// </summary>
internal static partial class Templates
{
    internal static string UiRuntime(string ns) => Localized ? LocalizedUiRuntime(PlainUiRuntime(ns), ns) : PlainUiRuntime(ns);

    /// <summary>
    /// The localized variant: display formats use the visitor's language (Strings.Culture), currency keeps the symbol of the
    /// default language, and the default date formats become the short patterns of the language.
    /// </summary>
    private static string LocalizedUiRuntime(string text, string ns)
    {
        text = ReplaceOnce(text, "namespace " + ns + ".Web;", "using " + ns + ".Localization;\n\nnamespace " + ns + ".Web;");
        text = ReplaceOnce(text, """return Numeric(value, FormatString.Length > 0 ? FormatString : "C");""",
            """return Numeric(value, FormatString.Length > 0 ? FormatString : "C", Strings.CurrencyFormat);""");
        text = ReplaceOnce(text, "private string Numeric(object value, string format)", "private string Numeric(object value, string format, IFormatProvider? provider = null)");
        text = ReplaceOnce(text, "return WebUtility.HtmlEncode(number.ToString(format, CultureInfo.CurrentCulture));",
            "return WebUtility.HtmlEncode(number.ToString(format, provider ?? Strings.Culture));");
        text = ReplaceOnce(text, "? WebUtility.HtmlEncode(date.ToString(FormatString, CultureInfo.CurrentCulture))",
            "? WebUtility.HtmlEncode(date.ToString(FormatString, Strings.Culture))");
        text = ReplaceOnce(text, ": WebUtility.HtmlEncode(date.ToString(defaultFormat, CultureInfo.InvariantCulture));",
            ": WebUtility.HtmlEncode(LanguageDate(date, defaultFormat));");
        text = ReplaceOnce(text, "    private string Dated(object value, string defaultFormat)", LanguageDateMethod + "    private string Dated(object value, string defaultFormat)");
        return text;
    }

    /// <summary>The default date formats of the field display kinds, written as the short patterns of the visitor's language.</summary>
    private const string LanguageDateMethod = """
            private static string LanguageDate(DateTime date, string defaultFormat)
            {
                var culture = Strings.Culture;
                if (ReferenceEquals(culture, CultureInfo.InvariantCulture))
                {
                    return date.ToString(defaultFormat, CultureInfo.InvariantCulture);
                }

                var pattern = defaultFormat switch
                {
                    "yyyy-MM-dd" => culture.DateTimeFormat.ShortDatePattern,
                    "HH:mm" => culture.DateTimeFormat.ShortTimePattern,
                    _ => culture.DateTimeFormat.ShortDatePattern + " " + culture.DateTimeFormat.ShortTimePattern,
                };
                return date.ToString(pattern, culture);
            }


        """;

    private static string PlainUiRuntime(string ns) => """"
        using System.Globalization;
        using System.Net;
        using System.Text;
        using System.Text.RegularExpressions;
        using Dapper;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Data.Sqlite;

        namespace @@NS@@.Web;

        /// <summary>Customized edit controls. Every value is HTML-encoded here.</summary>
        public static class FieldEditor
        {
            public sealed class Spec
            {
                public string Name { get; init; } = "";
                public string Label { get; init; } = "";
                /// <summary>text, textarea, password, email, url, tel, number, date, datetime-local, time, color, hidden, select, radio, checkbox or readonly.</summary>
                public string Control { get; init; } = "text";
                public string Value { get; init; } = "";
                public bool Required { get; init; }
                public bool AutoFocus { get; init; }
                public bool Checked { get; init; }
                public string Help { get; init; } = "";
                public string Placeholder { get; init; } = "";
                public string Tooltip { get; init; } = "";
                public string CssClass { get; init; } = "";
                public string Style { get; init; } = "";
                public string Min { get; init; } = "";
                public string Max { get; init; } = "";
                public string Step { get; init; } = "";
                public string Pattern { get; init; } = "";
                public int Rows { get; init; } = 3;
                public int MaxLength { get; init; }
                public int MinLength { get; init; }
                /// <summary>HTML shown for a read-only field (already encoded by the caller).</summary>
                public string ReadOnlyHtml { get; init; } = "";
                public IEnumerable<KeyValuePair<string, string>> Options { get; init; } = [];
            }

            private static string Attribute(string name, string value) =>
                value.Length == 0 ? "" : " " + name + "=\"" + Html.Attr(value) + "\"";

            public static string GroupStart(string title) =>
                "<fieldset class=\"mb-3\"><legend class=\"h6\">" + Html.Encode(title) + "</legend>";

            public const string GroupEnd = "</fieldset>";

            /// <summary>True when the value matches the whole pattern. An invalid pattern or a timeout counts as no match.</summary>
            public static bool Matches(string value, string pattern)
            {
                try
                {
                    return Regex.IsMatch(value, "^(?:" + pattern + ")$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                }
                catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            public static string Render(Spec s)
            {
                if (s.Control == "hidden")
                {
                    return Html.Hidden(s.Name, s.Value);
                }

                var id = Html.Attr(s.Name);
                var label = Html.Encode(s.Label);
                var title = Attribute("title", s.Tooltip);
                var style = Attribute("style", s.Style);
                var css = s.CssClass.Length == 0 ? "" : " " + Html.Attr(s.CssClass);
                var autofocus = s.AutoFocus ? " autofocus" : "";
                var required = s.Required ? " required" : "";
                var help = s.Help.Length == 0 ? "" : "<div class=\"form-text\">" + Html.Encode(s.Help) + "</div>";

                switch (s.Control)
                {
                    case "readonly":
                        return "<div class=\"mb-3\"><label class=\"form-label\"" + title + ">" + label + "</label>"
                            + "<div class=\"form-control-plaintext" + css + "\"" + style + ">" + s.ReadOnlyHtml + "</div>" + help + "</div>";

                    case "checkbox":
                        return "<div class=\"form-check mb-3\"><input class=\"form-check-input" + css + "\" type=\"checkbox\" id=\"" + id
                            + "\" name=\"" + id + "\" value=\"true\"" + (s.Checked ? " checked" : "") + title + style + autofocus
                            + "><label class=\"form-check-label\" for=\"" + id + "\"" + title + ">" + label + "</label>" + help + "</div>";

                    case "select":
                    {
                        var sb = new StringBuilder("<div class=\"mb-3\"><label class=\"form-label\" for=\"" + id + "\"" + title + ">" + label
                            + "</label><select class=\"form-select" + css + "\" id=\"" + id + "\" name=\"" + id + "\"" + required + title + style + autofocus + ">");
                        if (!s.Required || s.Value.Length == 0)
                        {
                            sb.Append("<option value=\"\">").Append(Html.Encode(s.Placeholder)).Append("</option>");
                        }

                        var found = false;
                        foreach (var option in s.Options)
                        {
                            var selected = option.Key == s.Value;
                            found |= selected;
                            sb.Append("<option value=\"").Append(Html.Attr(option.Key)).Append('"').Append(selected ? " selected" : "")
                                .Append('>').Append(Html.Encode(option.Value)).Append("</option>");
                        }

                        if (s.Value.Length > 0 && !found)
                        {
                            // A stored value that is no longer an option stays selected, so saving does not change it silently.
                            sb.Append("<option value=\"").Append(Html.Attr(s.Value)).Append("\" selected>").Append(Html.Encode(s.Value)).Append("</option>");
                        }

                        return sb.Append("</select>").Append(help).Append("</div>").ToString();
                    }

                    case "radio":
                    {
                        var sb = new StringBuilder("<div class=\"mb-3\"><label class=\"form-label d-block\"" + title + ">" + label + "</label>");
                        var index = 0;
                        var options = s.Options.ToList();
                        if (!s.Required)
                        {
                            options.Insert(0, new KeyValuePair<string, string>("", "None"));
                        }

                        foreach (var option in options)
                        {
                            var optionId = id + "_" + index++;
                            sb.Append("<div class=\"form-check form-check-inline\"><input class=\"form-check-input").Append(css).Append("\" type=\"radio\" id=\"")
                                .Append(optionId).Append("\" name=\"").Append(id).Append("\" value=\"").Append(Html.Attr(option.Key)).Append('"')
                                .Append(option.Key == s.Value ? " checked" : "").Append(required).Append(title).Append(style)
                                .Append("><label class=\"form-check-label\" for=\"").Append(optionId).Append("\">").Append(Html.Encode(option.Value)).Append("</label></div>");
                        }

                        return sb.Append(help).Append("</div>").ToString();
                    }

                    case "textarea":
                        return "<div class=\"mb-3\"><label class=\"form-label\" for=\"" + id + "\"" + title + ">" + label + "</label>"
                            + "<textarea class=\"form-control" + css + "\" id=\"" + id + "\" name=\"" + id + "\" rows=\"" + Math.Max(1, s.Rows) + "\""
                            + (s.MaxLength > 0 ? " maxlength=\"" + s.MaxLength + "\"" : "") + (s.MinLength > 0 ? " minlength=\"" + s.MinLength + "\"" : "")
                            + Attribute("placeholder", s.Placeholder) + required + title + style + autofocus + ">" + Html.Encode(s.Value) + "</textarea>" + help + "</div>";
                }

                var type = s.Control is "password" or "email" or "url" or "tel" or "number" or "date" or "datetime-local" or "time" or "color" ? s.Control : "text";
                var textual = type is "text" or "password" or "email" or "url" or "tel";
                var ranged = type is "number" or "date" or "datetime-local" or "time";
                var attributes = new StringBuilder();
                if (textual)
                {
                    attributes.Append(s.MaxLength > 0 ? " maxlength=\"" + s.MaxLength + "\"" : "")
                        .Append(s.MinLength > 0 ? " minlength=\"" + s.MinLength + "\"" : "")
                        .Append(Attribute("pattern", s.Pattern))
                        .Append(Attribute("placeholder", s.Placeholder));
                }
                else if (ranged)
                {
                    attributes.Append(Attribute("min", s.Min)).Append(Attribute("max", s.Max)).Append(Attribute("step", s.Step));
                    if (type == "number")
                    {
                        attributes.Append(Attribute("placeholder", s.Placeholder));
                    }
                }

                var inputClass = type == "color" ? "form-control form-control-color" : "form-control";
                var value = type == "password" ? "" : s.Value;
                return "<div class=\"mb-3\"><label class=\"form-label\" for=\"" + id + "\"" + title + ">" + label + "</label>"
                    + "<input class=\"" + inputClass + css + "\" id=\"" + id + "\" name=\"" + id + "\" type=\"" + type + "\" value=\"" + Html.Attr(value) + "\""
                    + attributes + required + title + style + autofocus + ">" + help + "</div>";
            }
        }

        /// <summary>Customized display of a field value on the list and view pages. Results are HTML.</summary>
        public sealed class FieldView
        {
            public sealed record Rule(string Operator, string Value, string Style, string Text);

            public string Kind { get; init; } = "Auto";
            public string FormatString { get; init; } = "";
            public string TrueText { get; init; } = "";
            public string FalseText { get; init; } = "";
            public Rule[] Rules { get; init; } = [];

            /// <summary>The value as text for comparisons: booleans are 1 and 0, numbers use the invariant culture.</summary>
            public static string Raw(object? value) => value switch
            {
                null => "",
                bool flag => flag ? "1" : "0",
                string text => text,
                DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };

            /// <summary>Evaluates one condition. Both sides are compared as numbers when both parse, otherwise as text.</summary>
            public static bool Test(string op, string actual, string expected)
            {
                switch (op)
                {
                    case "IsEmpty":
                        return string.IsNullOrWhiteSpace(actual);
                    case "IsNotEmpty":
                        return !string.IsNullOrWhiteSpace(actual);
                    case "Contains":
                        return actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
                    case "NotEquals":
                        return Compare(actual, expected) != 0;
                    case "GreaterThan":
                        return Compare(actual, expected) > 0;
                    case "LessThan":
                        return Compare(actual, expected) < 0;
                    default:
                        return Compare(actual, expected) == 0;
                }
            }

            private static int Compare(string a, string b) =>
                decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var x)
                && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var y)
                    ? x.CompareTo(y)
                    : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

            /// <summary>A Bootstrap contextual name reduced to letters, digits, dash and underscore.</summary>
            public static string Token(string value, string fallback)
            {
                var clean = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
                return clean.Length == 0 ? fallback : clean;
            }

            private static string Text(object value) => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

            public string Render(object? value, string? display = null)
            {
                var raw = Raw(value);
                if (Kind == "Badge")
                {
                    return RenderBadge(value, raw, display);
                }

                if (value is null)
                {
                    return "";
                }

                if (display is not null)
                {
                    return Html.Encode(display);
                }

                switch (Kind)
                {
                    case "MultilineText":
                        return Html.Encode(Text(value)).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
                    case "Number":
                        return Numeric(value, FormatString.Length > 0 ? FormatString : (value is long or int or short or byte) ? "N0" : "N2");
                    case "Currency":
                        return Numeric(value, FormatString.Length > 0 ? FormatString : "C");
                    case "Percent":
                        return Numeric(value, FormatString.Length > 0 ? FormatString : "P");
                    case "Date":
                        return Dated(value, "yyyy-MM-dd");
                    case "DateTime":
                        return Dated(value, "yyyy-MM-dd HH:mm");
                    case "Time":
                        return Dated(value, "HH:mm");
                    case "Boolean":
                        return Boolean(value);
                    case "Image":
                        return raw.Length > 0 && SafeUrl(raw)
                            ? "<img src=\"" + Html.Attr(raw) + "\" alt=\"\" class=\"img-thumbnail\" style=\"max-height:3rem\">"
                            : Html.Encode(raw);
                    case "Link":
                        return raw.Length > 0 && SafeUrl(raw)
                            ? "<a href=\"" + Html.Attr(raw) + "\" rel=\"noopener\">" + Html.Encode(raw) + "</a>"
                            : Html.Encode(raw);
                    case "EmailLink":
                        return raw.Length > 0 ? "<a href=\"mailto:" + Html.Attr(raw) + "\">" + Html.Encode(raw) + "</a>" : "";
                    default:
                        return Html.Encode(value);
                }
            }

            private string Numeric(object value, string format)
            {
                if (value is string text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                {
                    value = parsed;
                }

                if (value is IFormattable number and not DateTime)
                {
                    try
                    {
                        return WebUtility.HtmlEncode(number.ToString(format, CultureInfo.CurrentCulture));
                    }
                    catch (FormatException)
                    {
                    }
                }

                return Html.Encode(value);
            }

            private string Dated(object value, string defaultFormat)
            {
                if (value is string text && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    value = parsed;
                }

                if (value is DateTime date)
                {
                    try
                    {
                        return FormatString.Length > 0
                            ? WebUtility.HtmlEncode(date.ToString(FormatString, CultureInfo.CurrentCulture))
                            : WebUtility.HtmlEncode(date.ToString(defaultFormat, CultureInfo.InvariantCulture));
                    }
                    catch (FormatException)
                    {
                    }
                }

                return Html.Encode(value);
            }

            private string Boolean(object value)
            {
                bool flag;
                switch (value)
                {
                    case bool b:
                        flag = b;
                        break;
                    case string text:
                        flag = text is "1" or "true" or "True" or "yes" or "Yes";
                        break;
                    default:
                        flag = Raw(value) is not ("0" or "");
                        break;
                }

                var custom = flag ? TrueText : FalseText;
                return custom.Length > 0 ? Html.Encode(custom) : Html.Encode(flag);
            }

            private string RenderBadge(object? value, string raw, string? display)
            {
                var plain = display is not null ? Html.Encode(display) : value is null ? "" : Html.Encode(value);
                foreach (var rule in Rules)
                {
                    if (Test(rule.Operator, raw, rule.Value))
                    {
                        var inner = rule.Text.Length > 0 ? Html.Encode(rule.Text) : plain;
                        return "<span class=\"badge text-bg-" + Token(rule.Style, "secondary") + "\">" + inner + "</span>";
                    }
                }

                return plain;
            }

            /// <summary>Only http, https and relative addresses are allowed as links and images.</summary>
            public static bool SafeUrl(string url)
            {
                url = url.Trim();
                if (url.Length == 0 || url.StartsWith("//", StringComparison.Ordinal))
                {
                    return false;
                }

                if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                {
                    return absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps;
                }

                return !url.Contains(':');
            }

            /// <summary>A column header that sorts by this field. Only fields the page lists are linked; the page whitelists the names again.</summary>
            public static string SortHeader(HttpRequest request, string route, string field, string caption, string attributes)
            {
                var current = request.Query["sort"].ToString();
                var descending = request.Query["dir"].ToString() == "desc";
                var active = string.Equals(current, field, StringComparison.Ordinal);
                var next = active && !descending ? "desc" : "asc";
                var query = new StringBuilder();
                foreach (var pair in request.Query)
                {
                    if (pair.Key is "sort" or "dir" or "page")
                    {
                        continue;
                    }

                    foreach (var value in pair.Value)
                    {
                        query.Append('&').Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value ?? ""));
                    }
                }

                var arrow = active ? (descending ? " &#9660;" : " &#9650;") : "";
                return "<th" + attributes + "><a class=\"link-body-emphasis text-decoration-none\" href=\"" + Html.Attr(route + "?sort=" + Uri.EscapeDataString(field) + "&dir=" + next + query)
                    + "\">" + Html.Encode(caption) + arrow + "</a></th>";
            }

            /// <summary>
            /// The sort control of the Cards layout: one button per sortable column in a button group. The active column shows the
            /// direction and toggles it; the others sort ascending. Other query parameters (q, f_ filters, pageSize) are kept, page is reset.
            /// </summary>
            public static string SortBar(HttpRequest request, string route, (string Field, string Caption)[] fields)
            {
                var current = request.Query["sort"].ToString();
                var descending = request.Query["dir"].ToString() == "desc";
                var query = new StringBuilder();
                foreach (var pair in request.Query)
                {
                    if (pair.Key is "sort" or "dir" or "page")
                    {
                        continue;
                    }

                    foreach (var value in pair.Value)
                    {
                        query.Append('&').Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value ?? ""));
                    }
                }

                var html = new StringBuilder("<div class=\"btn-group btn-group-sm mb-3\" role=\"group\" data-sort-bar>");
                foreach (var (field, caption) in fields)
                {
                    var active = string.Equals(current, field, StringComparison.Ordinal);
                    var next = active && !descending ? "desc" : "asc";
                    var arrow = active ? (descending ? " &#9660;" : " &#9650;") : "";
                    html.Append("<a class=\"btn btn-outline-secondary").Append(active ? " active" : "").Append("\" href=\"")
                        .Append(Html.Attr(route + "?sort=" + Uri.EscapeDataString(field) + "&dir=" + next + query)).Append('"')
                        .Append(active ? " aria-current=\"true\"" : "").Append('>').Append(Html.Encode(caption)).Append(arrow).Append("</a>");
                }

                return html.Append("</div>").ToString();
            }
        }

        /// <summary>Key and display rows of another table. The identifiers are quoted by the generator; keys are always bound as parameters.</summary>
        public sealed class LookupSource(string table, string key, string display, string filter, string order)
        {
            public static string KeyOf(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

            /// <summary>All rows for a drop-down or radio group, filtered and ordered as configured.</summary>
            public async Task<List<KeyValuePair<string, string>>> OptionsAsync(string connectionString)
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                var rows = await connection.QueryAsync<(object Key, object? Display)>("SELECT " + key + ", " + display + " FROM " + table
                    + (filter.Length > 0 ? " WHERE (" + filter + ")" : "") + " ORDER BY " + order);
                return rows.Select(r => new KeyValuePair<string, string>(KeyOf(r.Key), KeyOf(r.Display))).ToList();
            }

            /// <summary>Display texts for the given keys, in one parameterised query per 500 keys. The filter is not applied, so stored values always resolve.</summary>
            public async Task<Dictionary<string, string>> TextsAsync(SqliteConnection connection, IEnumerable<object?> keys)
            {
                var result = new Dictionary<string, string>();
                var distinct = keys.Where(k => k is not null).Select(k => k!).Distinct().ToList();
                for (var offset = 0; offset < distinct.Count; offset += 500)
                {
                    var chunk = distinct.Skip(offset).Take(500).ToList();
                    var args = new DynamicParameters();
                    var names = new List<string>();
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        names.Add("@k" + i);
                        args.Add("@k" + i, chunk[i]);
                    }

                    var rows = await connection.QueryAsync<(object Key, object? Display)>(
                        "SELECT " + key + ", " + display + " FROM " + table + " WHERE " + key + " IN (" + string.Join(", ", names) + ")", args);
                    foreach (var row in rows)
                    {
                        result[KeyOf(row.Key)] = KeyOf(row.Display);
                    }
                }

                return result;
            }

            /// <summary>The display text of a stored value, or null when the lookup has no such row.</summary>
            public static string? Pick(Dictionary<string, string> texts, object? value) =>
                value is not null && texts.TryGetValue(KeyOf(value), out var text) ? text : null;

            /// <summary>The display text of a key in an already loaded option list, or null.</summary>
            public static string? Find(IEnumerable<KeyValuePair<string, string>> options, string key)
            {
                foreach (var option in options)
                {
                    if (option.Key == key)
                    {
                        return option.Value;
                    }
                }

                return null;
            }
        }
        """".Replace("@@NS@@", ns);
}
