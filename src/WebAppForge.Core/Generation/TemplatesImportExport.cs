using System.Text;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    /// <summary>The import and export settings of the site being generated; null (the default) when the site has neither.</summary>
    [ThreadStatic]
    private static ImportExportSettings? _importExport;

    internal static ImportExportSettings? ImportExportConfig
    {
        get => _importExport;
        set => _importExport = value;
    }

    /// <summary>True when the site being generated offers Excel or Word, which need the Open XML SDK package.</summary>
    [ThreadStatic]
    private static bool _exchangeOpenXml;

    internal static bool ExchangeOpenXml
    {
        get => _exchangeOpenXml;
        set => _exchangeOpenXml = value;
    }

    private const string OpenXmlPackage = """    <PackageReference Include="DocumentFormat.OpenXml" Version="3.1.1" />""";

    /// <summary>True when the site of <paramref name="project"/> exports or imports at least one table.</summary>
    internal static bool UsesImportExport(Project project) => ImportExportRules.IsUsed(project);

    /// <summary>The fields of a generated table copy that an export and an import carry.</summary>
    private static List<FieldInfo> ExchangeFields(TableInfo table) =>
        table.Fields.Where(f => ImportExportRules.Included(f, table.OwnerField, table.ImportExport)).ToList();

    private static bool ExchangeExports(TableInfo table) =>
        ImportExportConfig is { ExportFormats: not DataFormats.None } && table.ImportExport.Export && ExchangeFields(table).Count > 0;

    private static bool ExchangeImports(TableInfo table) =>
        ImportExportConfig is { ImportFormats: not DataFormats.None } && table.ImportExport.Import && ExchangeFields(table).Count > 0;

    /// <summary>True when the generated copy of a table exports or imports, so its Pages class gets the export and import code.</summary>
    internal static bool UsesExchange(TableInfo table) => ExchangeExports(table) || ExchangeImports(table);

    /// <summary>True when a route of the table needs a user level limit, which brings in the Security namespace.</summary>
    private static bool ExchangeNeedsSecurity(TableInfo table) =>
        (ExchangeExports(table) && ExchangeExportLevel(table) > 0) || (ExchangeImports(table) && ExchangeImportLevel(table) > 0);

    /// <summary>An export needs the view level of the table and the export level of the project.</summary>
    private static int ExchangeExportLevel(TableInfo table) => Math.Max(table.ViewMinLevel, ImportExportConfig?.ExportMinLevel ?? 0);

    /// <summary>An import changes rows, so it needs the edit level of the table and the import level of the project.</summary>
    private static int ExchangeImportLevel(TableInfo table) => Math.Max(table.EditMinLevel, ImportExportConfig?.ImportMinLevel ?? 0);

    private static string ExchangeKind(FieldType type) => type switch
    {
        FieldType.Integer => "DataExchange.Kind.Integer",
        FieldType.Decimal => "DataExchange.Kind.Decimal",
        FieldType.Boolean => "DataExchange.Kind.Boolean",
        FieldType.Date => "DataExchange.Kind.Date",
        FieldType.DateTime => "DataExchange.Kind.DateTime",
        _ => "DataExchange.Kind.Text",
    };

    /// <summary>
    /// The lookup an import checks a field against: the lookup of the field when it has one, else the table a single-column foreign key
    /// refers to (the key values only). Null when the field has neither.
    /// </summary>
    private static string? ExchangeLookup(TableInfo table, FieldInfo field)
    {
        if (LookupOf(field) is { } lookup)
        {
            return $"new DataExchange.Lookup({Lit(QuoteIdent(lookup.Table.Trim()))}, {Lit(QuoteIdent(lookup.KeyField.Trim()))}, {Lit(QuoteIdent(lookup.DisplayField.Trim()))}, {Lit(lookup.Filter.Trim())})";
        }

        var foreign = table.ForeignKeys.FirstOrDefault(fk =>
            fk.Columns.Count == 1 && fk.ReferencedColumns.Count == 1 && fk.ReferencedTable.Length > 0
            && string.Equals(fk.Columns[0], field.Name, StringComparison.OrdinalIgnoreCase));
        return foreign is null
            ? null
            : $"new DataExchange.Lookup({Lit(QuoteIdent(foreign.ReferencedTable))}, {Lit(QuoteIdent(foreign.ReferencedColumns[0]))}, \"\", \"\")";
    }

    /// <summary>The routes of the export and the import of a table, written into its Map method.</summary>
    private static void AppendExchangeRoutes(StringBuilder sb, TableInfo table)
    {
        if (ExchangeExports(table))
        {
            var guard = LevelGuard(ExchangeExportLevel(table), ViewDeniedMessage);
            sb.AppendLine($"        app.MapGet(Route + \"/export\", (HttpRequest request) => ExportPage(request, connectionString)){guard};");
        }

        if (ExchangeImports(table))
        {
            var guard = LevelGuard(ExchangeImportLevel(table), EditDeniedMessage);
            sb.AppendLine($"        app.MapGet(Route + \"/import\", (HttpContext context, IAntiforgery antiforgery) => DataExchange.UploadPage(ExchangeSpec(), context, antiforgery)){guard};");
            sb.AppendLine($"        app.MapGet(Route + \"/import/template\", () => DataExchange.Template(ExchangeSpec())){guard};");
            sb.AppendLine($"        app.MapPost(Route + \"/import\", (HttpContext context, IAntiforgery antiforgery) => DataExchange.Upload(ExchangeSpec(), context, antiforgery, connectionString)){guard};");
            sb.AppendLine($"        app.MapPost(Route + \"/import/apply\", (HttpContext context, IAntiforgery antiforgery) => DataExchange.Apply(ExchangeSpec(), context, antiforgery, connectionString)){guard};");
        }
    }

    /// <summary>The export and import buttons of the list page.</summary>
    private static void AppendExchangeToolbar(StringBuilder sb, TableInfo table)
    {
        if (UsesExchange(table))
        {
            sb.AppendLine($"        body.Append(DataExchange.Toolbar(request, ExchangeSpec(), {(ExchangeExports(table) ? "true" : "false")}, {(ExchangeImports(table) ? "true" : "false")}));");
        }
    }

    /// <summary>ExchangeSpec (the columns of the export and the import) and, for an export, ExportPage.</summary>
    private static void AppendExchange(StringBuilder sb, TableInfo table)
    {
        if (!UsesExchange(table))
        {
            return;
        }

        var fields = ExchangeFields(table);
        var settings = table.ImportExport;
        var exportMax = Math.Max(0, settings.ExportMaxRows);
        var upsert = settings.ImportMode == ImportMode.Upsert && table.PrimaryKeys.Count > 0;

        sb.AppendLine("    private static DataExchange.Spec ExchangeSpec() => new(");
        sb.AppendLine("        Route,");
        sb.AppendLine("        Caption,");
        sb.AppendLine("        Table,");
        sb.AppendLine($"        {(IsOwned(table) ? "OwnerColumn" : "\"\"")},");
        sb.AppendLine($"        {(upsert ? "true" : "false")},");
        sb.AppendLine($"        {Lit(exportMax > 0 ? Db.Top(exportMax) : string.Empty)},");
        sb.AppendLine($"        {Lit(exportMax > 0 ? Db.Limit(exportMax) : string.Empty)},");
        sb.AppendLine("        new DataExchange.Col[]");
        sb.AppendLine("        {");
        foreach (var field in fields)
        {
            var ui = field.Ui;
            var properties = new List<string>();
            if (field.IsRequired)
            {
                properties.Add("Required = true");
            }

            if (field.IsPrimaryKey)
            {
                properties.Add("IsKey = true");
            }

            if (field.IsPrimaryKey && field.IsAutoIncrement)
            {
                properties.Add("IsAuto = true");
            }

            if (field.HasDefault)
            {
                properties.Add("HasDefault = true");
            }

            if (field.Type == FieldType.Text)
            {
                if (ui.MaxLength > 0)
                {
                    properties.Add($"MaxLength = {ui.MaxLength}");
                }

                if (ui.MinLength > 0)
                {
                    properties.Add($"MinLength = {ui.MinLength}");
                }

                if (ui.Pattern.Trim().Length > 0)
                {
                    properties.Add($"Pattern = {Lit(ui.Pattern.Trim())}");
                    if (ui.PatternMessage.Trim().Length > 0)
                    {
                        properties.Add($"PatternMessage = {Lit(ui.PatternMessage.Trim())}");
                    }
                }
            }

            if (field.Type is FieldType.Integer or FieldType.Decimal or FieldType.Date or FieldType.DateTime)
            {
                if (ui.Min.Trim().Length > 0)
                {
                    properties.Add($"Min = {Lit(ui.Min.Trim())}");
                }

                if (ui.Max.Trim().Length > 0)
                {
                    properties.Add($"Max = {Lit(ui.Max.Trim())}");
                }
            }

            var lookup = ExchangeLookup(table, field);
            if (lookup is not null)
            {
                properties.Add($"Lookup = {lookup}");
            }
            else if (ControlOf(field) is EditControl.Select or EditControl.Radio && ui.Options.Count > 0)
            {
                properties.Add("Choices = new DataExchange.Choice[] { "
                    + string.Join(", ", ui.Options.Select(o => $"new({Lit(o.Value)}, {Lit(o.Text.Length > 0 ? o.Text : o.Value)})")) + " }");
            }

            var initializer = properties.Count == 0 ? string.Empty : " { " + string.Join(", ", properties) + " }";
            sb.AppendLine($"            new DataExchange.Col({Lit(field.Name)}, {FieldText(field)}, {Lit(QuoteIdent(field.Name))}, {ExchangeKind(field.Type)}){initializer},");
        }

        sb.AppendLine("        });");
        sb.AppendLine();

        if (!ExchangeExports(table))
        {
            return;
        }

        sb.AppendLine("    private static async Task<IResult> ExportPage(HttpRequest request, string connectionString)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (DataExchange.ExportFormat(request.Query[\"format\"]) is not { } format)");
        sb.AppendLine("        {");
        sb.AppendLine("            return Results.BadRequest();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var parameters = new List<KeyValuePair<string, object>>();");
        sb.AppendLine("        var where = BuildWhere(request, parameters);");
        sb.AppendLine("        var order = Order;");
        if (IsCustomTable(table))
        {
            // The same sort the list page applies: a whitelisted column of the request, or the sort levels of the filter builder.
            sb.AppendLine("        var sortIndex = Array.IndexOf(SortFields, request.Query[\"sort\"].ToString());");
            sb.AppendLine("        if (sortIndex >= 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            order = \" ORDER BY \" + SortColumns[sortIndex] + (request.Query[\"dir\"].ToString() == \"desc\" ? \" DESC\" : \" ASC\") + SortTieBreak;");
            sb.AppendLine("        }");
            if (table.Designs.List.FilterBuilder)
            {
                sb.AppendLine("        else if (GridKit.Order(request, SortFields, SortColumns) is { } levels)");
                sb.AppendLine("        {");
                sb.AppendLine("            order = levels + SortTieBreak;");
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        await using var connection = new SqliteConnection(connectionString);");
        sb.AppendLine("        await connection.OpenAsync();");
        sb.AppendLine("        return await DataExchange.Export(ExchangeSpec(), format, connection, where, order, Db.Args(parameters));");
        sb.AppendLine("    }");
        sb.AppendLine();
    }
}
