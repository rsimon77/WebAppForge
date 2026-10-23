using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The Languages tab: multi-language settings, the list of languages, optional automatic translation and the grid of
/// translations. Every value reads and writes <see cref="Project.Localization"/> directly, so Save persists the edits and
/// the project becomes dirty. Nothing is sent to a translation service unless automatic translation is on, a provider is
/// chosen and its key is set, and the user confirmed the first translation.
/// </summary>
public partial class LanguagesViewModel : EditorViewModelBase
{
    /// <summary>The language filter entry that shows every language.</summary>
    public const string AllLanguagesLabel = "All languages";

    private readonly MainViewModel _main;
    private List<TranslationRow> _allRows = [];
    private ObservableCollection<TranslationRow> _rows = [];
    private IReadOnlyList<TranslationRow> _selectedRows = [];
    private IReadOnlyList<string> _visibleLanguages = [];
    private CancellationTokenSource? _cancel;
    private TranslationProviderKind? _confirmedProvider;
    private readonly HashSet<string> _confirmedLanguages = new(StringComparer.OrdinalIgnoreCase);

    private string _newLanguageText = string.Empty;
    private LanguageRow? _selectedLanguage;
    private string _filterLanguage = AllLanguagesLabel;
    private bool _onlyMissing;
    private bool _onlyUnreviewed;
    private bool _onlyStale;
    private string _search = string.Empty;
    private bool _isTranslating;
    private int _progressValue;
    private int _progressMaximum = 1;
    private string _translateMessage = string.Empty;
    private string _resultSummary = string.Empty;

