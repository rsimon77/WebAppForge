using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>A text of the project that can be translated: its key, the source text and the language that text is written in.</summary>
public sealed record SourceText(string Key, string Text, string Language);

/// <summary>Which translations were made, kept or skipped by one <see cref="TranslationService"/> run.</summary>
/// <param name="BuiltIn">Texts left alone because a built-in translation pack covers them (see <see cref="TranslationPacks"/>).</param>
public sealed record TranslationResult(int Translated, int UpToDate, int Protected, int BuiltIn = 0);

/// <summary>Lists every text of a project that goes through the translation store.</summary>
public static class LocalizationKeys
{
    public static string Table(string table) => "table." + table;

    public static string Field(string table, string field) => "field." + table + "." + field;

    public static string Report(string report) => "report." + report;

    /// <summary>Key of the custom heading of the aggregate at <paramref name="index"/> of a summary report.</summary>
    public static string ReportMeasure(string report, int index) => "report." + report + ".m" + index;

    public static string ReportDescription(string report) => "report." + report + ".description";

    /// <summary>The title of the home page.</summary>
    public const string SiteTitle = "site.title";

    /// <summary>The footer text of every page.</summary>
    public const string SiteFooter = "site.footer";

    /// <summary>A menu group heading; tables and reports that share a group share its text.</summary>
    public static string MenuGroup(string group) => "menu." + group;

    /// <summary>A text of the design of a page: title, subtitle, header, footer, submit, cancel, success, addButton or empty.</summary>
    public static string Page(string table, string page, string part) => "page." + table + "." + page + "." + part;

    /// <summary>A text of a field's settings: help, placeholder, tooltip, required, pattern, true or false.</summary>
    public static string FieldUi(string table, string field, string part) => "fieldui." + table + "." + field + "." + part;

    /// <summary>The text of a static option of a field, by its stored value.</summary>
    public static string FieldOption(string table, string field, string value) => FieldUi(table, field, "option." + value);

    /// <summary>The text of a badge rule of a field, by its position.</summary>
    public static string FieldBadge(string table, string field, int index) => FieldUi(table, field, "badge" + index);

    /// <summary>The legend of a group of fields on the forms of a table.</summary>
    public static string FieldGroup(string table, string group) => "group." + table + "." + group;

    /// <summary>A text of a page of a multi-page form: caption or description.</summary>
    public static string FormPage(string table, string page, string part) => "formpage." + table + "." + page + "." + part;

    /// <summary>The caption of the detail link at <paramref name="index"/> of a master table.</summary>
    public static string Detail(string master, int index) => "detail." + master + "." + index;

    /// <summary>
    /// What a key is, for the editor: "UI" (a fixed text), "Table", "Field", "Report", "Site", "Menu", "Page", "Field setting",
    /// "Field group", "Form page" or "Detail".
    /// </summary>
    public static string KindOf(string key)
    {
        var prefix = key.Split('.', 2)[0];
        return prefix switch
        {
            "table" => "Table",
            "field" => "Field",
            "report" => "Report",
            "site" => "Site",
            "menu" => "Menu",
            "page" => "Page",
            "fieldui" => "Field setting",
            "group" => "Field group",
            "formpage" => "Form page",
            "detail" => "Detail",
            _ => "UI",
        };
    }

    /// <summary>The page kinds whose design texts are enumerated, in the order of <c>PageDesigns</c>.</summary>
    private static readonly string[] DesignPages = ["list", "view", "add", "copy", "edit", "delete", "search"];

