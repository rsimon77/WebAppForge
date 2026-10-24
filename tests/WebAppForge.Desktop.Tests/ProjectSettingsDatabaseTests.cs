using System.Collections.Generic;
using System.Linq;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>Regression: the Database section of Project settings always showed SQLite, whatever engine the Database tab had.</summary>
public class ProjectSettingsDatabaseTests
{
    [Fact]
    public void TheProviderFollowsTheDatabaseTab()
    {
        var (main, _) = CustomizationTests.Project();
        var settings = main.ProjectSettings;
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("SQLite", settings.Provider);
        Assert.True(settings.IsSqlite);

        main.Database.SelectedProvider = DatabaseViewModel.Providers.Single(p => p.Key == DatabaseProviders.SqlServer);

        Assert.Equal(DatabaseProviders.DisplayName(DatabaseProviders.SqlServer), settings.Provider);
        Assert.False(settings.IsSqlite);
        Assert.Contains(nameof(ProjectSettingsViewModel.Provider), changed);
        Assert.Contains(nameof(ProjectSettingsViewModel.IsSqlite), changed);
        Assert.Equal("Not set", settings.ServerConnection);

        main.Database.ConnectionStringVariable = "APP_DB";
        Assert.Equal("Environment variable APP_DB", settings.ServerConnection);
    }

    [Fact]
    public void ALoadedServerProjectShowsItsProvider()
    {
        var (main, _) = CustomizationTests.Project();
        var project = main.Current;
        project.Database.Provider = DatabaseProviders.MySql;
        project.Database.ConnectionString = "Server=localhost;Database=shop";
        main.Database.LoadFrom(project);

        Assert.Equal(DatabaseProviders.DisplayName(DatabaseProviders.MySql), main.ProjectSettings.Provider);
        Assert.False(main.ProjectSettings.IsSqlite);
        Assert.Equal("Connection string in the project", main.ProjectSettings.ServerConnection);
    }
}
