using System;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The Project tab: identity, output, site, runtime, global CSS and script, database and build settings of the open
/// project. Every property reads and writes the model of the open project directly, so Save persists the edits.
/// Numbers and names are checked: a bad value shows <see cref="EditorViewModelBase.Message"/> and the model keeps the last valid value.
/// </summary>
public partial class ProjectSettingsViewModel : EditorViewModelBase
{
    private static readonly Regex VersionPattern = new(@"^\d+(\.\d+){1,3}([-+][0-9A-Za-z.\-+]+)?$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex NamespacePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex AssemblyPattern = new(@"^[A-Za-z_][A-Za-z0-9_.\-]*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly MainViewModel _main;
    private string _securitySummary = string.Empty;
    private string _localizationSummary = string.Empty;

    public ProjectSettingsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += OnMainChanged;
        _main.Generate.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GenerateViewModel.OutputFolder))
            {
                OnPropertyChanged(nameof(OutputFolder));
            }
        };
        _main.Database.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DatabaseViewModel.DatabasePath))
            {
                OnPropertyChanged(nameof(DatabasePath));
            }
            else if (e.PropertyName == nameof(DatabaseViewModel.SelectedProvider))
            {
                OnPropertyChanged(nameof(Provider));
                OnPropertyChanged(nameof(IsSqlite));
                OnPropertyChanged(nameof(ServerConnection));
            }
            else if (e.PropertyName is nameof(DatabaseViewModel.ConnectionString) or nameof(DatabaseViewModel.ConnectionStringVariable))
            {
                OnPropertyChanged(nameof(ServerConnection));
            }
        };
    }

    private Project Project => _main.Current;

    private ProjectMetadata Meta => _main.Current.Meta;

    private BuildSettings Build => _main.Current.Build;

    /// <summary>Re-reads everything from the open project. Called when a project is opened or created.</summary>
    public void Reload()
    {
        ForgetRawText();
        Message = string.Empty;
        RefreshSummaries();
        OnPropertyChanged(string.Empty);
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ProjectName))
        {
            OnPropertyChanged(nameof(Name));
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex))
        {
            RefreshSummaries();
        }
    }

    /// <summary>The output folder picker of the Generate tab, shared so both tabs show the same value.</summary>
    public IAsyncRelayCommand BrowseOutputCommand => _main.Generate.BrowseCommand;

    /// <summary>The database file picker of the Database tab.</summary>
    public IAsyncRelayCommand BrowseDatabaseCommand => _main.Database.BrowseCommand;

    // ---- Identity ----

    public string Name
    {
        get => _main.ProjectName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Message = Localizer.Instance["ProjectSettings_TheProjectNameCannotBeEmpty"];
                return;
            }

            Message = string.Empty;
            if (_main.ProjectName != value)
            {
                _main.ProjectName = value;
                OnPropertyChanged();
            }
        }
    }

    public string ProjectId => Project.ProjectId.ToString();

    public string Description
    {
        get => Meta.Description;
        set => Put(Meta.Description, value ?? string.Empty, v => Meta.Description = v);
    }

    public string Author
    {
        get => Meta.Author;
        set => Put(Meta.Author, value ?? string.Empty, v => Meta.Author = v);
    }

    public string Company
    {
        get => Meta.Company;
        set => Put(Meta.Company, value ?? string.Empty, v => Meta.Company = v);
    }

    public string Copyright
    {
        get => Meta.Copyright;
        set => Put(Meta.Copyright, value ?? string.Empty, v => Meta.Copyright = v);
    }

    public string Version
    {
        get => Shown(nameof(Version), Meta.Version);
        set
        {
            var text = value ?? string.Empty;
            Accept(nameof(Version), text,
                VersionPattern.IsMatch(text.Trim()) ? null : Localizer.Instance["ProjectSettings_VersionMustLookLike10"],
                () => Meta.Version = text.Trim());
        }
    }

    /// <summary>Gives the project a new identifier after the user confirms. The old id stays in generated sites that used it.</summary>
    [RelayCommand]
    public async Task RegenerateProjectIdAsync()
    {
        var confirmed = await _main.Dialogs.ConfirmAsync(
            Localizer.Instance["ProjectSettings_RegenerateProjectId"],
            Localizer.Instance["ProjectSettings_TheProjectIdIdentifiesThisProject"],
            Localizer.Instance["ProjectSettings_RegenerateButton"]);
        if (!confirmed)
        {
            return;
        }

        Project.ProjectId = Guid.NewGuid();
        OnPropertyChanged(nameof(ProjectId));
        Changed();
        _main.Status = Localizer.Instance["ProjectSettings_CreatedANewProjectId"];
    }

    // ---- Output ----

    public string OutputFolder
    {
        get => _main.Generate.OutputFolder;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Message = Localizer.Instance["ProjectSettings_TheOutputFolderCannotBeEmpty"];
                OnPropertyChanged();
                return;
            }

            Message = string.Empty;
            _main.Generate.OutputFolder = value.Trim();
        }
    }

    public string RootNamespace
    {
        get => Shown(nameof(RootNamespace), Meta.RootNamespace);
        set
        {
            var text = (value ?? string.Empty).Trim();
            Accept(nameof(RootNamespace), text,
                text.Length == 0 || NamespacePattern.IsMatch(text) ? null : Localizer.Instance["ProjectSettings_TheNamespaceMustBeLettersDigits"],
                () => Meta.RootNamespace = text);
        }
    }

    public string AssemblyName
    {
        get => Shown(nameof(AssemblyName), Meta.AssemblyName);
        set
        {
            var text = (value ?? string.Empty).Trim();
            Accept(nameof(AssemblyName), text,
                text.Length == 0 || AssemblyPattern.IsMatch(text) ? null : Localizer.Instance["ProjectSettings_TheAssemblyNameCanUseLetters"],
                () => Meta.AssemblyName = text);
        }
    }

    // ---- Site ----

    public string SiteTitle
    {
        get => Project.AspNet.Title;
        set => Put(Project.AspNet.Title, value ?? string.Empty, v => Project.AspNet.Title = v);
    }

    public string Theme
    {
        get => Project.AspNet.Theme;
        set => Put(Project.AspNet.Theme, value ?? string.Empty, v => Project.AspNet.Theme = v);
    }

    public bool IncludeHomePage
    {
        get => Project.AspNet.IncludeHomePage;
        set => Put(Project.AspNet.IncludeHomePage, value, v => Project.AspNet.IncludeHomePage = v);
    }

    public string FaviconPath
    {
        get => Meta.FaviconPath;
        set => Put(Meta.FaviconPath, value ?? string.Empty, v => Meta.FaviconPath = v);
    }

    public string LogoPath
    {
        get => Meta.LogoPath;
        set => Put(Meta.LogoPath, value ?? string.Empty, v => Meta.LogoPath = v);
    }

    public string FooterText
    {
        get => Meta.FooterText;
        set => Put(Meta.FooterText, value ?? string.Empty, v => Meta.FooterText = v);
    }

    public string ColorMode
    {
        get => Meta.ColorMode;
        set => Put(Meta.ColorMode, value ?? "auto", v => Meta.ColorMode = v);
    }

    public string NavbarStyle
    {
        get => Meta.NavbarStyle;
        set => Put(Meta.NavbarStyle, value ?? "light", v => Meta.NavbarStyle = v);
    }

    [RelayCommand]
    public async Task BrowseFaviconAsync()
    {
        var path = await PickImageAsync();
        if (path is not null)
        {
            FaviconPath = path;
        }
    }

    [RelayCommand]
    public void ClearFavicon() => FaviconPath = string.Empty;

    [RelayCommand]
    public async Task BrowseLogoAsync()
    {
        var path = await PickImageAsync();
        if (path is not null)
        {
            LogoPath = path;
        }
    }

    [RelayCommand]
    public void ClearLogo() => LogoPath = string.Empty;

    private async Task<string?> PickImageAsync()
    {
        var folder = _main.ProjectFolder;
        var path = await _main.Dialogs.PickImageFileAsync(folder);
        if (path is null)
        {
            return null;
        }

        return folder is null ? path : ProjectPaths.ToRelative(folder, path);
    }

    // ---- Runtime ----

    public string Environment
    {
        get => Meta.Environment;
        set => Put(Meta.Environment, value ?? "Development", v => Meta.Environment = v);
    }

    public string HttpPortText
    {
        get => Shown(nameof(HttpPortText), Meta.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        set => SetPort(nameof(HttpPortText), Localizer.Instance["ProjectSettings_HTTPPort"], value, Meta.HttpsPort, port => Meta.HttpPort = port);
    }

    public string HttpsPortText
    {
        get => Shown(nameof(HttpsPortText), Meta.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        set => SetPort(nameof(HttpsPortText), Localizer.Instance["ProjectSettings_HTTPSPort"], value, Meta.HttpPort, port => Meta.HttpsPort = port);
    }

    private void SetPort(string name, string label, string? text, int otherPort, Action<int> store)
    {
        var raw = text ?? string.Empty;
        string? error = null;
        var port = 0;
        if (!TryWholeNumber(raw, 1, 65535, out port))
        {
            error = RangeMessage(label, 1, 65535);
        }
        else if (port == otherPort)
        {
            error = Localizer.Instance["ProjectSettings_TheHTTPAndHTTPSPortsMust"];
        }

        Accept(name, raw, error, () => store(port));
    }

    public bool UseHttps
    {
        get => Meta.UseHttps;
        set
        {
            if (Put(Meta.UseHttps, value, v => Meta.UseHttps = v))
            {
                OnPropertyChanged(nameof(CanUseHsts));
            }
        }
    }

    /// <summary>HSTS only has an effect on HTTPS, so the box is enabled while HTTPS is on.</summary>
    public bool CanUseHsts => Meta.UseHttps;

    public bool EnableHsts
    {
        get => Meta.EnableHsts;
        set => Put(Meta.EnableHsts, value, v => Meta.EnableHsts = v);
    }

    public bool EnableResponseCompression
    {
        get => Meta.EnableResponseCompression;
        set => Put(Meta.EnableResponseCompression, value, v => Meta.EnableResponseCompression = v);
    }

    public bool EnableHealthEndpoint
    {
        get => Meta.EnableHealthEndpoint;
        set => Put(Meta.EnableHealthEndpoint, value, v => Meta.EnableHealthEndpoint = v);
    }

    public string LogLevel
    {
        get => Meta.LogLevel;
        set => Put(Meta.LogLevel, value ?? "Information", v => Meta.LogLevel = v);
    }

    // ---- Global CSS and script ----

    public string GlobalCss
    {
        get => Meta.GlobalCss;
        set => Put(Meta.GlobalCss, value ?? string.Empty, v => Meta.GlobalCss = v);
    }

    public string GlobalScript
    {
        get => Meta.GlobalScript;
        set => Put(Meta.GlobalScript, value ?? string.Empty, v => Meta.GlobalScript = v);
    }

    // ---- Database ----

    /// <summary>The engine chosen on the Database tab.</summary>
    public string Provider => _main.Database.SelectedProvider.Name;

    public bool IsSqlite => _main.Database.IsSqlite;

    /// <summary>Where a server project's connection string comes from; the string itself is not shown, it may hold a password.</summary>
    public string ServerConnection =>
        !string.IsNullOrWhiteSpace(_main.Database.ConnectionStringVariable)
            ? Localizer.Instance.Format("ProjectSettings_EnvironmentVariable", _main.Database.ConnectionStringVariable.Trim())
            : string.IsNullOrWhiteSpace(_main.Database.ConnectionString) ? Localizer.Instance["ProjectSettings_NotSet"] : Localizer.Instance["ProjectSettings_ConnectionStringInTheProject"];

    public string DatabasePath
    {
        get => _main.Database.DatabasePath;
        set => _main.Database.DatabasePath = value ?? string.Empty;
    }

    // ---- Build and Docker ----

    public bool BuildAfterGenerate
    {
        get => Build.BuildAfterGenerate;
        set => Put(Build.BuildAfterGenerate, value, v => Build.BuildAfterGenerate = v);
    }

    public string Configuration
    {
        get => Build.Configuration;
        set => Put(Build.Configuration, value ?? "Debug", v => Build.Configuration = v);
    }

    public bool Publish
    {
        get => Build.Publish;
        set => Put(Build.Publish, value, v => Build.Publish = v);
    }

    public string PublishFolder
    {
        get => Shown(nameof(PublishFolder), Build.PublishFolder);
        set
        {
            var text = (value ?? string.Empty).Trim();
            Accept(nameof(PublishFolder), text,
                text.Length > 0 ? null : Localizer.Instance["ProjectSettings_ThePublishFolderCannotBeEmpty"],
                () => Build.PublishFolder = text);
        }
    }

    public string RuntimeIdentifier
    {
        get => Build.RuntimeIdentifier;
        set => Put(Build.RuntimeIdentifier, (value ?? string.Empty).Trim(), v => Build.RuntimeIdentifier = v);
    }

    public bool SelfContained
    {
        get => Build.SelfContained;
        set => Put(Build.SelfContained, value, v => Build.SelfContained = v);
    }

    public bool GenerateDockerfile
    {
        get => Build.GenerateDockerfile;
        set => Put(Build.GenerateDockerfile, value, v => Build.GenerateDockerfile = v);
    }

    public string DockerImageName
    {
        get => Build.DockerImageName;
        set => Put(Build.DockerImageName, (value ?? string.Empty).Trim(), v => Build.DockerImageName = v);
    }

    public string DockerExposePortText
    {
        get => Shown(nameof(DockerExposePortText), Build.DockerExposePort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            var raw = value ?? string.Empty;
            var ok = TryWholeNumber(raw, 1, 65535, out var port);
            Accept(nameof(DockerExposePortText), raw, ok ? null : RangeMessage(Localizer.Instance["ProjectSettings_TheExposedPort"], 1, 65535), () => Build.DockerExposePort = port);
        }
    }

    public string DockerBaseSdkImage
    {
        get => Shown(nameof(DockerBaseSdkImage), Build.DockerBaseSdkImage);
        set => SetImage(nameof(DockerBaseSdkImage), Localizer.Instance["ProjectSettings_SDKImage"], value, v => Build.DockerBaseSdkImage = v);
    }

    public string DockerBaseRuntimeImage
    {
        get => Shown(nameof(DockerBaseRuntimeImage), Build.DockerBaseRuntimeImage);
        set => SetImage(nameof(DockerBaseRuntimeImage), Localizer.Instance["ProjectSettings_RuntimeImage"], value, v => Build.DockerBaseRuntimeImage = v);
    }

    private void SetImage(string name, string label, string? value, Action<string> store)
    {
        var text = (value ?? string.Empty).Trim();
        Accept(name, text,
            text.Length > 0 && !text.Contains(' ') ? null : Localizer.Instance.Format("ProjectSettings_MustBeAnImageNameWithout", label),
            () => store(text));
    }

    public bool GenerateCompose
    {
        get => Build.GenerateCompose;
        set => Put(Build.GenerateCompose, value, v => Build.GenerateCompose = v);
    }

    public bool BuildDockerImage
    {
        get => Build.BuildDockerImage;
        set => Put(Build.BuildDockerImage, value, v => Build.BuildDockerImage = v);
    }

    // ---- Security and localization summary ----

    public string SecuritySummary
    {
        get => _securitySummary;
        private set => SetProperty(ref _securitySummary, value);
    }

    public string LocalizationSummary
    {
        get => _localizationSummary;
        private set => SetProperty(ref _localizationSummary, value);
    }

    public bool HasLocalizationTab => MainViewModel.TabNames.Contains("Languages");

    public void RefreshSummaries()
    {
        var security = Project.Security;
        SecuritySummary = !security.Enabled
            ? Localizer.Instance["ProjectSettings_SignInIsOffEveryPage"]
            : Localizer.Instance.Format("ProjectSettings_SignInIsOnUsersAreIn", string.IsNullOrEmpty(security.UserTable) ? Localizer.Instance["ProjectSettings_NoTableChosen"] : security.UserTable)
              + (string.IsNullOrEmpty(security.UserLevelField) ? "." : Localizer.Instance.Format("ProjectSettings_WithUserLevelsFrom", security.UserLevelField))
              + (security.AllowRegistration ? Localizer.Instance["ProjectSettings_NewUsersCanRegister"] : string.Empty);

        var localization = Project.Localization;
        LocalizationSummary = !localization.Enabled
            ? Localizer.Instance["ProjectSettings_MultiLanguageSupportIsOff"]
            : Localizer.Instance.Format("ProjectSettings_MultiLanguageSupportIsOn", string.Join(", ", localization.AllLanguages), localization.DefaultLanguage);
    }

    [RelayCommand]
    public void ShowSecurityTab() => _main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Security");

    [RelayCommand]
    public void ShowLocalizationTab()
    {
        var index = MainViewModel.TabNames.ToList().IndexOf("Languages");
        if (index < 0)
        {
            _main.Status = Localizer.Instance["ProjectSettings_ThereIsNoLanguagesTabIn"];
            return;
        }

        _main.ActiveTabIndex = index;
    }
}
