using System.Text;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Referential integrity of master rows (delete rules, cascading updates) and the source of the generated Web/ReferentialIntegrity.cs and
/// Web/MasterDetailKit.cs. The delete plan of a table is built from set-based SQL at generation time: nested cascades are expanded
/// statically, so no loop with a depth limit is needed (the validator rejects every cascade cycle, including a self cascade).
/// Cascades intentionally bypass the ownership filter of the detail tables: they remove the orphans of a record the user may delete.
/// </summary>
internal static partial class Templates
{
    private static bool MdDeletePlanned(TableInfo t) =>
        t.PrimaryKeys.Count > 0 && MdLinks(t).Any(l => l.Link.OnDelete != DeleteRule.None);

    private static List<FieldInfo> MdTracked(TableInfo t)
    {
        var list = new List<FieldInfo>();
        foreach (var l in MdLinks(t).Where(l => l.Link.OnUpdate == UpdateRule.Cascade && l.Pairs.Any(p => !p.Master.IsPrimaryKey)))
        {
            foreach (var (m, _) in l.Pairs)
            {
                if (!list.Any(f => MdSame(f.Name, m.Name)))
                {
                    list.Add(m);
                }
            }
        }

        return list;
    }

    private static bool MdUpdatePlanned(TableInfo t) => t.PrimaryKeys.Count > 0 && MdTracked(t).Count > 0;

    internal static bool UsesReferentialIntegrity(IEnumerable<TableInfo> tables) =>
        tables.Any(t => MdDeletePlanned(t) || MdUpdatePlanned(t));

    /// <summary>Arguments array of the key (and the owner) for ReferentialIntegrity calls.</summary>
    private static string MdArgs(TableInfo table, string ownerExpr)
    {
        var items = KeyNames(table).Select(n => $"(\"@{n}\", {n})").ToList();
        if (IsOwned(table))
        {
            items.Add($"(\"@owner\", {ownerExpr})");
        }

        return "new (string, object?)[] { " + string.Join(", ", items) + " }";
    }

    private static string MdOldNew(TableInfo table, string className, string item) =>
        "new object?[] { " + string.Join(", ", MdTracked(table).Select(f => ParameterValue(f, item, className))) + " }";

    private static (List<(string Caption, string Sql)> Blockers, List<(string Caption, string Sql)> Counts, List<string> Statements) MdBuildPlan(TableInfo root)
    {
        var blockers = new List<(string, string)>();
        var counts = new List<(string, string)>();
        var statements = new List<string>();
        var keys = root.PrimaryKeys;
        var names = KeyNames(root);
        string RootScope(string a) => string.Join(" AND ", keys.Select((k, i) => a + "." + QuoteIdent(k.Name) + " = @" + names[i]));

        void Walk(TableInfo table, Func<string, string> scope, int depth)
        {
            if (depth > 50)
            {
                throw new InvalidOperationException($"Master/detail: the delete rules below '{root.Name}' are nested deeper than 50 levels.");
            }

            foreach (var md in MdLinks(table))
            {
                var rule = md.Link.OnDelete;
                if (rule == DeleteRule.None)
                {
                    continue;
                }

                var parent = "t" + depth;
                string Scope(string childRef) =>
                    "EXISTS (SELECT 1 FROM " + QuoteIdent(table.Name) + " " + parent + " WHERE " + scope(parent) + " AND "
                    + string.Join(" AND ", md.Pairs.Select(p => parent + "." + QuoteIdent(p.Master.Name) + " = " + childRef + "." + QuoteIdent(p.Detail.Name))) + ")";
                var target = QuoteIdent(md.Detail.Name);
                var where = Scope(target);
                var caption = MdCaptionExpr(md);
                var count = "SELECT COUNT(*) FROM " + target + " WHERE " + where;
                switch (rule)
                {
                    case DeleteRule.Restrict:
                        blockers.Add((caption, count));
                        break;
                    case DeleteRule.Cascade:
                        counts.Add((caption, count));
                        Walk(md.Detail, Scope, depth + 1);
                        statements.Add("DELETE FROM " + target + " WHERE " + where);
                        break;
                    case DeleteRule.SetNull:
                        statements.Add("UPDATE " + target + " SET " + string.Join(", ", md.Pairs.Select(p => QuoteIdent(p.Detail.Name) + " = NULL")) + " WHERE " + where);
                        break;
                }
            }
        }

        Walk(root, RootScope, 0);
        return (blockers, counts, statements);
    }

