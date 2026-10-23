using System;
using System.IO;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>The Database tab: SQLite file selection and reading the table list from it.</summary>
public partial class DatabaseViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    public DatabaseViewModel(MainViewModel main)
    {
        _main = main;
    }

    public string Provider => "sqlite";

    /// <summary>Copies the database path from the project. Called whenever a project is loaded.</summary>
    public void LoadFrom(Project project)
    {
        DatabasePath = project.Database.Path;
    }

    partial void OnDatabasePathChanged(string value)
    {
        _main.Current.Database.Path = value;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _main.Dialogs.PickSqliteFileAsync(_main.ProjectFolder);
        if (path is null)
        {
            return;
        }

        DatabasePath = _main.ProjectFolder is null ? path : ProjectPaths.ToRelative(_main.ProjectFolder, path);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            _main.Status = "Enter the SQLite file path or use Browse first.";
            return;
        }

        if (!Path.IsPathRooted(DatabasePath) && _main.ProjectFolder is null)
        {
            _main.Status = "Save the project first. Relative database paths are resolved against the project folder.";
            return;
        }

        var project = _main.Current;
        var fullPath = ProjectPaths.Resolve(_main.ProjectFolder, DatabasePath);

        // SQLite creates an empty file when opened, so check for it first to avoid silently making a new database.
        if (!File.Exists(fullPath))
        {
            _main.Status = $"Database file not found: {fullPath}";
            return;
        }

        var connectionString = project.Database.ResolveConnectionString(_main.ProjectFolder ?? string.Empty);
        try
        {
            var discovered = await Task.Run(() => new SqliteSchemaReader().ReadTables(connectionString));
            var result = SchemaMerger.Merge(project, discovered);
            _main.Tables.Reload();
            _main.Security.Refresh();

            var message = $"Connected: {result.Total} table(s), {result.Added} new";
            if (result.Removed.Count > 0)
            {
                message += $", removed {result.Removed.Count} no longer in the database ({string.Join(", ", result.Removed)})";
            }

            _main.Status = message + ".";
        }
        catch (Exception ex)
        {
            _main.Status = $"Connect failed: {ex.Message}";
        }
    }
}
