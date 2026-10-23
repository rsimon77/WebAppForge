using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>Owns the open project, project file commands, settings, the status line and the tab view models.</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Tab names in TabControl order. <see cref="ActiveTabIndex"/> indexes this list.</summary>
    public static IReadOnlyList<string> TabNames { get; } = ["Database", "Tables", "Fields", "ASP.NET", "Security", "Generate", "Project", "Pages", "Languages", "Relations"];

    private readonly IDialogService _dialogs;
    private readonly SettingsStore? _store;
    private readonly IAppearance? _appearance;
    private readonly SessionIdleTracker _idle;
    private DateTime _lastAutosaveUtc = DateTime.UtcNow;
    private string? _autosavedSnapshot;

    /// <summary>The project as last saved or loaded, serialized. The open project is dirty when it differs.</summary>
    private string? _savedSnapshot;

    [ObservableProperty]
    private Project _current = new();

    [ObservableProperty]
    private string? _projectPath;

    [ObservableProperty]
    private string _projectName = "Project1";

    [ObservableProperty]
    private string _status = "Create a new project or open an existing one.";

    [ObservableProperty]
    private bool _isSessionIdle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breadcrumb))]
    private int _activeTabIndex;

    public MainViewModel(IDialogService dialogs)
        : this(dialogs, store: null, appearance: null)
    {
    }

    /// <param name="store">Where settings are kept. Null keeps them in memory only.</param>
    /// <param name="appearance">Applies the settings to the application. Null applies nothing (used by tests).</param>
    public MainViewModel(IDialogService dialogs, SettingsStore? store, IAppearance? appearance)
    {
        _dialogs = dialogs;
        _store = store;
        _appearance = appearance;
        Database = new DatabaseViewModel(this);
        Tables = new TablesViewModel(this);
        Generate = new GenerateViewModel(this);
        Security = new SecurityViewModel(this);
        ProjectSettings = new ProjectSettingsViewModel(this);
        FieldDesigner = new FieldDesignerViewModel(this);
        PageDesigner = new PageDesignerViewModel(this);
        Languages = new LanguagesViewModel(this);
        Relations = new RelationsViewModel(this);

        Settings = store?.Load() ?? new AppSettings();
        _idle = new SessionIdleTracker(TimeSpan.FromMinutes(Settings.SessionTimeoutMinutes), DateTime.UtcNow);

        ApplyProject(new Project(), null, "New project. Save it to choose a project folder.");
        var appearanceMessage = _appearance?.Apply(Settings);
        if (appearanceMessage is not null)
        {
            Status = appearanceMessage;
        }
    }

    public DatabaseViewModel Database { get; }

    public TablesViewModel Tables { get; }

    public GenerateViewModel Generate { get; }

    public SecurityViewModel Security { get; }

    /// <summary>The Project tab.</summary>
    public ProjectSettingsViewModel ProjectSettings { get; }

    /// <summary>The field designer on the Fields tab.</summary>
    public FieldDesignerViewModel FieldDesigner { get; }

    /// <summary>The Pages tab (table look and page designs).</summary>
    public PageDesignerViewModel PageDesigner { get; }

    /// <summary>The Languages tab (multi-language settings and translations).</summary>
    public LanguagesViewModel Languages { get; }

    /// <summary>The Relations tab (master/detail links and their validation).</summary>
    public RelationsViewModel Relations { get; }

    /// <summary>Build runner, site runner, translation providers and other machine services of the Generate and Languages tabs. Tests replace them.</summary>
    public ToolServices Tools { get; init; } = new();

    public IDialogService Dialogs => _dialogs;

    /// <summary>The machine services the Options dialog uses (environment variables, tool detection, opening a folder).</summary>
    public OptionsEnvironment OptionsEnvironment { get; init; } = new();

    /// <summary>Raised once exiting has been confirmed. The window closes itself, which ends the application.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>The Core build tools (paths, proxy, timeout) from the current settings. Built on each use, so Options changes apply to the next build.</summary>
    public WebAppForge.Core.Build.BuildTools BuildTools => BuildToolsFactory.From(Settings);

    /// <summary>The per-user settings in effect.</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>True when the open project differs from the last saved or loaded state.</summary>
    public bool IsDirty => Snapshot(Current) != _savedSnapshot;

    /// <summary>Folder of the project file, or null while the project is unsaved.</summary>
    public string? ProjectFolder => ProjectPath is null ? null : Path.GetDirectoryName(ProjectPath);

    public string FolderDisplay => ProjectFolder ?? "(not saved yet)";

    /// <summary>"Project > Tab > Table > Field" for the current selection. The table and field appear only where the tab shows them.</summary>
    public string Breadcrumb
    {
        get
        {
            var tab = TabNames[Math.Clamp(ActiveTabIndex, 0, TabNames.Count - 1)];
            var parts = new List<string>
            {
                string.IsNullOrWhiteSpace(ProjectName) ? "Untitled project" : ProjectName,
                tab,
            };

            if ((tab == "Tables" || tab == "Fields" || tab == "Pages" || tab == "Relations") && Tables.SelectedTable is { } table)
            {
                parts.Add(table.Name);
            }

            if (tab == "Relations" && Relations.SelectedLink is { } relation && !string.IsNullOrWhiteSpace(relation.Link.DetailTable))
            {
                parts.Add(relation.Link.DetailTable);
            }

            if (tab == "Fields" && Tables.SelectedField is { } selectedField)
            {
                parts.Add(selectedField.Name);
            }

            if (tab == "Languages" && Languages.ActiveFilterLanguage is { } language)
            {
                parts.Add(language);
            }

            return string.Join(" > ", parts);
        }
    }

    partial void OnProjectPathChanged(string? value)
    {
        OnPropertyChanged(nameof(ProjectFolder));
        OnPropertyChanged(nameof(FolderDisplay));
    }

    partial void OnProjectNameChanged(string value)
    {
        Current.Name = value;
        OnPropertyChanged(nameof(Breadcrumb));
    }

    /// <summary>Called by the tables view model when its table or field selection changes.</summary>
    internal void RefreshBreadcrumb() => OnPropertyChanged(nameof(Breadcrumb));

    /// <summary>Records user input: resets the idle timer and hides the idle notice (the keep-alive).</summary>
    public void NoteActivity(DateTime now)
    {
        _idle.Touch(now);
        IsSessionIdle = false;
    }

    /// <summary>Shows the idle notice once the timeout has passed without input. The window calls this on a timer.</summary>
    public void CheckSession(DateTime now)
    {
        if (_idle.IsIdle(now))
        {
            IsSessionIdle = true;
        }
    }

    /// <summary>Stores and applies new settings. A problem with a custom styles file is shown in the status line.</summary>
    public void ApplySettings(AppSettings next, string successMessage)
    {
        Settings = next.Normalized();
        _idle.Timeout = TimeSpan.FromMinutes(Settings.SessionTimeoutMinutes);
        _store?.Save(Settings);
        Status = _appearance?.Apply(Settings) ?? successMessage;
    }

    /// <summary>Opens the project that was open last when the "reopen last project" option is on and the file still exists.</summary>
    public bool ReopenLastProject()
    {
        var path = Settings.LastProjectPath;
        if (!Settings.ReopenLastProject || path is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var project = ProjectFile.Load(path);
            ApplyProject(project, path, $"Reopened {Path.GetFileName(path)} with {project.Tables.Count} table(s).");
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Could not reopen {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Writes a backup copy of the unsaved project next to the project file as &lt;name&gt;.autosave.xml when the autosave
    /// interval has passed. The project file itself is never touched. Returns the backup path, or null when nothing was written.
    /// </summary>
    public string? AutosaveIfDue(DateTime nowUtc)
    {
        if (Settings.AutosaveMinutes <= 0 || ProjectPath is null || nowUtc - _lastAutosaveUtc < TimeSpan.FromMinutes(Settings.AutosaveMinutes))
        {
            return null;
        }

        _lastAutosaveUtc = nowUtc;
        var snapshot = Snapshot(Current);
        if (snapshot == _savedSnapshot || snapshot == _autosavedSnapshot)
        {
            return null;
        }

        var backup = Path.Combine(ProjectFolder!, Path.GetFileNameWithoutExtension(ProjectPath) + ".autosave.xml");
        if (string.Equals(Path.GetFullPath(backup), Path.GetFullPath(ProjectPath), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            ProjectFile.Save(Current, backup);
            _autosavedSnapshot = snapshot;
            Status = $"Autosaved a backup copy to {Path.GetFileName(backup)}.";
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Autosave failed: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// True when the application may close. With unsaved changes (and "confirm on exit" on) it asks Save, Discard or
    /// Cancel; Cancel keeps the application open.
    /// </summary>
    public async Task<bool> ConfirmExitAsync()
        => !Settings.ConfirmOnExit || await ConfirmReplaceAsync();

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (await ConfirmExitAsync())
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NoteRecent(string path)
    {
        var full = Path.GetFullPath(path);
        Settings.RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        Settings.RecentFiles.Insert(0, full);
        Settings.LastProjectPath = full;
        Settings = Settings.Normalized();
        _store?.Save(Settings);
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (!await ConfirmReplaceAsync())
        {
            return;
        }

        ApplyProject(new Project(), null, "New project. Save it to choose a project folder.");
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmReplaceAsync())
        {
            return;
        }

        var path = await _dialogs.OpenProjectAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            var project = ProjectFile.Load(path);
            ApplyProject(project, path, $"Opened {Path.GetFileName(path)} with {project.Tables.Count} table(s).");
            NoteRecent(path);
        }
        catch (Exception ex)
        {
            Status = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (ProjectPath is null)
        {
            await SaveAsAsync();
            return;
        }

        WriteProject(ProjectPath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var suggested = ProjectPath is null ? $"{Current.Name}.xml" : Path.GetFileName(ProjectPath);
        var path = await _dialogs.SaveProjectAsync(suggested);
        if (path is null)
        {
            return;
        }

        var oldFolder = ProjectFolder;
        var newFolder = Path.GetDirectoryName(path);
        if (oldFolder is not null && newFolder is not null
            && !string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
        {
            // Keep relative paths pointing at the same files after the project moves.
            RebaseRelativePaths(oldFolder, newFolder);
            Database.LoadFrom(Current);
            Generate.LoadFrom(Current);
            Security.LoadFrom(Current);
            ProjectSettings.Reload();
        }

        WriteProject(path);
    }

    [RelayCommand]
    private Task ShowOptionsAsync() => ShowOptionsPageAsync(null);

    /// <summary>Opens Tools &gt; Options on the Tools and paths page.</summary>
    [RelayCommand]
    public Task ShowToolOptionsAsync() => ShowOptionsPageAsync(OptionsCategory.Tools);

    private async Task ShowOptionsPageAsync(string? categoryId)
    {
        var options = new OptionsViewModel(Settings, next => ApplySettings(next, "Options applied."), _dialogs, OptionsEnvironment);
        if (categoryId is not null)
        {
            options.SelectedCategory = options.Categories.First(c => c.Id == categoryId);
        }

        await _dialogs.ShowOptionsAsync(options);
    }

    [RelayCommand]
    private async Task ChooseCustomStylesAsync()
    {
        var path = await _dialogs.PickXamlFileAsync(Settings.CustomStylesPath);
        if (path is null)
        {
            return;
        }

        var next = Settings.Clone();
        next.CustomStylesPath = path;
        ApplySettings(next, $"Custom styles set to {Path.GetFileName(path)}.");
    }

    [RelayCommand]
    private void ShowTablesTab() => ActiveTabIndex = TabNames.ToList().IndexOf("Tables");

    [RelayCommand]
    private void ShowFieldsTab() => ActiveTabIndex = TabNames.ToList().IndexOf("Fields");

    [RelayCommand]
    private void About() => Status = "WebAppForge desktop editor. Appearance and session settings are under Tools > Options.";

    /// <summary>
    /// Returns true when the open project may be replaced. With unsaved changes it asks Save, Discard or Cancel.
    /// Save returns true only when the save completed.
    /// </summary>
    private async Task<bool> ConfirmReplaceAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        switch (await _dialogs.ConfirmUnsavedChangesAsync())
        {
            case UnsavedChangesChoice.Save:
                await SaveProjectCommand.ExecuteAsync(null);
                return !IsDirty;
            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Replaces the open project and refreshes every tab. The project is clean afterwards.</summary>
    private void ApplyProject(Project project, string? path, string status)
    {
        Current = project;
        ProjectPath = path;
        ProjectName = project.Name;
        Database.LoadFrom(project);
        Tables.Reload();
        Generate.LoadFrom(project);
        Security.LoadFrom(project);
        ProjectSettings.Reload();
        FieldDesigner.Reload();
        PageDesigner.Reload();
        Languages.Reload();
        Relations.Reload();
        _savedSnapshot = Snapshot(project);
        Status = status;
    }

    private void WriteProject(string path)
    {
        try
        {
            ProjectFile.Save(Current, path);
            ProjectPath = path;
            _savedSnapshot = Snapshot(Current);
            Status = $"Saved {path}.";
            NoteRecent(path);
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
    }

    private static string Snapshot(Project project)
        => ProjectFile.Write(project).ToString(SaveOptions.DisableFormatting) + CustomizationFingerprint.Compute(project);

    private void RebaseRelativePaths(string oldFolder, string newFolder)
    {
        var database = Current.Database;
        if (!string.IsNullOrEmpty(database.Path) && !Path.IsPathRooted(database.Path))
        {
            database.Path = ProjectPaths.ToRelative(newFolder, ProjectPaths.Resolve(oldFolder, database.Path));
        }

        var meta = Current.Meta;
        meta.FaviconPath = Rebased(meta.FaviconPath, oldFolder, newFolder);
        meta.LogoPath = Rebased(meta.LogoPath, oldFolder, newFolder);

        if (!string.IsNullOrEmpty(Current.OutputFolder) && !Path.IsPathRooted(Current.OutputFolder))
        {
            Current.OutputFolder = ProjectPaths.ToRelative(newFolder, ProjectPaths.Resolve(oldFolder, Current.OutputFolder));
        }
    }

    private static string Rebased(string path, string oldFolder, string newFolder)
        => string.IsNullOrEmpty(path) || Path.IsPathRooted(path)
            ? path
            : ProjectPaths.ToRelative(newFolder, ProjectPaths.Resolve(oldFolder, path));
}
