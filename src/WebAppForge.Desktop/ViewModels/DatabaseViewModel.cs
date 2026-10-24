using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Core.Schema;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>A database product in the provider picker.</summary>
public sealed record ProviderChoice(string Key, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The Database tab: the provider, the SQLite file or the connection string of a server (SQL Server, Azure SQL, MySQL), testing the
/// connection and reading the table list.
/// </summary>
public partial class DatabaseViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private bool _loading;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSqlite), nameof(IsServer), nameof(ConnectionPlaceholder), nameof(ConnectionHint), nameof(PasswordWarning))]
    private ProviderChoice _selectedProvider = Providers[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordWarning))]
    private string _connectionString = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordWarning), nameof(VariableState))]
    private string _connectionStringVariable = string.Empty;

    public DatabaseViewModel(MainViewModel main)
    {
        _main = main;
    }

    public static IReadOnlyList<ProviderChoice> Providers { get; } =
        DatabaseProviders.All.Select(p => new ProviderChoice(p, DatabaseProviders.DisplayName(p))).ToList();

    /// <summary>The provider choices for binding (a static list does not bind as ItemsSource from XAML here).</summary>
    public IReadOnlyList<ProviderChoice> ProviderChoices => Providers;

    public string Provider => SelectedProvider.Key;

    public bool IsSqlite => !IsServer;

    public bool IsServer => DatabaseProviders.IsServer(Provider);

    /// <summary>An example connection string of the selected provider.</summary>
    public string ConnectionPlaceholder => Provider switch
    {
        DatabaseProviders.AzureSql => "Server=tcp:myserver.database.windows.net,1433;Database=mydb;Authentication=Active Directory Default;Encrypt=True",
        DatabaseProviders.MySql => "Server=localhost;Port=3306;Database=mydb;User ID=app;Password=...",
        _ => "Server=localhost;Database=mydb;Integrated Security=True;TrustServerCertificate=True",
    };

    public string ConnectionHint => Provider switch
    {
        DatabaseProviders.AzureSql => "Active Directory Default signs in with your Azure CLI or Visual Studio account here and with the managed identity in Azure, so no password is stored. In production, Active Directory Managed Identity connects faster.",
        DatabaseProviders.MySql => "Tables of the database named in the connection string are read. MySQL 8 and MariaDB 10.6 or later.",
        _ => "Tables of every schema are read; tables outside dbo are named schema.table. SQL Server 2016 or later.",
    };

    /// <summary>A warning when the connection string holds a password that would be saved in plain text, else null.</summary>
    public string? PasswordWarning =>
        IsServer && ConnectionStringVariable.Trim().Length == 0 && HasPassword(ConnectionString)
            ? "This connection string has a password. It is saved in the project file and in the generated appsettings.json as plain text. Put it in an environment variable instead, or use Windows or Entra ID sign-in."
            : null;

    /// <summary>Whether the environment variable is set in the editor's environment, else null when no variable is named.</summary>
    public string? VariableState
    {
        get
        {
            var name = ConnectionStringVariable.Trim();
            if (name.Length == 0)
            {
                return null;
            }

            return Environment.GetEnvironmentVariable(name) is { Length: > 0 }
                ? $"{name} is set here. The generated site reads it too, unless ConnectionStrings:Default is configured."
                : $"{name} is not set in the editor's environment. Set it and restart the editor, or Connect cannot reach the database.";
        }
    }

    private static bool HasPassword(string connectionString) =>
        connectionString.Split(';').Select(p => p.Split('=', 2)[0].Trim()).Any(k => k.Equals("password", StringComparison.OrdinalIgnoreCase) || k.Equals("pwd", StringComparison.OrdinalIgnoreCase));

    /// <summary>Copies the database settings from the project. Called whenever a project is loaded.</summary>
    public void LoadFrom(Project project)
    {
        _loading = true;
        try
        {
            var key = DatabaseProviders.Normalize(project.Database.Provider);
            SelectedProvider = Providers.FirstOrDefault(p => p.Key == key) ?? new ProviderChoice(project.Database.Provider, project.Database.Provider);
            DatabasePath = project.Database.Path;
            ConnectionString = project.Database.ConnectionString;
            ConnectionStringVariable = project.Database.ConnectionStringVariable;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedProviderChanged(ProviderChoice value)
    {
        if (!_loading)
        {
            _main.Current.Database.Provider = value.Key;
        }
    }

    partial void OnDatabasePathChanged(string value)
    {
        if (!_loading)
        {
            _main.Current.Database.Path = value;
        }
    }

    partial void OnConnectionStringChanged(string value)
    {
        if (!_loading)
        {
            _main.Current.Database.ConnectionString = value;
        }
    }

    partial void OnConnectionStringVariableChanged(string value)
    {
        if (!_loading)
        {
            _main.Current.Database.ConnectionStringVariable = value;
        }
    }

    /// <summary>
    /// The connection string the editor uses for the project's database, or the reason there is none: no SQLite file (or a relative
    /// path in an unsaved project, or a missing file), or no server connection string.
    /// </summary>
    public (string? ConnectionString, string? Problem) Resolve()
    {
        var database = _main.Current.Database;
        if (!DatabaseProviders.IsKnown(database.Provider))
        {
            return (null, Core.Generation.ProjectGenerator.UnsupportedProviderMessage(database.Provider));
        }

        if (database.IsServer)
        {
            var variable = database.ConnectionStringVariable.Trim();
            var resolved = database.ResolveConnectionString(_main.ProjectFolder ?? string.Empty);
            if (resolved.Length > 0)
            {
                return (resolved, null);
            }

            return (null, variable.Length > 0
                ? $"The environment variable {variable} is not set in the editor's environment."
                : "Enter the connection string on the Database section first.");
        }

        if (string.IsNullOrWhiteSpace(database.Path))
        {
            return (null, "Enter the SQLite file path or use Browse first.");
        }

        if (!Path.IsPathRooted(database.Path) && _main.ProjectFolder is null)
        {
            return (null, "Save the project first. Relative database paths are resolved against the project folder.");
        }

        // SQLite creates an empty file when opened, so check for it first to avoid silently making a new database.
        var fullPath = ProjectPaths.Resolve(_main.ProjectFolder, database.Path);
        return File.Exists(fullPath) ? (database.ResolveConnectionString(_main.ProjectFolder ?? string.Empty), null) : (null, $"Database file not found: {fullPath}");
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
    private async Task TestAsync()
    {
        var (connectionString, problem) = Resolve();
        if (connectionString is null)
        {
            _main.Status = problem!;
            return;
        }

        var provider = Provider;
        _main.Status = $"Connecting to {SelectedProvider.Name}...";
        var error = await Task.Run(() => DatabaseConnections.TestAsync(provider, connectionString));
        _main.Status = error is null ? $"Connected to {SelectedProvider.Name}." : $"Connection failed: {error}";
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var (connectionString, problem) = Resolve();
        if (connectionString is null)
        {
            _main.Status = problem!;
            return;
        }

        var project = _main.Current;
        var provider = project.Database.Provider;
        try
        {
            var discovered = await Task.Run(() => DatabaseConnections.SchemaReader(provider).ReadTables(connectionString));
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