    private static string MdStepArray(List<(string Caption, string Sql)> steps) =>
        steps.Count == 0
            ? "[]"
            : "[" + string.Join(", ", steps.Select(s => $"new(() => {s.Caption}, {Lit(s.Sql)})")) + "]";

    // ---- JSON API ---------------------------------------------------------------------------------

    /// <summary>The PUT body when linked values cascade: reads the old row, updates and cascades in one transaction.</summary>
    private static void AppendMdApiUpdate(StringBuilder sb, string className, TableInfo table, List<FieldInfo> updateFields, bool owned)
    {
        void L(string line) => sb.AppendLine(line);
        L("            var read = new DynamicParameters();");
        AppendKeyParameters(sb, table, "            ", "read");
        AppendOwnerParameter(sb, owned, "read", "            ");
        L($"            var before = await connection.QueryFirstOrDefaultAsync<{className}>({Lit("SELECT " + SelectColumns(table) + " FROM " + QuoteIdent(table.Name) + " WHERE " + KeyWhereSql(table) + (owned ? " AND " + OwnerCondition(table) : string.Empty))}, read);");
        L("            if (before is null)");
        L("            {");
        L("                return Results.NotFound();");
        L("            }");
        L(string.Empty);
        L($"            var mdOld = {MdOldNew(table, className, "before")};");
        L($"            var mdNew = {MdOldNew(table, className, "item")};");
        L("            await ReferentialIntegrity.BeginAsync(connection);");
        L("            try");
        L("            {");
        L("                var args = new DynamicParameters();");
        AppendParameters(sb, updateFields, "item", className, "                ");
        AppendKeyParameters(sb, table, "                ");
        L($"                if (await connection.ExecuteAsync({Lit(UpdateSql(table, updateFields))}, args) == 0)");
        L("                {");
        L("                    await ReferentialIntegrity.RollbackAsync(connection);");
        L("                    return Results.NotFound();");
        L("                }");
        L(string.Empty);
        L($"                await ReferentialIntegrity.Update_{className}(connection, mdOld, mdNew);");
        L("                await ReferentialIntegrity.CommitAsync(connection);");
        L("            }");
        L("            catch");
        L("            {");
        L("                await ReferentialIntegrity.RollbackAsync(connection);");
        L("                throw;");
        L("            }");
        L(string.Empty);
        L("            return Results.NoContent();");
    }

    /// <summary>GET /api/master/keys/detail: the detail rows of one master row (guards of both tables apply).</summary>
    private static void AppendMdApiDetailList(StringBuilder sb, string className, TableInfo table, MdLink md, List<MdLink> all, string viewGuard)
    {
        var d = md.Detail;
        var dClass = Identifier.Pascal(d.Name);
        var segment = Identifier.Route(d.Name);
        if (all.First(l => MdSame(l.Detail.Name, d.Name)).Index != md.Index)
        {
            segment += "-" + md.Index;
        }

        var owned = IsOwned(table);
        var keys = table.PrimaryKeys;
        var names = KeyNames(table);
        void L(string line) => sb.AppendLine(line);
        L(string.Empty);
        L($"        group.MapGet(\"/{KeyRoute(table)}/{segment}\", async ({KeyParams(table)}, ClaimsPrincipal user) =>");
        L("        {");
        if (d.ViewMinLevel > 0)
        {
            L($"            if (!Permissions.Allowed(user, {d.ViewMinLevel}))");
            L("            {");
            L($"                return Results.Text({Lit(ViewDeniedMessage)}, \"text/plain; charset=utf-8\", statusCode: StatusCodes.Status403Forbidden);");
            L("            }");
            L(string.Empty);
        }

        L("            await using var connection = new SqliteConnection(connectionString);");
        L("            await connection.OpenAsync();");
        L("            var check = new DynamicParameters();");
        AppendKeyParameters(sb, table, "            ", "check");
        AppendOwnerParameter(sb, owned, "check", "            ");
        L($"            if (await connection.ExecuteScalarAsync<long>({Lit("SELECT COUNT(*) FROM " + QuoteIdent(table.Name) + " WHERE " + KeyWhereSql(table) + (owned ? " AND " + OwnerCondition(table) : string.Empty))}, check) == 0)");
        L("            {");
        L("                return Results.NotFound();");
        L("            }");
        L(string.Empty);
        var scope = "EXISTS (SELECT 1 FROM " + QuoteIdent(table.Name) + " t0 WHERE "
            + string.Join(" AND ", keys.Select((k, i) => "t0." + QuoteIdent(k.Name) + " = @" + names[i]))
            + " AND " + string.Join(" AND ", md.Pairs.Select(p => "t0." + QuoteIdent(p.Master.Name) + " = " + QuoteIdent(d.Name) + "." + QuoteIdent(p.Detail.Name))) + ")";
        var detailOwned = IsOwned(d);
        var sql = "SELECT " + SelectColumns(d) + " FROM " + QuoteIdent(d.Name) + " WHERE " + scope
            + (detailOwned ? " AND " + QuoteIdent(d.OwnerField) + " = @downer" : string.Empty) + OrderSql(d);
        L("            var args = new DynamicParameters();");
        AppendKeyParameters(sb, table, "            ");
        if (detailOwned)
        {
            L("            args.Add(\"@downer\", user.Identity?.Name ?? string.Empty);");
        }

        L($"            var items = (await connection.QueryAsync<{dClass}>({Lit(sql)}, args)).AsList();");
        L("            return Results.Ok(items);");
        L($"        }}){viewGuard};");
    }

