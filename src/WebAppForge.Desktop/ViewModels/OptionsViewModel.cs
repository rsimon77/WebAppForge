using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebAppForge.Desktop.Localization;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One entry in the category list on the left of the Options dialog. The title follows the UI language.</summary>
public sealed record OptionsCategory(string Id, LocalizedText Title)
{
    public const string General = "General";
    public const string Appearance = "Appearance";
    public const string Generate = "Generate";
    public const string Tools = "Tools";
    public const string Translation = "Translation";
    public const string Network = "Network";
    public const string Advanced = "Advanced";
}

/// <summary>One entry of the UI language list: a culture name, or null for the operating system's language.</summary>
public sealed record UiLanguageChoice(string? Name, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// The editable copy of <see cref="AppSettings"/> behind the Application Options dialog. Numbers are edited as text so a
/// bad entry can be shown with a message; nothing reaches the application until <see cref="Apply"/> or <see cref="Ok"/>.
/// </summary>
public sealed partial class OptionsViewModel : ViewModelBase
{
    private static readonly Regex ModelIdPattern = new("^[A-Za-z0-9._:/-]+$", RegexOptions.Compiled);
    private static readonly string[] ProxySchemes = ["http", "https", "socks5"];

    private readonly Action<AppSettings> _apply;
    private readonly IDialogService _dialogs;
    private readonly OptionsEnvironment _environment;
    private AppSettings _baseline;
    private bool _validating;

    // General
    [ObservableProperty] private bool _reopenLastProject;
    [ObservableProperty] private string _recentFilesLengthText = "";
    [ObservableProperty] private bool _confirmOnExit;
    [ObservableProperty] private string _autosaveMinutesText = "";
    [ObservableProperty] private string _sessionTimeoutText = "";

    // Appearance
    [ObservableProperty] private UiLanguageChoice? _uiLanguage;
    [ObservableProperty] private string _themeName = ThemePalettes.Default.Name;
    [ObservableProperty] private AppearanceMode _appearance;
    [ObservableProperty] private string _uiFontSizeText = "";
    [ObservableProperty] private bool _compactTables;
    [ObservableProperty] private string _customStylesPath = "";

    // Generate
    [ObservableProperty] private string _outputFolderPattern = "";
    [ObservableProperty] private int _defaultPageSize;
    [ObservableProperty] private bool _openOutputAfterGenerate;
    [ObservableProperty] private bool _clearOutputBeforeGenerate;
    [ObservableProperty] private bool _buildAfterGenerate;
    [ObservableProperty] private BuildConfiguration _buildConfiguration;

    // Tools and paths
    [ObservableProperty] private string _dotnetPath = "";
    [ObservableProperty] private string _nodePath = "";
    [ObservableProperty] private string _dockerPath = "";
    [ObservableProperty] private string _dotnetStatus = "";
    [ObservableProperty] private string _nodeStatus = "";
    [ObservableProperty] private string _dockerStatus = "";
    [ObservableProperty] private bool _isDetecting;

    // Translation and AI
    [ObservableProperty] private TranslationProvider _translationProvider;
    [ObservableProperty] private string _claudeModelId = "";
    [ObservableProperty] private string _googleKeyStatus = "";
    [ObservableProperty] private string _anthropicKeyStatus = "";

    // Network
    [ObservableProperty] private string _httpProxyUrl = "";
    [ObservableProperty] private string _requestTimeoutText = "";

    // Advanced
    [ObservableProperty] private AppLogLevel _logLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralPage), nameof(IsAppearancePage), nameof(IsGeneratePage), nameof(IsToolsPage),
        nameof(IsTranslationPage), nameof(IsNetworkPage), nameof(IsAdvancedPage))]
    private OptionsCategory _selectedCategory;

    [ObservableProperty]
    private string _message = "";

    public OptionsViewModel(AppSettings settings, Action<AppSettings> apply, IDialogService dialogs, OptionsEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _apply = apply;
        _dialogs = dialogs;
        _environment = environment ?? new OptionsEnvironment();
        _baseline = settings.Normalized();

        var tr = Localizer.Instance;
        Categories =
        [
            new(OptionsCategory.General, tr.Text("Options_General")),
            new(OptionsCategory.Appearance, tr.Text("Options_Appearance")),
            new(OptionsCategory.Generate, tr.Text("Common_Generate")),
            new(OptionsCategory.Tools, tr.Text("Options_ToolsAndPaths")),
            new(OptionsCategory.Translation, tr.Text("Options_TranslationAndAI")),
            new(OptionsCategory.Network, tr.Text("Options_Network")),
            new(OptionsCategory.Advanced, tr.Text("Options_Advanced")),
        ];
        _selectedCategory = Categories[0];

        // The system entry names the language it stands for; the others show their own name ("Magyar", not "Hungarian").
        UiLanguages =
        [
            new(null, tr.Format("Options_SystemLanguage", CultureInfo.InstalledUICulture.NativeName)),
            .. Localizer.AvailableCultures().Select(c => new UiLanguageChoice(c.Name, Capitalize(c.NativeName))),
        ];

        Load(_baseline);
        RefreshKeyStatus();
    }

    public IReadOnlyList<OptionsCategory> Categories { get; }

    /// <summary>The system language, then English and every language the app ships texts for.</summary>
    public IReadOnlyList<UiLanguageChoice> UiLanguages { get; }

    public IReadOnlyList<string> ThemeNames { get; } = ThemePalettes.All.Select(p => p.Name).ToList();

    public IReadOnlyList<AppearanceMode> Appearances { get; } = Enum.GetValues<AppearanceMode>();

    public IReadOnlyList<int> PageSizes => AppSettings.PageSizeChoices;

    public IReadOnlyList<BuildConfiguration> BuildConfigurations { get; } = Enum.GetValues<BuildConfiguration>();

    public IReadOnlyList<TranslationProvider> TranslationProviders { get; } = Enum.GetValues<TranslationProvider>();

    public IReadOnlyList<AppLogLevel> LogLevels { get; } = Enum.GetValues<AppLogLevel>();

    public string SettingsFilePath => Path.Combine(_environment.SettingsFolder, "settings.json");

    public bool IsGeneralPage => SelectedCategory.Id == OptionsCategory.General;

    public bool IsAppearancePage => SelectedCategory.Id == OptionsCategory.Appearance;

    public bool IsGeneratePage => SelectedCategory.Id == OptionsCategory.Generate;

    public bool IsToolsPage => SelectedCategory.Id == OptionsCategory.Tools;

    public bool IsTranslationPage => SelectedCategory.Id == OptionsCategory.Translation;

    public bool IsNetworkPage => SelectedCategory.Id == OptionsCategory.Network;

    public bool IsAdvancedPage => SelectedCategory.Id == OptionsCategory.Advanced;

    public bool IsClearOutputWarningVisible => ClearOutputBeforeGenerate;

    // Validation messages: null when the entry is fine.

    public string? RecentFilesLengthError => IntError(RecentFilesLengthText, "Recent files", AppSettings.MinRecentFilesLength, AppSettings.MaxRecentFilesLength);

    public string? AutosaveMinutesError => IntError(AutosaveMinutesText, "Autosave interval", AppSettings.MinAutosaveMinutes, AppSettings.MaxAutosaveMinutes, "minutes, 0 = off");

    public string? SessionTimeoutError => IntError(SessionTimeoutText, "Session idle timeout", AppSettings.MinTimeoutMinutes, AppSettings.MaxTimeoutMinutes, "minutes");

    public string? UiFontSizeError => IntError(UiFontSizeText, "UI font size", AppSettings.MinUiFontSize, AppSettings.MaxUiFontSize);

    public string? RequestTimeoutError => IntError(RequestTimeoutText, "Request timeout", AppSettings.MinRequestTimeoutSeconds, AppSettings.MaxRequestTimeoutSeconds, "seconds");

    public string? OutputFolderPatternError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OutputFolderPattern))
            {
                return "Enter an output folder pattern.";
            }

            return OutputFolderPattern.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || OutputFolderPattern.IndexOfAny(['*', '?', '<', '>', '|', '"']) >= 0
                ? "The pattern contains characters that are not allowed in a path."
                : null;
        }
    }

    public string? ClaudeModelIdError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ClaudeModelId))
            {
                return "Enter a Claude model id.";
            }

            return ModelIdPattern.IsMatch(ClaudeModelId.Trim()) ? null : "The model id may contain letters, digits and . _ : / - only.";
        }
    }

    public string? HttpProxyUrlError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HttpProxyUrl))
            {
                return null;
            }

            return Uri.TryCreate(HttpProxyUrl.Trim(), UriKind.Absolute, out var uri)
                   && ProxySchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
                   && !string.IsNullOrEmpty(uri.Host)
                ? null
                : "Enter a proxy URL such as http://proxy.example.com:8080 (http, https or socks5).";
        }
    }

    public IReadOnlyList<string> Errors => new[]
    {
        RecentFilesLengthError, AutosaveMinutesError, SessionTimeoutError, UiFontSizeError,
        OutputFolderPatternError, ClaudeModelIdError, HttpProxyUrlError, RequestTimeoutError,
    }.OfType<string>().ToList();

    public bool HasErrors => Errors.Count > 0;

    public bool CanApply => !HasErrors;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_validating || e.PropertyName is null || e.PropertyName.EndsWith("Error", StringComparison.Ordinal)
            || e.PropertyName is nameof(Errors) or nameof(HasErrors) or nameof(CanApply) or nameof(Message)
            || e.PropertyName.StartsWith("Is", StringComparison.Ordinal) && e.PropertyName.EndsWith("Page", StringComparison.Ordinal))
        {
            return;
        }

        _validating = true;
        try
        {
            foreach (var name in new[]
            {
                nameof(RecentFilesLengthError), nameof(AutosaveMinutesError), nameof(SessionTimeoutError), nameof(UiFontSizeError),
                nameof(OutputFolderPatternError), nameof(ClaudeModelIdError), nameof(HttpProxyUrlError), nameof(RequestTimeoutError),
                nameof(Errors), nameof(HasErrors), nameof(CanApply), nameof(IsClearOutputWarningVisible),
            })
            {
                OnPropertyChanged(name);
            }
        }
        finally
        {
            _validating = false;
        }
    }

    /// <summary>The edited values as settings. Entries that are not valid numbers keep the value they had when the dialog opened.</summary>
    public AppSettings ToSettings()
    {
        var s = _baseline.Clone();
        s.ReopenLastProject = ReopenLastProject;
        s.RecentFilesLength = ParseOr(RecentFilesLengthText, s.RecentFilesLength);
        s.ConfirmOnExit = ConfirmOnExit;
        s.AutosaveMinutes = ParseOr(AutosaveMinutesText, s.AutosaveMinutes);
        s.SessionTimeoutMinutes = ParseOr(SessionTimeoutText, s.SessionTimeoutMinutes);

        s.UiLanguage = UiLanguage?.Name;
        s.ThemeName = ThemeName;
        s.Appearance = Appearance;
        s.UiFontSize = ParseOr(UiFontSizeText, s.UiFontSize);
        s.CompactTables = CompactTables;
        s.CustomStylesPath = CustomStylesPath;

        s.DefaultOutputFolderPattern = OutputFolderPattern;
        s.DefaultPageSize = DefaultPageSize;
        s.OpenOutputAfterGenerate = OpenOutputAfterGenerate;
        s.ClearOutputBeforeGenerate = ClearOutputBeforeGenerate;
        s.BuildAfterGenerate = BuildAfterGenerate;
        s.DefaultBuildConfiguration = BuildConfiguration;

        s.DotnetPath = DotnetPath;
        s.NodePath = NodePath;
        s.DockerPath = DockerPath;

        s.TranslationProvider = TranslationProvider;
        s.ClaudeModelId = ClaudeModelId;

        s.HttpProxyUrl = HttpProxyUrl;
        s.RequestTimeoutSeconds = ParseOr(RequestTimeoutText, s.RequestTimeoutSeconds);

        s.LogLevel = LogLevel;
        return s.Normalized();
    }

    /// <summary>Applies the edited values now and keeps the dialog open. Returns false (and applies nothing) while an entry is invalid.</summary>
    public bool Apply()
    {
        if (HasErrors)
        {
            Message = "Fix the highlighted entries first.";
            return false;
        }

        var settings = ToSettings();
        _apply(settings);
        _baseline = settings;
        Message = "Applied.";
        return true;
    }

    /// <summary>The OK button: applies and reports whether the dialog may close.</summary>
    public bool Ok() => Apply();

    /// <summary>Puts one page back to the default values. Nothing is applied until OK or Apply.</summary>
    public void ResetCategory(string categoryId)
    {
        var defaults = new AppSettings();
        switch (categoryId)
        {
            case OptionsCategory.General:
                LoadGeneral(defaults);
                break;
            case OptionsCategory.Appearance:
                LoadAppearance(defaults);
                break;
            case OptionsCategory.Generate:
                LoadGenerate(defaults);
                break;
            case OptionsCategory.Tools:
                LoadTools(defaults);
                break;
            case OptionsCategory.Translation:
                LoadTranslation(defaults);
                break;
            case OptionsCategory.Network:
                LoadNetwork(defaults);
                break;
            case OptionsCategory.Advanced:
                LoadAdvanced(defaults);
                break;
        }

        Message = "Defaults restored on this page. Click Apply or OK to keep them.";
    }

    [RelayCommand]
    private void ResetPage() => ResetCategory(SelectedCategory.Id);

    /// <summary>Resets every setting after a confirmation, and applies the defaults at once.</summary>
    [RelayCommand]
    private async Task ResetAllAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Reset all settings",
            "Reset every option on every page to its default value? The recent files list and the last project are cleared too.",
            "Reset all");
        if (!confirmed)
        {
            return;
        }

        _baseline = new AppSettings();
        Load(_baseline);
        _apply(_baseline.Clone());
        Message = "All settings were reset to their defaults.";
    }

    [RelayCommand]
    private async Task BrowseCustomStylesAsync()
    {
        var path = await _dialogs.PickXamlFileAsync(CustomStylesPath);
        if (path is not null)
        {
            CustomStylesPath = path;
        }
    }

    [RelayCommand]
    private void ClearCustomStyles() => CustomStylesPath = string.Empty;

    [RelayCommand]
    private async Task BrowseDotnetAsync() => DotnetPath = await PickAsync(DotnetPath) ?? DotnetPath;

    [RelayCommand]
    private async Task BrowseNodeAsync() => NodePath = await PickAsync(NodePath) ?? NodePath;

    [RelayCommand]
    private async Task BrowseDockerAsync() => DockerPath = await PickAsync(DockerPath) ?? DockerPath;

    /// <summary>Resolves dotnet, node and docker (the configured path, else PATH) and shows the version each one reports.</summary>
    [RelayCommand]
    private async Task DetectToolsAsync()
    {
        IsDetecting = true;
        try
        {
            DotnetStatus = (await _environment.Detector.DetectAsync("dotnet", DotnetPath)).Message;
            NodeStatus = (await _environment.Detector.DetectAsync("node", NodePath)).Message;
            DockerStatus = (await _environment.Detector.DetectAsync("docker", DockerPath)).Message;
        }
        finally
        {
            IsDetecting = false;
        }
    }

    [RelayCommand]
    private void RefreshKeyStatus()
    {
        GoogleKeyStatus = KeyStatus(OptionsEnvironment.GoogleKeyVariable);
        AnthropicKeyStatus = KeyStatus(OptionsEnvironment.AnthropicKeyVariable);
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(_environment.SettingsFolder);
            _environment.OpenFolder(_environment.SettingsFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Message = $"Could not open the settings folder: {ex.Message}";
        }
    }

    private async Task<string?> PickAsync(string current)
        => await _dialogs.PickExecutableAsync(string.IsNullOrWhiteSpace(current) ? null : current);

    private string KeyStatus(string variable)
        => string.IsNullOrEmpty(_environment.GetVariable(variable)) ? $"{variable} is not set." : $"{variable} is set.";

    private static string Capitalize(string name)
        => name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];

    private static int ParseOr(string text, int fallback)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string? IntError(string? text, string name, int min, int max, string? unit = null)
    {
        var suffix = unit is null ? string.Empty : $" ({unit})";
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? null
            : $"{name} must be a whole number from {min} to {max}{suffix}.";
    }

    private void Load(AppSettings s)
    {
        LoadGeneral(s);
        LoadAppearance(s);
        LoadGenerate(s);
        LoadTools(s);
        LoadTranslation(s);
        LoadNetwork(s);
        LoadAdvanced(s);
    }

    private void LoadGeneral(AppSettings s)
    {
        ReopenLastProject = s.ReopenLastProject;
        RecentFilesLengthText = s.RecentFilesLength.ToString(CultureInfo.InvariantCulture);
        ConfirmOnExit = s.ConfirmOnExit;
        AutosaveMinutesText = s.AutosaveMinutes.ToString(CultureInfo.InvariantCulture);
        SessionTimeoutText = s.SessionTimeoutMinutes.ToString(CultureInfo.InvariantCulture);
    }

    private void LoadAppearance(AppSettings s)
    {
        // A language the app no longer ships (or a hand-edited name) shows as the system language.
        UiLanguage = UiLanguages.FirstOrDefault(l => string.Equals(l.Name, s.UiLanguage, StringComparison.OrdinalIgnoreCase)) ?? UiLanguages[0];
        ThemeName = s.ThemeName;
        Appearance = s.Appearance;
        UiFontSizeText = s.UiFontSize.ToString(CultureInfo.InvariantCulture);
        CompactTables = s.CompactTables;
        CustomStylesPath = s.CustomStylesPath ?? string.Empty;
    }

    private void LoadGenerate(AppSettings s)
    {
        OutputFolderPattern = s.DefaultOutputFolderPattern;
        DefaultPageSize = s.DefaultPageSize;
        OpenOutputAfterGenerate = s.OpenOutputAfterGenerate;
        ClearOutputBeforeGenerate = s.ClearOutputBeforeGenerate;
        BuildAfterGenerate = s.BuildAfterGenerate;
        BuildConfiguration = s.DefaultBuildConfiguration;
    }

    private void LoadTools(AppSettings s)
    {
        DotnetPath = s.DotnetPath ?? string.Empty;
        NodePath = s.NodePath ?? string.Empty;
        DockerPath = s.DockerPath ?? string.Empty;
        DotnetStatus = NodeStatus = DockerStatus = string.Empty;
    }

    private void LoadTranslation(AppSettings s)
    {
        TranslationProvider = s.TranslationProvider;
        ClaudeModelId = s.ClaudeModelId;
    }

    private void LoadNetwork(AppSettings s)
    {
        HttpProxyUrl = s.HttpProxyUrl ?? string.Empty;
        RequestTimeoutText = s.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private void LoadAdvanced(AppSettings s) => LogLevel = s.LogLevel;
}