    public LanguagesViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += OnMainChanged;
    }

    private LocalizationSettings Settings => _main.Current.Localization;

    private ITranslationProviderFactory ProviderFactory
        => _main.Tools.TranslationProviders ?? new DefaultTranslationProviderFactory(() => _main.Settings, _main.Tools.ApiKeys);

    /// <summary>Raised when the set of language columns of the grid changed, so the view rebuilds its columns.</summary>
    public event EventHandler? ColumnsChanged;

    // ---- Settings ----

    public bool Enabled
    {
        get => Settings.Enabled;
        set => Put(Settings.Enabled, value, v => Settings.Enabled = v);
    }

    public string DefaultLanguage
    {
        get => Settings.DefaultLanguage;
        set
        {
            // A combo box reports null while its item list is rebuilt; that is not a choice.
            if (value is null)
            {
                return;
            }

            if (!LanguageCatalog.TryNormalize(value, out var code, out var error))
            {
                Message = error;
                OnPropertyChanged();
                return;
            }

            if (string.Equals(code, Settings.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // The old default stays a language of the site; the new one is no longer listed twice.
            var old = Settings.DefaultLanguage;
            Settings.Languages.RemoveAll(l => string.Equals(l, code, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(old) && !Settings.Languages.Contains(old, StringComparer.OrdinalIgnoreCase))
            {
                Settings.Languages.Add(old);
            }

            Settings.DefaultLanguage = code;
            Message = string.Empty;
            OnPropertyChanged();
            RebuildAll();
        }
    }

    public bool AutoTranslate
    {
        get => Settings.AutoTranslate;
        set
        {
            if (Put(Settings.AutoTranslate, value, v => Settings.AutoTranslate = v))
            {
                OnPropertyChanged(nameof(CanTranslate));
            }
        }
    }

    public IReadOnlyList<TranslationProviderKind> ProviderChoices { get; } =
        [TranslationProviderKind.None, TranslationProviderKind.Google, TranslationProviderKind.Claude];

    public TranslationProviderKind Provider
    {
        get => Settings.TranslationProvider;
        set
        {
            if (Put(Settings.TranslationProvider, value, v => Settings.TranslationProvider = v))
            {
                OnPropertyChanged(nameof(ProviderStatus));
                OnPropertyChanged(nameof(IsProviderReady));
                OnPropertyChanged(nameof(CanTranslate));
            }
        }
    }

    public string TranslationNotes
    {
        get => Settings.TranslationNotes;
        set => Put(Settings.TranslationNotes, value ?? string.Empty, v => Settings.TranslationNotes = v);
    }

    /// <summary>Says whether the environment variable of the chosen provider is set. The key itself is never shown.</summary>
    public string ProviderStatus
    {
        get
        {
            var kind = Settings.TranslationProvider;
            if (kind == TranslationProviderKind.None)
            {
                return "No translation service chosen. Choose Google or Claude to translate automatically.";
            }

            var variable = EnvironmentApiKeyProvider.VariableFor(kind);
            return KeyIsSet(kind)
                ? $"{variable} is set. The key is read from the environment and is never saved in the project."
                : $"{variable} is not set. Set it as an environment variable and restart the editor.";
        }
    }

    public bool IsProviderReady => Settings.TranslationProvider != TranslationProviderKind.None && KeyIsSet(Settings.TranslationProvider);

    /// <summary>True when automatic translation could start: it is on, a provider is chosen and its key is set.</summary>
    public bool CanTranslate => Settings.AutoTranslate && IsProviderReady && !IsTranslating;

    private bool KeyIsSet(TranslationProviderKind kind) => !string.IsNullOrWhiteSpace(_main.Tools.ApiKeys.GetApiKey(kind));

    // ---- Languages ----

    public ObservableCollection<LanguageRow> Languages { get; } = [];

    /// <summary>The language codes of the site, default first. Source of the default picker.</summary>
    public ObservableCollection<string> LanguageCodes { get; } = [];

    /// <summary>"All languages" and then the codes. Source of the language filter.</summary>
    public ObservableCollection<string> FilterChoices { get; } = [];

    public LanguageRow? SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    public string NewLanguageText
    {
        get => _newLanguageText;
        set => SetProperty(ref _newLanguageText, value ?? string.Empty);
    }

    /// <summary>Adds the language typed in <see cref="NewLanguageText"/>.</summary>
    [RelayCommand]
    public void AddLanguage()
    {
        if (AddLanguageCode(NewLanguageText))
        {
            NewLanguageText = string.Empty;
        }
    }

    /// <summary>Adds a language by BCP-47 code. Returns false with <see cref="EditorViewModelBase.Message"/> set when the code is not valid or already listed.</summary>
    public bool AddLanguageCode(string? text)
    {
        if (!LanguageCatalog.TryNormalize(text, out var code, out var error))
        {
            Message = error;
            return false;
        }

        if (Settings.AllLanguages.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            Message = $"{code} is already one of the languages.";
            return false;
        }

        Settings.Languages.Add(code);
        Message = string.Empty;
        RebuildAll();
        return true;
    }

    /// <summary>Lets the user pick one of the common languages (with native names) and adds it.</summary>
    [RelayCommand]
    public async Task PickLanguageAsync()
    {
        var existing = Settings.AllLanguages;
        var offered = LanguageCatalog.Common.Where(l => !existing.Contains(l.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        var choice = await _main.Dialogs.ChooseOneAsync("Add language", "Choose a language to add to the site.", offered.Select(l => l.Display).ToList());
        if (choice is null)
        {
            return;
        }

        var picked = offered.FirstOrDefault(l => l.Display == choice);
        if (picked is not null)
        {
            AddLanguageCode(picked.Code);
        }
    }

    /// <summary>Removes a language from the site. The default language cannot be removed. Its stored translations are kept in the project.</summary>
    [RelayCommand]
    public void RemoveLanguage(LanguageRow? row)
    {
        row ??= SelectedLanguage;
        if (row is null)
        {
            Message = "Select a language to remove.";
            return;
        }

        if (row.IsDefault)
        {
            Message = "The default language cannot be removed. Choose another default language first.";
            return;
        }

        Settings.Languages.RemoveAll(l => string.Equals(l, row.Code, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(FilterLanguage, row.Code, StringComparison.OrdinalIgnoreCase))
        {
            _filterLanguage = AllLanguagesLabel;
        }

        Message = string.Empty;
        RebuildAll();
    }

    // ---- Grid ----

    public ObservableCollection<TranslationRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>The languages the grid shows as columns: the filter language, or all languages.</summary>
    public IReadOnlyList<string> VisibleLanguages => _visibleLanguages;

    public int TotalRowCount => _allRows.Count;

    public int ShownRowCount => _rows.Count;

    public string FilterLanguage
    {
        get => _filterLanguage;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? AllLanguagesLabel : value;
            if (SetProperty(ref _filterLanguage, next))
            {
                ApplyFilter(columnsMayChange: true);
                _main.RefreshBreadcrumb();
            }
        }
    }

    /// <summary>The language code chosen in the filter, or null for all languages.</summary>
    public string? ActiveFilterLanguage
        => _filterLanguage == AllLanguagesLabel ? null
            : Settings.AllLanguages.FirstOrDefault(l => string.Equals(l, _filterLanguage, StringComparison.OrdinalIgnoreCase));

    public bool OnlyMissing
    {
        get => _onlyMissing;
        set
        {
            if (SetProperty(ref _onlyMissing, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool OnlyUnreviewed
    {
        get => _onlyUnreviewed;
        set
        {
            if (SetProperty(ref _onlyUnreviewed, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool OnlyStale
    {
        get => _onlyStale;
        set
        {
            if (SetProperty(ref _onlyStale, value))
            {
                ApplyFilter();
            }
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Re-applies the filters. The grid does this on its own only when a filter changes, so a row does not vanish while it is being edited.</summary>
    [RelayCommand]
    public void RefreshGrid()
    {
        foreach (var row in _allRows)
        {
            row.Refresh();
        }

        ApplyFilter();
    }

    /// <summary>The grid tells the view model which rows are selected, for "Translate selected rows".</summary>
    public void SetSelectedRows(IEnumerable<TranslationRow> rows)
    {
        _selectedRows = rows.ToList();
        OnPropertyChanged(nameof(SelectedRowCount));
    }

    public int SelectedRowCount => _selectedRows.Count;

    // ---- Loading ----

    /// <summary>Re-reads everything from the open project. Called when a project is opened or created.</summary>
    public void Reload()
    {
        ForgetRawText();
        Message = string.Empty;
        _confirmedProvider = null;
        _confirmedLanguages.Clear();
        TranslateMessage = string.Empty;
        ResultSummary = string.Empty;
        _filterLanguage = AllLanguagesLabel;
        RebuildAll();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Rebuilds rows and the provider status without forgetting what was confirmed. Called when the tab is shown.</summary>
    public void Refresh()
    {
        RebuildAll();
        OnPropertyChanged(nameof(ProviderStatus));
        OnPropertyChanged(nameof(IsProviderReady));
        OnPropertyChanged(nameof(CanTranslate));
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex)
            && MainViewModel.TabNames[Math.Clamp(_main.ActiveTabIndex, 0, MainViewModel.TabNames.Count - 1)] == "Languages")
        {
            Refresh();
        }
    }

    private void RebuildAll()
    {
        var languages = Settings.AllLanguages;

        Languages.Clear();
        foreach (var code in languages)
        {
            Languages.Add(new LanguageRow(code, string.Equals(code, Settings.DefaultLanguage.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        Sync(LanguageCodes, languages);
        Sync(FilterChoices, new[] { AllLanguagesLabel }.Concat(languages).ToList());
        if (ActiveFilterLanguage is null)
        {
            _filterLanguage = AllLanguagesLabel;
        }

        _allRows = LocalizationKeys.Enumerate(_main.Current)
            .Select(s => new TranslationRow(s.Key, s.Text, s.Language, languages, Settings))
            .ToList();

        OnPropertyChanged(nameof(FilterLanguage));
        OnPropertyChanged(nameof(DefaultLanguage));
        ApplyFilter(columnsMayChange: true);
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="source"/> without clearing it first, so a bound combo box keeps its selection where it can.</summary>
    private static void Sync(ObservableCollection<string> target, IReadOnlyList<string> source)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!source.Contains(target[i], StringComparer.Ordinal))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count && target[i] == source[i])
            {
                continue;
            }

            var existing = target.IndexOf(source[i]);
            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, source[i]);
            }
        }
    }

    private void ApplyFilter(bool columnsMayChange = false)
    {
        var all = Settings.AllLanguages;
        IReadOnlyList<string> visible = ActiveFilterLanguage is { } one ? new List<string> { one } : all.ToList();
        var columnsChanged = columnsMayChange || !visible.SequenceEqual(_visibleLanguages, StringComparer.Ordinal);
        _visibleLanguages = visible;

        var text = _search.Trim();
        var shown = new ObservableCollection<TranslationRow>(_allRows.Where(r => Matches(r, visible, text)));
        Rows = shown;
        OnPropertyChanged(nameof(VisibleLanguages));
        OnPropertyChanged(nameof(TotalRowCount));
        OnPropertyChanged(nameof(ShownRowCount));
        if (columnsChanged)
        {
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Matches(TranslationRow row, IReadOnlyList<string> languages, string search)
    {
        var cells = languages.Select(row.CellFor).Where(c => c is { IsSource: false }).Cast<TranslationCell>().ToList();
        if (_onlyMissing && !cells.Any(c => c.IsMissing))
        {
            return false;
        }

        if (_onlyUnreviewed && !cells.Any(c => c.IsUnreviewed))
        {
            return false;
        }

        if (_onlyStale && !cells.Any(c => c.IsStale))
        {
            return false;
        }

        if (search.Length == 0)
        {
            return true;
        }

        return row.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.SourceText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || cells.Any(c => c.Text.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Translating ----

    public bool IsTranslating
    {
        get => _isTranslating;
        private set
        {
            if (SetProperty(ref _isTranslating, value))
            {
                OnPropertyChanged(nameof(IsProgressIndeterminate));
                OnPropertyChanged(nameof(CanTranslate));
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isTranslating;

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public int ProgressMaximum
    {
        get => _progressMaximum;
        private set
        {
            if (SetProperty(ref _progressMaximum, value))
            {
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    /// <summary>A single language is one request; there is no step to count, so the bar just runs.</summary>
    public bool IsProgressIndeterminate => _isTranslating && _progressMaximum <= 1;

    /// <summary>Why the last translation did not start (automatic translation off, no key, declined), or empty.</summary>
    public string TranslateMessage
    {
        get => _translateMessage;
        private set
        {
            if (SetProperty(ref _translateMessage, value))
            {
                OnPropertyChanged(nameof(HasTranslateMessage));
            }
        }
    }

    public bool HasTranslateMessage => _translateMessage.Length > 0;

    /// <summary>The counts of the last translation run: translated, skipped because reviewed, failed with the error message.</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        private set
        {
            if (SetProperty(ref _resultSummary, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => _resultSummary.Length > 0;

    public int LastTranslated { get; private set; }

    /// <summary>Texts that were left alone because a person reviewed (or typed) them.</summary>
    public int LastSkippedReviewed { get; private set; }

    /// <summary>Texts that could not be translated.</summary>
    public int LastFailed { get; private set; }

    /// <summary>One "language: message" per language that failed.</summary>
    public IReadOnlyList<string> LastFailures { get; private set; } = [];

    public bool LastCancelled { get; private set; }

    /// <summary>Translates every missing (or outdated, unreviewed) text of the filter language, or of all languages.</summary>
    [RelayCommand]
    public Task TranslateMissingAsync() => TranslateAsync(selectedOnly: false);

    /// <summary>Translates the missing texts of the selected grid rows only.</summary>
    [RelayCommand]
    public Task TranslateSelectedAsync() => TranslateAsync(selectedOnly: true);

    [RelayCommand]
    public void CancelTranslation() => _cancel?.Cancel();

    private async Task TranslateAsync(bool selectedOnly)
    {
        if (IsTranslating)
        {
            return;
        }

        TranslateMessage = string.Empty;
        var settings = Settings;

        // The opt-in guard: nothing leaves the machine unless every one of these holds.
        if (!settings.AutoTranslate)
        {
            TranslateMessage = "Automatic translation is off. Tick 'Translate automatically with an online service' first. Nothing was sent.";
            return;
        }

        var kind = settings.TranslationProvider;
        if (kind == TranslationProviderKind.None)
        {
            TranslateMessage = "Choose a translation service (Google or Claude) first. Nothing was sent.";
            return;
        }

        if (!KeyIsSet(kind))
        {
            TranslateMessage = $"{EnvironmentApiKeyProvider.VariableFor(kind)} is not set, so {kind} cannot be used. Nothing was sent.";
            return;
        }

        HashSet<string>? keys = null;
        if (selectedOnly)
        {
            keys = _selectedRows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            if (keys.Count == 0)
            {
                TranslateMessage = "Select one or more rows in the grid first.";
                return;
            }
        }

        IReadOnlyList<string> targets = ActiveFilterLanguage is { } one ? new List<string> { one } : settings.AllLanguages.ToList();
        var plan = BuildPlan(targets, keys);
        LastSkippedReviewed = plan.Protected;
        if (plan.PendingByLanguage.Count == 0)
        {
            LastTranslated = 0;
            LastFailed = 0;
            LastFailures = [];
            LastCancelled = false;
            ResultSummary = $"Nothing to translate. {plan.UpToDate} up to date, {plan.Protected} skipped because reviewed or typed by hand.";
            _main.Status = ResultSummary;
            return;
        }

        var languagesToSend = plan.PendingByLanguage.Keys.ToList();
        var total = plan.PendingByLanguage.Values.Sum();
        if (NeedsConfirmation(kind, languagesToSend))
        {
            var confirmed = await _main.Dialogs.ConfirmAsync(
                "Send texts to a translation service?",
                ConfirmationText(kind, languagesToSend, total, settings.TranslationNotes),
                "Translate");
            if (!confirmed)
            {
                TranslateMessage = "Translation cancelled. Nothing was sent.";
                return;
            }

            _confirmedProvider = kind;
            foreach (var language in languagesToSend)
            {
                _confirmedLanguages.Add(language);
            }
        }

        var provider = ProviderFactory.Create(kind);
        if (provider is null)
        {
            TranslateMessage = "The translation service could not be created. Nothing was sent.";
            return;
        }

        await RunAsync(provider, plan, keys);
    }

    private bool NeedsConfirmation(TranslationProviderKind kind, IReadOnlyList<string> languages)
        => _confirmedProvider != kind || languages.Any(l => !_confirmedLanguages.Contains(l));

    private static string ConfirmationText(TranslationProviderKind kind, IReadOnlyList<string> languages, int count, string notes)
    {
        var service = kind == TranslationProviderKind.Google ? "Google Cloud Translation" : "Claude (Anthropic)";
        var text = new StringBuilder();
        text.Append("These texts will be sent to a third party over the internet:\n\n");
        text.Append("Service: ").Append(service).Append('\n');
        text.Append("Target languages: ").Append(string.Join(", ", languages)).Append('\n');
        text.Append("Strings to send: ").Append(count).Append('\n');
        if (!string.IsNullOrWhiteSpace(notes))
        {
            text.Append("Your translation notes are sent with them.\n");
        }

        text.Append("\nTranslated texts are marked as machine translated and unreviewed. Nothing is sent if you cancel.");
        return text.ToString();
    }

    private async Task RunAsync(ITranslationProvider provider, TranslationPlan plan, HashSet<string>? keys)
    {
        var real = Settings;
        var scratch = BuildScratch(plan, keys);
        var service = new TranslationService(provider);
        var languages = plan.PendingByLanguage.Keys.ToList();

        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var token = cancel.Token;
        IsTranslating = true;
        ProgressValue = 0;
        ProgressMaximum = languages.Count;
        ResultSummary = string.Empty;
        var translated = 0;
        var failedStrings = 0;
        var failures = new List<string>();
        var cancelled = false;

        try
        {
            foreach (var language in languages)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var failed = false;
                try
                {
                    await Task.Run(() => service.TranslateAsync(scratch.Project, [language], token), token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failed = true;
                    failures.Add($"{language}: {ex.Message}");
                }

                // Texts translated before a failure or a cancel are kept.
                var merged = scratch.MergeInto(real, language);
                translated += merged;
                if (failed)
                {
                    failedStrings += Math.Max(0, plan.PendingByLanguage[language] - merged);
                }

                ProgressValue++;
                if (cancelled)
                {
                    break;
                }
            }
        }
        finally
        {
            _cancel = null;
            IsTranslating = false;
        }

        LastTranslated = translated;
        LastFailed = failedStrings;
        LastFailures = failures;
        LastCancelled = cancelled;
        ResultSummary = Summarize(translated, plan.Protected, failedStrings, failures, cancelled);
        _main.Status = ResultSummary;
        RefreshGrid();
    }

    private static string Summarize(int translated, int skipped, int failed, IReadOnlyList<string> failures, bool cancelled)
    {
        var text = new StringBuilder();
        if (cancelled)
        {
            text.Append("Cancelled. ");
        }

        text.Append($"Translated {translated}, skipped {skipped} because reviewed or typed by hand, failed {failed}.");
        foreach (var failure in failures)
        {
            text.Append(' ').Append(failure);
        }

        return text.ToString();
    }

    private TranslationPlan BuildPlan(IReadOnlyList<string> targets, HashSet<string>? keys)
    {
        var settings = Settings;
        var sources = LocalizationKeys.Enumerate(_main.Current).Where(s => keys is null || keys.Contains(s.Key)).ToList();
        var plan = new TranslationPlan();
        foreach (var target in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pending = 0;
            foreach (var source in sources.Where(s => !string.Equals(s.Language, target, StringComparison.OrdinalIgnoreCase)))
            {
                var existing = settings.Find(source.Key, target);
                if (existing is null)
                {
                    pending++;
                }
                else if (existing.Reviewed || existing.Source == TranslationSource.Manual)
                {
                    plan.Protected++;
                }
                else if (existing.SourceHash == TranslationEntry.HashOf(source.Text))
                {
                    plan.UpToDate++;
                }
                else
                {
                    pending++;
                }
            }

            if (pending > 0)
            {
                plan.PendingByLanguage[target] = pending;
            }
        }

        return plan;
    }

    /// <summary>
    /// A private copy of the translation settings and the caption texts for the service to work on, so the background work
    /// never touches the open project. Rows that were not selected are frozen as protected entries, so the real service
    /// leaves them alone.
    /// </summary>
    private Scratch BuildScratch(TranslationPlan plan, HashSet<string>? keys)
    {
        var real = _main.Current;
        var project = new Project { Name = real.Name };
        var target = project.Localization;
        var source = real.Localization;
        target.Enabled = true;
        target.AutoTranslate = true;
        target.DefaultLanguage = source.DefaultLanguage;
        target.Languages.AddRange(source.Languages);
        target.TranslationNotes = source.TranslationNotes;
        target.TranslationProvider = source.TranslationProvider;

        foreach (var table in real.Tables.Where(t => t.Generate))
        {
            var copy = new TableInfo { Name = table.Name, Caption = table.Caption, Generate = true };
            foreach (var field in table.Fields)
            {
                copy.Fields.Add(new FieldInfo { Name = field.Name, Caption = field.Caption });
            }

            project.Tables.Add(copy);
        }

        var known = new HashSet<TranslationEntry>(ReferenceEqualityComparer.Instance);
        foreach (var entry in source.Entries)
        {
            var clone = new TranslationEntry
            {
                Key = entry.Key,
                Language = entry.Language,
                Text = entry.Text,
                Source = entry.Source,
                Reviewed = entry.Reviewed,
                SourceHash = entry.SourceHash,
            };
            target.Entries.Add(clone);
            known.Add(clone);
        }

        if (keys is not null)
        {
            foreach (var text in LocalizationKeys.Enumerate(project).Where(s => !keys.Contains(s.Key)))
            {
                foreach (var language in plan.PendingByLanguage.Keys)
                {
                    var existing = target.Find(text.Key, language);
                    target.Entries.RemoveAll(e => e.Key == text.Key && string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase));
                    var frozen = new TranslationEntry
                    {
                        Key = text.Key,
                        Language = language,
                        Text = existing?.Text ?? string.Empty,
                        Source = TranslationSource.Manual,
                        Reviewed = true,
                        SourceHash = existing?.SourceHash ?? string.Empty,
                    };
                    target.Entries.Add(frozen);
                    known.Add(frozen);
                }
            }
        }

        return new Scratch(project, known);
    }

    // ---- CSV ----

    /// <summary>Asks for a file and writes every stored translation as key, language, text.</summary>
    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        var path = await _main.Dialogs.PickCsvToSaveAsync($"{_main.ProjectName}-translations.csv");
        if (path is not null)
        {
            ExportTo(path);
        }
    }

    /// <summary>Asks for a file and imports its translations as typed-by-hand entries.</summary>
    [RelayCommand]
    public async Task ImportCsvAsync()
    {
        var path = await _main.Dialogs.PickCsvToOpenAsync(_main.ProjectFolder);
        if (path is not null)
        {
            ImportFrom(path);
        }
    }

    /// <summary>Writes the translations of the project to a CSV file (UTF-8). Returns the number of rows written, or -1 when the file could not be written.</summary>
    public int ExportTo(string path)
    {
        var records = _allRows
            .SelectMany(r => r.Cells.Where(c => c.Entry is not null).Select(c => new TranslationCsvRecord(r.Key, c.Language, c.Entry!.Text)))
            .ToList();
        try
        {
            File.WriteAllText(path, TranslationCsv.Write(records), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultSummary = $"Export failed: {ex.Message}";
            _main.Status = ResultSummary;
            return -1;
        }

        ResultSummary = $"Exported {records.Count} translation(s) to {Path.GetFileName(path)}.";
        _main.Status = ResultSummary;
        return records.Count;
    }

    /// <summary>
    /// Reads translations from a CSV file. Each imported text becomes a manual, reviewed entry (like typing it into the grid).
    /// Lines for unknown keys or languages, for the source language of a key, or without text are skipped and counted.
    /// Returns the number of translations imported, or -1 when the file could not be read.
    /// </summary>
    public int ImportFrom(string path)
    {
        string csv;
        try
        {
            csv = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultSummary = $"Import failed: {ex.Message}";
            _main.Status = ResultSummary;
            return -1;
        }

        var records = TranslationCsv.Parse(csv, out var problems);
        var byKey = _allRows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        int imported = 0, unknownKey = 0, unknownLanguage = 0, skipped = problems.Count;
        foreach (var record in records)
        {
            if (!byKey.TryGetValue(record.Key, out var row))
            {
                unknownKey++;
            }
            else if (row.CellFor(record.Language) is not { } cell)
            {
                unknownLanguage++;
            }
            else if (cell.IsSource || record.Text.Length == 0)
            {
                skipped++;
            }
            else
            {
                cell.Text = record.Text;
                imported++;
            }
        }

        ResultSummary = $"Imported {imported} translation(s). Skipped {unknownKey} for unknown keys, {unknownLanguage} for languages not in the project, {skipped} other.";
        _main.Status = ResultSummary;
        RefreshGrid();
        return imported;
    }

    private sealed class TranslationPlan
    {
        public Dictionary<string, int> PendingByLanguage { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Protected { get; set; }

        public int UpToDate { get; set; }
    }

    private sealed class Scratch(Project project, HashSet<TranslationEntry> known)
    {
        public Project Project { get; } = project;

        /// <summary>Copies the entries the service added for a language into the open project and returns how many.</summary>
        public int MergeInto(LocalizationSettings real, string language)
        {
            var added = Project.Localization.Entries
                .Where(e => string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase) && !known.Contains(e))
                .ToList();
            foreach (var entry in added)
            {
                real.Entries.RemoveAll(e => e.Key == entry.Key && string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase));
                real.Entries.Add(new TranslationEntry
                {
                    Key = entry.Key,
                    Language = entry.Language,
                    Text = entry.Text,
                    Source = entry.Source,
                    Reviewed = entry.Reviewed,
                    SourceHash = entry.SourceHash,
                });
                known.Add(entry);
            }

            return added.Count;
        }
    }
}
