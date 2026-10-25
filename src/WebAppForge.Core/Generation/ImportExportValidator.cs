using System.Globalization;
using System.Text.RegularExpressions;
using WebAppForge.Core.Model;
using M = WebAppForge.Core.Localization.CoreMessages;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Which fields and tables a generated site exports and imports, and the checks of the settings. The generator refuses a project
/// with any problem; the desktop editor lists the same problems while the settings are edited.
/// </summary>
public static class ImportExportRules
{
    public const int MaxUploadLimitMb = 100;

    private static readonly Regex Token = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the users table of the login security: its password hash and level never leave the site, and it has no import.</summary>
    public static bool IsUserTable(Project project, TableInfo table) =>
        project.Security.Enabled && !project.Security.UsesIdentity && Same(table.Name, project.Security.UserTable);

    /// <summary>
    /// True when a field is part of the export and the import: not secret, not binary, not the owner field (the site sets it),
    /// and not in the table's excluded fields. <paramref name="hidden"/> is true for a field the site hides by security rules.
    /// </summary>
    internal static bool Included(FieldInfo field, string ownerField, TableImportExport settings, bool hidden = false) =>
        !hidden
        && !field.IsSecret
        && field.Type != FieldType.Binary
        && (ownerField.Length == 0 || !Same(field.Name, ownerField))
        && !settings.ExcludedFields.Any(x => Same(x, field.Name));

    /// <summary>The fields a table exports and imports, in table order.</summary>
    public static List<FieldInfo> Fields(Project project, TableInfo table)
    {
        var owner = project.Security.Enabled ? table.OwnerField : string.Empty;
        var userTable = IsUserTable(project, table);
        return table.Fields
            .Where(f => Included(f, owner, table.ImportExport,
                userTable && (Same(f.Name, project.Security.PasswordField) || (project.Security.UserLevelField.Length > 0 && Same(f.Name, project.Security.UserLevelField)))))
            .ToList();
    }

    public static bool CanExport(Project project, TableInfo table) =>
        project.ImportExport.Enabled && table.Generate && table.ImportExport.Export && !Schema.IdentitySchema.IsIdentityTable(project.Security, table.Name)
        && project.ImportExport.ExportFormats != DataFormats.None && Fields(project, table).Count > 0;

    public static bool CanImport(Project project, TableInfo table) =>
        project.ImportExport.Enabled && table.Generate && table.ImportExport.Import && !IsUserTable(project, table) && !Schema.IdentitySchema.IsIdentityTable(project.Security, table.Name)
        && project.ImportExport.ImportFormats != DataFormats.None && Fields(project, table).Count > 0;

    /// <summary>True when the generated site has an export or an import for at least one table.</summary>
    public static bool IsUsed(Project project) =>
        project.ImportExport.Enabled && project.Tables.Any(t => CanExport(project, t) || CanImport(project, t));

    /// <summary>True when the site needs the Open XML SDK package: Excel or Word is offered for an export or an import that exists.</summary>
    public static bool UsesOpenXml(Project project)
    {
        const DataFormats openXml = DataFormats.Xlsx | DataFormats.Word;
        var s = project.ImportExport;
        return s.Enabled
            && ((project.Tables.Any(t => CanExport(project, t)) && (s.ExportFormats & openXml) != 0)
                || (project.Tables.Any(t => CanImport(project, t)) && (s.ImportFormats & openXml) != 0));
    }

    /// <summary>Throws for the first problem of the import and export settings.</summary>
    public static void Validate(Project project)
    {
        var problems = Problems(project);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(problems[0]);
        }
    }

    /// <summary>Every problem of the settings. Nothing is checked while import and export is off.</summary>
    public static List<string> Problems(Project project)
    {
        var problems = new List<string>();
        var s = project.ImportExport;
        if (!s.Enabled)
        {
            return problems;
        }

        static string Prefix(string text) => M.Format("ImportExport_Problem", text);
        if (s.ExportFormats == DataFormats.None && s.ImportFormats == DataFormats.None)
        {
            problems.Add(Prefix(M.Text("ImportExport_NoFormat")));
        }

        if (s.MaxUploadMb < 1 || s.MaxUploadMb > MaxUploadLimitMb)
        {
            problems.Add(Prefix(M.Format("ImportExport_UploadLimit", MaxUploadLimitMb)));
        }

        if (s.DelimiterChar is null)
        {
            problems.Add(Prefix(M.Text("ImportExport_Delimiter")));
        }

        if (!IsUsableDateFormat(s.DateFormat))
        {
            problems.Add(Prefix(M.Format("ImportExport_DateFormat", s.DateFormat)));
        }

        if (FileNameProblem(s.ExportFileNamePattern) is { } nameProblem)
        {
            problems.Add(Prefix(nameProblem));
        }

        if (s.ExportMinLevel < 0 || s.ImportMinLevel < 0)
        {
            problems.Add(Prefix(M.Text("ImportExport_NegativeLevel")));
        }

        foreach (var table in project.Tables.Where(t => t.Generate))
        {
            var settings = table.ImportExport;
            if (!settings.Export && !settings.Import)
            {
                continue;
            }

            void Add(string text) => problems.Add(M.Format("ImportExport_TableProblem", table.Name, text));
            if (settings.ExportMaxRows < 0)
            {
                Add(M.Text("ImportExport_MaxRows"));
            }

            foreach (var excluded in settings.ExcludedFields.Where(x => !table.Fields.Any(f => Same(f.Name, x))))
            {
                Add(M.Format("ImportExport_ExcludedMissing", excluded));
            }

            if (!CanImport(project, table))
            {
                continue;
            }

            var fields = Fields(project, table);
            if (settings.ImportMode == ImportMode.Upsert)
            {
                if (table.PrimaryKeys.Count == 0)
                {
                    Add(M.Text("ImportExport_UpsertNoKey"));
                }

                foreach (var key in table.PrimaryKeys.Where(k => !fields.Contains(k)))
                {
                    Add(M.Format("ImportExport_KeyLeftOut", key.Name));
                }
            }

            var owner = project.Security.Enabled ? table.OwnerField : string.Empty;
            foreach (var field in table.Fields.Where(f =>
                f.IsRequired && !f.HasDefault && !f.IsAutoIncrement && !fields.Contains(f) && (owner.Length == 0 || !Same(f.Name, owner))))
            {
                Add(M.Format("ImportExport_RequiredLeftOut", field.Name));
            }
        }

        return problems;
    }

    /// <summary>True when <paramref name="format"/> writes a date that it also reads back.</summary>
    public static bool IsUsableDateFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        try
        {
            var sample = new DateTime(2024, 12, 31);
            var text = sample.ToString(format, CultureInfo.InvariantCulture);
            return DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed.Date == sample;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The problem of a file name pattern, or null when it is fine.</summary>
    public static string? FileNameProblem(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return M.Text("ImportExport_FileNameEmpty");
        }

        foreach (Match match in Token.Matches(pattern))
        {
            if (match.Value is not ("{table}" or "{date}" or "{datetime}"))
            {
                return M.Format("ImportExport_FileNamePart", match.Value);
            }
        }

        var plain = Token.Replace(pattern, "x");
        if (plain.Contains('{') || plain.Contains('}'))
        {
            return M.Text("ImportExport_FileNameBrace");
        }

        if (plain.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || plain.Contains('/') || plain.Contains('\\'))
        {
            return M.Text("ImportExport_FileNameCharacter");
        }

        return plain.Any(char.IsLetterOrDigit) ? null : M.Text("ImportExport_FileNameLetter");
    }
}
