using System.Text;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Generator side of page designs (see <see cref="PageDesign"/>) and the source of the generated Web/PageKit.cs. A table whose
/// designs are all at their defaults takes the original code paths, so its output is unchanged and the runtime is not written.
/// </summary>
internal static partial class Templates
{
    // ---- what is designed -----------------------------------------------------------------------

    /// <summary>True when the table sets anything in <see cref="PageDesigns"/>; only such a table gets the designed pages.</summary>
    internal static bool Designed(TableInfo table) => !table.Designs.IsDefaultExceptMultiPage() || UsesFormPages(table);

    private static PageDesign DesignOf(TableInfo table, string page) => page switch
    {
        "list" => table.Designs.List,
        "view" => table.Designs.View,
        "add" => table.Designs.Add,
        "copy" => table.Designs.Copy,
        "edit" => table.Designs.Edit,
        "delete" => table.Designs.Delete,
        _ => table.Designs.Search,
    };

    private static string DesignName(string page) => "Design" + char.ToUpperInvariant(page[0]) + page[1..];

    /// <summary>Where the list page opens the page of an action: in the page itself unless the design asks for a modal or offcanvas.</summary>
    private static PageContainer ContainerOf(TableInfo table, string action) =>
        Designed(table) && action != "list" ? DesignOf(table, action).Container : PageContainer.Page;

    private static bool Has(string value) => !string.IsNullOrWhiteSpace(value);

    private static readonly string[] ActionPages = ["add", "copy", "edit", "delete"];

    /// <summary>Pages that show a success message after they saved.</summary>
    private static List<string> FlashPages(TableInfo table) =>
        Designed(table) ? ActionPages.Where(p => Has(DesignOf(table, p).SuccessMessage)).ToList() : [];

    private static string FlashCookieName(TableInfo table) => "flash_" + Token(Identifier.Route(table.Name), "t");

    /// <summary>Extra argument of a RenderForm call: the page kind that picks the design. Empty for tables that are not designed.</summary>
    private static string KindArg(TableInfo table, string kind) => Designed(table) ? ", " + Lit(kind) : string.Empty;

    /// <summary>The argument of PageKit.Done: the key of the success message of the page, or empty.</summary>
    private static string DoneCall(TableInfo table, string page) =>
        $"PageKit.Done(context, {BackTarget(table)}, FlashCookie, {Lit(Has(DesignOf(table, page).SuccessMessage) ? page : string.Empty)})";

    private static List<int> PageSizesOf(TableInfo table) =>
        Designed(table) ? table.Designs.List.PageSizeOptions.Where(n => n is >= 1 and <= 1000).Distinct().ToList() : [];

    private static string OptionsInit(PageDesign d, string confirmId)
    {
        var parts = new List<string>();
        void Add(string name, string value)
        {
            if (Has(value))
            {
                parts.Add($"{name} = {Lit(value)}");
            }
        }

        Add("Title", d.Title.Trim());
        Add("Subtitle", d.Subtitle.Trim());
        Add("HeaderHtml", d.HeaderHtml);
        Add("FooterHtml", d.FooterHtml);
        Add("CssClass", d.CssClass.Trim());
        Add("CustomCss", d.CustomCss);
        Add("CustomScript", d.CustomScript);
        if (d.FormLayout != FormLayout.Vertical)
        {
            parts.Add($"Layout = {Lit(d.FormLayout.ToString())}");
        }

        Add("SubmitText", d.SubmitText.Trim());
        Add("CancelText", d.CancelText.Trim());
        if (!d.ShowCancel)
        {
            parts.Add("ShowCancel = false");
        }

        Add("SubmitCssClass", d.SubmitCssClass.Trim());
        if (d.Buttons != ButtonPlacement.Bottom)
        {
            parts.Add($"Buttons = {Lit(d.Buttons.ToString())}");
        }

        if (d.ConfirmBeforeSave)
        {
            parts.Add($"Confirm = {Ui(confirmId)}");
        }

        return parts.Count == 0 ? "new()" : "new() { " + string.Join(", ", parts) + " }";
    }

