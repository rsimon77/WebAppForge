using System.Text;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>Source text for every generated file. Static text lives in raw string literals; per-field code is built from the field model.</summary>
internal static partial class Templates
{
    // ---- localization ---------------------------------------------------------------------------
    // When Localized is set (project.Localization.Enabled, set by ProjectGenerator for one Generate call), every caption and fixed
    // UI text is emitted as a call to the generated Strings helper, with the English text as fallback. When it is not set, the
    // helpers below return the plain string literals, so the output is the same as before localization existed.

    /// <summary>True while generating a site with localization. Set by <see cref="ProjectGenerator"/> at the start of each Generate call.</summary>
    [ThreadStatic]
    internal static bool Localized;

    [ThreadStatic]
    private static SqlDialect? _db;

    [ThreadStatic]
    private static bool _entraAuth;

    /// <summary>True when the site signs in to SQL Server with Microsoft Entra ID, which needs SqlClient's Azure extension package.</summary>
    internal static bool EntraAuth
    {
        get => _entraAuth;
        set => _entraAuth = value;
    }

    [ThreadStatic]
    private static string? _connectionVariable;

    /// <summary>Environment variable the generated site reads its connection string from (empty: appsettings.json only).</summary>
    internal static string ConnectionVariable
    {
        get => _connectionVariable ?? string.Empty;
        set => _connectionVariable = value;
    }

    /// <summary>The SQL dialect of the site being generated. SQLite unless a generation sets another.</summary>
    internal static SqlDialect Db
    {
        get => _db ?? SqlDialect.Sqlite;
        set => _db = value;
    }

    /// <summary>Name of the table whose pages are being generated; part of the key of its field captions.</summary>
    [ThreadStatic]
    private static string? _localizedTable;

    /// <summary>C# expression for a fixed UI text.</summary>
    internal static string Ui(string id) => Localized
        ? $"Strings.T({Lit(UiStrings.KeyOf(id))}, {Lit(UiStrings.English(id))})"
        : Lit(UiStrings.English(id));

    /// <summary>C# expression for a fixed UI text followed by a plain suffix, e.g. "Add " before a caption.</summary>
    private static string UiSuffix(string id, string suffix) => Localized
        ? Ui(id) + " + " + Lit(suffix)
        : Lit(UiStrings.English(id) + suffix);

    /// <summary>
    /// A fixed UI text placed inside a generated string literal (between quotes). Localized, it closes the literal, appends the
    /// HTML-encoded text and opens the literal again.
    /// </summary>
    private static string UiInText(string id) => Localized
        ? "\" + Html.Encode(" + Ui(id) + ") + \""
        : UiStrings.English(id);

    /// <summary>C# expression for the caption of a table.</summary>
    private static string TableText(TableInfo table) => Localized
        ? $"Strings.T({Lit(LocalizationKeys.Table(table.Name))}, {Lit(Caption(table))})"
        : Lit(Caption(table));

    /// <summary>C# expression for the caption of a field of the table being generated.</summary>
    private static string FieldText(FieldInfo field) => Localized
        ? $"Strings.T({Lit(LocalizationKeys.Field(_localizedTable ?? string.Empty, field.Name))}, {Lit(FieldCaption(field))})"
        : Lit(FieldCaption(field));

    /// <summary>C# expression for a validation message such as "{0} is required." with the field caption filled in.</summary>
    private static string FieldMessage(string id, FieldInfo field) => Localized
        ? $"Strings.F({Lit(UiStrings.KeyOf(id))}, {Lit(UiStrings.English(id))}, {FieldText(field)})"
        : Lit(UiStrings.English(id).Replace("{0}", FieldCaption(field)));

    public static string Csproj(string sqliteVersion, ProjectMetadata? meta = null) => CsprojProperties(CsprojBase(sqliteVersion), meta);

    private static string CsprojBase(string sqliteVersion) => """
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>

          <ItemGroup>
            @@PACKAGES@@
          </ItemGroup>

        </Project>

        """.Replace("    @@PACKAGES@@", DapperPackage + "\n" + Db.PackageReferences + (EntraAuth ? "\n" + EntraPackage : string.Empty) + (ExchangeOpenXml ? "\n" + OpenXmlPackage : string.Empty)).Replace("@@SQLITE@@", sqliteVersion);

    /// <summary>Dapper maps rows to the models and binds parameters in every generated site.</summary>
    private const string DapperPackage = """    <PackageReference Include="Dapper" Version="2.1.89" />""";

    /// <summary>
    /// Microsoft.Data.SqlClient 7 moved the "Active Directory" sign-in modes to this package, which registers them when it is
    /// referenced. Same version as the driver.
    /// </summary>
    private const string EntraPackage = """    <PackageReference Include="Microsoft.Data.SqlClient.Extensions.Azure" Version="7.1.1" />""";

    public static string AppSettings(string connectionString, ProjectMetadata? meta = null) =>
        AppSettingsMeta(AppSettingsBase(connectionString), meta);

    private static string AppSettingsBase(string connectionString)
    {
        var escaped = connectionString.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return """
            {
              "ConnectionStrings": {
                "Default": "@@CONN@@"
              },
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.AspNetCore": "Warning"
                }
              }
            }

            """.Replace("@@CONN@@", escaped);
    }

    public static string Program(string ns, AspNetSettings aspnet, IReadOnlyList<TableInfo> tables, SecuritySettings security, ProjectMetadata? meta = null)
    {
        meta ??= new ProjectMetadata();
        var secure = security.Enabled;
        var sb = new StringBuilder();
        if (meta.EnableHealthEndpoint)
        {
            sb.AppendLine("using Dapper;");
            sb.AppendLine("using Microsoft.Data.Sqlite;");
        }

        if (secure)
        {
            sb.AppendLine("using Microsoft.AspNetCore.Antiforgery;");
            sb.AppendLine("using Microsoft.AspNetCore.Authentication.Cookies;");
            if (security.TrackFailedAttempts)
            {
                sb.AppendLine($"using {ns}.Security;");
            }
        }

        sb.AppendLine($"using {ns}.Endpoints;");
        if (Localized)
        {
            sb.AppendLine($"using {ns}.Localization;");
        }

        sb.AppendLine($"using {ns}.Pages;");
        if (Reports.Count > 0)
        {
            sb.AppendLine($"using {ns}.Reports;");
        }

        sb.AppendLine($"using {ns}.Web;");
        sb.AppendLine();
        sb.AppendLine("Db.Configure();");
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        if (ConnectionVariable.Length > 0)
        {
            // The connection string stays out of appsettings.json; ConnectionStrings:Default still wins when it is configured.
            sb.AppendLine("var connectionString = builder.Configuration.GetConnectionString(\"Default\") is { Length: > 0 } configured");
            sb.AppendLine("    ? configured");
            sb.AppendLine($"    : Environment.GetEnvironmentVariable({Lit(ConnectionVariable)}) is {{ Length: > 0 }} fromVariable");
            sb.AppendLine("        ? fromVariable");
            sb.AppendLine($"        : throw new InvalidOperationException({Lit($"Set the connection string in the {ConnectionVariable} environment variable or in ConnectionStrings:Default.")});");
        }
        else
        {
            sb.AppendLine("var connectionString = builder.Configuration.GetConnectionString(\"Default\")");
            sb.AppendLine("    ?? throw new InvalidOperationException(\"Connection string 'Default' is missing.\");");
        }
        sb.AppendLine("builder.Services.AddAntiforgery();");
        if (UsesCompression(meta))
        {
            sb.AppendLine("builder.Services.AddResponseCompression();");
        }

        if (secure)
        {
            sb.AppendLine("builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)");
            sb.AppendLine("    .AddCookie(options =>");
            sb.AppendLine("    {");
            sb.AppendLine("        options.LoginPath = \"/login\";");
            sb.AppendLine("        options.AccessDeniedPath = \"/login\";");
            sb.AppendLine("    });");
            sb.AppendLine("builder.Services.AddAuthorization();");
        }

        sb.AppendLine();
        sb.AppendLine("var app = builder.Build();");
        if (meta.EnableHsts)
        {
            sb.AppendLine("app.UseHsts();");
        }

        if (meta.UseHttps)
        {
            sb.AppendLine("app.UseHttpsRedirection();");
        }

        if (UsesCompression(meta))
        {
            sb.AppendLine("app.UseResponseCompression();");
        }

        if (UsesStaticFiles(meta))
        {
            // Serves wwwroot (favicon and logo) before authentication, so the icons also load on the login page.
            sb.AppendLine("app.UseStaticFiles();");
        }

        if (Localized)
        {
            // Loads Localization/strings.<lang>.json, picks the language of each request and maps /setlanguage (anonymous).
            sb.AppendLine("Strings.Load(app.Environment.ContentRootPath);");
            sb.AppendLine("app.Use(Strings.Middleware);");
            sb.AppendLine("app.MapSetLanguage();");
        }

        sb.AppendLine("app.UseAntiforgery();");
        if (secure)
        {
            sb.AppendLine("app.UseAuthentication();");
            sb.AppendLine("app.UseAuthorization();");
            sb.AppendLine();
            if (security.TrackFailedAttempts)
            {
                // Creates login_attempts before any request is served. Idempotent: CREATE TABLE IF NOT EXISTS.
                sb.AppendLine("await LoginAttempts.EnsureTableAsync(connectionString);");
                sb.AppendLine();
            }

            // /login and /logout are mapped on app, which stays anonymous. Everything else goes through the
            // secured group, which requires a signed-in user for the pages, the home page and the API routes.
            sb.AppendLine("app.MapAuthPages(connectionString);");
            if (security.AllowRegistration)
            {
                // Sign-up is anonymous, so it stays on app like /login.
                sb.AppendLine("app.MapRegistrationPages(connectionString);");
            }

            sb.AppendLine("var secured = app.MapGroup(\"\").RequireAuthorization();");
            if (security.AllowChangePassword)
            {
                sb.AppendLine("secured.MapChangePasswordPages(connectionString);");
            }
        }

        if (Auditing is { CreateTable: true })
        {
            // Creates the audit log table before any request is served. Idempotent, like the login attempts table.
            sb.AppendLine($"await {AuditType}.EnsureTableAsync(connectionString);");
        }

        if (meta.EnableHealthEndpoint)
        {
            // Anonymous on purpose: monitors call it without signing in. It only reports whether the database opens.
            sb.AppendLine("app.MapGet(\"/health\", async () =>");
            sb.AppendLine("{");
            sb.AppendLine("    try");
            sb.AppendLine("    {");
            sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("        await connection.OpenAsync();");
            sb.AppendLine("        await connection.ExecuteScalarAsync(\"SELECT 1\");");
            sb.AppendLine("        return Results.Text(\"Healthy\");");
            sb.AppendLine("    }");
            sb.AppendLine("    catch (Exception)");
            sb.AppendLine("    {");
            sb.AppendLine("        return Results.Text(\"Unhealthy\", statusCode: 503);");
            sb.AppendLine("    }");
            sb.AppendLine("});");
        }

        sb.AppendLine();
        var target = secure ? "secured" : "app";

        if (aspnet.IncludeHomePage)
        {
            var handler = secure ? "(HttpContext context, IAntiforgery antiforgery) => " : "() => ";
            var groupedMenu = MenuCustomized(tables);
            sb.AppendLine(groupedMenu
                ? $"{target}.MapGet(\"/\", {handler}Html.Page({Lit(aspnet.Title)}, Html.GroupedMenu(new (string Group, string Caption, string Href, string Icon)[]"
                : $"{target}.MapGet(\"/\", {handler}Html.Page({Lit(aspnet.Title)}, Html.Menu(new (string Caption, string Href)[]");
            sb.AppendLine("{");
            foreach (var table in groupedMenu ? MenuTables(tables) : tables)
            {
                sb.AppendLine(groupedMenu
                    ? $"    ({Lit(table.Ui.MenuGroup)}, {TableText(table)}, {Lit("/" + Identifier.Route(table.Name))}, {Lit(Token(table.Ui.Icon))}),"
                    : $"    ({TableText(table)}, {Lit("/" + Identifier.Route(table.Name))}),");
            }

            foreach (var report in MenuReports())
            {
                sb.AppendLine(groupedMenu
                    ? $"    ({Lit(report.MenuGroup)}, {ReportText(report)}, {Lit(ReportRoute(report))}, \"\"),"
                    : $"    ({ReportText(report)}, {Lit(ReportRoute(report))}),");
            }

            if (Auditing is { ShowLogPage: true })
            {
                sb.AppendLine(groupedMenu
                    ? $"    (\"\", {Ui("auditTrail")}, {Lit(AuditRoute)}, \"\"),"
                    : $"    ({Ui("auditTrail")}, {Lit(AuditRoute)}),");
            }

            if (secure && security.AllowChangePassword)
            {
                sb.AppendLine($"}}) + Html.Link(\"/changepassword\", {Ui("changePassword")}) + AuthPages.LogoutForm(context, antiforgery)));");
            }
            else
            {
                sb.AppendLine(secure
                    ? "}) + AuthPages.LogoutForm(context, antiforgery)));"
                    : "})));");
            }
            sb.AppendLine();
        }

        foreach (var table in tables)
        {
            var className = Identifier.Pascal(table.Name);
            sb.AppendLine($"{target}.Map{className}Endpoints(connectionString);");
            sb.AppendLine($"{target}.Map{className}Pages(connectionString);");
        }

        foreach (var report in Reports)
        {
            sb.AppendLine($"{target}.Map{ReportClass(report)}(connectionString);");
        }

        if (Auditing is { ShowLogPage: true })
        {
            sb.AppendLine($"{target}.MapAuditTrailPages(connectionString);");
        }

        sb.AppendLine();
        sb.AppendLine("app.Run();");
        return sb.ToString();
    }

