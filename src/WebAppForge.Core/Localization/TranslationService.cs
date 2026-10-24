using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>A text of the project that can be translated: its key, the source text and the language that text is written in.</summary>
public sealed record SourceText(string Key, string Text, string Language);

/// <summary>Which translations were made, kept or skipped by one <see cref="TranslationService"/> run.</summary>
public sealed record TranslationResult(int Translated, int UpToDate, int Protected);

/// <summary>Lists every text of a project that goes through the translation store.</summary>
public static class LocalizationKeys
{
    public static string Table(string table) => "table." + table;

    public static string Field(string table, string field) => "field." + table + "." + field;

    public static string Report(string report) => "report." + report;

    /// <summary>Key of the custom heading of the aggregate at <paramref name="index"/> of a summary report.</summary>
    public static string ReportMeasure(string report, int index) => "report." + report + ".m" + index;

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

        var generated = project.Tables.Where(t => t.Generate).ToList();
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
        int translated = 0, upToDate = 0, protectedCount = 0;

        foreach (var target in targets)
        {
            // Pending texts are grouped by source language so one request translates one language pair.
            foreach (var group in sources.Where(s => !SameLanguage(s.Language, target)).GroupBy(s => s.Language, StringComparer.OrdinalIgnoreCase))
            {
                var pending = new List<SourceText>();
                foreach (var source in group)
                {
                    var existing = settings.Find(source.Key, target);
                    if (existing is null)
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

        return new TranslationResult(translated, upToDate, protectedCount);
    }

    private static string? NotesOrNull(LocalizationSettings settings) =>
        string.IsNullOrWhiteSpace(settings.TranslationNotes) ? null : settings.TranslationNotes;

    private static bool SameLanguage(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
