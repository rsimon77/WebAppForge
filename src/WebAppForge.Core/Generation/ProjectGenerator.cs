using System.Text;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Generates an ASP.NET Core 10 web project from a <see cref="Project"/>: server-rendered list, view,
/// add, copy, edit, delete and search pages per table, plus a JSON API. Output comes from the templates
/// in <see cref="Templates"/> only; no third-party template set is used.
/// </summary>
public sealed class ProjectGenerator
{
    private const string SqliteVersion = "10.0.13";

    /// <summary>
    /// Writes the generated files under <paramref name="outputDirectory"/> and returns their relative paths (forward slashes).
    /// <paramref name="projectFolder"/> is the folder that relative database paths resolve against.
    /// </summary>
    public IReadOnlyList<string> Generate(Project project, string outputDirectory, string projectFolder)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        ArgumentException.ThrowIfNullOrEmpty(projectFolder);

        if (!string.Equals(project.Database.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Provider '{project.Database.Provider}' is not supported yet. Only SQLite is implemented.");
        }

        // Localization is off by default; the templates then produce the same output as before it existed.
        var localized = project.Localization.Enabled;
        if (localized)
        {
            Localization.LocalizedSite.Validate(project.Localization);
        }

        MasterDetailValidator.Validate(project.Tables);

        // Reports with errors would generate a site that does not compile or shows wrong data, so the first error stops generation.
        var reportError = Reports.ReportValidator.Validate(project).FirstOrDefault(i => i.Severity == Reports.ReportIssueSeverity.Error);
        if (reportError is not null)
        {
            throw new InvalidOperationException(reportError.Message);
        }

        Templates.Localized = localized;
        var ns = Identifier.Pascal(project.Name);
        var tables = project.Tables.Where(t => t.Generate).ToList();
        var copies = tables.Select(t => GeneratedCopy(t, project.Security)).ToList();
        Templates.MdTables = copies;
        var files = new Dictionary<string, string>
        {
            [$"{ns}.csproj"] = Templates.Csproj(SqliteVersion, project.Meta),
            ["Program.cs"] = Templates.Program(ns, project.AspNet, tables, project.Security, project.Meta, project.Reports),
            ["appsettings.json"] = Templates.AppSettings(project.Database.ResolveConnectionString(projectFolder), project.Meta),
            ["Web/Html.cs"] = Templates.HtmlHelper(ns, project.Meta, tables, project.Reports),
        };

        // The runtime for customized fields and tables is only part of sites that use it.
        if (tables.Any(Templates.IsCustomTable))
        {
            files["Web/Ui.cs"] = Templates.UiRuntime(ns);
        }

        // The page runtime (partial pages, form layouts, flash messages) is only part of sites with a designed page.
        if (tables.Any(Templates.Designed) || copies.Any(Templates.UsesPreviewContainers))
        {
            files["Web/PageKit.cs"] = Templates.PageKitRuntime(ns);
        }

        // Multi-page forms need their own runtime, written only when a table splits a page into pages.
        if (tables.Any(Templates.UsesFormPages))
        {
            files["Web/FormPages.cs"] = Templates.FormPagesRuntime(ns);
        }

        // Master/detail helpers and referential integrity are only part of sites with detail links.
        if (Templates.UsesMasterDetail(copies))
        {
            files["Web/MasterDetailKit.cs"] = Templates.MasterDetailKitFile(ns);
        }

        if (Templates.UsesReferentialIntegrity(copies))
        {
            files["Web/ReferentialIntegrity.cs"] = Templates.ReferentialIntegrityFile(ns, copies);
        }

        // Reports need the engine (SQL and result shaping) and the pages; sites without reports get neither.
        if (project.Reports.Count > 0)
        {
            files["Web/ReportEngine.cs"] = TemplatesReports.EngineFile(ns);
            files["Web/Reports.cs"] = TemplatesReports.ReportsFile(project, ns);
        }

        if (Templates.EnvironmentName(project.Meta) is { } environment)
        {
            files["Properties/launchSettings.json"] = Templates.LaunchSettings(ns, project.Meta, environment);
        }

        for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            var table = tables[tableIndex];
            // SQLite assigns rowids only to a single INTEGER PRIMARY KEY, so a composite key cannot auto-increment.
            if (table.PrimaryKeys.Count > 1 && table.PrimaryKeys.Any(k => k.IsAutoIncrement))
            {
                throw new NotSupportedException($"Table '{table.Name}' has a composite key with an auto-increment column, which SQLite does not support.");
            }

            if (project.Security.Enabled && !string.IsNullOrEmpty(table.OwnerField))
            {
                ValidateOwnerField(table);
            }

            var generatedTable = copies[tableIndex];
            var className = Identifier.Pascal(table.Name);
            files[$"Models/{className}.cs"] = Templates.Model(ns, className, generatedTable);
            files[$"Endpoints/{className}Endpoints.cs"] = Templates.Endpoints(ns, className, generatedTable);
            files[$"Pages/{className}Pages.cs"] = Templates.Pages(ns, className, generatedTable);
        }

