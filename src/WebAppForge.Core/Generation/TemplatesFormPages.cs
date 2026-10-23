using System.Text;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Multi-page forms: the view, add, copy and edit pages of a table split into the pages of TableInfo.FormPages (tabs, pills, accordion
/// or wizard). A table whose designs leave MultiPage at None, or that defines no form pages, is generated exactly as before.
/// </summary>
internal static partial class Templates
{
    private static MultiPageMode ModeOf(TableInfo table, string page) =>
        table.FormPages.Count == 0 ? MultiPageMode.None : DesignOf(table, page).MultiPage;

    /// <summary>True when the add, copy or edit page of the table is split into pages.</summary>
    private static bool IsPagedForm(TableInfo table) =>
        ModeOf(table, "add") != MultiPageMode.None || ModeOf(table, "copy") != MultiPageMode.None || ModeOf(table, "edit") != MultiPageMode.None;

    private static bool IsPagedView(TableInfo table) => ModeOf(table, "view") != MultiPageMode.None;

    /// <summary>True when any page of the table is split into pages; such a site gets Web/FormPages.cs.</summary>
    internal static bool UsesFormPages(TableInfo table) => IsPagedForm(table) || IsPagedView(table);

    /// <summary>True when a split page shows a Bootstrap icon, so the layout links the icon font.</summary>
    internal static bool UsesFormPageIcons(TableInfo table) =>
        UsesFormPages(table) && table.FormPages.Any(p => Token(p.Icon).Length > 0);

    /// <summary>Index into TableInfo.FormPages of the page a field is on: the named page, or the first when the name is empty or unknown.</summary>
    private static int PageIndexOf(TableInfo table, FieldInfo field)
    {
        var name = field.Ui.FormPage.Trim();
        if (name.Length == 0)
        {
            return 0;
        }

        var index = table.FormPages.FindIndex(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
    }

    private static string PageCaption(FormPageDef page) =>
        Has(page.Caption) ? page.Caption.Trim() : page.Name.Trim();

    private static string PageExpr(TableInfo table, int index, string html)
    {
        var page = table.FormPages[index];
        return $"new FormPages.Page({index}, {Lit(page.Name.Trim())}, {Lit(PageCaption(page))}, {Lit(Token(page.Icon))}, {Lit(page.Description.Trim())}, {html})";
    }

    /// <summary>Pages that have at least one of the given fields, in page order, with their fields in field order.</summary>
    private static List<(int Index, List<FieldInfo> Fields)> FieldsByPage(TableInfo table, IEnumerable<FieldInfo> fields)
    {
        var list = fields.ToList();
        var result = new List<(int, List<FieldInfo>)>();
        for (var i = 0; i < table.FormPages.Count; i++)
        {
            var onPage = list.Where(f => PageIndexOf(table, f) == i).ToList();
            if (onPage.Count > 0)
            {
                result.Add((i, onPage));
            }
        }

        return result;
    }

    /// <summary>True when the list page opens a split page in a modal or offcanvas, so it carries the boot script of the page runtime.</summary>
    private static bool ListNeedsFormPageBoot(TableInfo table) =>
        UsesFormPages(table)
        && new[] { "add", "copy", "edit", "view" }.Any(p => ModeOf(table, p) != MultiPageMode.None && DesignOf(table, p).Container != PageContainer.Page);

    private static string TagBefore(int number) => $"        var pk_e{number} = errors.Count;";

    private static string TagAfter(TableInfo table, FieldInfo field, int number) =>
        $"        FormPages.Tag(errors, pk_e{number}, {PageIndexOf(table, field)});";

    /// <summary>The paged branch of a designed RenderForm; body is the form builder and is swapped per page.</summary>
    private static void AppendPagedFormBody(StringBuilder sb, string className, TableInfo table)
    {
        sb.AppendLine("        {");
        sb.AppendLine("            var outer = body;");
        sb.AppendLine("            var pages = new List<FormPages.Page>();");
        var (hosted, _) = MdPlacement(table, MdEditLinks(table), true, MdGridExpr);
        var byPage = MdWithHosts(FieldsByPage(table, table.Fields.Where(IsFormField)), hosted.Keys);
        if (byPage.Count > 0)
        {
            sb.AppendLine("            var start = 0;");
        }

        foreach (var (index, fields) in byPage)
        {
            var hostExpr = hosted.TryGetValue(index, out var hostList) ? " + " + string.Join(" + ", hostList) : string.Empty;
            if (fields.Count == 0)
            {
                sb.AppendLine($"            pages.Add({PageExpr(table, index, "string.Empty" + hostExpr)});");
                continue;
            }

            sb.AppendLine("            body = new StringBuilder();");
            sb.AppendLine("            body.Append(PageKit.RowStart(o));");
            string? group = null;
            foreach (var field in fields)
            {
                AppendFormField(sb, className, table, field, ref group);
            }

            sb.AppendLine("            body.Append(PageKit.RowEnd(o));");
            if (!string.IsNullOrEmpty(group))
            {
                sb.AppendLine("            body.Append(FieldEditor.GroupEnd);");
            }

            sb.AppendLine($"            pages.Add({PageExpr(table, index, "body.ToString()" + hostExpr)});");
        }

        sb.AppendLine("            body = outer;");
        sb.AppendLine("            body.Append(FormPages.Render(mp, pages, errors, errMap, true));");
        sb.AppendLine("        }");
    }

    /// <summary>The view page of a table split into pages: one definition list per page.</summary>
    private static void AppendPagedViewBody(StringBuilder sb, TableInfo table, Func<FieldInfo, string> line, Dictionary<int, List<string>>? hosted = null)
    {
        sb.AppendLine($"        var mp = {Lit(ModeOf(table, "view").ToString())};");
        sb.AppendLine("        var pages = new List<FormPages.Page>();");
        sb.AppendLine("        var body = new StringBuilder();");
        foreach (var (index, fields) in MdWithHosts(FieldsByPage(table, table.Fields.Where(f => f.ShowInView)), (hosted?.Keys.ToList() ?? new List<int>())))
        {
            var hostExpr = hosted is not null && hosted.TryGetValue(index, out var hostList) ? " + " + string.Join(" + ", hostList) : string.Empty;
            if (fields.Count == 0)
            {
                sb.AppendLine($"        pages.Add({PageExpr(table, index, "string.Empty" + hostExpr)});");
                continue;
            }

            sb.AppendLine("        body.Clear();");
            sb.AppendLine("        body.Append(\"<dl class=\\\"row\\\">\");");
            foreach (var field in fields)
            {
                sb.AppendLine(line(field));
            }

            sb.AppendLine("        body.Append(\"</dl>\");");
            sb.AppendLine($"        pages.Add({PageExpr(table, index, "body.ToString()" + hostExpr)});");
        }

        sb.AppendLine("        body.Clear();");
        sb.AppendLine("        body.Append(FormPages.Render(mp, pages, new List<string>(), Array.Empty<int>(), false));");
        sb.AppendLine("        body.Append(\"<div>\");");
    }

    // ---- the runtime ----------------------------------------------------------------------------

    internal static string FormPagesRuntime(string ns) => """"
        using System.Runtime.CompilerServices;
        using System.Text;

        namespace @@NS@@.Web;

        /// <summary>
        /// Multi-page forms and view pages: tabs, pills, accordion and wizard. Every field stays in one form, so posting and validation do
        /// not change. Without JavaScript all pages are shown one after the other. Captions and descriptions are project text and are encoded.
        /// </summary>
        public static class FormPages
        {
            public const string PreviousText = "Previous";
            public const string NextText = "Next";
            public const string StepText = "Step {0} of {1}";

            /// <summary>One page: Index is the position in the table's page list (stable even when empty pages are left out).</summary>
            public sealed record Page(int Index, string Name, string Caption, string Icon, string Description, string Html);

            private static readonly ConditionalWeakTable<List<string>, List<int>> Tags = new();

            /// <summary>Remembers that the errors added since index <paramref name="first"/> belong to a page.</summary>
            public static void Tag(List<string> errors, int first, int page)
            {
                if (errors.Count <= first)
                {
                    return;
                }

                var tags = Tags.GetOrCreateValue(errors);
                while (tags.Count < errors.Count)
                {
                    tags.Add(-1);
                }

                for (var i = first; i < errors.Count; i++)
                {
                    tags[i] = page;
                }
            }

            /// <summary>For each error the page it belongs to, or -1.</summary>
            public static int[] ErrorMap(List<string> errors)
            {
                var map = new int[errors.Count];
                Array.Fill(map, -1);
                if (Tags.TryGetValue(errors, out var tags))
                {
                    for (var i = 0; i < map.Length && i < tags.Count; i++)
                    {
                        map[i] = tags[i];
                    }
                }

                return map;
            }

            /// <summary>The alert with the errors that do not belong to a page (the others are listed under their page).</summary>
            public static string ErrorAlert(List<string> errors, int[] map)
            {
                var sb = new StringBuilder();
                for (var i = 0; i < errors.Count; i++)
                {
                    if (i < map.Length && map[i] >= 0)
                    {
                        continue;
                    }

                    if (sb.Length == 0)
                    {
                        sb.Append("<div class=\"alert alert-danger\"><ul class=\"mb-0\">");
                    }

                    sb.Append("<li>").Append(Html.Encode(errors[i])).Append("</li>");
                }

                return sb.Length == 0 ? "" : sb.Append("</ul></div>").ToString();
            }

            private const string Style = "<style>.pk-pages.pk-js .pk-pane{display:none}.pk-pages.pk-js .pk-pane.active{display:block}.pk-pages.pk-js .pk-title{display:none}"
                + ".pk-pages:not(.pk-js) .pk-js-only{display:none!important}.pk-pages:not(.pk-js) .collapse{display:block}.pk-pages .pk-pane{margin-bottom:1rem}</style>";

            private static string Icon(string icon) =>
                icon.Length == 0 ? "" : "<i class=\"bi bi-" + Html.Attr(icon) + " me-1\"></i>";

            private static string Badge(Dictionary<int, int> counts, int index) =>
                counts.TryGetValue(index, out var count)
                    ? " <span class=\"badge text-bg-danger ms-1\" title=\"" + count + "\">" + count + "</span>"
                    : "";

            private static string Content(Page page, List<string> errors, int[] map)
            {
                var sb = new StringBuilder();
                var first = true;
                for (var i = 0; i < errors.Count && i < map.Length; i++)
                {
                    if (map[i] != page.Index)
                    {
                        continue;
                    }

                    sb.Append(first ? "<div class=\"alert alert-danger pk-page-errors\"><ul class=\"mb-0\">" : "").Append("<li>").Append(Html.Encode(errors[i])).Append("</li>");
                    first = false;
                }

                if (!first)
                {
                    sb.Append("</ul></div>");
                }

                if (page.Description.Trim().Length > 0)
                {
                    sb.Append("<p class=\"text-muted\">").Append(Html.Encode(page.Description)).Append("</p>");
                }

                return sb.Append(page.Html).ToString();
            }

            /// <summary>mode is Tabs, Pills, Accordion or Wizard. Pages without content are left out. The first page with an error is shown, and every page with errors gets a badge.</summary>
            public static string Render(string mode, List<Page> pages, List<string> errors, int[] map, bool editable)
            {
                var visible = pages.Where(p => p.Html.Trim().Length > 0).ToList();
                if (visible.Count == 0)
                {
                    return "";
                }

                var counts = new Dictionary<int, int>();
                var active = -1;
                for (var i = 0; i < errors.Count && i < map.Length; i++)
                {
                    if (map[i] < 0)
                    {
                        continue;
                    }

                    counts[map[i]] = counts.GetValueOrDefault(map[i]) + 1;
                    if (active < 0)
                    {
                        active = visible.FindIndex(p => p.Index == map[i]);
                    }
                }

                if (active < 0)
                {
                    active = 0;
                }

                var sb = new StringBuilder(Style);
                var kind = mode.ToLowerInvariant();
                if (kind == "accordion")
                {
                    sb.Append("<div class=\"accordion mb-3 pk-pages\" data-pk-pages=\"accordion\" data-pk-active=\"").Append(active).Append("\">");
                    for (var i = 0; i < visible.Count; i++)
                    {
                        var p = visible[i];
                        var open = i == active;
                        sb.Append("<div class=\"accordion-item\"><h2 class=\"accordion-header\"><button type=\"button\" class=\"accordion-button")
                            .Append(open ? "" : " collapsed").Append("\" aria-expanded=\"").Append(open ? "true" : "false").Append("\" aria-controls=\"pk-acc-").Append(p.Index)
                            .Append("\" data-pk-target=\"pk-acc-").Append(p.Index).Append("\">").Append(Icon(p.Icon)).Append(Html.Encode(p.Caption)).Append(Badge(counts, p.Index))
                            .Append("</button></h2><div id=\"pk-acc-").Append(p.Index).Append("\" class=\"accordion-collapse collapse pk-acc").Append(open ? " show" : "")
                            .Append("\"><div class=\"accordion-body\">").Append(Content(p, errors, map)).Append("</div></div></div>");
                    }

                    return sb.Append("</div>").Append(Boot).ToString();
                }

                var wizard = kind == "wizard";
                sb.Append("<div class=\"mb-3 pk-pages\" data-pk-pages=\"").Append(wizard ? "wizard" : kind == "pills" ? "pills" : "tabs").Append("\" data-pk-active=\"").Append(active)
                    .Append("\" data-pk-step=\"").Append(Html.Attr(StepText)).Append("\">");
                var navClass = wizard ? "nav nav-pills nav-fill mb-2" : kind == "pills" ? "nav nav-pills mb-3" : "nav nav-tabs mb-3";
                if (wizard)
                {
                    sb.Append("<div class=\"pk-js-only mb-3\">");
                }

                sb.Append("<ul class=\"").Append(navClass).Append(wizard ? "" : " pk-js-only").Append("\" role=\"tablist\">");
                for (var i = 0; i < visible.Count; i++)
                {
                    var p = visible[i];
                    sb.Append("<li class=\"nav-item\" role=\"presentation\"><button type=\"button\" class=\"nav-link").Append(i == active ? " active" : "")
                        .Append("\" role=\"tab\" aria-selected=\"").Append(i == active ? "true" : "false").Append("\" aria-controls=\"pk-pane-").Append(p.Index)
                        .Append("\" data-pk-target=\"pk-pane-").Append(p.Index).Append("\">");
                    if (wizard)
                    {
                        sb.Append("<span class=\"badge rounded-pill text-bg-secondary me-1\">").Append(i + 1).Append("</span>");
                    }

                    sb.Append(Icon(p.Icon)).Append(Html.Encode(p.Caption)).Append(Badge(counts, p.Index)).Append("</button></li>");
                }

                sb.Append("</ul>");
                if (wizard)
                {
                    sb.Append("<div class=\"progress\" role=\"progressbar\" aria-label=\"Progress\" aria-valuemin=\"0\" aria-valuemax=\"100\" aria-valuenow=\"0\" style=\"height:.5rem\">"
                        + "<div class=\"progress-bar\" style=\"width:0%\"></div></div><div class=\"small text-muted mt-1 pk-step-label\"></div></div>");
                }

                for (var i = 0; i < visible.Count; i++)
                {
                    var p = visible[i];
                    sb.Append("<section class=\"pk-pane").Append(i == active ? " active" : "").Append("\" role=\"tabpanel\" id=\"pk-pane-").Append(p.Index).Append("\">")
                        .Append("<h3 class=\"h5 pk-title\">").Append(Icon(p.Icon)).Append(Html.Encode(p.Caption)).Append("</h3>")
                        .Append(Content(p, errors, map)).Append("</section>");
                }

                if (wizard)
                {
                    sb.Append("<div class=\"d-flex justify-content-between mb-3 pk-js-only\"><button type=\"button\" class=\"btn btn-outline-secondary\" data-pk-prev>")
                        .Append(Html.Encode(PreviousText)).Append("</button><button type=\"button\" class=\"btn btn-primary\" data-pk-next>")
                        .Append(Html.Encode(NextText)).Append("</button></div>");
                }

                return sb.Append("</div>").Append(Boot).ToString();
            }

            /// <summary>
            /// Starts the pages of the document and of every fragment inserted later (a page fetched into a modal or offcanvas): scripts inside
            /// inserted markup do not run, so a mutation observer calls <c>pkPages(node)</c> after the insert. Safe to include more than once.
            /// </summary>
            public const string Boot = "<script>" + BootScript + "</script>";

            public const string BootScript = """
        (function(){if(window.pkPages){window.pkPages(document);return;}
        function setup(el){el.setAttribute('data-pk-ready','1');el.classList.add('pk-js');
        var mode=el.getAttribute('data-pk-pages'),acc=mode==='accordion',wiz=mode==='wizard';
        var panes=[].slice.call(el.querySelectorAll(acc?'.pk-acc':'.pk-pane')),btns=[].slice.call(el.querySelectorAll('[data-pk-target]'));
        var form=el.closest('form'),cur=0,n=panes.length;
        var bar=el.querySelector('.progress-bar'),prog=el.querySelector('.progress'),label=el.querySelector('.pk-step-label'),prev=el.querySelector('[data-pk-prev]'),next=el.querySelector('[data-pk-next]');
        function submits(){return form&&wiz?[].slice.call(form.querySelectorAll('button[type=submit],input[type=submit]')).filter(function(b){return !el.contains(b);}):[];}
        function show(i){cur=i;
        panes.forEach(function(p,k){p.classList.toggle(acc?'show':'active',k===i);});
        btns.forEach(function(b,k){if(acc){b.classList.toggle('collapsed',k!==i);b.setAttribute('aria-expanded',k===i?'true':'false');}else{b.classList.toggle('active',k===i);b.setAttribute('aria-selected',k===i?'true':'false');}});
        if(wiz){var pct=Math.round((i+1)*100/n);if(bar){bar.style.width=pct+'%';}if(prog){prog.setAttribute('aria-valuenow',pct);}
        if(label){label.textContent=(el.getAttribute('data-pk-step')||'').replace('{0}',i+1).replace('{1}',n);}
        if(prev){prev.disabled=i===0;}if(next){next.style.display=i===n-1?'none':'';}
        submits().forEach(function(b){b.classList.toggle('d-none',i!==n-1);});}}
        function valid(i){var f=panes[i].querySelectorAll('input,select,textarea');for(var k=0;k<f.length;k++){if(!f[k].checkValidity()){f[k].reportValidity();return false;}}return true;}
        function go(t){if(acc){show(cur===t?-1:t);return;}
        if(wiz&&t>cur){for(var j=cur;j<t;j++){show(j);if(!valid(j)){return;}}}show(t);}
        btns.forEach(function(b,k){b.addEventListener('click',function(){go(k);});});
        if(prev){prev.addEventListener('click',function(){if(cur>0){show(cur-1);}});}
        if(next){next.addEventListener('click',function(){if(valid(cur)&&cur<n-1){show(cur+1);}});}
        if(form){form.addEventListener('invalid',function(e){var p=e.target.closest&&e.target.closest(acc?'.pk-acc':'.pk-pane');var i=p?panes.indexOf(p):-1;if(i>=0&&i!==cur){show(i);}},true);
        if(wiz){form.addEventListener('submit',function(e){if(cur<n-1){e.preventDefault();if(valid(cur)){show(cur+1);}}});}}
        show(Math.min(parseInt(el.getAttribute('data-pk-active')||'0',10)||0,n-1));}
        function init(root){if(root.querySelectorAll){[].slice.call(root.querySelectorAll('.pk-pages:not([data-pk-ready])')).forEach(setup);}
        if(root.matches&&root.matches('.pk-pages:not([data-pk-ready])')){setup(root);}}
        window.pkPages=function(root){if(root===document&&document.readyState==='loading'){return;}init(root);};
        function start(){init(document);
        new MutationObserver(function(ms){ms.forEach(function(m){[].slice.call(m.addedNodes).forEach(function(x){if(x.nodeType===1){init(x);}});});}).observe(document.documentElement,{childList:true,subtree:true});}
        if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',start);}else{start();}
        })();
        """;
        }
        """".Replace("@@NS@@", ns);
}