    /// <summary>Designs, flash messages and page sizes of a designed table, as statics of its pages class.</summary>
    private static void AppendDesignStatics(StringBuilder sb, TableInfo table)
    {
        foreach (var page in new[] { "list", "view", "add", "copy", "edit", "delete", "search" })
        {
            var design = DesignOf(table, page);
            var init = OptionsInit(design, page == "delete" ? "pageConfirmDelete" : "pageConfirmSave");
            // A localized confirmation is in the language of the request, so the options are built per request.
            sb.AppendLine(Localized && design.ConfirmBeforeSave
                ? $"    private static PageKit.Options {DesignName(page)} => {init};"
                : $"    private static readonly PageKit.Options {DesignName(page)} = {init};");
        }

        var flash = FlashPages(table);
        if (flash.Count > 0)
        {
            sb.AppendLine($"    private const string FlashCookie = {Lit(FlashCookieName(table))};");
            sb.AppendLine("    private static readonly KeyValuePair<string, string>[] FlashMessages = ["
                + string.Join(", ", flash.Select(p => $"new({Lit(p)}, {Lit(DesignOf(table, p).SuccessMessage.Trim())})")) + "];");
        }
        else
        {
            sb.AppendLine("    private const string FlashCookie = \"\";");
        }

        var sizes = PageSizesOf(table);
        if (sizes.Count > 0)
        {
            sb.AppendLine("    private static readonly int[] PageSizeOptions = [" + string.Join(", ", sizes) + "];");
        }
    }

    /// <summary>C# expression for a link to a page of an action: a plain link, or one the list page opens in a modal or offcanvas.</summary>
    private static string ActionLink(TableInfo table, string action, string href, string text, string? css)
    {
        var container = ContainerOf(table, action);
        if (container == PageContainer.Page)
        {
            return css is null ? $"Html.Link({href}, {text})" : $"Html.Link({href}, {text}, {Lit(css)})";
        }

        return $"PageKit.OpenLink({href}, {text}, {Lit(css ?? "btn btn-sm btn-outline-primary")}, {Lit(container == PageContainer.Modal ? "modal" : "offcanvas")})";
    }

