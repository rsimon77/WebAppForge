using System.Text;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    /// <summary>Route of the audit trail page.</summary>
    internal const string AuditRoute = "/audit-trail";

    [ThreadStatic]
    private static AuditSettings? _audit;

    /// <summary>The audit settings of the site being generated, or null when the site keeps no audit trail.</summary>
    internal static AuditSettings? Auditing
    {
        get => _audit;
        set => _audit = value;
    }

    /// <summary>True when changes to <paramref name="table"/> are written to the audit trail.</summary>
    internal static bool IsAudited(TableInfo table) =>
        Auditing is { } audit && table.Audit.Enabled && !string.Equals(table.Name, audit.LogTable.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static bool AuditsInserts(TableInfo table) => IsAudited(table) && table.Audit.Inserts;

    internal static bool AuditsUpdates(TableInfo table) => IsAudited(table) && table.Audit.Updates;

    internal static bool AuditsDeletes(TableInfo table) => IsAudited(table) && table.Audit.Deletes;

    /// <summary>The fields whose values an entry keeps: not keys, secret, binary or excluded.</summary>
    internal static List<FieldInfo> AuditFields(TableInfo table) =>
        table.Fields.Where(f => !f.IsPrimaryKey && !f.IsSecret && f.Type != FieldType.Binary
            && !table.Audit.ExcludedFields.Any(x => string.Equals(x.Trim(), f.Name, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>The problems of the audit settings, for generation and for the editor. Empty when the trail is off.</summary>
    public static List<string> AuditProblems(Project project)
    {
        var problems = new List<string>();
        var audit = project.Audit;
        if (!audit.Enabled)
        {
            return problems;
        }

        var name = audit.LogTable.Trim();
        if (name.Length == 0)
        {
            problems.Add("The audit trail needs the name of its log table.");
        }
        else if (project.Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { } clash && !IsAuditLog(clash))
        {
            problems.Add($"The audit log table '{name}' has the name of a table of the project. Choose another name, or remove that table.");
        }

        foreach (var table in project.Tables.Where(t => t.Generate && t.Audit.Enabled))
        {
            foreach (var field in table.Audit.ExcludedFields.Where(x => !table.Fields.Any(f => string.Equals(f.Name, x.Trim(), StringComparison.OrdinalIgnoreCase))))
            {
                problems.Add($"Table '{table.Name}' excludes the field '{field}' from the audit trail, but it has no such field.");
            }
        }

        return problems;
    }

    /// <summary>
    /// True for a table of the project that is the log table itself, read back from a database the site already wrote to. The
    /// generator leaves it out, so that reading the schema again does not turn the log into a table of the site.
    /// </summary>
    internal static bool IsAuditLog(TableInfo table) =>
        new[] { "occurred_at", "table_name", "action", "record_key", "changes" }
            .All(c => table.Fields.Any(f => string.Equals(f.Name, c, StringComparison.OrdinalIgnoreCase)));

    /// <summary>True when <paramref name="table"/> is the log table of the project's audit trail.</summary>
    public static bool IsAuditLogOf(Project project, TableInfo table) =>
        project.Audit.Enabled && string.Equals(table.Name, project.Audit.LogTable.Trim(), StringComparison.OrdinalIgnoreCase) && IsAuditLog(table);

    /// <summary>AuditKey() and AuditValues() of the model of an audited table.</summary>
    private static void AppendAuditModelMembers(StringBuilder sb, string className, TableInfo table)
    {
        if (!IsAudited(table))
        {
            return;
        }

        var keys = table.PrimaryKeys.Select(k => $"Convert.ToString({PropName(className, k)}, System.Globalization.CultureInfo.InvariantCulture)").ToList();
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The key of the record in the audit trail: the key values joined by a slash.</summary>");
        sb.AppendLine(keys.Count == 0
            ? "    public string AuditKey() => string.Empty;"
            : $"    public string AuditKey() => string.Join(\"/\", {string.Join(", ", keys)});");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The values the audit trail keeps, by field name: every field except keys, secret, binary and excluded ones.</summary>");
        sb.AppendLine("    public Dictionary<string, object?> AuditValues() => new()");
        sb.AppendLine("    {");
        foreach (var field in AuditFields(table))
        {
            sb.AppendLine($"        [{Lit(field.Name)}] = {PropName(className, field)},");
        }

        sb.AppendLine("    };");
    }

    /// <summary>C# statement that writes one audit entry for a row of <paramref name="table"/>.</summary>
    private static string AuditWrite(TableInfo table, string action, string http, string key, string before, string after) =>
        $"await Audit.WriteAsync(connection, {http}, {Lit(table.Name)}, {Lit(action)}, {key}, {before}, {after});";

    /// <summary>
    /// Wraps the statements of <paramref name="body"/> in a transaction with the audit entry, so that a change is never kept
    /// without its entry. <paramref name="body"/> writes at the given indent and may return from inside the try.
    /// </summary>
    private static void AppendAuditTransaction(StringBuilder sb, string indent, Action<string> body)
    {
        sb.AppendLine($"{indent}await Audit.BeginAsync(connection);");
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        body(indent + "    ");
        sb.AppendLine($"{indent}    await Audit.CommitAsync(connection);");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}catch");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    await Audit.RollbackAsync(connection);");
        sb.AppendLine($"{indent}    throw;");
        sb.AppendLine($"{indent}}}");
    }

    /// <summary>The Audit.Capture entries of the delete plan of <paramref name="root"/>: the detail rows a cascade deletes or a set-null changes.</summary>
    private static List<(string Table, string Action, string Sql, List<string> NullFields)> MdAuditCaptures(TableInfo root) =>
        root.PrimaryKeys.Count == 0 || Auditing is null ? [] : MdBuildPlan(root).Captures;

    /// <summary>
    /// The detail fields a cascading update of <paramref name="md"/> sets and the trail keeps: empty when the detail table is not
    /// audited for updates.
    /// </summary>
    private static List<FieldInfo> MdAuditedUpdateFields(MdLink md) =>
        !AuditsUpdates(md.Detail)
            ? []
            : md.Pairs.Where(p => !p.Master.IsPrimaryKey).Select(p => p.Detail)
                .Where(f => AuditFields(md.Detail).Any(a => MdSame(a.Name, f.Name))).ToList();

    /// <summary>True when the cascading update of <paramref name="table"/> changes rows of an audited detail table.</summary>
    private static bool MdUpdateAudited(TableInfo table) =>
        Auditing is not null && MdUpdatePlanned(table)
        && MdLinks(table).Any(l => l.Link.OnUpdate == UpdateRule.Cascade && l.Pairs.Any(p => !p.Master.IsPrimaryKey) && MdAuditedUpdateFields(l).Count > 0);

    /// <summary>
    /// The extra arguments of ReferentialIntegrity.DeleteAsync for an audited delete: the HTTP context, and a callback that writes the
    /// entry of the deleted row inside the delete transaction. Empty when neither the row nor its cascades are audited.
    /// </summary>
    private static string MdAuditDeleteArgs(TableInfo table, string http, string beforeVar)
    {
        var captures = MdAuditCaptures(table).Count > 0;
        var root = AuditsDeletes(table);
        if (!captures && !root)
        {
            return string.Empty;
        }

        return root
            ? $", {http}, async () => {{ if ({beforeVar} is not null) {{ {AuditWrite(table, "delete", http, beforeVar + ".AuditKey()", beforeVar + ".AuditValues()", "null")} }} }}"
            : $", {http}";
    }

    /// <summary>
    /// The ReferentialIntegrity runtime of a site with an audit trail: the delete plan reads the rows it cascades to before the
    /// statements run, and writes their entries, and the entry of the deleted row, before the transaction commits.
    /// </summary>
    private static string AuditedIntegrityHead()
    {
        static string Swap(string text, string from, string to) =>
            text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) : throw new InvalidOperationException("The integrity runtime changed: " + from);

        var head = IntegrityRuntimeHead;
        head = Swap(head, "Step[] Counts, string[] Statements);", "Step[] Counts, string[] Statements, Audit.Capture[]? Captures = null);");
        head = Swap(head, "Task<Outcome> DeleteAsync(SqliteConnection connection, Plan plan, (string Name, object? Value)[] args)",
            "Task<Outcome> DeleteAsync(SqliteConnection connection, Plan plan, (string Name, object? Value)[] args,\n"
            + "        HttpContext? http = null, Func<Task>? onDeleted = null)");
        head = Swap(head, "foreach (var statement in plan.Statements)",
            "var captured = plan.Captures is { Length: > 0 } captures ? await Audit.CaptureAsync(connection, captures, sql => Bind(sql, args)) : null;\n"
            + "            foreach (var statement in plan.Statements)");
        head = Swap(head, "await ExecAsync(connection, \"DELETE FROM \" + plan.Table + \" WHERE \" + plan.Where, args);",
            "await ExecAsync(connection, \"DELETE FROM \" + plan.Table + \" WHERE \" + plan.Where, args);\n"
            + "            if (onDeleted is not null)\n            {\n                await onDeleted();\n            }\n\n"
            + "            if (captured is not null)\n            {\n                await Audit.WriteCapturedAsync(connection, http, captured);\n            }\n");
        return head;
    }

    /// <summary>
    /// SELECT of the rows an integrity statement on <paramref name="table"/> changes, for the audit trail: the key columns as __k0,
    /// __k1, ... and the audited fields by name (or only <paramref name="fields"/>).
    /// </summary>
    private static string AuditCaptureSql(TableInfo table, string where, IEnumerable<FieldInfo>? fields = null)
    {
        var columns = table.PrimaryKeys.Select((k, i) => QuoteIdent(k.Name) + " AS " + QuoteIdent("__k" + i))
            .Concat((fields ?? AuditFields(table)).Select(f => QuoteIdent(f.Name) + " AS " + QuoteIdent(f.Name)));
        return "SELECT " + string.Join(", ", columns) + " FROM " + QuoteIdent(table.Name) + " WHERE " + where;
    }

    /// <summary>Web/Audit.cs of a site with an audit trail.</summary>
    public static string AuditRuntime(string ns)
    {
        var audit = Auditing!;
        var table = QuoteIdent(audit.LogTable.Trim());
        var mode = audit.Values switch
        {
            AuditValues.None => 0,
            AuditValues.All => 2,
            _ => 1,
        };
        return ("""
            using System.Globalization;
            using System.Text.Json;
            using Dapper;
            using Microsoft.AspNetCore.Http;
            using Microsoft.Data.Sqlite;

            namespace @@NS@@.Web;

            /// <summary>
            /// The audit trail: one entry per inserted, updated or deleted record of an audited table, written on the connection of
            /// the change inside its transaction. Values are kept as JSON: {"field": {"old": ..., "new": ...}}.
            /// </summary>
            public static class Audit
            {
                public const string Table = @@TABLE@@;

                private const string CreateSql = @@CREATE@@;

                private const string InsertSql = "INSERT INTO " + Table
                    + " (occurred_at, user_name, ip_address, table_name, action, record_key, changes) VALUES (@at, @user, @ip, @table, @action, @key, @changes)";

                /// <summary>0: no values; 1: the values of new and deleted records and the changed fields of an update; 2: every field.</summary>
                private const int Mode = @@MODE@@;

                private const bool RecordIpAddress = @@IP@@;

                /// <summary>Rows that an integrity statement will change, read before it runs: the SELECT names the keys __k0, __k1, ...</summary>
                public sealed record Capture(string Table, string Action, string Sql, string[] NullFields);

                /// <summary>Creates the log table when it does not exist. Program.cs calls it once at startup.</summary>
                public static async Task EnsureTableAsync(string connectionString)
                {
                    await using var connection = new SqliteConnection(connectionString);
                    await connection.OpenAsync();
                    await connection.ExecuteAsync(CreateSql);
                }

                public static async Task BeginAsync(SqliteConnection connection) => await connection.ExecuteAsync("BEGIN IMMEDIATE");

                public static async Task CommitAsync(SqliteConnection connection) => await connection.ExecuteAsync("COMMIT");

                public static async Task RollbackAsync(SqliteConnection connection)
                {
                    try
                    {
                        await connection.ExecuteAsync("ROLLBACK");
                    }
                    catch (SqliteException)
                    {
                        // No transaction is active any more.
                    }
                }

                /// <summary>Writes one entry. An update that changed none of the kept fields writes nothing.</summary>
                public static async Task WriteAsync(SqliteConnection connection, HttpContext? http, string table, string action, string key,
                    IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after)
                {
                    var changes = Changes(before, after, out var changed);
                    if (!changed)
                    {
                        return;
                    }

                    await connection.ExecuteAsync(InsertSql, new
                    {
                        at = DateTime.UtcNow,
                        user = http?.User.Identity?.Name,
                        ip = RecordIpAddress ? http?.Connection.RemoteIpAddress?.ToString() : null,
                        table,
                        action,
                        key,
                        changes,
                    });
                }

                /// <summary>The JSON of the kept values, or null in mode 0. <paramref name="changed"/> is false for an update that changed nothing.</summary>
                public static string? Changes(IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after, out bool changed)
                {
                    var update = before is not null && after is not null;
                    changed = !update;
                    var result = new Dictionary<string, Dictionary<string, object?>>();
                    foreach (var name in (before ?? after ?? new Dictionary<string, object?>()).Keys)
                    {
                        var old = Plain(before?.GetValueOrDefault(name));
                        var now = Plain(after?.GetValueOrDefault(name));
                        var differs = update && !Equals(old, now);
                        changed |= differs;
                        if (update && Mode == 1 && !differs)
                        {
                            continue;
                        }

                        var entry = new Dictionary<string, object?>();
                        if (before is not null)
                        {
                            entry["old"] = old;
                        }

                        if (after is not null)
                        {
                            entry["new"] = now;
                        }

                        result[name] = entry;
                    }

                    return Mode == 0 ? null : JsonSerializer.Serialize(result);
                }

                /// <summary>A value as the trail compares and stores it: numbers as decimals, dates as ISO text, other values as text.</summary>
                public static object? Plain(object? value) => value switch
                {
                    null or DBNull => null,
                    bool flag => flag,
                    decimal number => number,
                    byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                    double number when !double.IsNaN(number) && !double.IsInfinity(number) && Math.Abs(number) < 7.9e28 => (decimal)number,
                    float number when !float.IsNaN(number) && !float.IsInfinity(number) => (decimal)number,
                    DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture),
                };

                /// <summary>Reads the rows of each capture before the statements run.</summary>
                public static async Task<List<(Capture Capture, List<Dictionary<string, object?>> Rows)>> CaptureAsync(
                    SqliteConnection connection, Capture[] captures, Func<string, DynamicParameters> bind)
                {
                    var result = new List<(Capture, List<Dictionary<string, object?>>)>();
                    foreach (var capture in captures)
                    {
                        var rows = (await connection.QueryAsync(capture.Sql, bind(capture.Sql)))
                            .Select(row => ((IDictionary<string, object>)row).ToDictionary(p => p.Key, p => (object?)p.Value))
                            .ToList();
                        result.Add((capture, rows));
                    }

                    return result;
                }

                /// <summary>Writes the entries of captured rows after their statements ran: deletes, or updates that set the fields to null.</summary>
                public static async Task WriteCapturedAsync(SqliteConnection connection, HttpContext? http,
                    List<(Capture Capture, List<Dictionary<string, object?>> Rows)> captured)
                {
                    foreach (var (capture, rows) in captured)
                    {
                        foreach (var row in rows)
                        {
                            var values = row.Where(p => !p.Key.StartsWith("__k", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value);
                            if (capture.Action == "delete")
                            {
                                await WriteAsync(connection, http, capture.Table, "delete", KeyOf(row), values, null);
                            }
                            else
                            {
                                var after = values.ToDictionary(p => p.Key, p => capture.NullFields.Contains(p.Key) ? null : p.Value);
                                await WriteAsync(connection, http, capture.Table, "update", KeyOf(row), values, after);
                            }
                        }
                    }
                }

                /// <summary>Writes update entries for captured rows whose <paramref name="newValues"/> fields were changed by one statement.</summary>
                public static async Task WriteUpdatedAsync(SqliteConnection connection, HttpContext? http, string table,
                    List<Dictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> newValues)
                {
                    foreach (var row in rows)
                    {
                        var values = row.Where(p => !p.Key.StartsWith("__k", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value);
                        var after = values.ToDictionary(p => p.Key, p => newValues.TryGetValue(p.Key, out var value) ? value : p.Value);
                        await WriteAsync(connection, http, table, "update", KeyOf(row), values, after);
                    }
                }

                /// <summary>The record key of a captured row: its __k columns joined by a slash.</summary>
                private static string KeyOf(Dictionary<string, object?> row) =>
                    string.Join("/", row.Where(p => p.Key.StartsWith("__k", StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => Convert.ToString(p.Value, CultureInfo.InvariantCulture)));
            }
            """).Replace("@@NS@@", ns)
            .Replace("@@TABLE@@", Lit(table))
            .Replace("@@CREATE@@", Lit(Db.CreateAuditTable(audit.LogTable.Trim())))
            .Replace("@@MODE@@", mode.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("@@IP@@", audit.RecordIpAddress ? "true" : "false");
    }

    /// <summary>Pages/AuditTrailPages.cs: the entries, newest first, with filters on table, action, user, record and date.</summary>
    public static string AuditPage(string ns, IReadOnlyList<TableInfo> tables, bool levelSecured)
    {
        var audit = Auditing!;
        var audited = tables.Where(IsAudited).ToList();
        var level = levelSecured ? Math.Max(0, audit.LogPageMinLevel) : 0;
        var sb = new StringBuilder();
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        if (Localized)
        {
            sb.AppendLine($"using {ns}.Localization;");
        }

        if (level > 0)
        {
            sb.AppendLine($"using {ns}.Security;");
        }

        sb.AppendLine($"using {ns}.Web;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Pages;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The audit trail page. Every value is HTML-encoded; filters are bound as parameters.</summary>");
        sb.AppendLine("public static class AuditTrailPages");
        sb.AppendLine("{");
        sb.AppendLine($"    public const string Route = {Lit(AuditRoute)};");
        sb.AppendLine("    private const int PageSize = 50;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>One entry as the page reads it.</summary>");
        sb.AppendLine("    public sealed class Entry");
        sb.AppendLine("    {");
        sb.AppendLine("        public long Id { get; set; }");
        sb.AppendLine("        public DateTime OccurredAt { get; set; }");
        sb.AppendLine("        public string? UserName { get; set; }");
        sb.AppendLine("        public string? IpAddress { get; set; }");
        sb.AppendLine("        public string TableName { get; set; } = string.Empty;");
        sb.AppendLine("        public string Action { get; set; } = string.Empty;");
        sb.AppendLine("        public string? RecordKey { get; set; }");
        sb.AppendLine("        public string? Changes { get; set; }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The audited tables: name, caption and the view page of a record (null without one).</summary>");
        sb.AppendLine("    private static (string Name, string Caption, string? View)[] Tables => new (string, string, string?)[]");
        sb.AppendLine("    {");
        foreach (var table in audited)
        {
            var view = table.Pages.View && table.PrimaryKeys.Count > 0 ? Lit("/" + Identifier.Route(table.Name) + "/view/") : "null";
            sb.AppendLine($"        ({Lit(table.Name)}, {TableText(table)}, {view}),");
        }

        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public static IEndpointRouteBuilder MapAuditTrailPages(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        var guard = level > 0 ? $".RequireLevel({level}, {Lit(ViewDeniedMessage)})" : string.Empty;
        sb.AppendLine($"        app.MapGet(Route, (HttpRequest request) => ListPage(request, connectionString)){guard};");
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine();
        var orderAndPage = " ORDER BY id DESC" + Db.Page;
        sb.AppendLine($$"""
                private static async Task<IResult> ListPage(HttpRequest request, string connectionString)
                {
                    var table = request.Query["t"].ToString();
                    var action = request.Query["a"].ToString();
                    var user = request.Query["u"].ToString().Trim();
                    var key = request.Query["k"].ToString().Trim();
                    var from = DateOnly.TryParse(request.Query["from"], CultureInfo.InvariantCulture, out var f) ? f : (DateOnly?)null;
                    var to = DateOnly.TryParse(request.Query["to"], CultureInfo.InvariantCulture, out var t) ? t : (DateOnly?)null;
                    var clauses = new List<string>();
                    var args = new DynamicParameters();
                    if (Array.Exists(Tables, x => x.Name == table))
                    {
                        clauses.Add("table_name = @table");
                        args.Add("@table", table);
                    }

                    if (action is "insert" or "update" or "delete")
                    {
                        clauses.Add("action = @action");
                        args.Add("@action", action);
                    }

                    if (user.Length > 0)
                    {
                        clauses.Add("user_name LIKE @user");
                        args.Add("@user", "%" + user + "%");
                    }

                    if (key.Length > 0)
                    {
                        clauses.Add("record_key = @key");
                        args.Add("@key", key);
                    }

                    if (from is { } first)
                    {
                        clauses.Add("occurred_at >= @from");
                        args.Add("@from", first.ToDateTime(TimeOnly.MinValue));
                    }

                    if (to is { } last)
                    {
                        clauses.Add("occurred_at < @to");
                        args.Add("@to", last.AddDays(1).ToDateTime(TimeOnly.MinValue));
                    }

                    var where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
                    await using var connection = new SqliteConnection(connectionString);
                    await connection.OpenAsync();
                    var total = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM " + Audit.Table + where, args);
                    var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
                    var page = Math.Clamp(int.TryParse(request.Query["page"], out var p) ? p : 1, 1, pageCount);
                    args.Add("@limit", PageSize);
                    args.Add("@offset", (page - 1) * PageSize);
                    var entries = (await connection.QueryAsync<Entry>(
                        "SELECT id AS Id, occurred_at AS OccurredAt, user_name AS UserName, ip_address AS IpAddress, table_name AS TableName, action AS Action, "
                        + "record_key AS RecordKey, changes AS Changes FROM " + Audit.Table + where + {{Lit(orderAndPage)}}, args)).AsList();

                    var body = new StringBuilder();
                    body.Append("<form method=\"get\" action=\"").Append(Html.Attr(Route)).Append("\" class=\"row g-2 align-items-end mb-3\">");
                    body.Append(Select("t", {{Ui("auditTable")}}, table, Tables.Select(x => (x.Name, x.Caption))));
                    body.Append(Select("a", {{Ui("auditAction")}}, action, new[] { ("insert", ActionText("insert")), ("update", ActionText("update")), ("delete", ActionText("delete")) }));
                    body.Append(Input("u", {{Ui("auditUser")}}, "text", user));
                    body.Append(Input("k", {{Ui("auditRecord")}}, "text", key));
                    body.Append(Input("from", {{Ui("auditFrom")}}, "date", from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty));
                    body.Append(Input("to", {{Ui("auditTo")}}, "date", to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty));
                    body.Append("<div class=\"col-auto\"><button type=\"submit\" class=\"btn btn-sm btn-primary\">").Append(Html.Encode({{Ui("auditApply")}})).Append("</button> ")
                        .Append("<a class=\"btn btn-sm btn-outline-secondary\" href=\"").Append(Html.Attr(Route)).Append("\">").Append(Html.Encode({{Ui("auditClear")}})).Append("</a></div></form>");
                    body.Append("<div class=\"table-responsive\"><table class=\"table table-sm table-striped align-middle audit-trail\"><thead><tr>")
                        .Append("<th>").Append(Html.Encode({{Ui("auditWhen")}})).Append("</th><th>").Append(Html.Encode({{Ui("auditUser")}})).Append("</th>")
                        .Append("<th>").Append(Html.Encode({{Ui("auditTable")}})).Append("</th><th>").Append(Html.Encode({{Ui("auditAction")}})).Append("</th>")
                        .Append("<th>").Append(Html.Encode({{Ui("auditRecord")}})).Append("</th><th>").Append(Html.Encode({{Ui("auditChanges")}})).Append("</th></tr></thead><tbody>");
                    foreach (var entry in entries)
                    {
                        var known = Array.Find(Tables, x => x.Name == entry.TableName);
                        body.Append("<tr><td class=\"text-nowrap\">").Append(Html.Encode(entry.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("</td>")
                            .Append("<td>").Append(Html.Encode(entry.UserName ?? string.Empty));
                        if (!string.IsNullOrEmpty(entry.IpAddress))
                        {
                            body.Append("<div class=\"small text-muted\">").Append(Html.Encode(entry.IpAddress)).Append("</div>");
                        }

                        body.Append("</td><td>").Append(Html.Encode(known.Caption ?? entry.TableName)).Append("</td>")
                            .Append("<td><span class=\"badge ").Append(entry.Action switch { "insert" => "text-bg-success", "delete" => "text-bg-danger", _ => "text-bg-secondary" })
                            .Append("\">").Append(Html.Encode(ActionText(entry.Action))).Append("</span></td><td>");
                        var record = Html.Encode(entry.RecordKey ?? string.Empty);
                        body.Append(known.View is { } view && entry.Action != "delete" && !string.IsNullOrEmpty(entry.RecordKey)
                            ? "<a href=\"" + Html.Attr(view + string.Join("/", entry.RecordKey.Split('/').Select(Uri.EscapeDataString))) + "\">" + record + "</a>"
                            : record);
                        body.Append("</td><td>").Append(ChangesHtml(entry.Changes)).Append("</td></tr>");
                    }

                    if (entries.Count == 0)
                    {
                        body.Append("<tr><td colspan=\"6\" class=\"text-muted\">").Append(Html.Encode({{Ui("auditEmpty")}})).Append("</td></tr>");
                    }

                    body.Append("</tbody></table></div>");
                    body.Append(Html.PageLinks(request, page, pageCount, Route));
                    return Html.Page({{Ui("auditTrail")}}, body.ToString());
                }

                private static string ActionText(string action) => action switch
                {
                    "insert" => {{Ui("auditInsert")}},
                    "update" => {{Ui("auditUpdate")}},
                    "delete" => {{Ui("auditDelete")}},
                    _ => action,
                };

                private static string Select(string name, string caption, string current, IEnumerable<(string Value, string Text)> options)
                {
                    var sb = new StringBuilder("<div class=\"col-auto\"><label class=\"form-label small mb-0\" for=\"at-").Append(name).Append("\">").Append(Html.Encode(caption))
                        .Append("</label><select class=\"form-select form-select-sm\" id=\"at-").Append(name).Append("\" name=\"").Append(name).Append("\"><option value=\"\">")
                        .Append(Html.Encode({{Ui("auditAll")}})).Append("</option>");
                    foreach (var (value, text) in options)
                    {
                        sb.Append("<option value=\"").Append(Html.Attr(value)).Append('"').Append(value == current ? " selected" : string.Empty).Append('>').Append(Html.Encode(text)).Append("</option>");
                    }

                    return sb.Append("</select></div>").ToString();
                }

                private static string Input(string name, string caption, string type, string value) =>
                    "<div class=\"col-auto\"><label class=\"form-label small mb-0\" for=\"at-" + name + "\">" + Html.Encode(caption) + "</label><input class=\"form-control form-control-sm\" type=\""
                    + type + "\" id=\"at-" + name + "\" name=\"" + name + "\" value=\"" + Html.Attr(value) + "\"></div>";

                /// <summary>The kept values as a list: "field: old → new", "field: value" for a new or deleted record.</summary>
                private static string ChangesHtml(string? json)
                {
                    if (string.IsNullOrEmpty(json))
                    {
                        return string.Empty;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(json);
                        var sb = new StringBuilder("<ul class=\"list-unstyled small mb-0\">");
                        foreach (var field in document.RootElement.EnumerateObject())
                        {
                            var hasOld = field.Value.TryGetProperty("old", out var old);
                            var hasNew = field.Value.TryGetProperty("new", out var now);
                            sb.Append("<li><span class=\"text-muted\">").Append(Html.Encode(field.Name)).Append(":</span> ");
                            if (hasOld && hasNew)
                            {
                                sb.Append("<del>").Append(Html.Encode(Text(old))).Append("</del> &rarr; ").Append(Html.Encode(Text(now)));
                            }
                            else
                            {
                                sb.Append(Html.Encode(Text(hasOld ? old : now)));
                            }

                            sb.Append("</li>");
                        }

                        return sb.Append("</ul>").ToString();
                    }
                    catch (JsonException)
                    {
                        return Html.Encode(json);
                    }
                }

                private static string Text(JsonElement value) => value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => "(empty)",
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    _ => value.GetRawText(),
                };
            }
            """);
        return sb.ToString();
    }
}