    internal static string ReferentialIntegrityFile(string ns, IReadOnlyList<TableInfo> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        if (Localized)
        {
            sb.AppendLine($"using {ns}.Localization;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Web;");
        sb.AppendLine();
        sb.Append(IntegrityRuntimeHead);
        foreach (var table in tables)
        {
            var cls = Identifier.Pascal(table.Name);
            if (MdDeletePlanned(table))
            {
                var (blockers, counts, statements) = MdBuildPlan(table);
                var where = KeyWhereSql(table) + (IsOwned(table) ? " AND " + OwnerCondition(table) : string.Empty);
                sb.AppendLine();
                sb.AppendLine($"    public static readonly Plan Plan_{cls} = new({Lit(QuoteIdent(table.Name))}, {Lit(where)},");
                sb.AppendLine($"        {MdStepArray(blockers)},");
                sb.AppendLine($"        {MdStepArray(counts)},");
                sb.AppendLine($"        {(statements.Count == 0 ? "[]" : "[" + string.Join(", ", statements.Select(Lit)) + "]")});");
            }

            if (MdUpdatePlanned(table))
            {
                var tracked = MdTracked(table);
                sb.AppendLine();
                sb.AppendLine($"    /// <summary>Copies changed linked values of a {table.Name} row to its detail rows (UpdateRule.Cascade). Values are in tracked-field order.</summary>");
                sb.AppendLine($"    public static async Task Update_{cls}(SqliteConnection connection, object?[] oldValues, object?[] newValues)");
                sb.AppendLine("    {");
                foreach (var md in MdLinks(table).Where(l => l.Link.OnUpdate == UpdateRule.Cascade && l.Pairs.Any(p => !p.Master.IsPrimaryKey)))
                {
                    int Idx(FieldInfo f) => tracked.FindIndex(x => MdSame(x.Name, f.Name));
                    var changed = md.Pairs.Where(p => !p.Master.IsPrimaryKey).Select(p => Idx(p.Master)).Distinct().ToList();
                    var sets = string.Join(", ", md.Pairs.Where(p => !p.Master.IsPrimaryKey).Select(p => QuoteIdent(p.Detail.Name) + " = @n" + Idx(p.Master)));
                    var where = string.Join(" AND ", md.Pairs.Select(p => QuoteIdent(p.Detail.Name) + " = @o" + Idx(p.Master)));
                    var args = md.Pairs.Where(p => !p.Master.IsPrimaryKey).Select(p => $"(\"@n{Idx(p.Master)}\", newValues[{Idx(p.Master)}])")
                        .Concat(md.Pairs.Select(p => $"(\"@o{Idx(p.Master)}\", oldValues[{Idx(p.Master)}])")).Distinct();
                    sb.AppendLine($"        if (Changed(oldValues, newValues, {string.Join(", ", changed)}))");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            await ExecAsync(connection, {Lit("UPDATE " + QuoteIdent(md.Detail.Name) + " SET " + sets + " WHERE " + where)}, new (string, object?)[] {{ {string.Join(", ", args)} }});");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }

                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private const string IntegrityRuntimeHead = """
        /// <summary>
        /// Referential integrity for master rows: delete rules (Restrict, Cascade, Set null) and cascading updates of linked values.
        /// Every change runs in one transaction (BEGIN IMMEDIATE, ROLLBACK on error). Cascades intentionally bypass the ownership filter of
        /// the detail tables: they remove the orphans of a record the user is allowed to delete.
        /// </summary>
        public static class ReferentialIntegrity
        {
            public const int Deleted = 0;
            public const int NotFound = 1;
            public const int Blocked = 2;

            /// <summary>One counting statement; the caption names the detail table.</summary>
            public sealed record Step(Func<string> Caption, string Sql);

            public sealed record Plan(string Table, string Where, Step[] Blockers, Step[] Counts, string[] Statements);

            public sealed record Outcome(int Status, string Message);

            public static async Task BeginAsync(SqliteConnection connection) => await ExecAsync(connection, "BEGIN IMMEDIATE", []);

            public static async Task CommitAsync(SqliteConnection connection) => await ExecAsync(connection, "COMMIT", []);

            public static async Task RollbackAsync(SqliteConnection connection)
            {
                try
                {
                    await ExecAsync(connection, "ROLLBACK", []);
                }
                catch (SqliteException)
                {
                    // No transaction is active any more.
                }
            }

            public static async Task ExecAsync(SqliteConnection connection, string sql, (string Name, object? Value)[] args) =>
                await connection.ExecuteAsync(sql, Bind(sql, args));

            /// <summary>The arguments that <paramref name="sql"/> names.</summary>
            private static DynamicParameters Bind(string sql, (string Name, object? Value)[] args)
            {
                var parameters = new DynamicParameters();
                foreach (var (name, value) in args)
                {
                    if (sql.Contains(name, StringComparison.Ordinal))
                    {
                        parameters.Add(name, value is DBNull ? null : value);
                    }
                }

                return parameters;
            }

            private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, (string Name, object? Value)[] args) =>
                await connection.ExecuteScalarAsync<long>(sql, Bind(sql, args));

            public static bool Changed(object?[] oldValues, object?[] newValues, params int[] indexes) =>
                indexes.Any(i => !Equals(oldValues[i], newValues[i]));

            /// <summary>The non-empty counts of the steps with the caption of each detail table.</summary>
            public static async Task<List<(string Caption, long Count)>> CountsAsync(SqliteConnection connection, Step[] steps, (string Name, object? Value)[] args)
            {
                var result = new List<(string, long)>();
                foreach (var step in steps)
                {
                    var n = await ScalarAsync(connection, step.Sql, args);
                    if (n > 0)
                    {
                        result.Add((step.Caption(), n));
                    }
                }

                return result;
            }

            public static string BlockedText(List<(string Caption, long Count)> blockers) =>
                string.Join(" ", blockers.Select(b => "Cannot delete: " + b.Count + " related " + b.Caption + " record(s) exist."));

            /// <summary>Notes for the delete confirmation: what blocks the delete and what will also be deleted. Captions are encoded.</summary>
            public static string Decorate(string body, List<(string Caption, long Count)> blockers, List<(string Caption, long Count)> cascades)
            {
                var note = new System.Text.StringBuilder();
                foreach (var b in blockers)
                {
                    note.Append("<div class=\"alert alert-danger\" role=\"alert\">Cannot delete: ").Append(b.Count).Append(" related ")
                        .Append(Html.Encode(b.Caption)).Append(" record(s) exist.</div>");
                }

                foreach (var c in cascades)
                {
                    note.Append("<div class=\"alert alert-warning\" role=\"status\">This will also delete ").Append(c.Count).Append(" record(s) in ")
                        .Append(Html.Encode(c.Caption)).Append(".</div>");
                }

                var at = body.IndexOf("</p>", StringComparison.Ordinal);
                body = at < 0 ? note + body : body.Insert(at + 4, note.ToString());
                return blockers.Count == 0 ? body : body.Replace("<button type=\"submit\"", "<button type=\"submit\" disabled");
            }

            /// <summary>Deletes a master row with its dependents in one transaction: refuses when a Restrict rule is hit, cascades, sets null.</summary>
            public static async Task<Outcome> DeleteAsync(SqliteConnection connection, Plan plan, (string Name, object? Value)[] args)
            {
                await BeginAsync(connection);
                try
                {
                    if (await ScalarAsync(connection, "SELECT COUNT(*) FROM " + plan.Table + " WHERE " + plan.Where, args) == 0)
                    {
                        await RollbackAsync(connection);
                        return new Outcome(NotFound, string.Empty);
                    }

                    var blockers = await CountsAsync(connection, plan.Blockers, args);
                    if (blockers.Count > 0)
                    {
                        await RollbackAsync(connection);
                        return new Outcome(Blocked, BlockedText(blockers));
                    }

                    foreach (var statement in plan.Statements)
                    {
                        await ExecAsync(connection, statement, args);
                    }

                    await ExecAsync(connection, "DELETE FROM " + plan.Table + " WHERE " + plan.Where, args);
                    await CommitAsync(connection);
                    return new Outcome(Deleted, string.Empty);
                }
                catch
                {
                    await RollbackAsync(connection);
                    throw;
                }
            }
        """;

    // ---- the page helpers -----------------------------------------------------------------------

    internal static string MasterDetailKitFile(string ns) => (""""
        using System.Globalization;
        using System.Text;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Extensions.Primitives;

        namespace @@NS@@.Web;

        /// <summary>Helpers of master/detail pages: return URLs, presets of detail forms, the pager of detail grids and the expandable row script.</summary>
        public static class MasterDetailKit
        {
            /// <summary>Only a path on this site may be a return target (same rule as the login page).</summary>
            public static bool IsLocalPath(string? url) =>
                url is not null
                && url.StartsWith("/", StringComparison.Ordinal)
                && !url.StartsWith("//", StringComparison.Ordinal)
                && !url.StartsWith("/\\", StringComparison.Ordinal)
                && url.IndexOf(':') < 0
                && url.IndexOf('\\') < 0
                && !url.Any(char.IsControl);

            /// <summary>The current page (path and query, query values escaped) as a return URL; "/" when it is not a local path.</summary>
            public static string Here(HttpRequest request)
            {
                // Plain string concatenation: PathString + string would escape the question mark of the query again.
                var url = request.PathBase.ToUriComponent() + request.Path.ToUriComponent();
                var query = new StringBuilder();
                foreach (var pair in request.Query)
                {
                    foreach (var value in pair.Value)
                    {
                        query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
                    }
                }

                url += query.ToString();
                return IsLocalPath(url) ? url : "/";
            }

            /// <summary>The verified returnUrl of the request (query, or the posted form), else <paramref name="fallback"/>.</summary>
            public static string Back(HttpRequest request, string fallback)
            {
                var url = request.Query["returnUrl"].ToString();
                if (url.Length == 0 && request.HasFormContentType)
                {
                    try
                    {
                        url = request.Form["returnUrl"].ToString();
                    }
                    catch (InvalidOperationException)
                    {
                        // The form is not read yet; the query is the only source then.
                    }
                }

                return IsLocalPath(url) ? url : fallback;
            }

            /// <summary>The query string that carries returnUrl (and, for add and copy, the m_ presets) from one page to the next.</summary>
            public static string Carry(HttpRequest request, bool presets)
            {
                var query = new StringBuilder();
                foreach (var pair in request.Query)
                {
                    var keep = pair.Key == "returnUrl" ? IsLocalPath(pair.Value.ToString()) : presets && pair.Key.StartsWith("m_", StringComparison.Ordinal);
                    if (!keep)
                    {
                        continue;
                    }

                    query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value.ToString()));
                }

                return query.ToString();
            }

            /// <summary>0 when none of the preset fields is in the query, 1 when all are present and not empty, 2 otherwise.</summary>
            public static int PresetState(IQueryCollection query, string[] fields, out string[] values)
            {
                values = new string[fields.Length];
                var present = 0;
                var filled = 0;
                for (var i = 0; i < fields.Length; i++)
                {
                    if (query.TryGetValue("m_" + fields[i], out var value))
                    {
                        present++;
                        values[i] = value.ToString();
                        if (values[i].Length > 0)
                        {
                            filled++;
                        }
                    }
                    else
                    {
                        values[i] = string.Empty;
                    }
                }

                return present == 0 ? 0 : filled == fields.Length ? 1 : 2;
            }

            /// <summary>The typed value of a preset text (int, dec, bool as 1/0, date, text), or null when it does not fit.</summary>
            public static object? Parse(string text, string kind) => kind switch
            {
                "int" => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) ? whole : null,
                "dec" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null,
                "bool" => text is "1" or "true" or "True" ? 1L : text is "0" or "false" or "False" ? 0L : null,
                "date" => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null,
                _ => text,
            };

            public static string? Fixed(IReadOnlyDictionary<string, string>? presets, string name) =>
                presets is not null && presets.TryGetValue(name, out var value) ? value : null;

            /// <summary>A preset link field: shown read-only, posted through a hidden input (the server forces the verified value anyway).</summary>
            public static string FixedField(string label, string name, string value) =>
                "<div class=\"mb-3\"><label class=\"form-label\">" + Html.Encode(label) + "</label><input class=\"form-control\" type=\"text\" value=\""
                + Html.Attr(value) + "\" readonly>" + Html.Hidden(name, value) + "</div>";

            /// <summary>The form with the verified preset values in place of whatever was posted.</summary>
            public static IFormCollection WithValues(IFormCollection form, IReadOnlyDictionary<string, string> values)
            {
                var copy = new Dictionary<string, StringValues>(StringComparer.Ordinal);
                foreach (var pair in form)
                {
                    copy[pair.Key] = pair.Value;
                }

                foreach (var pair in values)
                {
                    copy[pair.Key] = pair.Value;
                }

                return new FormCollection(copy);
            }

            /// <summary>Page links of a detail grid. A grid inside a fetched fragment links to the fragment endpoint (the script reloads it).</summary>
            public static string Pager(HttpRequest request, string name, int page, int pageCount, string? fragmentBase)
            {
                if (pageCount <= 1)
                {
                    return string.Empty;
                }

                var others = new StringBuilder();
                if (fragmentBase is null)
                {
                    foreach (var pair in request.Query)
                    {
                        if (pair.Key == name)
                        {
                            continue;
                        }

                        foreach (var value in pair.Value)
                        {
                            others.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty)).Append('&');
                        }
                    }
                }

                var sb = new StringBuilder("<nav><ul class=\"pagination pagination-sm\">");
                for (var i = 1; i <= pageCount; i++)
                {
                    var href = (fragmentBase ?? (request.PathBase + request.Path)) + "?" + others + name + "=" + i;
                    sb.Append("<li class=\"page-item").Append(i == page ? " active" : string.Empty).Append("\"><a class=\"page-link\" href=\"").Append(Html.Attr(href)).Append('"')
                        .Append(fragmentBase is null ? string.Empty : " data-md-page").Append('>').Append(i).Append("</a></li>");
                }

                return sb.Append("</ul></nav>").ToString();
            }

            public static string ExpandButton(string href, string text) =>
                "<button type=\"button\" class=\"btn btn-sm btn-outline-secondary\" data-md-expand=\"" + Html.Attr(href) + "\" aria-expanded=\"false\">" + Html.Encode(text) + "</button>";

            /// <summary>Starts once per page: the expand buttons insert a table row with the fetched grid below their row.</summary>
            public const string ExpandScriptTag = "<script>" + """
        (function(){if(window.mdExpand){return;}window.mdExpand=1;
        function load(td,url){fetch(url,{headers:{'X-Requested-With':'fetch'},credentials:'same-origin'}).then(function(r){return r.text();}).then(function(h){td.innerHTML=h;});}
        document.addEventListener('click',function(e){var t=e.target;if(!t||!t.closest){return;}
        var b=t.closest('[data-md-expand]');
        if(b){e.preventDefault();var tr=b.closest('tr');if(!tr){return;}var url=b.getAttribute('data-md-expand');
        var n=tr.nextElementSibling;while(n&&n.hasAttribute('data-md-row')){if(n.getAttribute('data-md-for')===url){n.remove();b.setAttribute('aria-expanded','false');return;}n=n.nextElementSibling;}
        var row=document.createElement('tr');row.setAttribute('data-md-row','');row.setAttribute('data-md-for',url);
        var td=document.createElement('td');td.colSpan=tr.cells.length;row.appendChild(td);tr.parentNode.insertBefore(row,tr.nextSibling);
        load(td,url);b.setAttribute('aria-expanded','true');return;}
        var a=t.closest('a[data-md-page]');
        if(a){var cell=a.closest('td');if(cell&&cell.closest('[data-md-row]')){e.preventDefault();load(cell,a.getAttribute('href'));}}});
        })();
        """ + "</script>";
        }
        """").Replace("@@NS@@", ns);
}