    /// <summary>The columns of the list page in order, each with the width the design sets (empty = the field's own).</summary>
    private static List<(FieldInfo Field, string Width)> ListColumnsOf(TableInfo table)
    {
        var shown = table.Fields.Where(f => f.ShowInList).ToList();
        var configured = Designed(table) ? table.Designs.List.Columns : [];
        var result = new List<(FieldInfo, string)>();
        var resolved = 0;
        foreach (var column in configured)
        {
            var field = shown.FirstOrDefault(f => string.Equals(f.Name, column.Field?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                continue;
            }

            resolved++;
            if (column.Visible && !result.Any(r => ReferenceEquals(r.Item1, field)))
            {
                result.Add((field, column.Width?.Trim() ?? string.Empty));
            }
        }

        // No entry names a field the list can show (or none is configured): the standard columns.
        return resolved == 0 ? shown.Select(f => (f, string.Empty)).ToList() : result;
    }

    // ---- designed forms -------------------------------------------------------------------------

    private static void AppendDesignedSearchPage(StringBuilder sb, TableInfo table)
    {
        var buttons = $"{Ui("search")}, {Ui("cancel")}, Route, \"btn btn-primary\", false";
        sb.AppendLine("    private static IResult SearchPage(HttpRequest request)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var o = {DesignName("search")};");
        sb.AppendLine("        var body = new StringBuilder();");
        sb.AppendLine("        body.Append(\"<form method=\\\"get\\\" action=\\\"\").Append(Html.Attr(Route)).Append(\"\\\">\");");
        sb.AppendLine($"        body.Append(PageKit.TopButtons(o, {buttons}));");
        sb.AppendLine("        body.Append(PageKit.RowStart(o));");
        sb.AppendLine("        var start = body.Length;");
        sb.AppendLine($"        body.Append(Html.Input(\"q\", {Ui("quickSearch")}, \"text\", string.Empty, false));");
        sb.AppendLine("        PageKit.Wrap(body, start, o);");
        foreach (var field in table.Fields.Where(IsSearchable))
        {
            sb.AppendLine("        start = body.Length;");
            sb.AppendLine($"        body.Append(Html.Input({Lit("f_" + field.Name)}, {FieldText(field)}, \"text\", string.Empty, false));");
            sb.AppendLine("        PageKit.Wrap(body, start, o);");
        }

        sb.AppendLine("        body.Append(PageKit.RowEnd(o));");
        sb.AppendLine($"        body.Append(PageKit.BottomButtons(o, {buttons}));");
        sb.AppendLine("        body.Append(\"</form>\");");
        sb.AppendLine($"        return PageKit.Page(request, {UiSuffix("search", " ")} + Caption, body.ToString(), o);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>RenderForm of a designed table: the layout, buttons, title and partial output come from the design of the page kind (add, copy or edit).</summary>
    private static void AppendDesignedForm(StringBuilder sb, string className, TableInfo table)
    {
        var asyncForm = HasFormLookups(table);
        sb.AppendLine(asyncForm
            ? $"    private static async Task<IResult> RenderForm(string title, string action, {className} item, List<string> errors, bool edit, HttpContext context, IAntiforgery antiforgery, string kind, string connectionString{MdFormParams(table)})"
            : $"    private static IResult RenderForm(string title, string action, {className} item, List<string> errors, bool edit, HttpContext context, IAntiforgery antiforgery, string kind{MdFormParams(table)})");
        sb.AppendLine("    {");
        if (asyncForm)
        {
            AppendFormLookups(sb, className, table);
        }

        var buttons = $"{Ui("save")}, {Ui("cancel")}, {BackTarget(table)}, \"btn btn-primary\"";
        var paged = IsPagedForm(table);
        sb.AppendLine($"        var o = kind == \"add\" ? {DesignName("add")} : kind == \"copy\" ? {DesignName("copy")} : {DesignName("edit")};");
        if (paged)
        {
            sb.AppendLine($"        var mp = kind == \"add\" ? {Lit(ModeOf(table, "add").ToString())} : kind == \"copy\" ? {Lit(ModeOf(table, "copy").ToString())} : {Lit(ModeOf(table, "edit").ToString())};");
            sb.AppendLine("        var errMap = mp == \"None\" ? Array.Empty<int>() : FormPages.ErrorMap(errors);");
        }

        sb.AppendLine("        var body = new StringBuilder();");
        if (paged)
        {
            sb.AppendLine("        body.Append(FormPages.ErrorAlert(errors, errMap));");
        }
        else
        {
            sb.AppendLine("        if (errors.Count > 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            body.Append(\"<div class=\\\"alert alert-danger\\\"><ul class=\\\"mb-0\\\">\");");
            sb.AppendLine("            foreach (var error in errors)");
            sb.AppendLine("            {");
            sb.AppendLine("                body.Append(\"<li>\").Append(Html.Encode(error)).Append(\"</li>\");");
            sb.AppendLine("            }");
            sb.AppendLine("            body.Append(\"</ul></div>\");");
            sb.AppendLine("        }");
        }

        sb.AppendLine();
        sb.AppendLine("        body.Append(\"<form method=\\\"post\\\" action=\\\"\").Append(Html.Attr(action)).Append('\"').Append(PageKit.FormAttributes(o)).Append('>');");
        sb.AppendLine("        body.Append(Html.AntiforgeryField(context, antiforgery));");
        sb.AppendLine($"        body.Append(PageKit.TopButtons(o, {buttons}));");
        if (paged)
        {
            sb.AppendLine("        if (mp == \"None\")");
            sb.AppendLine("        {");
        }

        sb.AppendLine("        body.Append(PageKit.RowStart(o));");
        sb.AppendLine("        var start = body.Length;");
        string? group = null;
        foreach (var field in table.Fields.Where(IsFormField))
        {
            AppendFormField(sb, className, table, field, ref group);
        }

        sb.AppendLine("        body.Append(PageKit.RowEnd(o));");
        if (!string.IsNullOrEmpty(group))
        {
            sb.AppendLine("        body.Append(FieldEditor.GroupEnd);");
        }

        if (paged)
        {
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            AppendPagedFormBody(sb, className, table);
        }

        sb.AppendLine($"        body.Append(PageKit.BottomButtons(o, {buttons}));");
        sb.AppendLine("        body.Append(\"</form>\");");
        AppendMdEditTail(sb, table, paged);
        sb.AppendLine("        return PageKit.Page(context.Request, title, body.ToString(), o);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>The lines of one form field of a designed form: group boundaries, the input and its column wrapper.</summary>
    private static void AppendFormField(StringBuilder sb, string className, TableInfo table, FieldInfo field, ref string? group)
    {
        var prop = PropName(className, field);
        foreach (var groupLine in GroupTransition(group, field))
        {
            // A grid row cannot contain a fieldset, so the row is closed around group boundaries.
            sb.AppendLine("        body.Append(PageKit.RowEnd(o));");
            sb.AppendLine(groupLine);
            sb.AppendLine("        body.Append(PageKit.RowStart(o));");
        }

        group = field.Ui.Group;
        sb.AppendLine("        start = body.Length;");
        AppendMdFixed(sb, table, field, target =>
        {
            if (IsCompositeKey(table, field))
            {
                AppendCompositeKeyInput(target, field, prop);
            }
            else
            {
                var condition = FormCondition(field);
                var line = FormInputLine(field, prop);
                target.AppendLine(condition is null ? $"        {line}" : $"        if ({condition}) {line}");
            }
        });

        sb.AppendLine("        PageKit.Wrap(body, start, o);");
    }

    // ---- the runtime ----------------------------------------------------------------------------

    internal static string PageKitRuntime(string ns)
    {
        var text = PlainPageKitRuntime(ns);
        if (!Localized)
        {
            return text;
        }

        text = ReplaceOnce(text, "namespace " + ns + ".Web;", "using " + ns + ".Localization;\n\nnamespace " + ns + ".Web;");
        text = ReplaceOnce(text, """data-bs-dismiss=\"modal\" aria-label=\"Close\">""",
            """data-bs-dismiss=\"modal\" aria-label=\"" + Html.Attr(Strings.T("ui.pageClose", "Close")) + "\">""");
        text = ReplaceOnce(text, """data-bs-dismiss=\"offcanvas\" aria-label=\"Close\">""",
            """data-bs-dismiss=\"offcanvas\" aria-label=\"" + Html.Attr(Strings.T("ui.pageClose", "Close")) + "\">""");
        return ReplaceOnce(text, """for=\"pk-pagesize\">Rows per page</label>""",
            """for=\"pk-pagesize\">" + Html.Encode(Strings.T("ui.pageRowsPerPage", "Rows per page")) + "</label>""");
    }

    private static string PlainPageKitRuntime(string ns) => """"
        using System.Globalization;
        using System.Text;
        using Microsoft.AspNetCore.Http;

        namespace @@NS@@.Web;

        /// <summary>Designed pages: title, header and footer, form layouts, buttons, partial pages for modals, flash messages and page sizes.</summary>
        public static class PageKit
        {
            /// <summary>Settings of one page. Text values are encoded when written; HeaderHtml, FooterHtml, CustomCss and CustomScript are emitted as written by the project owner.</summary>
            public sealed class Options
            {
                public string Title { get; init; } = "";
                public string Subtitle { get; init; } = "";
                public string HeaderHtml { get; init; } = "";
                public string FooterHtml { get; init; } = "";
                public string CssClass { get; init; } = "";
                public string CustomCss { get; init; } = "";
                public string CustomScript { get; init; } = "";
                /// <summary>Vertical, Horizontal, TwoColumns or ThreeColumns.</summary>
                public string Layout { get; init; } = "Vertical";
                public string SubmitText { get; init; } = "";
                public string CancelText { get; init; } = "";
                public bool ShowCancel { get; init; } = true;
                public string SubmitCssClass { get; init; } = "";
                /// <summary>Top, Bottom or Both.</summary>
                public string Buttons { get; init; } = "Bottom";
                public string Confirm { get; init; } = "";
            }

            /// <summary>True for a request that wants only the page body: ?partial=1 or the header X-Requested-With: fetch.</summary>
            public static bool IsPartial(HttpRequest request) =>
                request.Query["partial"].ToString() == "1"
                || string.Equals(request.Headers["X-Requested-With"].ToString(), "fetch", StringComparison.OrdinalIgnoreCase);

            /// <summary>The whole page, or for a partial request only the heading and the body.</summary>
            public static IResult Page(HttpRequest request, string title, string body, Options o)
            {
                var heading = o.Title.Trim().Length > 0 ? o.Title.Trim() : title;
                var subtitle = o.Subtitle.Trim().Length == 0 ? "" : "<p class=\"text-muted\">" + Html.Encode(o.Subtitle.Trim()) + "</p>";
                if (IsPartial(request))
                {
                    return Results.Content("<div data-partial=\"1\"><h2 class=\"h4 mb-3\">" + Html.Encode(heading) + "</h2>" + subtitle + body + "</div>",
                        "text/html; charset=utf-8");
                }

                var content = subtitle + o.HeaderHtml + body + o.FooterHtml;
                if (o.CssClass.Trim().Length > 0)
                {
                    content = "<div class=\"" + Html.Attr(o.CssClass.Trim()) + "\">" + content + "</div>";
                }

                if (o.CustomCss.Length > 0)
                {
                    content += "<style>" + o.CustomCss + "</style>";
                }

                if (o.CustomScript.Length > 0)
                {
                    content += "<script>" + o.CustomScript + "</script>";
                }

                return Html.Page(heading, content);
            }

            /// <summary>The result of a successful save or delete: a redirect to the list, or {"ok":true} for a partial request. Sets the flash cookie when the page has a success message.</summary>
            public static IResult Done(HttpContext context, string route, string cookie, string flash)
            {
                if (flash.Length > 0)
                {
                    context.Response.Cookies.Append(cookie, flash, new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        Path = "/",
                        MaxAge = TimeSpan.FromMinutes(1),
                        SameSite = SameSiteMode.Lax,
                        Secure = context.Request.IsHttps,
                    });
                }

                return IsPartial(context.Request) ? Results.Content("{\"ok\":true}", "application/json") : Results.Redirect(route);
            }

            /// <summary>The success message to show once: the cookie only holds a key, the text comes from the page's own table and is encoded.</summary>
            public static string TakeFlash(HttpContext context, string cookie, KeyValuePair<string, string>[] messages)
            {
                if (cookie.Length == 0 || !context.Request.Cookies.TryGetValue(cookie, out var key))
                {
                    return "";
                }

                context.Response.Cookies.Delete(cookie, new CookieOptions { Path = "/" });
                foreach (var message in messages)
                {
                    if (message.Key == key)
                    {
                        return "<div class=\"alert alert-success\" role=\"status\">" + Html.Encode(message.Value) + "</div>";
                    }
                }

                return "";
            }

            // ---- forms ----

            public static string FormAttributes(Options o) =>
                o.Confirm.Length == 0 ? "" : " data-confirm=\"" + Html.Attr(o.Confirm) + "\" onsubmit=\"return confirm(this.dataset.confirm)\"";

            private static bool Grid(Options o) => o.Layout is "TwoColumns" or "ThreeColumns";

            public static string RowStart(Options o) => Grid(o) ? "<div class=\"row\">" : "";

            public static string RowEnd(Options o) => Grid(o) ? "</div>" : "";

            /// <summary>Puts the markup appended since <paramref name="start"/> (one field) into its column or row.</summary>
            public static void Wrap(StringBuilder body, int start, Options o)
            {
                if (body.Length == start || o.Layout == "Vertical")
                {
                    return;
                }

                var html = body.ToString(start, body.Length - start);
                body.Length = start;
                body.Append(Field(html, o));
            }

            private static string Field(string html, Options o)
            {
                if (html.StartsWith("<input type=\"hidden\"", StringComparison.Ordinal))
                {
                    return html;
                }

                switch (o.Layout)
                {
                    case "TwoColumns":
                        return "<div class=\"col-md-6\">" + html + "</div>";
                    case "ThreeColumns":
                        return "<div class=\"col-md-4\">" + html + "</div>";
                    case "Horizontal":
                        return Horizontal(html);
                    default:
                        return html;
                }
            }

            /// <summary>Label left, control right: every field block is a Bootstrap row with a label column and a control column.</summary>
            private static string Horizontal(string html)
            {
                const string plain = "<div class=\"mb-3\">";
                const string check = "<div class=\"form-check mb-3\">";
                var tail = "";
                var hidden = html.IndexOf("<input type=\"hidden\"", StringComparison.Ordinal);
                if (hidden > 0)
                {
                    tail = html[hidden..];
                    html = html[..hidden];
                }

                if (html.StartsWith(check, StringComparison.Ordinal))
                {
                    return "<div class=\"mb-3 row\"><div class=\"col-sm-9 offset-sm-3\">" + html + "</div></div>" + tail;
                }

                var labelEnd = html.IndexOf("</label>", StringComparison.Ordinal);
                if (html.StartsWith(plain, StringComparison.Ordinal) && html.EndsWith("</div>", StringComparison.Ordinal) && labelEnd > 0)
                {
                    labelEnd += "</label>".Length;
                    var label = html[plain.Length..labelEnd]
                        .Replace("class=\"form-label d-block\"", "class=\"col-sm-3 col-form-label\"")
                        .Replace("class=\"form-label\"", "class=\"col-sm-3 col-form-label\"");
                    var rest = html[labelEnd..^"</div>".Length];
                    return "<div class=\"mb-3 row\">" + label + "<div class=\"col-sm-9\">" + rest + "</div></div>" + tail;
                }

                return html + tail;
            }

            /// <summary>Submit and cancel buttons. <paramref name="cancelByDefault"/> is false for the search form, which shows a cancel link only when its text is set.</summary>
            public static string Buttons(Options o, string submit, string cancel, string route, string submitClass, bool cancelByDefault = true)
            {
                var css = submitClass;
                var custom = o.SubmitCssClass.Trim();
                if (custom.Length > 0)
                {
                    css = custom.Contains("btn", StringComparison.Ordinal) ? custom : "btn " + custom;
                }

                var text = o.SubmitText.Trim().Length > 0 ? o.SubmitText.Trim() : submit;
                var html = "<button type=\"submit\" class=\"" + Html.Attr(css) + "\">" + Html.Encode(text) + "</button> ";
                if (o.ShowCancel && (cancelByDefault || o.CancelText.Trim().Length > 0))
                {
                    html += Html.Link(route, o.CancelText.Trim().Length > 0 ? o.CancelText.Trim() : cancel, "btn btn-secondary");
                }

                return html;
            }

            public static string TopButtons(Options o, string submit, string cancel, string route, string submitClass, bool cancelByDefault = true) =>
                o.Buttons is "Top" or "Both" ? "<div class=\"mb-3\">" + Buttons(o, submit, cancel, route, submitClass, cancelByDefault) + "</div>" : "";

            public static string BottomButtons(Options o, string submit, string cancel, string route, string submitClass, bool cancelByDefault = true) =>
                o.Buttons is "Bottom" or "Both" ? Buttons(o, submit, cancel, route, submitClass, cancelByDefault) : "";

            // ---- list ----

            /// <summary>A link the list page opens in a modal or offcanvas (the script of <see cref="Containers"/> handles the click).</summary>
            public static string OpenLink(string href, string text, string cssClass, string kind) =>
                "<a class=\"" + Html.Attr(cssClass) + "\" href=\"" + Html.Attr(href) + "\" data-open=\"" + kind + "\">" + Html.Encode(text) + "</a>";

            /// <summary>The page size of the request when it is one of the configured options, otherwise the table's page size.</summary>
            public static int PageSize(HttpRequest request, int fallback, int[] options) =>
                int.TryParse(request.Query["pageSize"].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && Array.IndexOf(options, size) >= 0
                    ? size
                    : fallback;

            public static string PageSizeSelector(HttpRequest request, string route, int current, int[] options)
            {
                var query = new StringBuilder();
                foreach (var pair in request.Query)
                {
                    if (pair.Key is "page" or "pageSize")
                    {
                        continue;
                    }

                    foreach (var value in pair.Value)
                    {
                        query.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value ?? "")).Append('&');
                    }
                }

                var html = new StringBuilder("<div class=\"mb-3 d-flex align-items-center gap-2\"><label class=\"small text-muted\" for=\"pk-pagesize\">Rows per page</label>"
                    + "<select id=\"pk-pagesize\" class=\"form-select form-select-sm w-auto\" onchange=\"location.href=this.value\">");
                foreach (var size in options)
                {
                    html.Append("<option value=\"").Append(Html.Attr(route + "?" + query + "pageSize=" + size)).Append('"')
                        .Append(size == current ? " selected" : "").Append('>').Append(size).Append("</option>");
                }

                return html.Append("</select></div>").ToString();
            }

            /// <summary>
            /// The modal and offcanvas the list page opens pages in, and the script that fetches a page as a partial, shows it, submits
            /// its forms with fetch and reloads the list after a save. The Bootstrap script bundle is loaded here when the layout has not.
            /// </summary>
            public static string Containers(string route, bool modal, bool offcanvas)
            {
                var sb = new StringBuilder();
                if (modal)
                {
                    sb.Append("<div class=\"modal fade\" id=\"pk-modal\" tabindex=\"-1\" aria-hidden=\"true\"><div class=\"modal-dialog modal-lg\"><div class=\"modal-content\">"
                        + "<div class=\"modal-header border-0 pb-0\"><button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"modal\" aria-label=\"Close\"></button></div>"
                        + "<div class=\"modal-body pk-body\"></div></div></div></div>");
                }

                if (offcanvas)
                {
                    sb.Append("<div class=\"offcanvas offcanvas-end\" tabindex=\"-1\" id=\"pk-offcanvas\" style=\"width:min(36rem,100vw)\">"
                        + "<div class=\"offcanvas-header pb-0\"><button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"offcanvas\" aria-label=\"Close\"></button></div>"
                        + "<div class=\"offcanvas-body pk-body\"></div></div>");
                }

                var routeJson = System.Text.Json.JsonSerializer.Serialize(route);
                sb.Append("<script>(function(){var route=").Append(routeJson).Append(";").Append("""
        var cur=null;
        function ensure(cb){if(window.bootstrap){cb();return;}var s=document.createElement('script');s.src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js';s.onload=cb;document.head.appendChild(s);}
        function open(url,kind){ensure(function(){var el=document.getElementById(kind==='offcanvas'?'pk-offcanvas':'pk-modal');if(!el){return;}
        var inst=(kind==='offcanvas'?bootstrap.Offcanvas:bootstrap.Modal).getOrCreateInstance(el);cur={kind:kind,el:el,inst:inst};
        var body=el.querySelector('.pk-body');body.innerHTML='';
        fetch(url+(url.indexOf('?')<0?'?':'&')+'partial=1',{headers:{'X-Requested-With':'fetch'},credentials:'same-origin'}).then(function(r){return r.text();}).then(function(h){body.innerHTML=h;inst.show();});});}
        document.addEventListener('click',function(e){var a=e.target.closest&&e.target.closest('a');if(!a||e.defaultPrevented||e.button||e.ctrlKey||e.metaKey||e.shiftKey){return;}
        var kind=a.getAttribute('data-open');var href=a.getAttribute('href')||'';
        if(kind){e.preventDefault();open(href,kind);return;}
        if(cur&&cur.el.contains(a)){if(href===route){e.preventDefault();cur.inst.hide();}else if(href.indexOf(route+'/')===0){e.preventDefault();open(href,cur.kind);}}});
        document.addEventListener('submit',function(e){var f=e.target;if(e.defaultPrevented||!cur||!cur.el.contains(f)){return;}e.preventDefault();
        var body=cur.el.querySelector('.pk-body');
        if((f.getAttribute('method')||'get').toLowerCase()==='get'){location.href=(f.getAttribute('action')||route)+'?'+new URLSearchParams(new FormData(f)).toString();return;}
        fetch(f.getAttribute('action')||location.pathname,{method:'POST',body:new FormData(f),headers:{'X-Requested-With':'fetch'},credentials:'same-origin'}).then(function(r){
        if((r.headers.get('content-type')||'').indexOf('application/json')>=0){location.reload();return null;}return r.text();}).then(function(h){if(h!==null){body.innerHTML=h;}});});
        })();</script>
        """);
                return sb.ToString();
            }
        }
        """".Replace("@@NS@@", ns);
}