        if (project.Security.Enabled)
        {
            AddLoginFiles(files, ns, project.Security, tables);
        }

        Templates.Localized = false;
        Templates.MdTables = [];
        if (localized)
        {
            foreach (var (path, content) in Localization.LocalizedSite.Files(ns, project))
            {
                files[path] = content;
            }
        }

        var written = new List<string>();
        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(outputDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            written.Add(relativePath);
        }

        CopyAsset(project.Meta.FaviconPath, "favicon", Templates.FaviconFile(project.Meta), outputDirectory, projectFolder, written);
        CopyAsset(project.Meta.LogoPath, "logo", Templates.LogoFile(project.Meta), outputDirectory, projectFolder, written);
        return written;
    }

    /// <summary>Copies the favicon or logo of the project metadata to wwwroot. The path is relative to the project file folder.</summary>
    private static void CopyAsset(string path, string what, string fileName, string outputDirectory, string projectFolder, List<string> written)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var source = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(projectFolder, path));
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"The {what} file '{path}' was not found (looked for '{source}').", source);
        }

        var relative = "wwwroot/" + fileName;
        var target = Path.Combine(outputDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        written.Add(relative);
    }

    /// <summary>
    /// The owner field of a table with User ID security must exist and hold the user name as text.
    /// </summary>
    private static void ValidateOwnerField(TableInfo table)
    {
        var owner = table.Fields.FirstOrDefault(f => string.Equals(f.Name, table.OwnerField, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"User ID security is enabled for table '{table.Name}' but it has no owner field '{table.OwnerField}'.");
        if (owner.Type != FieldType.Text)
        {
            throw new InvalidOperationException($"The owner field '{owner.Name}' in table '{table.Name}' must be a text column to hold the user name, but it is {owner.Type}.");
        }
    }

    /// <summary>
    /// The copy of a table that is generated. The users table has its password field hidden from the HTML pages
    /// (list, view, search and forms). A table with User ID security has its owner field hidden the same way; the
    /// owner is set by the generated code, never by the user. Both are hidden only when security is enabled.
    /// The user level field of the user table is hidden the same way, so a signed-in user cannot raise a level through
    /// a form: BindForm binds only visible fields, so a posted level is ignored.
    /// The JSON API still returns every column, so the hash remains visible there; that is a known gap.
    /// The project's own settings are left unchanged; only the generated copy differs.
    /// </summary>
    private static TableInfo GeneratedCopy(TableInfo table, SecuritySettings security)
    {
        var passwordHidden = security.Enabled && string.Equals(table.Name, security.UserTable, StringComparison.OrdinalIgnoreCase);
        var ownerField = security.Enabled && !string.IsNullOrEmpty(table.OwnerField)
            ? table.Fields.FirstOrDefault(f => string.Equals(f.Name, table.OwnerField, StringComparison.OrdinalIgnoreCase))
            : null;

        // Level limits only apply when User Level security is on; otherwise the generated copy carries no limits.
        var levelSecured = HasUserLevelSecurity(security);
        var levelHidden = levelSecured && string.Equals(table.Name, security.UserTable, StringComparison.OrdinalIgnoreCase);

        var copy = new TableInfo
        {
            Name = table.Name,
            Caption = table.Caption,
            Generate = table.Generate,
            Filter = table.Filter,
            Pages = table.Pages,
            Ui = table.Ui,
            Designs = table.Designs,
            OwnerField = ownerField?.Name ?? string.Empty,
            ViewMinLevel = levelSecured ? table.ViewMinLevel : 0,
            EditMinLevel = levelSecured ? table.EditMinLevel : 0,
        };
        copy.Sort.AddRange(table.Sort);
        copy.Details.AddRange(table.Details);
        copy.ForeignKeys.AddRange(table.ForeignKeys);
        copy.FormPages.AddRange(table.FormPages);

        foreach (var field in table.Fields)
        {
            var isPassword = passwordHidden && string.Equals(field.Name, security.PasswordField, StringComparison.OrdinalIgnoreCase);
            var isOwner = ownerField is not null && ReferenceEquals(field, ownerField);
            var isLevel = levelHidden && string.Equals(field.Name, security.UserLevelField, StringComparison.OrdinalIgnoreCase);
            if (!isPassword && !isOwner && !isLevel)
            {
                copy.Fields.Add(field);
                continue;
            }

            copy.Fields.Add(new FieldInfo
            {
                Name = field.Name,
                Caption = field.Caption,
                Type = field.Type,
                IsPrimaryKey = field.IsPrimaryKey,
                IsAutoIncrement = field.IsAutoIncrement,
                IsRequired = field.IsRequired,
                HasDefault = field.HasDefault,
                KeyPosition = field.KeyPosition,
                ShowInList = false,
                ShowInView = false,
                ShowInAdd = false,
                ShowInEdit = false,
                Searchable = false,
                IsSecret = field.IsSecret || isPassword || isLevel,
            });
        }

        return copy;
    }

    /// <summary>True when User Level security is on: login is enabled and a user level field is named.</summary>
    private static bool HasUserLevelSecurity(SecuritySettings security) =>
        security.Enabled && !string.IsNullOrEmpty(security.UserLevelField);

    /// <summary>
    /// Adds the login files. The user table must be one of the generated tables, otherwise the site would be
    /// generated without any login and the failure is reported instead.
    /// </summary>
    private static void AddLoginFiles(Dictionary<string, string> files, string ns, SecuritySettings security, IReadOnlyList<TableInfo> tables)
    {
        var users = tables.FirstOrDefault(t => string.Equals(t.Name, security.UserTable, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Security is enabled but the user table '{security.UserTable}' is not one of the generated tables.");
        var userName = users.Fields.FirstOrDefault(f => string.Equals(f.Name, security.UserNameField, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Security is enabled but table '{users.Name}' has no user name field '{security.UserNameField}'.");
        var password = users.Fields.FirstOrDefault(f => string.Equals(f.Name, security.PasswordField, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Security is enabled but table '{users.Name}' has no password field '{security.PasswordField}'.");
        if (password.Type != FieldType.Text)
        {
            throw new InvalidOperationException($"The password field '{password.Name}' in table '{users.Name}' must be a text column to hold the password hash, but it is {password.Type}.");
        }

        if (HasUserLevelSecurity(security))
        {
            var level = users.Fields.FirstOrDefault(f => string.Equals(f.Name, security.UserLevelField, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"User level security is enabled but table '{users.Name}' has no user level field '{security.UserLevelField}'.");
            if (level.Type != FieldType.Integer)
            {
                throw new InvalidOperationException($"The user level field '{level.Name}' in table '{users.Name}' must be an integer column, but it is {level.Type}.");
            }

            files["Security/Permissions.cs"] = AuthTemplates.Permissions(ns);
        }

        files["Security/Passwords.cs"] = AuthTemplates.Passwords(ns);
        if (security.TrackFailedAttempts)
        {
            files["Security/LoginAttempts.cs"] = AuthTemplates.LoginAttempts(ns);
        }

        files["Pages/AuthPages.cs"] = AuthTemplates.AuthPages(ns, users.Name, userName.Name, password.Name, security);
        if (security.AllowRegistration || security.AllowChangePassword)
        {
            files["Pages/UserAccountPages.cs"] = AuthTemplates.UserAccountPages(ns, users, userName.Name, password.Name, security);
        }
    }
}