    /// <summary>
    /// The texts the project owner wrote for the generated site beyond table and field captions: site title and footer, menu
    /// groups, page designs, field settings, field groups, multi-page form pages, detail captions and report descriptions.
    /// Only non-empty texts of generated tables and reports are listed. Header and footer HTML is translated as HTML.
    /// </summary>
    public static IEnumerable<SourceText> ProjectTexts(Project project)
    {
        var language = project.Localization.DefaultLanguage;
        SourceText Text(string key, string text) => new(key, text, language);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SourceText>();
        void Add(string key, string? text)
        {
            var trimmed = text?.Trim() ?? string.Empty;
            if (trimmed.Length > 0 && seen.Add(key))
            {
                result.Add(Text(key, trimmed));
            }
        }

        if (project.AspNet.IncludeHomePage)
        {
            Add(SiteTitle, project.AspNet.Title);
        }

        Add(SiteFooter, project.Meta.FooterText);
        var tables = project.Tables.Where(t => t.Generate && !Generation.Templates.IsAuditLogOf(project, t)).ToList();
        foreach (var table in tables.Where(t => t.Ui.ShowInMenu))
        {
            Add(MenuGroup(table.Ui.MenuGroup.Trim()), table.Ui.MenuGroup);
        }

        foreach (var report in project.Reports.Where(r => r.Generate))
        {
            Add(MenuGroup(report.MenuGroup.Trim()), report.MenuGroup);
            Add(ReportDescription(report.Name), report.Description);
        }

        foreach (var table in tables)
        {
            if (Generation.Templates.Designed(table))
            {
                foreach (var page in DesignPages)
                {
                    var design = Generation.Templates.DesignOf(table, page);
                    Add(Page(table.Name, page, "title"), design.Title);
                    Add(Page(table.Name, page, "subtitle"), design.Subtitle);
                    Add(Page(table.Name, page, "header"), design.HeaderHtml);
                    Add(Page(table.Name, page, "footer"), design.FooterHtml);
                    Add(Page(table.Name, page, "submit"), design.SubmitText);
                    Add(Page(table.Name, page, "cancel"), design.CancelText);
                    if (page is "add" or "copy" or "edit" or "delete")
                    {
                        Add(Page(table.Name, page, "success"), design.SuccessMessage);
                    }

                    if (page == "list")
                    {
                        Add(Page(table.Name, page, "addButton"), design.AddButtonText);
                        Add(Page(table.Name, page, "empty"), design.EmptyText);
                    }
                }
            }

            foreach (var field in table.Fields.Where(Generation.Templates.IsCustom))
            {
                var ui = field.Ui;
                Add(FieldUi(table.Name, field.Name, "help"), ui.HelpText);
                Add(FieldUi(table.Name, field.Name, "placeholder"), ui.Placeholder);
                Add(FieldUi(table.Name, field.Name, "tooltip"), ui.Tooltip);
                Add(FieldUi(table.Name, field.Name, "required"), ui.RequiredMessage);
                Add(FieldUi(table.Name, field.Name, "pattern"), ui.PatternMessage);
                Add(FieldUi(table.Name, field.Name, "true"), ui.TrueText);
                Add(FieldUi(table.Name, field.Name, "false"), ui.FalseText);
                if (ui.Lookup is null)
                {
                    foreach (var option in ui.Options)
                    {
                        Add(FieldOption(table.Name, field.Name, option.Value), option.Text);
                    }
                }

                for (var i = 0; i < ui.BadgeRules.Count; i++)
                {
                    Add(FieldBadge(table.Name, field.Name, i), ui.BadgeRules[i].Text);
                }

                Add(FieldGroup(table.Name, ui.Group), ui.Group);
            }

            foreach (var page in table.FormPages)
            {
                var name = page.Name.Trim();
                Add(FormPage(table.Name, name, "caption"), page.Caption.Trim().Length > 0 ? page.Caption : name);
                Add(FormPage(table.Name, name, "description"), page.Description);
            }

            for (var i = 0; i < table.Details.Count; i++)
            {
                Add(Detail(table.Name, i), table.Details[i].Caption);
            }
        }

        return result;
    }

    /// <summary>Adds the fixed texts of a feature the site uses. A feature that is not used adds nothing, so its store stays as it was.</summary>
    private static void AddGroup(List<SourceText> result, IReadOnlyList<UiString> group, bool used)
    {
        if (!used)
        {
            return;
        }

        foreach (var ui in group)
        {
            result.Add(new SourceText(ui.Key, ui.English, "en"));
        }
    }

    /// <summary>
    /// The fixed UI strings (English), then the caption of each generated table and each of its fields (default language).
    /// An empty caption stands for the name, as in the generated pages.
    /// </summary>
    public static IReadOnlyList<SourceText> Enumerate(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var defaultLanguage = project.Localization.DefaultLanguage;
        var result = new List<SourceText>();
        foreach (var ui in UiStrings.All)
        {
            result.Add(new SourceText(ui.Key, ui.English, "en"));
        }

        foreach (var table in project.Tables.Where(t => t.Generate && !Generation.Templates.IsAuditLogOf(project, t)))
        {
            result.Add(new SourceText(Table(table.Name), string.IsNullOrEmpty(table.Caption) ? table.Name : table.Caption, defaultLanguage));
            foreach (var field in table.Fields)
            {
                result.Add(new SourceText(Field(table.Name, field.Name), string.IsNullOrEmpty(field.Caption) ? field.Name : field.Caption, defaultLanguage));
            }
        }

        if (Generation.Templates.UsesGridKit(project.Tables.Where(t => t.Generate)))
        {
            foreach (var ui in UiStrings.Grid)
            {
                result.Add(new SourceText(ui.Key, ui.English, "en"));
            }
        }

        if (project.Audit.Enabled)
        {
            foreach (var ui in UiStrings.Audit)
            {
                result.Add(new SourceText(ui.Key, ui.English, "en"));
            }
        }

        if (Generation.Templates.UsesImportExport(project))
        {
            foreach (var ui in UiStrings.ImportExport)
            {
                result.Add(new SourceText(ui.Key, ui.English, "en"));
            }
        }

        if (project.Security.UsesIdentity)
        {
            foreach (var ui in UiStrings.Identity)
            {
                result.Add(new SourceText(ui.Key, ui.English, "en"));
            }
        }

        result.AddRange(ProjectTexts(project));

        var generated = project.Tables.Where(t => t.Generate).ToList();
        AddGroup(result, UiStrings.Checks, generated.Any(t => t.Fields.Any(Generation.Templates.IsCustom)));
        AddGroup(result, UiStrings.PageKit, generated.Any(Generation.Templates.Designed) || Generation.Templates.UsesMasterDetail(generated));
        AddGroup(result, UiStrings.FormPages, generated.Any(Generation.Templates.UsesFormPages));
        AddGroup(result, UiStrings.Details, Generation.Templates.UsesMasterDetail(generated));
        AddGroup(result, UiStrings.Denied, project.Security.HasUserLevels);

        var reports = project.Reports.Where(r => r.Generate).ToList();
        if (reports.Count > 0)
        {
            foreach (var ui in UiStrings.Reports)
            {
                result.Add(new SourceText(ui.Key, ui.English, "en"));
            }

            foreach (var report in reports)
            {
                result.Add(new SourceText(Report(report.Name), string.IsNullOrEmpty(report.Caption) ? report.Name : report.Caption, defaultLanguage));
                for (var i = 0; i < report.Aggregates.Count; i++)
                {
                    if (report.Kind == ReportKind.Summary && report.Aggregates[i].Caption.Length > 0)
                    {
                        result.Add(new SourceText(ReportMeasure(report.Name, i), report.Aggregates[i].Caption, defaultLanguage));
                    }
                }
            }
        }

        return result;
    }
}