    public static string HtmlHelper(string ns, ProjectMetadata? meta = null, IReadOnlyList<TableInfo>? tables = null) =>
        ApplyLayout(LocalizedHtmlHelper(ns), meta ?? new ProjectMetadata(), tables ?? []);

    private static string LocalizedHtmlHelper(string ns)
    {
        var text = PlainHtmlHelper(ns);
        if (!Localized)
        {
            return text;
        }

        // Localized variant: the same helper with the language, the home link text, the Yes/No texts and the language switcher
        // taken from the generated Strings class. Each replacement must find its anchor, or the template changed and this must follow.
        text = ReplaceOnce(text, "namespace " + ns + ".Web;", "using " + ns + ".Localization;\n\nnamespace " + ns + ".Web;");
        text = ReplaceOnce(text, """bool flag => flag ? "Yes" : "No",""",
            """bool flag => WebUtility.HtmlEncode(flag ? Strings.T("ui.yes", "Yes") : Strings.T("ui.no", "No")),""");
        text = ReplaceOnce(text, """<html lang=\"en\"><head>""",
            """<html lang=\"" + Attr(Strings.CurrentLanguage) + "\"" + Strings.DirAttribute + "><head>""");
        text = ReplaceOnce(text, """href=\"/\">Home</a></div></nav>""",
            """href=\"/\">" + Encode(Strings.T("ui.home", "Home")) + "</a>" + Strings.Switcher() + "</div></nav>""");
        return text;
    }

    private static string ReplaceOnce(string text, string anchor, string replacement)
    {
        var index = text.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"Localization anchor not found in the Html helper template: {anchor}");
        }

        return text.Remove(index, anchor.Length).Insert(index, replacement);
    }

    private static string PlainHtmlHelper(string ns) => ("""
        using System.Globalization;
        using System.Net;
        using System.Text;
        using Microsoft.AspNetCore.Antiforgery;
        using Microsoft.AspNetCore.Http;

        namespace @@NS@@.Web;

        /// <summary>Small HTML helpers used by the generated pages. All values are HTML-encoded.</summary>
        public static class Html
        {
            public static string Encode(object? value) => value switch
            {
                null => string.Empty,
                bool flag => flag ? "Yes" : "No",
                DateTime date => WebUtility.HtmlEncode(date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                IFormattable number => WebUtility.HtmlEncode(number.ToString(null, CultureInfo.InvariantCulture)),
                _ => WebUtility.HtmlEncode(value.ToString() ?? string.Empty),
            };

            public static string Attr(string value) => WebUtility.HtmlEncode(value);

            public static IResult Page(string title, string body) =>
                Results.Content(Document(title, body), "text/html; charset=utf-8");

            public static string Document(string title, string body) =>
                "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">"
                + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
                + "<title>" + Encode(title) + "</title>"
                + "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css\">"
                + "</head><body>"
                + "<nav class=\"navbar navbar-expand bg-body-tertiary mb-4\"><div class=\"container\">"
                + "<a class=\"navbar-brand\" href=\"/\">Home</a></div></nav>"
                + "<main class=\"container\"><h1 class=\"h3 mb-3\">" + Encode(title) + "</h1>"
                + body
                + "</main></body></html>";

            public static string Link(string href, string text, string cssClass = "btn btn-sm btn-outline-primary") =>
                "<a class=\"" + cssClass + "\" href=\"" + Attr(href) + "\">" + Encode(text) + "</a>";

            public static string Input(string name, string label, string type, string value, bool required)
            {
                var requiredAttr = required ? " required" : string.Empty;
                return "<div class=\"mb-3\"><label class=\"form-label\" for=\"" + Attr(name) + "\">" + Encode(label) + "</label>"
                    + "<input class=\"form-control\" id=\"" + Attr(name) + "\" name=\"" + Attr(name) + "\" type=\"" + type
                    + "\" value=\"" + Attr(value) + "\"" + requiredAttr + "></div>";
            }

            public static string Hidden(string name, string value) =>
                "<input type=\"hidden\" name=\"" + Attr(name) + "\" value=\"" + Attr(value) + "\">";

            /// <summary>Hidden anti-forgery field that every generated form must post back.</summary>
            public static string AntiforgeryField(HttpContext context, IAntiforgery antiforgery) =>
                Hidden("__RequestVerificationToken", antiforgery.GetAndStoreTokens(context).RequestToken ?? string.Empty);

            /// <summary>Returns false when the posted anti-forgery token is missing or invalid.</summary>
            public static async Task<bool> IsAntiforgeryValidAsync(HttpContext context, IAntiforgery antiforgery)
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                    return true;
                }
                catch (AntiforgeryValidationException)
                {
                    return false;
                }
            }

            public static string Checkbox(string name, string label, bool isChecked)
            {
                var checkedAttr = isChecked ? " checked" : string.Empty;
                return "<div class=\"form-check mb-3\"><input class=\"form-check-input\" type=\"checkbox\" id=\"" + Attr(name)
                    + "\" name=\"" + Attr(name) + "\" value=\"true\"" + checkedAttr + "><label class=\"form-check-label\" for=\""
                    + Attr(name) + "\">" + Encode(label) + "</label></div>";
            }

            public static string Menu(IEnumerable<(string Caption, string Href)> items)
            {
                var sb = new StringBuilder("<ul class=\"list-group\">");
                foreach (var (caption, href) in items)
                {
                    sb.Append("<li class=\"list-group-item\"><a href=\"").Append(Attr(href)).Append("\">")
                        .Append(Encode(caption)).Append("</a></li>");
                }

                return sb.Append("</ul>").ToString();
            }

            public static string PageLinks(HttpRequest request, int page, int pageCount, string route)
            {
                if (pageCount <= 1)
                {
                    return string.Empty;
                }

                var query = new StringBuilder();
                foreach (var pair in request.Query)
                {
                    if (pair.Key == "page")
                    {
                        continue;
                    }

                    foreach (var value in pair.Value)
                    {
                        query.Append('&').Append(Uri.EscapeDataString(pair.Key)).Append('=')
                            .Append(Uri.EscapeDataString(value ?? string.Empty));
                    }
                }

                var links = new StringBuilder("<nav><ul class=\"pagination\">");
                for (var i = 1; i <= pageCount; i++)
                {
                    var active = i == page ? " active" : string.Empty;
                    links.Append("<li class=\"page-item").Append(active).Append("\"><a class=\"page-link\" href=\"")
                        .Append(Attr(route)).Append("?page=").Append(i).Append(query).Append("\">").Append(i).Append("</a></li>");
                }

                return links.Append("</ul></nav>").ToString();
            }
        }
        """).Replace("@@NS@@", ns);

    public static string Model(string ns, string className, TableInfo table)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Models;");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {className}");
        sb.AppendLine("{");
        foreach (var field in table.Fields)
        {
            if (field.IsSecret)
            {
                sb.AppendLine("    [JsonIgnore]");
            }

            sb.AppendLine($"    public {CSharpType(field)} {PropName(className, field)} {{ get; set; }}{DefaultFor(field)}");
        }

        AppendAuditModelMembers(sb, className, table);
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string Endpoints(string ns, string className, TableInfo table)
    {
        var keys = table.PrimaryKeys;
        var route = Identifier.Route(table.Name);
        var owned = IsOwned(table);
        // With User ID security the signed-in user is bound from HttpContext.User; the group already requires sign-in.
        var userOnly = owned ? "ClaimsPrincipal user" : string.Empty;
        var userSuffix = owned ? ", " + userOnly : string.Empty;
        var owner = "user.Identity?.Name ?? string.Empty";
        var sb = new StringBuilder();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        var mdApi = keys.Count > 0 ? MdLinks(table) : [];
        if (owned || mdApi.Count > 0)
        {
            sb.AppendLine("using System.Security.Claims;");
        }

        sb.AppendLine($"using {ns}.Models;");
        if (UsesSecurityNamespace(table))
        {
            sb.AppendLine($"using {ns}.Security;");
        }

        if (keys.Count > 0 && (MdDeletePlanned(table) || MdUpdatePlanned(table) || IsAudited(table)))
        {
            sb.AppendLine($"using {ns}.Web;");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Endpoints;");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}Endpoints");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IEndpointRouteBuilder Map{className}Endpoints(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var group = app.MapGroup({Lit("/api/" + route)});");
        // GET needs the view limit; POST, PUT and DELETE need the edit limit.
        var viewGuard = LevelGuard(table.ViewMinLevel, ViewDeniedMessage);
        var editGuard = LevelGuard(table.EditMinLevel, EditDeniedMessage);
        sb.AppendLine();
        var listSql = "SELECT " + SelectColumns(table) + " FROM " + QuoteIdent(table.Name) + (owned ? " WHERE " + OwnerCondition(table) : string.Empty);
        sb.AppendLine($"        group.MapGet(\"/\", async ({userOnly}) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("            await connection.OpenAsync();");
        sb.AppendLine(owned ? "            var args = new DynamicParameters();" : "            var args = (object?)null;");
        AppendOwnerParameter(sb, owned, "args", "            ");
        sb.AppendLine($"            var items = (await connection.QueryAsync<{className}>({Lit(listSql)}, args)).AsList();");
        sb.AppendLine("            return Results.Ok(items);");
        sb.AppendLine("        })" + viewGuard + ";");

        if (keys.Count > 0)
        {
            var keyRoute = KeyRoute(table);
            var keyParams = KeyParams(table);
            var keyPath = string.Join("/", keys.Select(k => "{item." + PropName(className, k) + "}"));
            var where = " WHERE " + KeyWhereSql(table) + (owned ? " AND " + OwnerCondition(table) : string.Empty);

            sb.AppendLine();
            sb.AppendLine($"        group.MapGet(\"/{keyRoute}\", async ({keyParams}{userSuffix}) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("            await connection.OpenAsync();");
            sb.AppendLine("            var args = new DynamicParameters();");
            AppendKeyParameters(sb, table, "            ");
            AppendOwnerParameter(sb, owned, "args", "            ");
            sb.AppendLine($"            var item = await connection.QueryFirstOrDefaultAsync<{className}>({Lit("SELECT " + SelectColumns(table) + " FROM " + QuoteIdent(table.Name) + where)}, args);");
            sb.AppendLine("            return item is null ? Results.NotFound() : Results.Ok(item);");
            sb.AppendLine("        })" + viewGuard + ";");

            var insertFields = InsertFields(table);
            sb.AppendLine();
            var auditInsert = AuditsInserts(table);
            var auditUpdate = AuditsUpdates(table);
            sb.AppendLine($"        group.MapPost(\"/\", async ({className} item{(auditInsert ? ", HttpContext http" : string.Empty)}{userSuffix}) =>");
            sb.AppendLine("        {");
            if (owned)
            {
                // The owner is always the signed-in user, whatever the request body contains.
                sb.AppendLine($"            item.{OwnerProp(table)} = {owner};");
            }

            sb.AppendLine("            await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("            await connection.OpenAsync();");
            if (auditInsert)
            {
                AppendAuditTransaction(sb, "            ", pad =>
                {
                    AppendInsertCall(sb, className, table, insertFields, pad);
                    sb.AppendLine($"{pad}{AuditWrite(table, "insert", "http", "item.AuditKey()", "null", "item.AuditValues()")}");
                });
            }
            else
            {
                AppendInsertCall(sb, className, table, insertFields, "            ");
            }

            sb.AppendLine($"            return Results.Created($\"/api/{route}/{keyPath}\", item);");
            sb.AppendLine("        })" + editGuard + ";");

            var updateFields = UpdateFields(table);
            if (updateFields.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"        group.MapPut(\"/{keyRoute}\", async ({keyParams}, {className} item{(auditUpdate || MdUpdateAudited(table) ? ", HttpContext http" : string.Empty)}{userSuffix}) =>");
                sb.AppendLine("        {");
                sb.AppendLine("            await using var connection = new SqliteConnection(connectionString);");
                sb.AppendLine("            await connection.OpenAsync();");
                if (owned)
                {
                    // The row must belong to the caller. The UPDATE is keyed only, so the owner column is never written.
                    sb.AppendLine("            var check = new DynamicParameters();");
                    AppendKeyParameters(sb, table, "            ", "check");
                    AppendOwnerParameter(sb, true, "check", "            ");
                    sb.AppendLine($"            if (await connection.ExecuteScalarAsync<long>({Lit("SELECT COUNT(*) FROM " + QuoteIdent(table.Name) + " WHERE " + KeyWhereSql(table) + " AND " + OwnerCondition(table))}, check) == 0)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                return Results.NotFound();");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                }

                if (MdUpdatePlanned(table))
                {
                    AppendMdApiUpdate(sb, className, table, updateFields, owned);
                }
                else if (auditUpdate)
                {
                    var readSql = Lit("SELECT " + SelectColumns(table) + " FROM " + QuoteIdent(table.Name) + where);
                    sb.AppendLine("            var read = new DynamicParameters();");
                    AppendKeyParameters(sb, table, "            ", "read");
                    AppendOwnerParameter(sb, owned, "read", "            ");
                    sb.AppendLine($"            var before = await connection.QueryFirstOrDefaultAsync<{className}>({readSql}, read);");
                    sb.AppendLine("            if (before is null)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                return Results.NotFound();");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                    AppendAuditTransaction(sb, "            ", pad =>
                    {
                        sb.AppendLine($"{pad}var args = new DynamicParameters();");
                        AppendParameters(sb, updateFields, "item", className, pad);
                        AppendKeyParameters(sb, table, pad);
                        sb.AppendLine($"{pad}if (await connection.ExecuteAsync({Lit(UpdateSql(table, updateFields))}, args) == 0)");
                        sb.AppendLine($"{pad}{{");
                        sb.AppendLine($"{pad}    await {AuditType}.RollbackAsync(connection);");
                        sb.AppendLine($"{pad}    return Results.NotFound();");
                        sb.AppendLine($"{pad}}}");
                        sb.AppendLine();
                        sb.AppendLine($"{pad}var after = await connection.QueryFirstOrDefaultAsync<{className}>({readSql}, read);");
                        sb.AppendLine($"{pad}{AuditWrite(table, "update", "http", "before.AuditKey()", "before.AuditValues()", "(after ?? item).AuditValues()")}");
                    });
                    sb.AppendLine();
                    sb.AppendLine("            return Results.NoContent();");
                }
                else
                {
                    sb.AppendLine("            var args = new DynamicParameters();");
                    AppendParameters(sb, updateFields, "item", className, "            ");
                    AppendKeyParameters(sb, table, "            ");
                    sb.AppendLine($"            return await connection.ExecuteAsync({Lit(UpdateSql(table, updateFields))}, args) == 0 ? Results.NotFound() : Results.NoContent();");
                }

                sb.AppendLine("        })" + editGuard + ";");
            }

            sb.AppendLine();
            var auditDelete = AuditsDeletes(table);
            var deleteHttp = auditDelete || (MdDeletePlanned(table) && MdAuditCaptures(table).Count > 0);
            var readBeforeSql = Lit("SELECT " + SelectColumns(table) + " FROM " + QuoteIdent(table.Name) + where);
            sb.AppendLine($"        group.MapDelete(\"/{keyRoute}\", async ({keyParams}{(deleteHttp ? ", HttpContext http" : string.Empty)}{userSuffix}) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("            await connection.OpenAsync();");
            if (MdDeletePlanned(table))
            {
                if (auditDelete)
                {
                    sb.AppendLine("            var read = new DynamicParameters();");
                    AppendKeyParameters(sb, table, "            ", "read");
                    AppendOwnerParameter(sb, owned, "read", "            ");
                    sb.AppendLine($"            var before = await connection.QueryFirstOrDefaultAsync<{className}>({readBeforeSql}, read);");
                }

                sb.AppendLine($"            var outcome = await ReferentialIntegrity.DeleteAsync(connection, ReferentialIntegrity.Plan_{className}, {MdArgs(table, "user.Identity?.Name ?? string.Empty")}{MdAuditDeleteArgs(table, "http", "before")});");
                sb.AppendLine("            return outcome.Status switch");
                sb.AppendLine("            {");
                sb.AppendLine("                ReferentialIntegrity.Blocked => Results.Json(new { error = outcome.Message }, statusCode: StatusCodes.Status409Conflict),");
                sb.AppendLine("                ReferentialIntegrity.NotFound => Results.NotFound(),");
                sb.AppendLine("                _ => Results.NoContent(),");
                sb.AppendLine("            };");
            }
            else if (auditDelete)
            {
                sb.AppendLine("            var args = new DynamicParameters();");
                AppendKeyParameters(sb, table, "            ");
                AppendOwnerParameter(sb, owned, "args", "            ");
                sb.AppendLine($"            var before = await connection.QueryFirstOrDefaultAsync<{className}>({readBeforeSql}, args);");
                sb.AppendLine("            if (before is null)");
                sb.AppendLine("            {");
                sb.AppendLine("                return Results.NotFound();");
                sb.AppendLine("            }");
                sb.AppendLine();
                AppendAuditTransaction(sb, "            ", pad =>
                {
                    sb.AppendLine($"{pad}if (await connection.ExecuteAsync({Lit("DELETE FROM " + QuoteIdent(table.Name) + where)}, args) == 0)");
                    sb.AppendLine($"{pad}{{");
                    sb.AppendLine($"{pad}    await {AuditType}.RollbackAsync(connection);");
                    sb.AppendLine($"{pad}    return Results.NotFound();");
                    sb.AppendLine($"{pad}}}");
                    sb.AppendLine();
                    sb.AppendLine($"{pad}{AuditWrite(table, "delete", "http", "before.AuditKey()", "before.AuditValues()", "null")}");
                });
                sb.AppendLine();
                sb.AppendLine("            return Results.NoContent();");
            }
            else
            {
                sb.AppendLine("            var args = new DynamicParameters();");
                AppendKeyParameters(sb, table, "            ");
                AppendOwnerParameter(sb, owned, "args", "            ");
                sb.AppendLine($"            return await connection.ExecuteAsync({Lit("DELETE FROM " + QuoteIdent(table.Name) + where)}, args) == 0 ? Results.NotFound() : Results.NoContent();");
            }

            sb.AppendLine("        })" + editGuard + ";");

            foreach (var md in mdApi)
            {
                AppendMdApiDetailList(sb, className, table, md, mdApi, viewGuard);
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string Pages(string ns, string className, TableInfo table)
    {
        var keys = table.PrimaryKeys;
        var pages = table.Pages;
        var route = "/" + Identifier.Route(table.Name);
        _localizedTable = table.Name;
        var hasPk = keys.Count > 0;
        // An UPDATE needs at least one non-key column. A composite key can still be edited: its key columns are shown read-only.
        var canEdit = pages.Edit && hasPk && (keys.Count > 1 || UpdateFields(table).Count > 0);
        var searchable = table.Fields.Where(IsSearchable).ToList();
        // List, search and view need the view limit; add, copy, edit and delete need the edit limit.
        var viewGuard = LevelGuard(table.ViewMinLevel, ViewDeniedMessage);
        var editGuard = LevelGuard(table.EditMinLevel, EditDeniedMessage);

        var sb = new StringBuilder();
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using Microsoft.AspNetCore.Antiforgery;");
        sb.AppendLine("using Microsoft.Data.Sqlite;");
        sb.AppendLine($"using {ns}.Models;");
        if (UsesSecurityNamespace(table))
        {
            sb.AppendLine($"using {ns}.Security;");
        }

        if (Localized)
        {
            sb.AppendLine($"using {ns}.Localization;");
        }

        sb.AppendLine($"using {ns}.Web;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Pages;");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}Pages");
        sb.AppendLine("{");
        sb.AppendLine($"    private const string Route = {Lit(route)};");
        // The caption comes from the Strings helper per request when localized, so it cannot be a constant.
        sb.AppendLine(Localized
            ? $"    private static string Caption => {TableText(table)};"
            : $"    private const string Caption = {Lit(Caption(table))};");
        sb.AppendLine($"    private const string Table = {Lit(QuoteIdent(table.Name))};");
        sb.AppendLine($"    private const string SelectColumns = {Lit(SelectColumns(table))};");
        if (keys.Count > 1)
        {
            sb.AppendLine($"    private const string KeyWhere = {Lit(KeyWhereSql(table))};");
        }
        else
        {
            sb.AppendLine($"    private const string PkColumn = {Lit(keys.Count == 0 ? string.Empty : QuoteIdent(keys[0].Name))};");
        }

        sb.AppendLine($"    private const string Filter = {Lit(table.Filter)};");
        sb.AppendLine($"    private const string Order = {Lit(OrderSql(table))};");
        sb.AppendLine($"    private const int PageSize = {Math.Max(1, table.Pages.PageSize)};");
        sb.AppendLine($"    private static readonly string[] SearchFields = {StringArray(searchable.Select(f => f.Name))};");
        sb.AppendLine($"    private static readonly string[] SearchColumns = {StringArray(searchable.Select(f => QuoteIdent(f.Name)))};");
        if (IsOwned(table))
        {
            sb.AppendLine($"    private const string OwnerColumn = {Lit(QuoteIdent(table.OwnerField))};");
        }

        AppendCustomStatics(sb, className, table);
        AppendGridStatics(sb, table);
        if (Designed(table))
        {
            AppendDesignStatics(sb, table);
        }

        AppendMdStatics(sb, className, table);

        sb.AppendLine();

        sb.AppendLine($"    public static IEndpointRouteBuilder Map{className}Pages(this IEndpointRouteBuilder app, string connectionString)");
        sb.AppendLine("    {");
        if (pages.List)
        {
            sb.AppendLine($"        app.MapGet(Route, (HttpRequest request) => ListPage(request, connectionString)){viewGuard};");
        }

        if (pages.Search)
        {
            sb.AppendLine(Designed(table)
                ? $"        app.MapGet(Route + \"/search\", (HttpRequest request) => SearchPage(request)){viewGuard};"
                : $"        app.MapGet(Route + \"/search\", () => SearchPage()){viewGuard};");
        }

        AppendExchangeRoutes(sb, table);

        if (hasPk)
        {
            var keyRoute = KeyRoute(table);
            var keyParams = KeyParams(table);
            var keyArgs = KeyArgs(table);

            if (pages.View)
            {
                sb.AppendLine(ViewHasContext(table)
                    ? $"        app.MapGet(Route + \"/view/{keyRoute}\", ({keyParams}, HttpContext context) => ViewPage({keyArgs}, context, connectionString)){viewGuard};"
                    : $"        app.MapGet(Route + \"/view/{keyRoute}\", ({keyParams}) => ViewPage({keyArgs}, connectionString)){viewGuard};");
            }

            if (pages.Add)
            {
                sb.AppendLine($"        app.MapGet(Route + \"/add\", (HttpContext context, IAntiforgery antiforgery) => AddForm(context, antiforgery{(HasFormLookups(table) || IsDetailTable(table) ? ", connectionString" : string.Empty)})){editGuard};");
                sb.AppendLine($"        app.MapPost(Route + \"/add\", (HttpContext context, IAntiforgery antiforgery) => AddPost(context, antiforgery, connectionString)){editGuard};");
            }

            if (pages.Copy)
            {
                sb.AppendLine($"        app.MapGet(Route + \"/copy/{keyRoute}\", ({keyParams}, HttpContext context, IAntiforgery antiforgery) => CopyForm({keyArgs}, context, antiforgery, connectionString)){editGuard};");
                sb.AppendLine($"        app.MapPost(Route + \"/copy/{keyRoute}\", ({keyParams}, HttpContext context, IAntiforgery antiforgery) => CopyPost({keyArgs}, context, antiforgery, connectionString)){editGuard};");
            }

            if (canEdit)
            {
                sb.AppendLine($"        app.MapGet(Route + \"/edit/{keyRoute}\", ({keyParams}, HttpContext context, IAntiforgery antiforgery) => EditForm({keyArgs}, context, antiforgery, connectionString)){editGuard};");
                sb.AppendLine($"        app.MapPost(Route + \"/edit/{keyRoute}\", ({keyParams}, HttpContext context, IAntiforgery antiforgery) => EditPost({keyArgs}, context, antiforgery, connectionString)){editGuard};");
            }

            if (pages.Delete)
            {
                sb.AppendLine($"        app.MapGet(Route + \"/delete/{keyRoute}\", ({keyParams}, HttpContext context, IAntiforgery antiforgery) => DeleteConfirm({keyArgs}, context, antiforgery, connectionString)){editGuard};");
                sb.AppendLine($"        app.MapPost(Route + \"/delete/{keyRoute}\", ({keyParams}, HttpContext context, IAntiforgery antiforgery) => DeletePost({keyArgs}, context, antiforgery, connectionString)){editGuard};");
            }

            foreach (var md in MdPreviewLinks(table))
            {
                sb.AppendLine($"        app.MapGet(Route + \"/details/{md.Index}/{keyRoute}\", ({keyParams}, HttpContext context) => DetailsFragment{md.Index}({keyArgs}, context, connectionString)){viewGuard};");
            }
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (pages.List)
        {
            AppendListPage(sb, className, table, hasPk && pages.View, canEdit, hasPk && pages.Copy, hasPk && pages.Delete, hasPk && pages.Add, pages.Search);
            AppendRowClass(sb, className, table);
        }

        if (pages.Search)
        {
            AppendSearchPage(sb, table);
        }

        AppendBuildWhere(sb, table);
        AppendExchange(sb, table);
        AppendMdGridMethods(sb, className, table);
        if (MdNeedsPresets(table))
        {
            AppendMdPresets(sb, className, table);
        }

        if (hasPk)
        {
            AppendFind(sb, className, table);
            if (pages.View)
            {
                AppendViewPage(sb, className, table, canEdit, pages.Copy, pages.Delete);
            }

            if (pages.Add)
            {
                AppendAddPages(sb, className, table);
            }

            if (pages.Copy)
            {
                AppendCopyPages(sb, table);
            }

            if (canEdit)
            {
                AppendEditPages(sb, className, table);
            }

            if (pages.Delete)
            {
                AppendDeletePages(sb, table);
            }

            AppendInsert(sb, className, table);
        }

        AppendForm(sb, className, table);
        AppendBindForm(sb, className, table);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendListPage(StringBuilder sb, string className, TableInfo table, bool canView, bool canEdit, bool canCopy, bool canDelete, bool canAdd, bool canSearch)
    {
        var design = table.Designs.List;
        var designed = Designed(table);
        var columns = ListColumnsOf(table);
        var listFields = columns.Select(c => c.Field).ToList();
        var keyValues = KeyPropExprs(className, table);
        var custom = IsCustomTable(table);
        var rowClass = ValidRowStyles(table).Count > 0;
        var sizes = PageSizesOf(table);
        var size = sizes.Count > 0 ? "pageSize" : "PageSize";
        var flash = FlashPages(table).Count > 0;
        var cards = designed && design.ListLayout == ListLayout.Cards;
        var rowNumbers = designed && design.ShowRowNumbers;
        var actionsFirst = designed && design.ShowActionsFirst;
        var containers = new List<PageContainer>();
        foreach (var (enabled, action) in new[] { (canAdd, "add"), (canSearch, "search"), (canView, "view"), (canEdit, "edit"), (canCopy, "copy"), (canDelete, "delete") })
        {
            if (enabled)
            {
                containers.Add(ContainerOf(table, action));
            }
        }

        // The statements of the action links of one record, shared by the table rows and the card footers.
        void Actions(string indent)
        {
            if (canView)
            {
                sb.AppendLine($"{indent}body.Append({ActionLink(table, "view", KeyUrl("view", keyValues), Ui("view"), null)}).Append(' ');");
            }

            if (canEdit)
            {
                sb.AppendLine($"{indent}body.Append({ActionLink(table, "edit", KeyUrl("edit", keyValues), Ui("edit"), "btn btn-sm btn-outline-secondary")}).Append(' ');");
            }

            if (canCopy)
            {
                sb.AppendLine($"{indent}body.Append({ActionLink(table, "copy", KeyUrl("copy", keyValues), Ui("copy"), "btn btn-sm btn-outline-secondary")}).Append(' ');");
            }

            if (canDelete)
            {
                sb.AppendLine($"{indent}body.Append({ActionLink(table, "delete", KeyUrl("delete", keyValues), Ui("delete"), "btn btn-sm btn-outline-danger")});");
            }

            foreach (var pl in MdPreviewLinks(table))
            {
                var url = KeyUrl("details/" + pl.Index, keyValues);
                var preview = MdPreviewOf(table, pl);
                var button = preview == DetailPreview.ExpandableRow
                    ? $"MasterDetailKit.ExpandButton({url}, {MdCaptionExpr(pl)})"
                    : $"PageKit.OpenLink({url}, {MdCaptionExpr(pl)}, \"btn btn-sm btn-outline-secondary\", {Lit(preview == DetailPreview.Modal ? "modal" : "offcanvas")})";
                sb.AppendLine($"{indent}body.Append(' ').Append({button});");
            }
        }

        sb.AppendLine("    private static async Task<IResult> ListPage(HttpRequest request, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        var parameters = new List<KeyValuePair<string, object>>();");
        sb.AppendLine("        var where = BuildWhere(request, parameters);");
        sb.AppendLine("        var requestedPage = int.TryParse(request.Query[\"page\"], out var parsed) ? parsed : 1;");
        if (sizes.Count > 0)
        {
            // Only the configured sizes are accepted; anything else falls back to the table's page size.
            sb.AppendLine("        var pageSize = PageKit.PageSize(request, PageSize, PageSizeOptions);");
        }

        sb.AppendLine();
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine();
        sb.AppendLine("        var args = Db.Args(parameters);");
        sb.AppendLine("        var total = await connection.ExecuteScalarAsync<long>(\"SELECT COUNT(*) FROM \" + Table + where, args);");
        sb.AppendLine();
        sb.AppendLine($"        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double){size}));");
        sb.AppendLine("        var page = Math.Clamp(requestedPage, 1, pageCount);");
        if (custom)
        {
            // Sorting by request: the name must be one of the whitelisted fields; the quoted column from the whitelist goes into the SQL.
            sb.AppendLine("        var order = Order;");
            sb.AppendLine("        var sortIndex = Array.IndexOf(SortFields, request.Query[\"sort\"].ToString());");
            sb.AppendLine("        if (sortIndex >= 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            order = \" ORDER BY \" + SortColumns[sortIndex] + (request.Query[\"dir\"].ToString() == \"desc\" ? \" DESC\" : \" ASC\") + SortTieBreak;");
            sb.AppendLine("        }");
            if (design.FilterBuilder)
            {
                // The sort levels of the filter builder apply when no column header sorts the list.
                sb.AppendLine("        else if (GridKit.Order(request, SortFields, SortColumns) is { } levels)");
                sb.AppendLine("        {");
                sb.AppendLine("            order = levels + SortTieBreak;");
                sb.AppendLine("        }");
            }

            sb.AppendLine();
        }

        sb.AppendLine($"        args.Add(\"@limit\", {size});");
        sb.AppendLine($"        args.Add(\"@offset\", (page - 1) * {size});");
        sb.AppendLine(custom
            ? $"        var items = (await connection.QueryAsync<{className}>(\"SELECT \" + SelectColumns + \" FROM \" + Table + where + order + {Lit(Db.Page)}, args)).AsList();"
            : $"        var items = (await connection.QueryAsync<{className}>(\"SELECT \" + SelectColumns + \" FROM \" + Table + where + Order + {Lit(Db.Page)}, args)).AsList();");
        sb.AppendLine();
        if (custom)
        {
            AppendDisplayLookups(sb, className, listFields, true);
        }

        sb.AppendLine("        var body = new StringBuilder();");
        if (flash)
        {
            sb.AppendLine("        body.Append(PageKit.TakeFlash(request.HttpContext, FlashCookie, FlashMessages));");
        }

        if (custom && ListIntro(table) is { Length: > 0 } intro)
        {
            sb.AppendLine(intro);
        }

        if (canAdd || canSearch)
        {
            sb.AppendLine("        body.Append(\"<div class=\\\"mb-3\\\">\")");
            if (canAdd)
            {
                var addText = designed && Has(design.AddButtonText) ? Lit(design.AddButtonText.Trim()) : Ui("add");
                sb.AppendLine($"            .Append({ActionLink(table, "add", "Route + \"/add\"", addText, "btn btn-sm btn-primary")}).Append(' ')");
            }

            if (canSearch)
            {
                sb.AppendLine($"            .Append({ActionLink(table, "search", "Route + \"/search\"", Ui("search"), "btn btn-sm btn-outline-secondary")})");
            }

            sb.AppendLine("            .Append(\"</div>\");");
        }

        AppendExchangeToolbar(sb, table);

        if (UsesGridTools(table))
        {
            sb.AppendLine($"        body.Append(GridKit.Panel(request, Route, GridColumns, GridSorts, {(design.FilterBuilder ? "true" : "false")}, {(design.QuickFilter ? "true" : "false")}, {(design.ResetButton ? "true" : "false")}));");
        }

        if (sizes.Count > 0)
        {
            sb.AppendLine("        body.Append(PageKit.PageSizeSelector(request, Route, pageSize, PageSizeOptions));");
        }

        var width = columns.Count + (rowNumbers ? 1 : 0) + 1;
        var emptyText = designed && Has(design.EmptyText) ? Lit(design.EmptyText.Trim()) : null;
        if (cards)
        {
            AppendCardList(sb, className, columns, rowNumbers, size, Actions, custom, rowClass);
            if (emptyText is not null)
            {
                sb.AppendLine($"        if (items.Count == 0) body.Append(\"<p class=\\\"text-muted\\\">\").Append(Html.Encode({emptyText})).Append(\"</p>\");");
            }
        }
        else
        {
            var tableCss = custom && table.Ui.CssClass.Trim().Length > 0 ? " " + HtmlText(table.Ui.CssClass.Trim()) : string.Empty;
            var classes = "table"
                + (design.Striped ? " table-striped" : string.Empty)
                + (design.Hover ? " table-hover" : string.Empty)
                + (design.Bordered ? " table-bordered" : string.Empty)
                + (design.ListLayout == ListLayout.Compact ? " table-sm" : string.Empty)
                + (design.StickyHeader ? " table-sticky" : string.Empty);
            if (design.StickyHeader)
            {
                // The scroll container keeps the header cells in view while the rows scroll.
                sb.AppendLine("        body.Append(\"<div class=\\\"table-responsive\\\" style=\\\"max-height:70vh;overflow-y:auto\\\">"
                    + "<style>.table-sticky thead th{position:sticky;top:0;z-index:2;background:var(--bs-body-bg);box-shadow:inset 0 -1px 0 var(--bs-border-color)}</style>\");");
            }

            sb.AppendLine($"        body.Append(\"<table class=\\\"{classes}{tableCss}\\\"><thead><tr>\");");
            if (rowNumbers)
            {
                sb.AppendLine("        body.Append(\"<th>#</th>\");");
            }

            if (actionsFirst)
            {
                sb.AppendLine("        body.Append(\"<th></th>\");");
            }

            foreach (var (field, columnWidth) in columns)
            {
                sb.AppendLine(custom
                    ? ListHeaderLine(field, columnWidth)
                    : columnWidth.Length > 0
                        ? $"        body.Append({Lit("<th style=\"" + HtmlText("width:" + columnWidth) + "\">")}).Append(Html.Encode({FieldText(field)})).Append(\"</th>\");"
                        : $"        body.Append(\"<th>\").Append(Html.Encode({FieldText(field)})).Append(\"</th>\");");
            }

            sb.AppendLine(actionsFirst
                ? "        body.Append(\"</tr></thead><tbody>\");"
                : "        body.Append(\"<th></th></tr></thead><tbody>\");");
            if (rowNumbers)
            {
                sb.AppendLine($"        var rowNo = (page - 1) * {size};");
            }

            sb.AppendLine("        foreach (var item in items)");
            sb.AppendLine("        {");
            sb.AppendLine(rowClass ? "            body.Append(\"<tr\").Append(RowClass(item)).Append(\">\");" : "            body.Append(\"<tr>\");");
            if (rowNumbers)
            {
                sb.AppendLine("            body.Append(\"<td class=\\\"text-muted\\\">\").Append(++rowNo).Append(\"</td>\");");
            }

            if (actionsFirst)
            {
                sb.AppendLine("            body.Append(\"<td class=\\\"text-nowrap\\\">\");");
                Actions("            ");
                sb.AppendLine("            body.Append(\"</td>\");");
            }

            foreach (var (field, _) in columns)
            {
                sb.AppendLine(custom
                    ? ListCellLine(field, PropName(className, field))
                    : $"            body.Append(\"<td>\").Append(Html.Encode(item.{PropName(className, field)})).Append(\"</td>\");");
            }

            if (actionsFirst)
            {
                sb.AppendLine("            body.Append(\"</tr>\");");
            }
            else
            {
                sb.AppendLine("            body.Append(\"<td class=\\\"text-nowrap\\\">\");");
                Actions("            ");
                sb.AppendLine("            body.Append(\"</td></tr>\");");
            }

            sb.AppendLine("        }");
            if (emptyText is not null)
            {
                sb.AppendLine($"        if (items.Count == 0) body.Append(\"<tr><td colspan=\\\"{width}\\\" class=\\\"text-muted\\\">\").Append(Html.Encode({emptyText})).Append(\"</td></tr>\");");
            }

            sb.AppendLine(design.StickyHeader
                ? "        body.Append(\"</tbody></table></div>\");"
                : "        body.Append(\"</tbody></table>\");");
        }

        if (!designed || design.ShowPager)
        {
            sb.AppendLine("        body.Append(Html.PageLinks(request, page, pageCount, Route));");
        }

        var previewKinds = MdPreviewLinks(table).Select(l => MdPreviewOf(table, l)).ToList();
        var modal = containers.Contains(PageContainer.Modal) || previewKinds.Contains(DetailPreview.Modal);
        var offcanvas = containers.Contains(PageContainer.Offcanvas) || previewKinds.Contains(DetailPreview.Offcanvas);
        if (previewKinds.Contains(DetailPreview.ExpandableRow))
        {
            sb.AppendLine("        body.Append(MasterDetailKit.ExpandScriptTag);");
        }

        if (modal || offcanvas)
        {
            sb.AppendLine($"        body.Append(PageKit.Containers(Route, {(modal ? "true" : "false")}, {(offcanvas ? "true" : "false")}));");
            if (ListNeedsFormPageBoot(table))
            {
                sb.AppendLine("        body.Append(FormPages.Boot);");
            }
        }

        sb.AppendLine(designed
            ? $"        return PageKit.Page(request, Caption, body.ToString(), {DesignName("list")});"
            : "        return Html.Page(Caption, body.ToString());");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>The card grid of the list page: one card per record with label and value lines and the action links in the footer.</summary>
    private static void AppendCardList(StringBuilder sb, string className, List<(FieldInfo Field, string Width)> columns, bool rowNumbers, string size, Action<string> actions, bool custom, bool rowClass)
    {
        if (custom)
        {
            // The cards have no column headers, so the sortable columns (same rule as ListHeaderLine) get a sort bar above them.
            var sortable = columns.Where(c => c.Field.Ui.Sortable && c.Field.Type != FieldType.Binary).ToList();
            if (sortable.Count > 0)
            {
                sb.AppendLine("        body.Append(FieldView.SortBar(request, Route, new (string, string)[] { " + string.Join(", ", sortable.Select(c => $"({Lit(c.Field.Name)}, {FieldText(c.Field)})")) + " }));");
            }
        }

        if (rowNumbers)
        {
            sb.AppendLine($"        var rowNo = (page - 1) * {size};");
        }

        sb.AppendLine("        body.Append(\"<div class=\\\"row row-cols-1 row-cols-md-2 row-cols-xl-3 g-3 mb-3\\\">\");");
        sb.AppendLine("        foreach (var item in items)");
        sb.AppendLine("        {");
        sb.AppendLine(rowClass
            ? "            body.Append(\"<div class=\\\"col\\\"><div class=\\\"card h-100\").Append(RowClass(item)).Append(\"\\\"><div class=\\\"card-body\\\"><dl class=\\\"row mb-0\\\">\");"
            : "            body.Append(\"<div class=\\\"col\\\"><div class=\\\"card h-100\\\"><div class=\\\"card-body\\\"><dl class=\\\"row mb-0\\\">\");");
        if (rowNumbers)
        {
            sb.AppendLine("            body.Append(\"<dt class=\\\"col-5 text-muted\\\">#</dt><dd class=\\\"col-7\\\">\").Append(++rowNo).Append(\"</dd>\");");
        }

        foreach (var (field, _) in columns)
        {
            sb.AppendLine($"            body.Append(\"<dt class=\\\"col-5 text-muted\\\">\").Append(Html.Encode({FieldText(field)})).Append(\"</dt><dd class=\\\"col-7\\\">\").Append({DisplayExpr(field, PropName(className, field))}).Append(\"</dd>\");");
        }

        sb.AppendLine("            body.Append(\"</dl></div><div class=\\\"card-footer text-nowrap\\\">\");");
        actions("            ");
        sb.AppendLine("            body.Append(\"</div></div></div>\");");
        sb.AppendLine("        }");
        sb.AppendLine("        body.Append(\"</div>\");");
    }

    private static void AppendSearchPage(StringBuilder sb, TableInfo table)
    {
        if (Designed(table))
        {
            AppendDesignedSearchPage(sb, table);
            return;
        }

        sb.AppendLine("    private static IResult SearchPage()");
        sb.AppendLine("    {");
        sb.AppendLine("        var body = new StringBuilder();");
        sb.AppendLine("        body.Append(\"<form method=\\\"get\\\" action=\\\"\").Append(Html.Attr(Route)).Append(\"\\\">\");");
        sb.AppendLine($"        body.Append(Html.Input(\"q\", {Ui("quickSearch")}, \"text\", string.Empty, false));");
        foreach (var field in table.Fields.Where(IsSearchable))
        {
            sb.AppendLine($"        body.Append(Html.Input({Lit("f_" + field.Name)}, {FieldText(field)}, \"text\", string.Empty, false));");
        }

        sb.AppendLine($"        body.Append(\"<button type=\\\"submit\\\" class=\\\"btn btn-primary\\\">{UiInText("search")}</button></form>\");");
        sb.AppendLine($"        return Html.Page({UiSuffix("search", " ")} + Caption, body.ToString());");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendBuildWhere(StringBuilder sb, TableInfo table)
    {
        if (IsOwned(table))
        {
            sb.AppendLine("    private static string OwnerOf(HttpContext context) => context.User.Identity?.Name ?? string.Empty;");
            sb.AppendLine();
        }

        sb.AppendLine("    private static string BuildWhere(HttpRequest request, List<KeyValuePair<string, object>> parameters)");
        sb.AppendLine("    {");
        sb.AppendLine("        var clauses = new List<string>();");
        sb.AppendLine("        if (Filter.Length > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            clauses.Add(\"(\" + Filter + \")\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var quick = request.Query[\"q\"].ToString().Trim();");
        sb.AppendLine("        if (quick.Length > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            var any = new List<string>();");
        sb.AppendLine("            foreach (var column in SearchColumns)");
        sb.AppendLine("            {");
        sb.AppendLine("                var name = \"@q\" + parameters.Count;");
        var cast = TextCastParts();
        sb.AppendLine($"                any.Add({Lit(cast.Before)} + column + {Lit(cast.After + " LIKE ")} + name);");
        sb.AppendLine("                parameters.Add(new KeyValuePair<string, object>(name, \"%\" + quick + \"%\"));");
        sb.AppendLine("            }");
        sb.AppendLine("            if (any.Count > 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                clauses.Add(\"(\" + string.Join(\" OR \", any) + \")\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        for (var i = 0; i < SearchFields.Length; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var value = request.Query[\"f_\" + SearchFields[i]].ToString().Trim();");
        sb.AppendLine("            if (value.Length == 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                continue;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            var name = \"@f\" + parameters.Count;");
        sb.AppendLine($"            clauses.Add({Lit(cast.Before)} + SearchColumns[i] + {Lit(cast.After + " LIKE ")} + name);");
        sb.AppendLine("            parameters.Add(new KeyValuePair<string, object>(name, \"%\" + value + \"%\"));");
        sb.AppendLine("        }");
        sb.AppendLine();
        var gridDesign = table.Designs.List;
        if (gridDesign.FilterBuilder || gridDesign.QuickFilter)
        {
            sb.AppendLine($"        var grid = GridKit.Where(request, GridColumns, parameters, {(gridDesign.FilterBuilder ? "true" : "false")}, {(gridDesign.QuickFilter ? "true" : "false")});");
            sb.AppendLine("        if (grid.Length > 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            clauses.Add(grid);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        if (IsOwned(table))
        {
            sb.AppendLine("        clauses.Add(OwnerColumn + \" = @owner\");");
            sb.AppendLine("        parameters.Add(new KeyValuePair<string, object>(\"@owner\", OwnerOf(request.HttpContext)));");
            sb.AppendLine();
        }

        sb.AppendLine("        return clauses.Count == 0 ? string.Empty : \" WHERE \" + string.Join(\" AND \", clauses);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendFind(StringBuilder sb, string className, TableInfo table)
    {
        sb.AppendLine($"    private static async Task<{className}?> Find(SqliteConnection connection, {KeyParams(table)}{(IsOwned(table) ? ", string owner" : string.Empty)})");
        sb.AppendLine("    {");
        var ownerSql = IsOwned(table) ? " + \" AND \" + OwnerColumn + \" = @owner\"" : string.Empty;
        sb.AppendLine("        var args = new DynamicParameters();");
        AppendKeyParameters(sb, table, "        ");
        if (IsOwned(table))
        {
            sb.AppendLine("        args.Add(\"@owner\", owner);");
        }

        sb.AppendLine($"        return await connection.QueryFirstOrDefaultAsync<{className}>(\"SELECT \" + SelectColumns + \" FROM \" + Table + \" WHERE \" + {KeyCondition(table)}{ownerSql}, args);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendViewPage(StringBuilder sb, string className, TableInfo table, bool canEdit, bool canCopy, bool canDelete)
    {
        var keyValues = KeyPropExprs(className, table);
        sb.AppendLine($"    private static async Task<IResult> ViewPage({KeyParams(table)}{(ViewHasContext(table) ? ", HttpContext context" : string.Empty)}, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine($"        var item = await Find(connection, {KeyArgs(table)}{OwnerArg(table)});");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.NotFound();");
        sb.AppendLine("        }");
        sb.AppendLine();
        AppendDisplayLookups(sb, className, table.Fields.Where(f => f.ShowInView && IsCustom(f)), false);
        var viewLinks = MdViewLinks(table);
        foreach (var l in viewLinks)
        {
            sb.AppendLine($"        var md{l.Index} = await DetailGrid{l.Index}(context, connection, item, false{MdToolsArg(l, true)});");
        }

        var (hostedGrids, belowGrids) = MdPlacement(table, viewLinks, IsPagedView(table), l => $"md{l.Index}");
        string ViewLine(FieldInfo field)
        {
            var viewValue = IsCustom(field) ? DisplayExpr(field, PropName(className, field)) : $"Html.Encode(item.{PropName(className, field)})";
            return $"        body.Append(\"<dt class=\\\"col-sm-3\\\">\").Append(Html.Encode({FieldText(field)})).Append(\"</dt><dd class=\\\"col-sm-9\\\">\").Append({viewValue}).Append(\"</dd>\");";
        }

        if (IsPagedView(table))
        {
            AppendPagedViewBody(sb, table, ViewLine, hostedGrids);
        }
        else
        {
            sb.AppendLine("        var body = new StringBuilder(\"<dl class=\\\"row\\\">\");");
            foreach (var field in table.Fields.Where(f => f.ShowInView))
            {
                sb.AppendLine(ViewLine(field));
            }

            sb.AppendLine("        body.Append(\"</dl><div>\");");
        }
        sb.AppendLine($"        body.Append(Html.Link(Route, {Ui("backToList")}, \"btn btn-sm btn-secondary\")).Append(' ');");
        if (canEdit)
        {
            sb.AppendLine($"        body.Append(Html.Link({KeyUrl("edit", keyValues)}, {Ui("edit")}, \"btn btn-sm btn-outline-secondary\")).Append(' ');");
        }

        if (canCopy)
        {
            sb.AppendLine($"        body.Append(Html.Link({KeyUrl("copy", keyValues)}, {Ui("copy")}, \"btn btn-sm btn-outline-secondary\")).Append(' ');");
        }

        if (canDelete)
        {
            sb.AppendLine($"        body.Append(Html.Link({KeyUrl("delete", keyValues)}, {Ui("delete")}, \"btn btn-sm btn-outline-danger\"));");
        }

        sb.AppendLine("        body.Append(\"</div>\");");
        foreach (var grid in belowGrids)
        {
            sb.AppendLine($"        body.Append({grid});");
        }

        sb.AppendLine(Designed(table)
            ? $"        return PageKit.Page(context.Request, Caption, body.ToString(), {DesignName("view")});"
            : "        return Html.Page(Caption, body.ToString());");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static bool MdShowOnAdd(TableInfo table) =>
        table.PrimaryKeys.Count > 0 && MdCanEdit(table) && MdLinks(table).Any(l => l.Link.ShowOnAdd);

    private static void AppendAddPages(StringBuilder sb, string className, TableInfo table)
    {
        var detail = IsDetailTable(table);
        var addUrl = detail ? "Route + \"/add\" + MasterDetailKit.Carry(context.Request, true)" : "Route + \"/add\"";
        var extra = detail ? ", presets: presets" : string.Empty;
        if (detail)
        {
            sb.AppendLine("    private static async Task<IResult> AddForm(HttpContext context, IAntiforgery antiforgery, string connectionString)");
            sb.AppendLine("    {");
            sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("        await connection.OpenAsync();");
            sb.AppendLine("        var presets = await ReadPresets(context, connection);");
            sb.AppendLine("        if (presets is null)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        var item = {AddItemExpr(className, table)};");
            sb.AppendLine("        ApplyPresets(item, presets);");
            sb.AppendLine($"        return {RenderCall(table, $"{UiSuffix("add", " ")} + Caption, {addUrl}, item, new List<string>(), false, context, antiforgery{KindArg(table, "add")}", extra)};");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            if (HasFormLookups(table))
            {
                sb.AppendLine("    private static async Task<IResult> AddForm(HttpContext context, IAntiforgery antiforgery, string connectionString) =>");
            }
            else
            {
                sb.AppendLine("    private static IResult AddForm(HttpContext context, IAntiforgery antiforgery) =>");
            }

            sb.AppendLine($"        {RenderCall(table, $"{UiSuffix("add", " ")} + Caption, Route + \"/add\", {AddItemExpr(className, table)}, new List<string>(), false, context, antiforgery{KindArg(table, "add")}")};");
            sb.AppendLine();
        }

        sb.AppendLine("    private static async Task<IResult> AddPost(HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var form = await context.Request.ReadFormAsync();");
        if (detail)
        {
            sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("        await connection.OpenAsync();");
            sb.AppendLine("        var presets = await ReadPresets(context, connection);");
            sb.AppendLine("        if (presets is null)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        form = MasterDetailKit.WithValues(form, presets);");
        }

        sb.AppendLine($"        var item = new {className}();");
        sb.AppendLine("        var errors = new List<string>();");
        sb.AppendLine("        BindForm(item, form, false, errors);");
        if (detail)
        {
            sb.AppendLine("        ApplyPresets(item, presets);");
        }

        sb.AppendLine("        if (errors.Count > 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return {RenderCall(table, $"{UiSuffix("add", " ")} + Caption, {addUrl}, item, errors, false, context, antiforgery{KindArg(table, "add")}", extra)};");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (IsOwned(table))
        {
            sb.AppendLine($"        item.{OwnerProp(table)} = OwnerOf(context);");
        }

        if (!detail)
        {
            sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
            sb.AppendLine("        await connection.OpenAsync();");
        }

        AppendPageInsert(sb, table);
        if (MdShowOnAdd(table))
        {
            // A new master record opens its edit page, so detail rows can be added right away. InsertItem stored the new key.
            sb.AppendLine($"        var editUrl = {KeyUrl("edit", KeyPropExprs(className, table))} + MasterDetailKit.Carry(context.Request, false);");
            sb.AppendLine(Designed(table)
                ? $"        return PageKit.Done(context, editUrl, FlashCookie, {Lit(Has(DesignOf(table, "add").SuccessMessage) ? "add" : string.Empty)});"
                : "        return Results.Redirect(editUrl);");
        }
        else
        {
            sb.AppendLine(Designed(table) ? $"        return {DoneCall(table, "add")};" : $"        return Results.Redirect({BackTarget(table)});");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>The insert of the add and copy pages; the audit entry of an audited table is written in the same transaction.</summary>
    private static void AppendPageInsert(StringBuilder sb, TableInfo table)
    {
        if (!AuditsInserts(table))
        {
            sb.AppendLine("        await InsertItem(connection, item);");
            return;
        }

        AppendAuditTransaction(sb, "        ", pad =>
        {
            sb.AppendLine($"{pad}await InsertItem(connection, item);");
            sb.AppendLine($"{pad}{AuditWrite(table, "insert", "context", "item.AuditKey()", "null", "item.AuditValues()")}");
        });
    }

    private static void AppendCopyPages(StringBuilder sb, TableInfo table)
    {
        var detail = IsDetailTable(table);
        var keyParams = KeyParams(table);
        var keyArgs = KeyArgs(table);
        var copyUrl = KeyUrl("copy", KeyNames(table)) + (detail ? " + MasterDetailKit.Carry(context.Request, true)" : string.Empty);
        var extra = detail ? ", presets: presets" : string.Empty;
        sb.AppendLine($"    private static async Task<IResult> CopyForm({keyParams}, HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine($"        var item = await Find(connection, {keyArgs}{OwnerArg(table)});");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.NotFound();");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (detail)
        {
            sb.AppendLine("        var presets = await ReadPresets(context, connection);");
            sb.AppendLine("        if (presets is null)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        ApplyPresets(item, presets);");
        }

        sb.AppendLine($"        return {RenderCall(table, $"{UiSuffix("copy", " ")} + Caption, {copyUrl}, item, new List<string>(), false, context, antiforgery{KindArg(table, "copy")}", extra)};");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static async Task<IResult> CopyPost({keyParams}, HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var form = await context.Request.ReadFormAsync();");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine($"        var item = await Find(connection, {keyArgs}{OwnerArg(table)});");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.NotFound();");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (detail)
        {
            sb.AppendLine("        var presets = await ReadPresets(context, connection);");
            sb.AppendLine("        if (presets is null)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        form = MasterDetailKit.WithValues(form, presets);");
        }

        sb.AppendLine("        var errors = new List<string>();");
        sb.AppendLine("        BindForm(item, form, false, errors);");
        if (detail)
        {
            sb.AppendLine("        ApplyPresets(item, presets);");
        }

        sb.AppendLine("        if (errors.Count > 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return {RenderCall(table, $"{UiSuffix("copy", " ")} + Caption, {copyUrl}, item, errors, false, context, antiforgery{KindArg(table, "copy")}", extra)};");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (IsOwned(table))
        {
            sb.AppendLine($"        item.{OwnerProp(table)} = OwnerOf(context);");
        }

        AppendPageInsert(sb, table);
        sb.AppendLine(Designed(table) ? $"        return {DoneCall(table, "copy")};" : $"        return Results.Redirect({BackTarget(table)});");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendEditPages(StringBuilder sb, string className, TableInfo table)
    {
        var updateFields = UpdateFields(table);
        var keyParams = KeyParams(table);
        var keyArgs = KeyArgs(table);
        var detail = IsDetailTable(table);
        var gridLinks = MdEditLinks(table).Count > 0;
        var editUrl = KeyUrl("edit", KeyNames(table)) + (detail ? " + MasterDetailKit.Carry(context.Request, false)" : string.Empty);
        var extra = gridLinks ? ", grids: grids" : string.Empty;
        var cascadeUpdate = MdUpdatePlanned(table) && updateFields.Count > 0;
        sb.AppendLine($"    private static async Task<IResult> EditForm({keyParams}, HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine($"        var item = await Find(connection, {keyArgs}{OwnerArg(table)});");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.NotFound();");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (gridLinks)
        {
            sb.AppendLine("        var grids = await DetailGridsForEdit(context, connection, item);");
        }

        sb.AppendLine($"        return {RenderCall(table, $"{UiSuffix("edit", " ")} + Caption, {editUrl}, item, new List<string>(), true, context, antiforgery{KindArg(table, "edit")}", extra)};");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private static async Task<IResult> EditPost({keyParams}, HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var form = await context.Request.ReadFormAsync();");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine($"        var item = await Find(connection, {keyArgs}{OwnerArg(table)});");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.NotFound();");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (cascadeUpdate)
        {
            sb.AppendLine($"        var mdOld = {MdOldNew(table, className, "item")};");
        }

        var auditUpdate = AuditsUpdates(table) && updateFields.Count > 0;
        if (auditUpdate)
        {
            sb.AppendLine("        var auditBefore = item.AuditValues();");
        }

        sb.AppendLine("        var errors = new List<string>();");
        sb.AppendLine("        BindForm(item, form, true, errors);");
        sb.AppendLine("        if (errors.Count > 0)");
        sb.AppendLine("        {");
        if (gridLinks)
        {
            sb.AppendLine($"            var fresh = await Find(connection, {keyArgs}{OwnerArg(table)}) ?? item;");
            sb.AppendLine("            var grids = await DetailGridsForEdit(context, connection, fresh);");
        }

        sb.AppendLine($"            return {RenderCall(table, $"{UiSuffix("edit", " ")} + Caption, {editUrl}, item, errors, true, context, antiforgery{KindArg(table, "edit")}", extra)};");
        sb.AppendLine("        }");
        sb.AppendLine();
        if (updateFields.Count > 0)
        {
            var transactional = cascadeUpdate || auditUpdate;
            var pad = transactional ? "    " : string.Empty;
            var transaction = cascadeUpdate ? "ReferentialIntegrity" : AuditType;
            if (cascadeUpdate)
            {
                sb.AppendLine($"        var mdNew = {MdOldNew(table, className, "item")};");
            }

            if (transactional)
            {
                sb.AppendLine($"        await {transaction}.BeginAsync(connection);");
                sb.AppendLine("        try");
                sb.AppendLine("        {");
            }

            sb.AppendLine($"{pad}        var args = new DynamicParameters();");
            AppendParameters(sb, updateFields, "item", className, pad + "        ");
            AppendKeyParameters(sb, table, pad + "        ");
            sb.AppendLine($"{pad}        await connection.ExecuteAsync({Lit(UpdateSql(table, updateFields))}, args);");
            if (auditUpdate)
            {
                sb.AppendLine($"            var auditAfter = await Find(connection, {keyArgs}{OwnerArg(table)});");
                sb.AppendLine($"            {AuditWrite(table, "update", "context", "item.AuditKey()", "auditBefore", "(auditAfter ?? item).AuditValues()")}");
            }

            if (cascadeUpdate)
            {
                sb.AppendLine($"            await ReferentialIntegrity.Update_{className}(connection, mdOld, mdNew{(MdUpdateAudited(table) ? ", context" : string.Empty)});");
            }

            if (transactional)
            {
                sb.AppendLine($"            await {transaction}.CommitAsync(connection);");
                sb.AppendLine("        }");
                sb.AppendLine("        catch");
                sb.AppendLine("        {");
                sb.AppendLine($"            await {transaction}.RollbackAsync(connection);");
                sb.AppendLine("            throw;");
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine(Designed(table) ? $"        return {DoneCall(table, "edit")};" : $"        return Results.Redirect({BackTarget(table)});");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendDeletePages(StringBuilder sb, TableInfo table)
    {
        var keyParams = KeyParams(table);
        var keyArgs = KeyArgs(table);
        var planned = MdDeletePlanned(table);
        var cls = Identifier.Pascal(table.Name);
        sb.AppendLine($"    private static async Task<IResult> DeleteConfirm({keyParams}, HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine($"        var item = await Find(connection, {keyArgs}{OwnerArg(table)});");
        sb.AppendLine("        if (item is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.NotFound();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        var target = {KeyUrl("delete", KeyNames(table))}{(IsDetailTable(table) ? " + MasterDetailKit.Carry(context.Request, false)" : string.Empty)};");
        if (planned)
        {
            var args = MdArgs(table, "OwnerOf(context)");
            sb.AppendLine($"        var mdArgs = {args};");
            sb.AppendLine($"        var blockers = await ReferentialIntegrity.CountsAsync(connection, ReferentialIntegrity.Plan_{cls}.Blockers, mdArgs);");
            sb.AppendLine($"        var cascades = await ReferentialIntegrity.CountsAsync(connection, ReferentialIntegrity.Plan_{cls}.Counts, mdArgs);");
        }

        var back = BackTarget(table);
        if (Designed(table))
        {
            var buttons = $"{Ui("delete")}, {Ui("cancel")}, {back}, \"btn btn-danger\"";
            sb.AppendLine($"        var o = {DesignName("delete")};");
            sb.AppendLine($"        var body = \"<p>{UiInText("confirmDelete")}</p>\"");
            sb.AppendLine("            + \"<form method=\\\"post\\\" action=\\\"\" + Html.Attr(target) + \"\\\"\" + PageKit.FormAttributes(o) + \">\"");
            sb.AppendLine("            + Html.AntiforgeryField(context, antiforgery)");
            sb.AppendLine($"            + PageKit.TopButtons(o, {buttons})");
            sb.AppendLine($"            + PageKit.BottomButtons(o, {buttons}) + \"</form>\";");
            if (planned)
            {
                sb.AppendLine("        body = ReferentialIntegrity.Decorate(body, blockers, cascades);");
            }

            sb.AppendLine($"        return PageKit.Page(context.Request, {UiSuffix("delete", " ")} + Caption, body, o);");
            sb.AppendLine("    }");
            sb.AppendLine();
            AppendDeletePost(sb, table);
            return;
        }

        sb.AppendLine($"        var body = \"<p>{UiInText("confirmDelete")}</p>\"");
        sb.AppendLine("            + \"<form method=\\\"post\\\" action=\\\"\" + Html.Attr(target) + \"\\\">\"");
        sb.AppendLine("            + Html.AntiforgeryField(context, antiforgery)");
        sb.AppendLine($"            + \"<button type=\\\"submit\\\" class=\\\"btn btn-danger\\\">{UiInText("delete")}</button> \"");
        sb.AppendLine($"            + Html.Link({back}, {Ui("cancel")}, \"btn btn-secondary\") + \"</form>\";");
        if (planned)
        {
            sb.AppendLine("        body = ReferentialIntegrity.Decorate(body, blockers, cascades);");
        }

        sb.AppendLine($"        return Html.Page({UiSuffix("delete", " ")} + Caption, body);");
        sb.AppendLine("    }");
        sb.AppendLine();
        AppendDeletePost(sb, table);
    }

    private static void AppendDeletePost(StringBuilder sb, TableInfo table)
    {
        var keyParams = KeyParams(table);
        var keyArgs = KeyArgs(table);
        var planned = MdDeletePlanned(table);
        var auditDelete = AuditsDeletes(table);
        sb.AppendLine($"    private static async Task<IResult> DeletePost({keyParams}, HttpContext context, IAntiforgery antiforgery, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        if (IsOwned(table))
        {
            sb.AppendLine(auditDelete
                ? $"        var auditItem = await Find(connection, {keyArgs}{OwnerArg(table)});"
                : $"        if (await Find(connection, {keyArgs}{OwnerArg(table)}) is null)");
            if (auditDelete)
            {
                sb.AppendLine("        if (auditItem is null)");
            }

            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        else if (auditDelete)
        {
            sb.AppendLine($"        var auditItem = await Find(connection, {keyArgs});");
        }

        if (planned)
        {
            // The plan re-checks the restrict rules, cascades and sets null in one transaction.
            sb.AppendLine($"        var outcome = await ReferentialIntegrity.DeleteAsync(connection, ReferentialIntegrity.Plan_{Identifier.Pascal(table.Name)}, {MdArgs(table, "OwnerOf(context)")}{MdAuditDeleteArgs(table, "context", "auditItem")});");
            sb.AppendLine("        if (outcome.Status == ReferentialIntegrity.NotFound)");
            sb.AppendLine("        {");
            sb.AppendLine("            return Results.NotFound();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        if (outcome.Status == ReferentialIntegrity.Blocked)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return Results.Text(Html.Document(Caption, \"<div class=\\\"alert alert-danger\\\" role=\\\"alert\\\">\" + Html.Encode(outcome.Message) + \"</div>\" + Html.Link({BackTarget(table)}, {Ui("cancel")}, \"btn btn-secondary\")), \"text/html; charset=utf-8\", statusCode: StatusCodes.Status409Conflict);");
            sb.AppendLine("        }");
        }
        else
        {
            sb.AppendLine("        var args = new DynamicParameters();");
            AppendKeyParameters(sb, table, "        ");
            if (auditDelete)
            {
                AppendAuditTransaction(sb, "        ", pad =>
                {
                    sb.AppendLine($"{pad}await connection.ExecuteAsync(\"DELETE FROM \" + Table + \" WHERE \" + {KeyCondition(table)}, args);");
                    sb.AppendLine($"{pad}if (auditItem is not null)");
                    sb.AppendLine($"{pad}{{");
                    sb.AppendLine($"{pad}    {AuditWrite(table, "delete", "context", "auditItem.AuditKey()", "auditItem.AuditValues()", "null")}");
                    sb.AppendLine($"{pad}}}");
                });
            }
            else
            {
                sb.AppendLine($"        await connection.ExecuteAsync(\"DELETE FROM \" + Table + \" WHERE \" + {KeyCondition(table)}, args);");
            }
        }

        sb.AppendLine(Designed(table) ? $"        return {DoneCall(table, "delete")};" : $"        return Results.Redirect({BackTarget(table)});");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void AppendInsert(StringBuilder sb, string className, TableInfo table)
    {
        sb.AppendLine($"    private static async Task InsertItem(SqliteConnection connection, {className} item)");
        sb.AppendLine("    {");
        AppendInsertCall(sb, className, table, InsertFields(table), "        ");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// Inserts <c>item</c> on <c>connection</c>. An identity key is read back by the same command (see
    /// <see cref="SqlDialect.IdentityAfterInsert"/>) and stored in the item.
    /// </summary>
    private static void AppendInsertCall(StringBuilder sb, string className, TableInfo table, List<FieldInfo> insertFields, string indent)
    {
        var keys = table.PrimaryKeys;
        sb.AppendLine($"{indent}var args = new DynamicParameters();");
        AppendParameters(sb, insertFields, "item", className, indent);
        if (keys.Count == 1 && keys[0].IsAutoIncrement)
        {
            var type = CSharpType(keys[0], forceNonNull: true);
            var cast = type == "long" ? string.Empty : $"({type})";
            sb.AppendLine($"{indent}item.{PropName(className, keys[0])} = {cast}await connection.ExecuteScalarAsync<long>({Lit(InsertSql(table, insertFields) + Db.IdentityAfterInsert)}, args);");
        }
        else
        {
            sb.AppendLine($"{indent}await connection.ExecuteAsync({Lit(InsertSql(table, insertFields))}, args);");
        }
    }

    private static void AppendForm(StringBuilder sb, string className, TableInfo table)
    {
        if (Designed(table))
        {
            AppendDesignedForm(sb, className, table);
            return;
        }

        var asyncForm = HasFormLookups(table);
        sb.AppendLine(asyncForm
            ? $"    private static async Task<IResult> RenderForm(string title, string action, {className} item, List<string> errors, bool edit, HttpContext context, IAntiforgery antiforgery, string connectionString{MdFormParams(table)})"
            : $"    private static IResult RenderForm(string title, string action, {className} item, List<string> errors, bool edit, HttpContext context, IAntiforgery antiforgery{MdFormParams(table)})");
        sb.AppendLine("    {");
        if (asyncForm)
        {
            AppendFormLookups(sb, className, table);
        }

        sb.AppendLine("        var body = new StringBuilder();");
        sb.AppendLine("        if (errors.Count > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            body.Append(\"<div class=\\\"alert alert-danger\\\"><ul class=\\\"mb-0\\\">\");");
        sb.AppendLine("            foreach (var error in errors)");
        sb.AppendLine("            {");
        sb.AppendLine("                body.Append(\"<li>\").Append(Html.Encode(error)).Append(\"</li>\");");
        sb.AppendLine("            }");
        sb.AppendLine("            body.Append(\"</ul></div>\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        body.Append(\"<form method=\\\"post\\\" action=\\\"\").Append(Html.Attr(action)).Append(\"\\\">\");");
        sb.AppendLine("        body.Append(Html.AntiforgeryField(context, antiforgery));");
        string? group = null;
        foreach (var field in table.Fields.Where(IsFormField))
        {
            var prop = PropName(className, field);
            foreach (var groupLine in GroupTransition(group, field))
            {
                sb.AppendLine(groupLine);
            }

            group = field.Ui.Group;
            AppendMdFixed(sb, table, field, target =>
            {
                if (IsCompositeKey(table, field))
                {
                    AppendCompositeKeyInput(target, field, prop);
                    return;
                }

                var condition = FormCondition(field);
                var line = FormInputLine(field, prop);
                target.AppendLine(condition is null ? $"        {line}" : $"        if ({condition}) {line}");
            });
        }

        if (!string.IsNullOrEmpty(group))
        {
            sb.AppendLine("        body.Append(FieldEditor.GroupEnd);");
        }

        sb.AppendLine($"        body.Append(\"<button type=\\\"submit\\\" class=\\\"btn btn-primary\\\">{UiInText("save")}</button> \");");
        sb.AppendLine($"        body.Append(Html.Link({BackTarget(table)}, {Ui("cancel")}, \"btn btn-secondary\"));");
        sb.AppendLine("        body.Append(\"</form>\");");
        AppendMdEditTail(sb, table, false);
        sb.AppendLine("        return Html.Page(title, body.ToString());");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// A composite key column is an input in add and copy, so the new row gets its keys. In edit it is shown
    /// read-only, and a hidden input carries the value so the form still posts the complete key.
    /// </summary>
    private static void AppendCompositeKeyInput(StringBuilder sb, FieldInfo field, string prop)
    {
        var name = Lit(field.Name);
        var label = FieldText(field);
        sb.AppendLine("        if (edit)");
        sb.AppendLine("        {");
        sb.AppendLine($"            body.Append(\"<div class=\\\"mb-3\\\"><label class=\\\"form-label\\\">\").Append(Html.Encode({label})).Append(\"</label><input class=\\\"form-control\\\" type=\\\"text\\\" value=\\\"\").Append(Html.Attr(item.{prop}.ToString())).Append(\"\\\" readonly></div>\");");
        sb.AppendLine($"            body.Append(Html.Hidden({name}, item.{prop}.ToString()));");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine($"            {FormInputLine(field, prop)}");
        sb.AppendLine("        }");
    }

    private static void AppendBindForm(StringBuilder sb, string className, TableInfo table)
    {
        sb.AppendLine($"    private static void BindForm({className} item, IFormCollection form, bool edit, List<string> errors)");
        sb.AppendLine("    {");
        var tagged = IsPagedForm(table);
        var number = 0;
        foreach (var field in table.Fields.Where(IsFormField))
        {
            var (bindAdd, bindEdit) = BindModes(field);
            if (!bindAdd && !bindEdit)
            {
                continue;
            }

            var condition = bindAdd && bindEdit ? null : bindEdit ? "edit" : "!edit";
            var block = BindBlock(field, PropName(className, field));
            if (tagged)
            {
                // The errors a field adds are remembered for its form page, so the page can show them and mark itself.
                number++;
                sb.AppendLine(TagBefore(number));
            }

            if (condition is null)
            {
                sb.Append(block);
            }
            else
            {
                sb.AppendLine($"        if ({condition})");
                sb.AppendLine("        {");
                sb.Append(IndentBlock(block, "    "));
                sb.AppendLine("        }");
            }

            if (tagged)
            {
                sb.AppendLine(TagAfter(table, field, number));
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    // ---- owner (User ID security) helpers -------------------------------------------------------

    /// <summary>True when the table has record-level security. Only the generated copy sets OwnerField, and only when security is enabled.</summary>
    private static bool IsOwned(TableInfo table) => table.OwnerField.Length > 0;

    private const string ViewDeniedMessage = "You do not have permission to view this table.";
    private const string EditDeniedMessage = "You do not have permission to change this table.";

    /// <summary>True when the table has a user level limit. Only the generated copy sets the limits, and only when User Level security is on.</summary>
    private static bool IsLevelGuarded(TableInfo table) => table.ViewMinLevel > 0 || table.EditMinLevel > 0;

    /// <summary>
    /// Route filter that applies a user level limit. It is written only when the limit is above 0, so a table whose limits
    /// are both 0 generates the same routes as before User Level security. Users below the limit get 403 as plain text.
    /// </summary>
    private static string LevelGuard(int minLevel, string deniedMessage) =>
        minLevel > 0 ? ".RequireLevel(" + minLevel + ", " + Lit(deniedMessage) + ")" : string.Empty;

    private static bool IsOwnerField(TableInfo table, FieldInfo field) => IsOwned(table) && field.Name == table.OwnerField;

    /// <summary>Columns written by UPDATE: every non-key column except the owner (set once at insert) and secret columns (never written from input).</summary>
    private static List<FieldInfo> UpdateFields(TableInfo table) =>
        table.Fields.Where(f => !f.IsPrimaryKey && !f.IsSecret && !IsOwnerField(table, f)).ToList();

    /// <summary>SQL condition that limits a row to its owner. The user name is bound as @owner.</summary>
    private static string OwnerCondition(TableInfo table) => QuoteIdent(table.OwnerField) + " = @owner";

    /// <summary>Extra argument for a generated Find call, or empty when the table has no owner.</summary>
    private static string OwnerArg(TableInfo table) => IsOwned(table) ? ", OwnerOf(context)" : string.Empty;

    /// <summary>C# property of the owner column in the generated model.</summary>
    private static string OwnerProp(TableInfo table) =>
        PropName(Identifier.Pascal(table.Name), table.Fields.First(f => f.Name == table.OwnerField));

    /// <summary>Binds @owner to the signed-in user name in a generated endpoint. Does nothing without an owner.</summary>
    private static void AppendOwnerParameter(StringBuilder sb, bool owned, string variable, string indent)
    {
        if (owned)
        {
            sb.AppendLine($"{indent}{variable}.Add(\"@owner\", user.Identity?.Name ?? string.Empty);");
        }
    }

    // ---- key helpers ----------------------------------------------------------------------------

    /// <summary>Route and parameter names for the key: "id" for a single key, "k0", "k1", ... for a composite key.</summary>
    private static List<string> KeyNames(TableInfo table)
    {
        var count = table.PrimaryKeys.Count;
        if (count <= 1)
        {
            return new List<string> { "id" };
        }

        return Enumerable.Range(0, count).Select(i => "k" + i).ToList();
    }

    /// <summary>C# parameter list for the key, e.g. "long id" or "long k0, string k1".</summary>
    private static string KeyParams(TableInfo table)
    {
        var keys = table.PrimaryKeys;
        var names = KeyNames(table);
        return string.Join(", ", keys.Select((key, i) => CSharpType(key, forceNonNull: true) + " " + names[i]));
    }

    private static string KeyArgs(TableInfo table) => string.Join(", ", KeyNames(table));

    /// <summary>Route template for the key, e.g. "{id}" or "{k0}/{k1}".</summary>
    private static string KeyRoute(TableInfo table) => string.Join("/", KeyNames(table).Select(name => "{" + name + "}"));

    /// <summary>SQL condition on the key columns with one bound parameter per column, e.g. "OrderID" = @k0 AND "ProductID" = @k1.</summary>
    private static string KeyWhereSql(TableInfo table)
    {
        var keys = table.PrimaryKeys;
        var names = KeyNames(table);
        return string.Join(" AND ", keys.Select((key, i) => QuoteIdent(key.Name) + " = @" + names[i]));
    }

    /// <summary>C# expression, used inside generated queries, that yields the key condition after " WHERE ".</summary>
    private static string KeyCondition(TableInfo table) => table.PrimaryKeys.Count > 1 ? "KeyWhere" : "PkColumn + \" = @id\"";

    /// <summary>Binds one parameter per key column. Parameter and variable names follow <see cref="KeyNames"/>.</summary>
    private static void AppendKeyParameters(StringBuilder sb, TableInfo table, string indent, string variable = "args")
    {
        foreach (var name in KeyNames(table))
        {
            sb.AppendLine($"{indent}{variable}.Add(\"@{name}\", {name});");
        }
    }

    /// <summary>Key property expressions of an item variable, e.g. "item.OrderId" and "item.ProductId".</summary>
    private static List<string> KeyPropExprs(string className, TableInfo table) =>
        table.PrimaryKeys.Select(key => "item." + PropName(className, key)).ToList();

    /// <summary>C# expression for Route + "/action/" + each value escaped with Uri.EscapeDataString and joined by "/".</summary>
    private static string KeyUrl(string action, IEnumerable<string> values) =>
        "Route + \"/" + action + "/\" + "
        + string.Join(" + \"/\" + ", values.Select(value => "Uri.EscapeDataString(" + value + ".ToString())"));

    private static bool IsCompositeKey(TableInfo table, FieldInfo field) => field.IsPrimaryKey && table.PrimaryKeys.Count > 1;

    // ---- per-field code -------------------------------------------------------------------------

    /// <summary>
    /// Which form modes a field is editable in. Add/copy may set a non-auto-increment key;
    /// edit never changes the key. Binary fields are not editable in forms.
    /// </summary>
    private static (bool Add, bool Edit) FormModes(FieldInfo field)
    {
        if (field.Type == FieldType.Binary)
        {
            return (false, false);
        }

        var add = field.ShowInAdd && !(field.IsPrimaryKey && field.IsAutoIncrement);
        var edit = field.ShowInEdit && !field.IsPrimaryKey;
        return (add, edit);
    }

    private static bool IsFormField(FieldInfo field) => FormModes(field) is (true, _) or (_, true);

    private static bool IsSearchable(FieldInfo field) => field.Searchable && field.Type != FieldType.Binary;

    /// <summary>Null when the field is shown in both modes; otherwise the mode test used in the generated code.</summary>
    private static string? FormCondition(FieldInfo field)
    {
        var (add, edit) = FormModes(field);
        if (add && edit)
        {
            return null;
        }

        return edit ? "edit" : "!edit";
    }

    private static string FormInputLine(FieldInfo field, string prop)
    {
        if (IsCustom(field))
        {
            return CustomFormLine(field, prop);
        }

        var name = Lit(field.Name);
        var label = FieldText(field);
        var required = field.IsRequired ? "true" : "false";
        var nullable = !field.IsRequired;
        return field.Type switch
        {
            FieldType.Boolean => $"body.Append(Html.Checkbox({name}, {label}, item.{prop} == true));",
            FieldType.Integer => $"body.Append(Html.Input({name}, {label}, \"number\", {NumberText(prop, nullable)}, {required}));",
            FieldType.Decimal => $"body.Append(Html.Input({name}, {label}, \"number\", {NumberText(prop, nullable)}, {required}).Replace(\" type=\\\"number\\\"\", \" type=\\\"number\\\" step=\\\"any\\\"\", StringComparison.Ordinal));",
            FieldType.Date => $"body.Append(Html.Input({name}, {label}, \"date\", {DateText(prop, nullable, "yyyy-MM-dd")}, {required}));",
            FieldType.DateTime => $"body.Append(Html.Input({name}, {label}, \"datetime-local\", {DateText(prop, nullable, "yyyy-MM-ddTHH:mm")}, {required}));",
            _ => $"body.Append(Html.Input({name}, {label}, \"text\", {TextText(prop, nullable)}, {required}));",
        };
    }

    private static string TextText(string prop, bool nullable) =>
        nullable ? $"item.{prop} ?? string.Empty" : $"item.{prop}";

    private static string NumberText(string prop, bool nullable) =>
        nullable
            ? $"item.{prop}?.ToString(CultureInfo.InvariantCulture) ?? string.Empty"
            : $"item.{prop}.ToString(CultureInfo.InvariantCulture)";

    private static string DateText(string prop, bool nullable, string format)
    {
        var f = Lit(format);
        return nullable
            ? $"item.{prop}?.ToString({f}, CultureInfo.InvariantCulture) ?? string.Empty"
            : $"item.{prop}.ToString({f}, CultureInfo.InvariantCulture)";
    }

    private static string BindBlock(FieldInfo field, string prop)
    {
        var sb = new StringBuilder();
        var name = Lit(field.Name);
        var label = FieldText(field);
        var nullable = !field.IsRequired;

        switch (field.Type)
        {
            case FieldType.Boolean:
                sb.AppendLine($"        item.{prop} = form.ContainsKey({name});");
                break;

            case FieldType.Text when IsCustom(field):
                sb.Append(CustomTextBind(field, prop));
                break;

            case FieldType.Text:
                sb.AppendLine("        {");
                sb.AppendLine($"            var value = form[{name}].ToString().Trim();");
                if (field.IsRequired)
                {
                    sb.AppendLine($"            if (value.Length == 0) errors.Add({FieldMessage("required", field)});");
                    sb.AppendLine($"            item.{prop} = value;");
                }
                else
                {
                    sb.AppendLine($"            item.{prop} = value.Length == 0 ? null : value;");
                }

                sb.AppendLine("        }");
                break;

            case FieldType.Integer:
            case FieldType.Decimal:
                EmitNumberBind(sb, field, prop, name, label, nullable, IsCustom(field) ? NumberChecks(field, prop) : null);
                break;

            case FieldType.Date:
            case FieldType.DateTime:
                EmitDateBind(sb, field, prop, name, label, nullable, IsCustom(field) ? DateChecks(field) : null);
                break;
        }

        return sb.ToString();
    }

    private static void EmitNumberBind(StringBuilder sb, FieldInfo field, string prop, string name, string label, bool nullable, List<string>? checks = null)
    {
        var parse = field.Type == FieldType.Integer
            ? "long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)"
            : "decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)";
        var invalid = FieldMessage("notNumber", field);
        var required = IsCustom(field) ? RequiredText(field) : FieldMessage("required", field);

        sb.AppendLine("        {");
        sb.AppendLine($"            var raw = form[{name}].ToString().Trim();");
        if (nullable)
        {
            sb.AppendLine("            if (raw.Length == 0)");
            sb.AppendLine("            {");
            sb.AppendLine($"                item.{prop} = null;");
            sb.AppendLine("            }");
        }
        else
        {
            sb.AppendLine("            if (raw.Length == 0)");
            sb.AppendLine("            {");
            sb.AppendLine($"                errors.Add({required});");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            else if (" + parse + ")");
        sb.AppendLine("            {");
        sb.AppendLine($"                item.{prop} = parsed;");
        foreach (var check in checks ?? [])
        {
            sb.AppendLine($"                {check}");
        }

        sb.AppendLine("            }");
        sb.AppendLine("            else");
        sb.AppendLine("            {");
        sb.AppendLine($"                errors.Add({invalid});");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    private static void EmitDateBind(StringBuilder sb, FieldInfo field, string prop, string name, string label, bool nullable, List<string>? checks = null)
    {
        var invalid = FieldMessage("notDate", field);
        var required = IsCustom(field) ? RequiredText(field) : FieldMessage("required", field);

        sb.AppendLine("        {");
        sb.AppendLine($"            var raw = form[{name}].ToString().Trim();");
        if (nullable)
        {
            sb.AppendLine("            if (raw.Length == 0)");
            sb.AppendLine("            {");
            sb.AppendLine($"                item.{prop} = null;");
            sb.AppendLine("            }");
        }
        else
        {
            sb.AppendLine("            if (raw.Length == 0)");
            sb.AppendLine("            {");
            sb.AppendLine($"                errors.Add({required});");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            else if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))");
        sb.AppendLine("            {");
        sb.AppendLine($"                item.{prop} = parsed;");
        foreach (var check in checks ?? [])
        {
            sb.AppendLine($"                {check}");
        }

        sb.AppendLine("            }");
        sb.AppendLine("            else");
        sb.AppendLine("            {");
        sb.AppendLine($"                errors.Add({invalid});");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    private static string IndentBlock(string block, string extra)
    {
        var sb = new StringBuilder();
        foreach (var line in block.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            sb.Append(extra).Append(line).Append('\n');
        }

        return sb.ToString();
    }

    private static void AppendParameters(StringBuilder sb, IReadOnlyList<FieldInfo> fields, string itemVariable, string className, string indent)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            // A binary null needs its type: SQL Server refuses an untyped null for a varbinary column.
            var type = fields[i].Type == FieldType.Binary ? ", System.Data.DbType.Binary" : string.Empty;
            sb.AppendLine($"{indent}args.Add(\"@p{i}\", {ParameterValue(fields[i], itemVariable, className)}{type});");
        }
    }

    // ---- SQL ------------------------------------------------------------------------------------

    private static List<FieldInfo> InsertFields(TableInfo table) =>
        table.Fields.Where(f => !(f.IsPrimaryKey && f.IsAutoIncrement) && !f.IsSecret).ToList();

    private static string InsertSql(TableInfo table, IReadOnlyList<FieldInfo> fields)
    {
        var columns = string.Join(", ", fields.Select(f => QuoteIdent(f.Name)));
        var values = string.Join(", ", Enumerable.Range(0, fields.Count).Select(i => "@p" + i));
        return "INSERT INTO " + QuoteIdent(table.Name) + " (" + columns + ") VALUES (" + values + ")";
    }

    private static string UpdateSql(TableInfo table, IReadOnlyList<FieldInfo> fields)
    {
        var sets = string.Join(", ", fields.Select((f, i) => QuoteIdent(f.Name) + " = @p" + i));
        var where = table.PrimaryKeys.Count == 0 ? string.Empty : " WHERE " + KeyWhereSql(table);
        return "UPDATE " + QuoteIdent(table.Name) + " SET " + sets + where;
    }

    /// <summary>The columns of a table, each named after its model property so that Dapper maps it (e.g. "order date" AS "OrderDate").</summary>
    private static string SelectColumns(TableInfo table)
    {
        var className = Identifier.Pascal(table.Name);
        return string.Join(", ", table.Fields.Select(f => QuoteIdent(f.Name) + " AS " + QuoteIdent(PropName(className, f))));
    }

    private static string OrderSql(TableInfo table)
    {
        if (table.Sort.Count > 0)
        {
            return " ORDER BY " + string.Join(", ", table.Sort.Select(s => QuoteIdent(s.Field) + (s.Descending ? " DESC" : " ASC")));
        }

        var keys = table.PrimaryKeys;
        return keys.Count == 0
            ? Db.FallbackOrder
            : " ORDER BY " + string.Join(", ", keys.Select(k => QuoteIdent(k.Name) + " ASC"));
    }

    /// <summary>Quoted SQL identifier of the dialect (raw SQL text; escape with <see cref="Lit"/> before emitting C#).</summary>
    internal static string QuoteIdent(string name) => Db.Quote(name);

    /// <summary>
    /// Rewrites a generated C# file for the dialect: the SQLite transaction statements, the unique constraint check, the
    /// login_attempts statements and the ADO.NET type names. SQLite files are returned unchanged.
    /// </summary>
    internal static string ForDialect(string code)
    {
        if (Db == SqlDialect.Sqlite)
        {
            return code;
        }

        var sqlite = SqlDialect.Sqlite;
        code = code
            .Replace(Lit(sqlite.BeginTransaction), Lit(Db.BeginTransaction))
            .Replace(Lit(sqlite.Commit), Lit(Db.Commit))
            .Replace(Lit(sqlite.Rollback), Lit(Db.Rollback))
            .Replace(sqlite.UniqueViolation, Db.UniqueViolation)
            .Replace(Lit(sqlite.CreateLoginAttempts), Lit(Db.CreateLoginAttempts));
        code = System.Text.RegularExpressions.Regex.Replace(code, "\"INSERT INTO login_attempts [^\n]*\n\\s*\\+ \"ON CONFLICT\\(username\\)[^\n]*\"", _ => Lit(Db.UpsertLoginAttempt));
        return Db.AdaptCode(code);
    }

    /// <summary>The text before and after the expression in the dialect's text cast: "CAST(" and " AS TEXT)" in SQLite.</summary>
    private static (string Before, string After) TextCastParts()
    {
        var parts = Db.TextCast("\u0001").Split('\u0001');
        return (parts[0], parts[1]);
    }

    private static string StringArray(IEnumerable<string> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return "Array.Empty<string>()";
        }

        return "new[] { " + string.Join(", ", list.Select(Lit)) + " }";
    }

    // ---- shared helpers -------------------------------------------------------------------------

    /// <summary>C# string literal for <paramref name="value"/>.</summary>
    internal static string Lit(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
        return "\"" + escaped + "\"";
    }

    private static string Caption(TableInfo table) =>
        string.IsNullOrEmpty(table.Caption) ? table.Name : table.Caption;

    private static string FieldCaption(FieldInfo field) =>
        string.IsNullOrEmpty(field.Caption) ? field.Name : field.Caption;

    /// <summary>Property name for a field. A property may not share the name of its class.</summary>
    internal static string PropName(string className, FieldInfo field)
    {
        var name = Identifier.Pascal(field.Name);
        return name == className ? name + "Value" : name;
    }

    private static string CSharpType(FieldInfo field, bool forceNonNull = false)
    {
        var baseType = field.Type switch
        {
            FieldType.Integer => "long",
            FieldType.Decimal => "decimal",
            FieldType.Boolean => "bool",
            FieldType.Date or FieldType.DateTime => "DateTime",
            FieldType.Binary => "byte[]",
            _ => "string",
        };

        var nullable = !forceNonNull && !field.IsRequired;
        return nullable ? baseType + "?" : baseType;
    }

    private static string DefaultFor(FieldInfo field)
    {
        if (!field.IsRequired)
        {
            return string.Empty;
        }

        return field.Type switch
        {
            FieldType.Text => " = string.Empty;",
            FieldType.Binary => " = Array.Empty<byte>();",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The value of a field as a parameter: the property itself. Dapper turns null into DBNull and sends a bool as a boolean,
    /// which SQLite stores as 1 or 0, SQL Server as a bit and MySQL as TINYINT(1).
    /// </summary>
    private static string ParameterValue(FieldInfo field, string itemVariable, string className) => $"{itemVariable}.{PropName(className, field)}";
}