/// <summary>Creates the provider named in the project settings.</summary>
public static class TranslationProviders
{
    /// <summary>The provider for the kind, or null for <see cref="TranslationProviderKind.None"/>.</summary>
    public static ITranslationProvider? Create(TranslationProviderKind kind, HttpClient client, IApiKeyProvider keys, string? claudeModel = null) => kind switch
    {
        TranslationProviderKind.Google => new GoogleTranslationProvider(client, keys),
        TranslationProviderKind.Claude => new ClaudeTranslationProvider(client, keys, claudeModel),
        _ => null,
    };
}

/// <summary>
/// Fills the translation store through a provider. This is explicit opt-in: <see cref="TranslateAsync"/> refuses to run unless
/// <see cref="LocalizationSettings.AutoTranslate"/> is set, and nothing else in this library calls a provider.
/// An entry is translated when it is missing, or when it came from a provider, is not reviewed, and its source text changed
/// (compared by <see cref="TranslationEntry.SourceHash"/>). Reviewed entries and manual entries are never overwritten.
/// A missing fixed text that a built-in pack translates (and that is not stale) is not sent.
/// </summary>
public sealed class TranslationService
{
    private readonly ITranslationProvider _provider;

    public TranslationService(ITranslationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <param name="project">The project whose <c>Localization.Entries</c> are updated in place.</param>
    /// <param name="targetLanguages">Languages to fill; null means every configured language.</param>
    public async Task<TranslationResult> TranslateAsync(Project project, IEnumerable<string>? targetLanguages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = project.Localization;
        if (!settings.AutoTranslate)
        {
            throw new InvalidOperationException("Automatic translation is not enabled for this project (Localization.AutoTranslate is off).");
        }

        var targets = (targetLanguages ?? settings.AllLanguages)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sources = LocalizationKeys.Enumerate(project);
        int translated = 0, upToDate = 0, protectedCount = 0, builtIn = 0;

        foreach (var target in targets)
        {
            // Pending texts are grouped by source language so one request translates one language pair.
            foreach (var group in sources.Where(s => !SameLanguage(s.Language, target)).GroupBy(s => s.Language, StringComparer.OrdinalIgnoreCase))
            {
                var pending = new List<SourceText>();
                foreach (var source in group)
                {
                    var existing = settings.Find(source.Key, target);
                    if (existing is null && TranslationPacks.Find(source.Key, target) is not null)
                    {
                        // A fresh built-in translation is used as it is; nothing is sent for it.
                        builtIn++;
                    }
                    else if (existing is null)
                    {
                        pending.Add(source);
                    }
                    else if (existing.Reviewed || existing.Source == TranslationSource.Manual)
                    {
                        protectedCount++;
                    }
                    else if (existing.SourceHash == TranslationEntry.HashOf(source.Text))
                    {
                        upToDate++;
                    }
                    else
                    {
                        pending.Add(source);
                    }
                }

                if (pending.Count == 0)
                {
                    continue;
                }

                var results = await _provider.TranslateAsync(
                    pending.Select(p => p.Text).ToList(), group.Key, target, NotesOrNull(settings), cancellationToken).ConfigureAwait(false);
                if (results.Count != pending.Count)
                {
                    throw new TranslationException($"The translation provider returned {results.Count} texts for {pending.Count} requested.");
                }

                for (var i = 0; i < pending.Count; i++)
                {
                    settings.Entries.RemoveAll(e => e.Key == pending[i].Key && string.Equals(e.Language, target, StringComparison.OrdinalIgnoreCase));
                    settings.Entries.Add(new TranslationEntry
                    {
                        Key = pending[i].Key,
                        Language = target,
                        Text = results[i],
                        Source = _provider.Source,
                        Reviewed = false,
                        SourceHash = TranslationEntry.HashOf(pending[i].Text),
                    });
                    translated++;
                }
            }
        }

        return new TranslationResult(translated, upToDate, protectedCount, builtIn);
    }

    private static string? NotesOrNull(LocalizationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.TranslationNotes) ? null : settings.TranslationNotes;

    private static bool SameLanguage(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
